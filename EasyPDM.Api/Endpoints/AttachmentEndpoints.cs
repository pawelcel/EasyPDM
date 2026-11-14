using System.Security.Cryptography;
using Npgsql;

// Załączniki — pliki dograne "z zewnątrz" (np. pliki CAD) do Części/Złożenia/Pliku.
// W odróżnieniu od struktury (item_relations) załącznik NIE jest osobnym elementem
// w drzewku: nie ma item_type, nie pokazuje się po lewej stronie i nie da się go
// stamtąd usunąć — zarządza się nim wyłącznie z panelu właściwości po prawej.
static class AttachmentEndpoints
{
    // Załączniki dotyczą tylko Części/Złożenia/Pliku — Folder ma już swój sposób
    // (podpięcie pliku jako elementu podrzędnego w strukturze).
    private static bool AcceptsAttachments(string itemType) => itemType != "folder";

    public static void MapAttachmentEndpoints(this WebApplication app, string connectionString, StorageSettings storage, UploadPolicy uploads)
    {
        // GET /api/items/{itemId}/attachments — odczyt, świadomie otwarty dla KAŻDEGO
        // zalogowanego użytkownika (zob. GET /api/items), bez sprawdzenia dostępu do projektu.
        app.MapGet("/api/items/{itemId:guid}/attachments", async (Guid itemId, HttpContext ctx) =>
        {
            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, itemId);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var sql = $"""
                SELECT a.id, a.file_name, a.file_size, a.uploaded_at, a.preview_role, a.revision_number,
                       {UserNames.FullNameSql("u")}
                FROM item_attachments a
                LEFT JOIN users u ON u.id = a.uploaded_by
                WHERE a.item_id = @itemId
                ORDER BY a.uploaded_at;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("itemId", itemId);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    id = reader.GetGuid(0),
                    fileName = reader.GetString(1),
                    fileSize = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2),
                    uploadedAt = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3),
                    role = reader.IsDBNull(4) ? null : reader.GetString(4),
                    revisionNumber = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                    uploadedBy = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
            }

            return Results.Ok(result);
        });

        // POST /api/items/{itemId}/attachments   multipart/form-data: file (wymagany)
        app.MapPost("/api/items/{itemId:guid}/attachments", async (Guid itemId, HttpRequest request, HttpContext ctx) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data.");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest("No file in the 'file' field.");
            if (uploads.Check(file) is { } refused)
                return refused;

            var role = form["role"].ToString();
            if (role == "") role = null;
            if (role is not null && role is not ("pdf" or "step" or "cad" or "drawing" or "image"))
                return Results.BadRequest("Field 'role' must be 'pdf', 'step', 'cad', 'drawing', 'image' or empty.");

            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, itemId);
            if (info is null)
                return Results.NotFound("The item does not exist.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (!AcceptsAttachments(info.Value.ItemType))
                return Results.BadRequest("This item type does not take attachments.");
            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status))
                return Results.BadRequest("Attachments can only be added while the item is In progress.");
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            var attachmentId = Guid.NewGuid();
            var extension = Path.GetExtension(file.FileName);
            var attachmentDir = Path.Combine(storage.Path, "attachments", itemId.ToString());
            Directory.CreateDirectory(attachmentDir);
            var storedPath = Path.Combine(attachmentDir, $"{attachmentId}{extension}");

            await using (var stream = File.Create(storedPath))
            {
                await file.CopyToAsync(stream);
            }

            string hash;
            using (var sha256 = SHA256.Create())
            await using (var readStream = File.OpenRead(storedPath))
            {
                hash = Convert.ToHexString(await sha256.ComputeHashAsync(readStream));
            }

            const string insertSql = """
                INSERT INTO item_attachments (id, item_id, file_name, file_path, file_hash, file_size, uploaded_at, uploaded_by, preview_role, revision_number)
                VALUES (@id, @itemId, @fileName, @filePath, @hash, @size, now(), @uploadedBy, @role, @revisionNumber);
                """;
            try
            {
                await ReplaceExistingRoleAttachmentAsync(conn, itemId, role, file.FileName, storedPath, user.Id);

                await using var insertCmd = new NpgsqlCommand(insertSql, conn);
                insertCmd.Parameters.AddWithValue("id", attachmentId);
                insertCmd.Parameters.AddWithValue("itemId", itemId);
                insertCmd.Parameters.AddWithValue("fileName", file.FileName);
                insertCmd.Parameters.AddWithValue("filePath", storedPath);
                insertCmd.Parameters.AddWithValue("hash", hash);
                insertCmd.Parameters.AddWithValue("uploadedBy", user.Id);
                insertCmd.Parameters.AddWithValue("size", file.Length);
                insertCmd.Parameters.AddWithValue("role", (object?)role ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("revisionNumber", (object?)info.Value.RevisionNumber ?? DBNull.Value);
                await insertCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                File.Delete(storedPath);
                throw;
            }

            await LogAttachmentHistoryAsync(conn, itemId, file.FileName, "added", user.Id);

            return Results.Created($"/api/attachments/{attachmentId}", new { id = attachmentId, fileName = file.FileName, role });
        });

        // POST /api/items/{itemId}/attachments/register   body: { "filePath": "..." }
        // Rejestruje JUŻ ISTNIEJĄCY plik w magazynie jako załącznik, BEZ kopiowania —
        // używane, gdy klient (np. makro FreeCAD) i serwer współdzielą ten sam dysk/magazyn:
        // plik już fizycznie leży w storageRoot, więc nie ma sensu przesyłać go drugi raz
        // przez HTTP. Ze względu na brak autoryzacji w API akceptowane są WYŁĄCZNIE ścieżki
        // leżące wewnątrz skonfigurowanego StorageRoot — inaczej dowolny wywołujący mógłby
        // "podpiąć" jako załącznik dowolny plik z dysku serwera.
        app.MapPost("/api/items/{itemId:guid}/attachments/register", async (Guid itemId, RegisterAttachmentRequest body, HttpContext ctx) =>
        {
            if (body.Role is not null && body.Role is not ("pdf" or "step" or "cad" or "drawing" or "image"))
                return Results.BadRequest("Field 'role' must be 'pdf', 'step', 'cad', 'drawing', 'image' or empty.");

            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, itemId);
            if (info is null)
                return Results.NotFound("The item does not exist.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (!AcceptsAttachments(info.Value.ItemType))
                return Results.BadRequest("This item type does not take attachments.");
            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status))
                return Results.BadRequest("Attachments can only be added while the item is In progress.");
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            string fullPath;
            string storageRootFull;
            try
            {
                fullPath = Path.GetFullPath(body.FilePath);
                storageRootFull = Path.GetFullPath(storage.Path);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest("Invalid file path.");
            }

            if (!fullPath.StartsWith(storageRootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return Results.BadRequest("The path must be inside the server's file storage.");

            if (!File.Exists(fullPath))
                return Results.NotFound("No file exists at the given path.");

            var attachmentId = Guid.NewGuid();
            var fileName = Path.GetFileName(fullPath);
            var fileSize = new FileInfo(fullPath).Length;

            string hash;
            using (var sha256 = SHA256.Create())
            await using (var readStream = File.OpenRead(fullPath))
            {
                hash = Convert.ToHexString(await sha256.ComputeHashAsync(readStream));
            }

            await ReplaceExistingRoleAttachmentAsync(conn, itemId, body.Role, fileName, fullPath, user.Id);

            const string insertSql = """
                INSERT INTO item_attachments (id, item_id, file_name, file_path, file_hash, file_size, uploaded_at, uploaded_by, preview_role, revision_number)
                VALUES (@id, @itemId, @fileName, @filePath, @hash, @size, now(), @uploadedBy, @role, @revisionNumber);
                """;
            await using var insertCmd = new NpgsqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("id", attachmentId);
            insertCmd.Parameters.AddWithValue("itemId", itemId);
            insertCmd.Parameters.AddWithValue("fileName", fileName);
            insertCmd.Parameters.AddWithValue("filePath", fullPath);
            insertCmd.Parameters.AddWithValue("hash", hash);
            insertCmd.Parameters.AddWithValue("uploadedBy", user.Id);
            insertCmd.Parameters.AddWithValue("size", fileSize);
            insertCmd.Parameters.AddWithValue("role", (object?)body.Role ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("revisionNumber", (object?)info.Value.RevisionNumber ?? DBNull.Value);
            await insertCmd.ExecuteNonQueryAsync();

            await LogAttachmentHistoryAsync(conn, itemId, fileName, "added", user.Id);

            return Results.Created($"/api/attachments/{attachmentId}", new { id = attachmentId, fileName, role = body.Role });
        });

        // GET /api/attachments/{id}/file — pobranie załącznika, świadomie otwarte dla
        // KAŻDEGO zalogowanego użytkownika (zob. GET /api/items), bez sprawdzenia dostępu
        // do projektu.
        app.MapGet("/api/attachments/{id:guid}/file", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT ia.file_path, ia.file_name
                FROM item_attachments ia
                WHERE ia.id = @id;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            string path, fileName;
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return Results.NotFound();

                path = reader.GetString(0);
                fileName = reader.GetString(1);
            }

            if (!File.Exists(path))
                return Results.NotFound("The file is missing from the server's storage.");

            return Results.File(path, "application/octet-stream", fileName);
        });

        // DELETE /api/attachments/{id}
        app.MapDelete("/api/attachments/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string filePath;
            Guid itemId;
            string fileName;
            string itemType;
            string? status;
            Guid? ownerId;
            bool ownerLocked;
            Guid projectId;
            string? role;
            await using (var selectCmd = new NpgsqlCommand(
                """
                SELECT ia.file_path, ia.item_id, ia.file_name, i.item_type, i.status, i.owner_id, i.owner_locked, i.project_id, ia.preview_role
                FROM item_attachments ia JOIN items i ON i.id = ia.item_id WHERE ia.id = @id;
                """, conn))
            {
                selectCmd.Parameters.AddWithValue("id", id);
                await using var reader = await selectCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.NotFound();

                filePath = reader.GetString(0);
                itemId = reader.GetGuid(1);
                fileName = reader.GetString(2);
                itemType = reader.GetString(3);
                status = reader.IsDBNull(4) ? null : reader.GetString(4);
                ownerId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5);
                ownerLocked = reader.GetBoolean(6);
                projectId = reader.GetGuid(7);
                role = reader.IsDBNull(8) ? null : reader.GetString(8);
            }

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (ItemEndpoints.IsLocked(itemType, status))
                return Results.BadRequest("Attachments can only be removed while the item is In progress.");

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, ownerId, ownerLocked))
                return ItemEndpoints.OwnerLockedForbidden();
            var userId = user.Id;

            await using (var deleteCmd = new NpgsqlCommand("DELETE FROM item_attachments WHERE id = @id;", conn))
            {
                deleteCmd.Parameters.AddWithValue("id", id);
                await deleteCmd.ExecuteNonQueryAsync();
            }

            await LogAttachmentHistoryAsync(conn, itemId, fileName, "removed", userId);

            try { File.Delete(filePath); } catch (IOException) { /* magazyn i tak jest sierocy — nie blokujemy usunięcia rekordu */ }

            // Zrzut modelu (rola "image") istnieje WYŁĄCZNIE po to, żeby pokazać ten model —
            // powstaje razem ze STEP-em przy wysyłce z CAD-a i bez niego nie ma czego
            // przedstawiać. Usuwany razem, inaczej zostawałby w bazie i w magazynie obrazek,
            // którego nic już nie wyświetla ani nie da się z niczym powiązać.
            if (role == "step")
                await DeleteRoleAttachmentsAsync(conn, itemId, "image", userId);

            return Results.Ok();
        });
    }

    // "pdf"/"step" to sloty JEDEN-załącznik-na-rolę (w odróżnieniu od "cad", które się
    // kumuluje — patrz komentarz w attachments-panel.tsx) — nowy zastępuje poprzedni.
    // Do niedawna to zastępowanie robił WYŁĄCZNIE frontend webowy (usuwał stary przed
    // wysłaniem nowego), więc każdy inny wywołujący tego endpointu (makra FreeCAD/
    // SolidWorks, wysyłające PDF/STEP bezpośrednio przez API) omijał to całkowicie —
    // stary załącznik zostawał w bazie I na dysku, niewidoczny w UI (RoleSlot pokazuje
    // tylko jeden, znaleziony przez .find()), ale zajmujący miejsce na zawsze. Wymuszone
    // tutaj, więc działa identycznie niezależnie od tego, kto woła ten endpoint.
    //
    // "cad"/"drawing" się kumulują, ale tylko między REWIZJAMI: makra nazywają plik
    // "numer (nazwa).REWIZJA.rozszerzenie", więc ta sama nazwa to ta sama rewizja wysłana
    // jeszcze raz -- i ona ma zastąpić poprzedni wpis, nie dołożyć kolejny. Bez tego każda
    // ponowna wysyłka rewizji A dokładała wiersz "…A.FCStd" (zgłoszone z praktyki: trzy
    // takie same w panelu), a przy wspólnym magazynie wszystkie wskazywały TEN SAM plik na
    // dysku, więc usunięcie jednego z nich kasowało plik spod pozostałych.
    //
    // newFilePath to ścieżka, pod którą leży NOWY plik: przy /register makro kopiuje go w
    // miejsce starego (ta sama ścieżka), więc stary wiersz znika, ale pliku nie wolno ruszyć.
    private static async Task ReplaceExistingRoleAttachmentAsync(
        NpgsqlConnection conn, Guid itemId, string? role, string fileName, string newFilePath, Guid userId)
    {
        // "image" dochodzi do jednoslotowych: zrzut przedstawia BIEŻĄCĄ postać modelu, więc
        // kolejna wysyłka ma go zastąpić, a nie odkładać obok (inaczej każdy upload zostawiałby
        // nieosiągalny plik na dysku -- RoleSlot i podgląd biorą pierwszy znaleziony).
        if (role is "pdf" or "step" or "image")
            await DeleteRoleAttachmentsAsync(conn, itemId, role, userId, newFilePath);
        else if (role is "cad" or "drawing")
            await DeleteSameNameAttachmentsAsync(conn, itemId, role, fileName, newFilePath, userId);
    }

    private static async Task DeleteSameNameAttachmentsAsync(
        NpgsqlConnection conn, Guid itemId, string role, string fileName, string newFilePath, Guid userId)
    {
        var existing = new List<string>();
        await using (var deleteCmd = new NpgsqlCommand("""
            DELETE FROM item_attachments
            WHERE item_id = @itemId AND preview_role = @role AND file_name = @fileName
            RETURNING file_path;
            """, conn))
        {
            deleteCmd.Parameters.AddWithValue("itemId", itemId);
            deleteCmd.Parameters.AddWithValue("role", role);
            deleteCmd.Parameters.AddWithValue("fileName", fileName);
            await using var reader = await deleteCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existing.Add(reader.GetString(0));
        }

        if (existing.Count == 0)
            return;

        await LogAttachmentHistoryAsync(conn, itemId, fileName, "removed", userId);
        foreach (var path in existing.Distinct())
            DeleteStoredFileUnlessSame(path, newFilePath);
    }

    private static void DeleteStoredFileUnlessSame(string path, string keepPath)
    {
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(keepPath), StringComparison.Ordinal))
            return;
        try { File.Delete(path); } catch (IOException) { /* magazyn i tak jest sierocy — nie blokujemy zastąpienia */ }
    }

    // Kasuje WSZYSTKIE załączniki danej roli na elemencie -- wiersze, pliki w magazynie i wpis
    // w historii. Używane w dwóch miejscach: przy zastępowaniu slotu nowym plikiem (wyżej)
    // oraz przy usuwaniu STEP-a, które zabiera ze sobą zrzut modelu (zob. MapDelete).
    private static async Task DeleteRoleAttachmentsAsync(
        NpgsqlConnection conn, Guid itemId, string role, Guid userId, string? keepPath = null)
    {
        var existing = new List<(Guid Id, string FileName, string FilePath)>();
        await using (var selectCmd = new NpgsqlCommand(
            "SELECT id, file_name, file_path FROM item_attachments WHERE item_id = @itemId AND preview_role = @role;", conn))
        {
            selectCmd.Parameters.AddWithValue("itemId", itemId);
            selectCmd.Parameters.AddWithValue("role", role);
            await using var reader = await selectCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existing.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        if (existing.Count == 0)
            return;

        await using (var deleteCmd = new NpgsqlCommand(
            "DELETE FROM item_attachments WHERE item_id = @itemId AND preview_role = @role;", conn))
        {
            deleteCmd.Parameters.AddWithValue("itemId", itemId);
            deleteCmd.Parameters.AddWithValue("role", role);
            await deleteCmd.ExecuteNonQueryAsync();
        }

        foreach (var (_, fileName, filePath) in existing)
        {
            await LogAttachmentHistoryAsync(conn, itemId, fileName, "removed", userId);
            if (keepPath is not null)
                DeleteStoredFileUnlessSame(filePath, keepPath);
            else
                try { File.Delete(filePath); } catch (IOException) { /* magazyn i tak jest sierocy — nie blokujemy zastąpienia */ }
        }
    }

    // Wpis do panelu "Historia" Części/Złożenia — osobna tabela, bo usunięty załącznik
    // znika z item_attachments, więc samo to nie wystarczy do zachowania śladu kto/kiedy.
    private static async Task LogAttachmentHistoryAsync(
        NpgsqlConnection conn, Guid itemId, string fileName, string action, Guid userId)
    {
        const string sql = """
            INSERT INTO item_attachment_history (item_id, file_name, action, user_id)
            VALUES (@itemId, @fileName, @action, @userId);
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("itemId", itemId);
        cmd.Parameters.AddWithValue("fileName", fileName);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }
}

record RegisterAttachmentRequest(string FilePath, string? Role = null);
