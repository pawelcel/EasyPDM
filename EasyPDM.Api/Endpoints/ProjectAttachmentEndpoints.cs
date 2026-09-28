using Npgsql;

// ZAŁĄCZNIKI PROJEKTU — dokumenty dotyczące całego zlecenia, nie pojedynczej Części:
// oferta, potwierdzenie przyjęcia zlecenia i wszystko inne, co przychodzi "do projektu".
//
// W odróżnieniu od załączników elementu (AttachmentEndpoints) nie ma tu blokad statusu ani
// właściciela — projekt nie ma ani jednego, ani drugiego. Jedyną strażą jest dostęp do
// samego projektu (project_users), i to po OBU stronach: także przy odczycie, bo lista
// ofert/zleceń mówi o warunkach handlowych, a nie jest — jak "Cała baza" — świadomie
// otwartym katalogiem części.
static class ProjectAttachmentEndpoints
{
    private static readonly string[] Roles = ["oferta", "zlecenie"];

    public static void MapProjectAttachmentEndpoints(this WebApplication app, string connectionString, StorageSettings storage)
    {
        // GET /api/projects/{projectId}/attachments
        app.MapGet("/api/projects/{projectId:guid}/attachments", async (Guid projectId, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            const string sql = """
                SELECT a.id, a.file_name, a.file_size, a.role, a.uploaded_at, u.display_name
                FROM project_attachments a
                LEFT JOIN users u ON u.id = a.uploaded_by
                WHERE a.project_id = @projectId
                ORDER BY a.uploaded_at;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", projectId);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    id = reader.GetGuid(0),
                    fileName = reader.GetString(1),
                    fileSize = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2),
                    role = reader.IsDBNull(3) ? null : reader.GetString(3),
                    uploadedAt = reader.GetDateTime(4),
                    uploadedBy = reader.IsDBNull(5) ? null : reader.GetString(5),
                });
            }
            return Results.Ok(result);
        });

        // POST /api/projects/{projectId}/attachments   multipart: file (wymagane), role (opcjonalne)
        app.MapPost("/api/projects/{projectId:guid}/attachments", async (Guid projectId, HttpRequest request, HttpContext ctx) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Oczekiwano danych multipart/form-data.");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest("Brak pliku w polu 'file'.");

            // Brak roli = zwykły załącznik (trzecia, nieograniczona kategoria obok oferty
            // i potwierdzenia zlecenia).
            var role = form["role"].ToString();
            if (string.IsNullOrWhiteSpace(role))
                role = null;
            else if (!Roles.Contains(role))
                return Results.BadRequest($"Pole 'role' musi być '{string.Join("' albo '", Roles)}' albo puste.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @id;", conn))
            {
                checkCmd.Parameters.AddWithValue("id", projectId);
                if (await checkCmd.ExecuteScalarAsync() is null)
                    return Results.NotFound("Projekt nie istnieje.");
            }

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            var attachmentId = Guid.NewGuid();
            var dir = Path.Combine(storage.Path, "project-attachments", projectId.ToString());
            Directory.CreateDirectory(dir);
            var storedPath = Path.Combine(dir, $"{attachmentId}{Path.GetExtension(file.FileName)}");

            await using (var stream = File.Create(storedPath))
                await file.CopyToAsync(stream);

            const string insertSql = """
                INSERT INTO project_attachments (id, project_id, file_name, file_path, file_size, role, uploaded_by)
                VALUES (@id, @projectId, @fileName, @filePath, @size, @role, @uploadedBy);
                """;
            try
            {
                await using var cmd = new NpgsqlCommand(insertSql, conn);
                cmd.Parameters.AddWithValue("id", attachmentId);
                cmd.Parameters.AddWithValue("projectId", projectId);
                cmd.Parameters.AddWithValue("fileName", file.FileName);
                cmd.Parameters.AddWithValue("filePath", storedPath);
                cmd.Parameters.AddWithValue("size", file.Length);
                cmd.Parameters.AddWithValue("role", (object?)role ?? DBNull.Value);
                cmd.Parameters.AddWithValue("uploadedBy", user.Id);
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                // Plik bez wiersza w bazie byłby sierotą w magazynie — nie do znalezienia
                // przez aplikację, a zajmujący miejsce.
                try { File.Delete(storedPath); } catch (IOException) { }
                throw;
            }

            return Results.Created($"/api/project-attachments/{attachmentId}",
                new { id = attachmentId, fileName = file.FileName, role });
        });

        // GET /api/project-attachments/{id}/download
        app.MapGet("/api/project-attachments/{id:guid}/download", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string fileName, filePath;
            Guid projectId;
            await using (var cmd = new NpgsqlCommand(
                "SELECT file_name, file_path, project_id FROM project_attachments WHERE id = @id;", conn))
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

        // DELETE /api/project-attachments/{id}
        app.MapDelete("/api/project-attachments/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string filePath;
            Guid projectId;
            await using (var cmd = new NpgsqlCommand(
                "SELECT file_path, project_id FROM project_attachments WHERE id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.NotFound();
                filePath = reader.GetString(0);
                projectId = reader.GetGuid(1);
            }

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            await using (var cmd = new NpgsqlCommand("DELETE FROM project_attachments WHERE id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            try { File.Delete(filePath); } catch (IOException) { }

            return Results.Ok();
        });
    }
}
