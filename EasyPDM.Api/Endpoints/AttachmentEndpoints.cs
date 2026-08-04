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

    public static void MapAttachmentEndpoints(this WebApplication app, string connectionString, StorageSettings storage)
    {
        // GET /api/items/{itemId}/attachments
        app.MapGet("/api/items/{itemId:guid}/attachments", async (Guid itemId, HttpContext ctx) =>
        {
            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, itemId);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            const string sql = """
                SELECT id, file_name, file_size, uploaded_at
                FROM item_attachments
                WHERE item_id = @itemId
                ORDER BY uploaded_at;
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
                    uploadedAt = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3)
                });
            }

            return Results.Ok(result);
        });

        // POST /api/items/{itemId}/attachments   multipart/form-data: file (wymagany)
        app.MapPost("/api/items/{itemId:guid}/attachments", async (Guid itemId, HttpRequest request, HttpContext ctx) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Oczekiwano danych multipart/form-data.");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest("Brak pliku w polu 'file'.");

            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, itemId);
            if (info is null)
                return Results.NotFound("Element nie istnieje.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (!AcceptsAttachments(info.Value.ItemType))
                return Results.BadRequest("Ten typ elementu nie przyjmuje załączników.");
            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status))
                return Results.BadRequest("Załączniki można dodawać tylko w statusie 'W pracy'.");
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
                INSERT INTO item_attachments (id, item_id, file_name, file_path, file_hash, file_size, uploaded_at)
                VALUES (@id, @itemId, @fileName, @filePath, @hash, @size, now());
                """;
            try
            {
                await using var insertCmd = new NpgsqlCommand(insertSql, conn);
                insertCmd.Parameters.AddWithValue("id", attachmentId);
                insertCmd.Parameters.AddWithValue("itemId", itemId);
                insertCmd.Parameters.AddWithValue("fileName", file.FileName);
                insertCmd.Parameters.AddWithValue("filePath", storedPath);
                insertCmd.Parameters.AddWithValue("hash", hash);
                insertCmd.Parameters.AddWithValue("size", file.Length);
                await insertCmd.ExecuteNonQueryAsync();
            }
            catch
            {
                File.Delete(storedPath);
                throw;
            }

            await LogAttachmentHistoryAsync(conn, itemId, file.FileName, "added", user.Id);

            return Results.Created($"/api/attachments/{attachmentId}", new { id = attachmentId, fileName = file.FileName });
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
            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, itemId);
            if (info is null)
                return Results.NotFound("Element nie istnieje.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (!AcceptsAttachments(info.Value.ItemType))
                return Results.BadRequest("Ten typ elementu nie przyjmuje załączników.");
            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status))
                return Results.BadRequest("Załączniki można dodawać tylko w statusie 'W pracy'.");
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
                return Results.BadRequest("Niepoprawna ścieżka pliku.");
            }

            if (!fullPath.StartsWith(storageRootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return Results.BadRequest("Ścieżka musi znajdować się wewnątrz magazynu plików serwera.");

            if (!File.Exists(fullPath))
                return Results.NotFound("Plik nie istnieje pod podaną ścieżką.");

            var attachmentId = Guid.NewGuid();
            var fileName = Path.GetFileName(fullPath);
            var fileSize = new FileInfo(fullPath).Length;

            string hash;
            using (var sha256 = SHA256.Create())
            await using (var readStream = File.OpenRead(fullPath))
            {
                hash = Convert.ToHexString(await sha256.ComputeHashAsync(readStream));
            }

            const string insertSql = """
                INSERT INTO item_attachments (id, item_id, file_name, file_path, file_hash, file_size, uploaded_at)
                VALUES (@id, @itemId, @fileName, @filePath, @hash, @size, now());
                """;
            await using var insertCmd = new NpgsqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("id", attachmentId);
            insertCmd.Parameters.AddWithValue("itemId", itemId);
            insertCmd.Parameters.AddWithValue("fileName", fileName);
            insertCmd.Parameters.AddWithValue("filePath", fullPath);
            insertCmd.Parameters.AddWithValue("hash", hash);
            insertCmd.Parameters.AddWithValue("size", fileSize);
            await insertCmd.ExecuteNonQueryAsync();

            await LogAttachmentHistoryAsync(conn, itemId, fileName, "added", user.Id);

            return Results.Created($"/api/attachments/{attachmentId}", new { id = attachmentId, fileName });
        });

        // GET /api/attachments/{id}/file — pobranie załącznika.
        app.MapGet("/api/attachments/{id:guid}/file", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT ia.file_path, ia.file_name, i.project_id
                FROM item_attachments ia JOIN items i ON i.id = ia.item_id
                WHERE ia.id = @id;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            string path, fileName;
            Guid projectId;
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return Results.NotFound();

                path = reader.GetString(0);
                fileName = reader.GetString(1);
                projectId = reader.GetGuid(2);
            }

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (!File.Exists(path))
                return Results.NotFound("Plik zniknął z magazynu na dysku serwera.");

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
            await using (var selectCmd = new NpgsqlCommand(
                """
                SELECT ia.file_path, ia.item_id, ia.file_name, i.item_type, i.status, i.owner_id, i.owner_locked, i.project_id
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
            }

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (ItemEndpoints.IsLocked(itemType, status))
                return Results.BadRequest("Załączniki można usuwać tylko w statusie 'W pracy'.");

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

            return Results.Ok();
        });
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

record RegisterAttachmentRequest(string FilePath);
