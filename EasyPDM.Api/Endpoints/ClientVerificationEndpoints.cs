using Npgsql;

// WERYFIKACJA KLIENTA — ślad akceptacji (albo uwag) klienta dla WYDANEJ Części/Złożenia:
// rosnąca lista wpisów, każdy z wynikiem ("zweryfikowany"/"do poprawy"), opcjonalnym
// komentarzem i własnymi załącznikami (np. e-mail z potwierdzeniem).
//
// Wpis wisi na PARZE (element, projekt), nie na samym elemencie — ta sama Część/Złożenie
// bywa współdzielona przez kilka projektów (item_relations nie zna granic projektu), a
// akceptuje ją konkretny klient konkretnego projektu. Dlatego te dane celowo NIE pokazują
// się w "Całej bazie", gdzie element ogląda się bez kontekstu projektu — jedyne wejście
// prowadzi przez widok projektu (zob. ścieżki /api/projects/{projectId}/... niżej).
static class ClientVerificationEndpoints
{
    private const string ResultVerified = "zweryfikowany";
    private const string ResultNeedsWork = "do_poprawy";

    public static void MapClientVerificationEndpoints(this WebApplication app, string connectionString, StorageSettings storage)
    {
        // GET /api/projects/{projectId}/items/{itemId}/client-verifications — pełna lista
        // wpisów dla tego elementu W TYM projekcie, najnowsze pierwsze, razem z załącznikami.
        app.MapGet("/api/projects/{projectId:guid}/items/{itemId:guid}/client-verifications",
            async (Guid projectId, Guid itemId, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            const string sql = """
                SELECT v.id, v.result, v.comment, v.revision_number, v.created_at, u.display_name
                FROM item_client_verifications v
                LEFT JOIN users u ON u.id = v.created_by
                WHERE v.item_id = @itemId AND v.project_id = @projectId
                ORDER BY v.created_at DESC;
                """;
            var entries = new List<Dictionary<string, object?>>();
            var byId = new Dictionary<Guid, List<object>>();
            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("itemId", itemId);
                cmd.Parameters.AddWithValue("projectId", projectId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var id = reader.GetGuid(0);
                    var attachments = new List<object>();
                    byId[id] = attachments;
                    entries.Add(new Dictionary<string, object?>
                    {
                        ["id"] = id,
                        ["result"] = reader.IsDBNull(1) ? null : reader.GetString(1),
                        ["comment"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                        ["revisionNumber"] = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3),
                        ["createdAt"] = reader.GetDateTime(4),
                        ["createdBy"] = reader.IsDBNull(5) ? null : reader.GetString(5),
                        ["attachments"] = attachments,
                    });
                }
            }

            if (entries.Count > 0)
            {
                // Załączniki jednym zapytaniem dla wszystkich wpisów naraz (ANY(@ids)) zamiast
                // osobnego zapytania per wpis — ta lista bywa długa przy kilku rundach uwag.
                const string attachmentsSql = """
                    SELECT verification_id, id, file_name, file_size, uploaded_at
                    FROM item_client_verification_attachments
                    WHERE verification_id = ANY(@ids)
                    ORDER BY uploaded_at;
                    """;
                await using var attCmd = new NpgsqlCommand(attachmentsSql, conn);
                attCmd.Parameters.AddWithValue("ids", byId.Keys.ToArray());
                await using var reader = await attCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    byId[reader.GetGuid(0)].Add(new
                    {
                        id = reader.GetGuid(1),
                        fileName = reader.GetString(2),
                        fileSize = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3),
                        uploadedAt = reader.GetDateTime(4),
                    });
                }
            }

            return Results.Ok(entries);
        });

        // GET /api/projects/{projectId}/client-verifications — podsumowanie: OSTATNI wynik dla
        // każdego elementu w tym projekcie, który ma choć jeden wpis. Zasila znaczniki w
        // drzewku i sekcję w panelu właściwości, żeby nie odpytywać serwera raz na element.
        app.MapGet("/api/projects/{projectId:guid}/client-verifications", async (Guid projectId, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            // DISTINCT ON (item_id) + ORDER BY created_at DESC daje dokładnie jeden, najnowszy
            // wiersz na element — to samo co okienkowe row_number()=1, tylko krócej i taniej.
            //
            // Dane elementu (numer/nazwa/aktualna rewizja) dołączane tutaj, a nie dobierane
            // po stronie frontu: zestawienie ma działać także tam, gdzie nie ma załadowanego
            // drzewka projektu, a i tak są już potrzebne do wyświetlenia wiersza.
            const string sql = """
                SELECT DISTINCT ON (v.item_id)
                       v.item_id, v.result, v.revision_number, v.created_at,
                       i.item_number, i.item_number_prefix, i.file_name, i.revision_number
                FROM item_client_verifications v
                JOIN items i ON i.id = v.item_id
                WHERE v.project_id = @projectId
                ORDER BY v.item_id, v.created_at DESC;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", projectId);
            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    itemId = reader.GetGuid(0),
                    result = reader.IsDBNull(1) ? null : reader.GetString(1),
                    revisionNumber = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                    createdAt = reader.GetDateTime(3),
                    itemNumber = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
                    itemNumberPrefix = reader.IsDBNull(5) ? null : reader.GetString(5),
                    fileName = reader.GetString(6),
                    itemRevisionNumber = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7),
                });
            }
            return Results.Ok(result);
        });

        // POST /api/projects/{projectId}/items/{itemId}/client-verifications
        // multipart/form-data: result (wymagane), comment (opcjonalny), file/files (opcjonalne).
        app.MapPost("/api/projects/{projectId:guid}/items/{itemId:guid}/client-verifications",
            async (Guid projectId, Guid itemId, HttpRequest request, HttpContext ctx) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Oczekiwano danych multipart/form-data.");

            var form = await request.ReadFormAsync();
            // Brak wyniku jest DOZWOLONY i znaczy "w trakcie weryfikacji" — rzecz poszła do
            // klienta i czekamy na odpowiedź. W oknie żaden wynik nie jest zaznaczony
            // domyślnie, więc to najczęstszy pierwszy wpis, nie błąd pominięcia pola.
            var verificationResult = form["result"].ToString();
            if (string.IsNullOrWhiteSpace(verificationResult))
                verificationResult = null;
            else if (verificationResult is not (ResultVerified or ResultNeedsWork))
                return Results.BadRequest($"Pole 'result' musi być '{ResultVerified}', '{ResultNeedsWork}' albo puste.");

            var comment = form["comment"].ToString();
            if (string.IsNullOrWhiteSpace(comment))
                comment = null;

            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, itemId);
            if (info is null)
                return Results.NotFound("Element nie istnieje.");
            if (info.Value.ItemType is not ("part" or "assembly"))
                return Results.BadRequest("Weryfikacji klienta podlegają tylko Części i Złożenia.");

            // Weryfikuje się to, co klient faktycznie dostał, czyli wersję WYDANĄ — dla elementu
            // w pracy/sprawdzanego nie ma jeszcze czego akceptować. Ten sam warunek decyduje po
            // stronie frontu o pokazaniu przycisku; tu jest powtórzony, bo UI nie jest strażą.
            if (info.Value.Status != "wydany")
                return Results.BadRequest("Weryfikację klienta można prowadzić tylko dla elementu w statusie 'Wydany'.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            // Element musi faktycznie należeć do tego projektu ALBO być w nim użyty jako
            // komponent (współdzielona Część spod złożenia z tego projektu) -- inaczej dałoby
            // się dopisać weryfikację do zupełnie niezwiązanego elementu, podając dowolne id.
            if (!await IsItemInProjectAsync(conn, itemId, projectId))
                return Results.BadRequest("Ten element nie należy do wskazanego projektu.");

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            var verificationId = Guid.NewGuid();

            const string insertSql = """
                INSERT INTO item_client_verifications
                    (id, item_id, project_id, result, comment, revision_number, created_by)
                VALUES (@id, @itemId, @projectId, @result, @comment, @revisionNumber, @createdBy);
                """;
            await using (var cmd = new NpgsqlCommand(insertSql, conn))
            {
                cmd.Parameters.AddWithValue("id", verificationId);
                cmd.Parameters.AddWithValue("itemId", itemId);
                cmd.Parameters.AddWithValue("projectId", projectId);
                cmd.Parameters.AddWithValue("result", (object?)verificationResult ?? DBNull.Value);
                cmd.Parameters.AddWithValue("comment", (object?)comment ?? DBNull.Value);
                cmd.Parameters.AddWithValue("revisionNumber", (object?)info.Value.RevisionNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("createdBy", user.Id);
                await cmd.ExecuteNonQueryAsync();
            }

            // Pliki przyjmujemy pod "file" ORAZ "files" — jeden wpis potrafi nieść kilka dowodów,
            // a różne klienty HTTP nazywają pole różnie.
            var files = form.Files.GetFiles("file").Concat(form.Files.GetFiles("files")).ToList();
            var storedPaths = new List<string>();
            try
            {
                foreach (var file in files)
                {
                    if (file.Length == 0)
                        continue;

                    var attachmentId = Guid.NewGuid();
                    var dir = Path.Combine(storage.Path, "client-verifications", verificationId.ToString());
                    Directory.CreateDirectory(dir);
                    var storedPath = Path.Combine(dir, $"{attachmentId}{Path.GetExtension(file.FileName)}");

                    await using (var stream = File.Create(storedPath))
                        await file.CopyToAsync(stream);
                    storedPaths.Add(storedPath);

                    const string attachmentSql = """
                        INSERT INTO item_client_verification_attachments
                            (id, verification_id, file_name, file_path, file_size)
                        VALUES (@id, @verificationId, @fileName, @filePath, @size);
                        """;
                    await using var attCmd = new NpgsqlCommand(attachmentSql, conn);
                    attCmd.Parameters.AddWithValue("id", attachmentId);
                    attCmd.Parameters.AddWithValue("verificationId", verificationId);
                    attCmd.Parameters.AddWithValue("fileName", file.FileName);
                    attCmd.Parameters.AddWithValue("filePath", storedPath);
                    attCmd.Parameters.AddWithValue("size", file.Length);
                    await attCmd.ExecuteNonQueryAsync();
                }
            }
            catch
            {
                // Wpis bez kompletu swoich załączników byłby mylącym śladem ("zweryfikowany",
                // ale dowodu brak) — przy błędzie kasujemy całość razem z plikami, które zdążyły
                // wylądować na dysku (załączniki znikają kaskadą po verification_id).
                foreach (var path in storedPaths)
                {
                    try { File.Delete(path); } catch (IOException) { }
                }
                await using (var cleanup = new NpgsqlCommand(
                    "DELETE FROM item_client_verifications WHERE id = @id;", conn))
                {
                    cleanup.Parameters.AddWithValue("id", verificationId);
                    await cleanup.ExecuteNonQueryAsync();
                }
                throw;
            }

            // Powiadomienie dopiero TERAZ, po komplecie wpisu razem z załącznikami — gdyby
            // poszło wcześniej, nieudany upload cofnąłby wpis (catch wyżej), a powiadomienie
            // o nieistniejącym zdarzeniu już by wisiało.
            //
            // Odbiorca: właściciel, a gdy go nie ma — twórca elementu. Ten drugi przypadek jest
            // tu REGUŁĄ, nie wyjątkiem: weryfikować da się wyłącznie element "wydany", a taki
            // ZAWSZE ma owner_id=NULL (zerowane przy przejściu na ten status), więc bez zapasu
            // na created_by te powiadomienia nie miałyby komu się pokazać. Ten sam wzorzec co
            // przy powiadomieniach o zmianie statusu w ItemEndpoints.
            //
            // Wpis bez wyniku ("w trakcie weryfikacji") świadomie nie powiadamia — to tylko
            // odnotowanie, że rzecz poszła do klienta, a nie zdarzenie wymagające czyjejś uwagi.
            var recipientId = info.Value.OwnerId ?? info.Value.CreatedBy;
            if (verificationResult is not null && recipientId is not null && recipientId != user.Id)
            {
                var notifyData = new
                {
                    itemLabel = ItemEndpoints.ItemLabel(
                        info.Value.FileName, info.Value.ItemNumber, info.Value.ItemNumberPrefix),
                };
                var type = verificationResult == ResultNeedsWork
                    ? "client_verification_needs_work"
                    : "client_verification_verified";
                await Notifications.NotifyAsync(
                    conn, app.Logger, recipientId.Value, type, notifyData, itemId: itemId, projectId: projectId);
            }

            return Results.Created($"/api/client-verifications/{verificationId}", new { id = verificationId });
        });

        // GET /api/client-verification-attachments/{id}/download — pobranie dowodu. Dostęp
        // sprawdzany przez projekt, do którego należy wpis (nie przez sam element).
        app.MapGet("/api/client-verification-attachments/{id:guid}/download", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT a.file_name, a.file_path, v.project_id
                FROM item_client_verification_attachments a
                JOIN item_client_verifications v ON v.id = a.verification_id
                WHERE a.id = @id;
                """;
            string fileName, filePath;
            Guid projectId;
            await using (var cmd = new NpgsqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.NotFound();
                fileName = reader.GetString(0);
                filePath = reader.GetString(1);
                projectId = reader.GetGuid(2);
            }

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (!File.Exists(filePath))
                return Results.NotFound("Plik nie istnieje w magazynie.");

            return Results.File(filePath, "application/octet-stream", fileName);
        });

        // DELETE /api/client-verifications/{id} — tylko administrator. Wpis weryfikacji to
        // ślad ustaleń z klientem, więc nie kasuje go zwykły użytkownik; usuwanie jest po to,
        // żeby dało się poprawić pomyłkę (np. wpis dodany pod złym elementem).
        app.MapDelete("/api/client-verifications/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var paths = new List<string>();
            await using (var pathCmd = new NpgsqlCommand(
                "SELECT file_path FROM item_client_verification_attachments WHERE verification_id = @id;", conn))
            {
                pathCmd.Parameters.AddWithValue("id", id);
                await using var reader = await pathCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    paths.Add(reader.GetString(0));
            }

            int deleted;
            await using (var cmd = new NpgsqlCommand("DELETE FROM item_client_verifications WHERE id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                deleted = await cmd.ExecuteNonQueryAsync();
            }
            if (deleted == 0)
                return Results.NotFound();

            foreach (var path in paths)
            {
                try { File.Delete(path); } catch (IOException) { }
            }

            return Results.Ok();
        });
    }

    // Element "jest w projekcie", jeśli formalnie do niego należy (items.project_id) ALBO
    // występuje w jego strukturze jako komponent — ta sama zasada, co przy budowaniu drzewka
    // projektu (zob. GET /api/projects/{id}/relations): współdzielona Część spod złożenia z
    // tego projektu jest jego częścią, choć jej project_id wskazuje gdzie indziej.
    private static async Task<bool> IsItemInProjectAsync(NpgsqlConnection conn, Guid itemId, Guid projectId)
    {
        const string sql = """
            WITH RECURSIVE reachable AS (
                SELECT id AS item_id FROM items WHERE project_id = @projectId
                UNION
                SELECT ir.child_id FROM item_relations ir
                JOIN reachable r ON ir.parent_id = r.item_id
            )
            SELECT 1 FROM reachable WHERE item_id = @itemId LIMIT 1;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("itemId", itemId);
        cmd.Parameters.AddWithValue("projectId", projectId);
        return await cmd.ExecuteScalarAsync() is not null;
    }
}
