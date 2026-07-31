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

    public static void MapAttachmentEndpoints(this WebApplication app, string connectionString, string storageRoot)
    {
        // GET /api/items/{itemId}/attachments
        app.MapGet("/api/items/{itemId:guid}/attachments", async (Guid itemId) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

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
        app.MapPost("/api/items/{itemId:guid}/attachments", async (Guid itemId, HttpRequest request) =>
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
            if (!AcceptsAttachments(info.Value.ItemType))
                return Results.BadRequest("Ten typ elementu nie przyjmuje załączników.");
            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status))
                return Results.BadRequest("Załączniki można dodawać tylko w statusie 'W pracy'.");

            var attachmentId = Guid.NewGuid();
            var extension = Path.GetExtension(file.FileName);
            var attachmentDir = Path.Combine(storageRoot, "attachments", itemId.ToString());
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

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

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

            return Results.Created($"/api/attachments/{attachmentId}", new { id = attachmentId, fileName = file.FileName });
        });

        // GET /api/attachments/{id}/file — pobranie załącznika.
        app.MapGet("/api/attachments/{id:guid}/file", async (Guid id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "SELECT file_path, file_name FROM item_attachments WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return Results.NotFound();

            var path = reader.GetString(0);
            var fileName = reader.GetString(1);

            if (!File.Exists(path))
                return Results.NotFound("Plik zniknął z magazynu na dysku serwera.");

            return Results.File(path, "application/octet-stream", fileName);
        });

        // DELETE /api/attachments/{id}
        app.MapDelete("/api/attachments/{id:guid}", async (Guid id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string? filePath = null;
            await using (var selectCmd = new NpgsqlCommand(
                "SELECT ia.file_path, i.item_type, i.status FROM item_attachments ia JOIN items i ON i.id = ia.item_id WHERE ia.id = @id;", conn))
            {
                selectCmd.Parameters.AddWithValue("id", id);
                await using var reader = await selectCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.NotFound();

                filePath = reader.GetString(0);
                var itemType = reader.GetString(1);
                var status = reader.IsDBNull(2) ? null : reader.GetString(2);
                if (ItemEndpoints.IsLocked(itemType, status))
                    return Results.BadRequest("Załączniki można usuwać tylko w statusie 'W pracy'.");
            }

            await using (var deleteCmd = new NpgsqlCommand("DELETE FROM item_attachments WHERE id = @id;", conn))
            {
                deleteCmd.Parameters.AddWithValue("id", id);
                await deleteCmd.ExecuteNonQueryAsync();
            }

            try { File.Delete(filePath); } catch (IOException) { /* magazyn i tak jest sierocy — nie blokujemy usunięcia rekordu */ }

            return Results.Ok();
        });
    }
}
