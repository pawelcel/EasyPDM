using Npgsql;

// Tworzenie, edycja i usuwanie projektów — wyłącznie dla administratora (ten sam wzorzec
// sprawdzania roli co w UserEndpoints/ItemEndpoints). Sam odczyt (GET) jest dostępny dla
// każdego zalogowanego użytkownika.
static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/projects — lista projektów z liczbą elementów w każdym. Administrator widzi
        // wszystkie; zwykły użytkownik tylko te, do których został przypisany (project_users) —
        // nieprzypisany projekt jest dla niego tak, jakby nie istniał (nie pojawia się na liście,
        // więc nie da się go wybrać ani przejrzeć jego struktury przez UI).
        app.MapGet("/api/projects", async (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT p.id, p.name, p.description, p.client, p.client_id, c.name, c.name2,
                       p.start_date, p.end_date, p.created_at, COUNT(i.id) AS item_count
                FROM projects p
                LEFT JOIN items i ON i.project_id = p.id
                LEFT JOIN clients c ON c.id = p.client_id
                WHERE @isAdmin OR EXISTS (
                    SELECT 1 FROM project_users pu WHERE pu.project_id = p.id AND pu.user_id = @userId
                )
                GROUP BY p.id, c.id
                ORDER BY p.name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("isAdmin", user.Role == "admin");
            cmd.Parameters.AddWithValue("userId", user.Id);
            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(ReadProject(reader));
            return Results.Ok(result);
        });

        // POST /api/projects   body: { name, description?, client?, startDate?, endDate? }
        app.MapPost("/api/projects", async (HttpContext ctx, ProjectRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa projektu nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                INSERT INTO projects (name, description, client_id, start_date, end_date)
                VALUES (@name, @description, @clientId, @startDate, @endDate)
                RETURNING id, name, description, client, client_id,
                    (SELECT name FROM clients WHERE clients.id = client_id) AS client_name,
                    (SELECT name2 FROM clients WHERE clients.id = client_id) AS client_name2,
                    start_date, end_date, created_at;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("description", (object?)body.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("clientId", (object?)body.ClientId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("startDate", (object?)body.StartDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("endDate", (object?)body.EndDate ?? DBNull.Value);

            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                await reader.ReadAsync();
                return Results.Ok(ReadProject(reader, itemCount: 0));
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Projekt o tej nazwie już istnieje.");
            }
        });

        // PATCH /api/projects/{id}   body: { name, description?, client?, startDate?, endDate? }
        app.MapPatch("/api/projects/{id:guid}", async (Guid id, HttpContext ctx, ProjectRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa projektu nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                UPDATE projects SET
                    name = @name,
                    description = @description,
                    client_id = @clientId,
                    start_date = @startDate,
                    end_date = @endDate
                WHERE id = @id
                RETURNING id, name, description, client, client_id,
                    (SELECT name FROM clients WHERE clients.id = client_id) AS client_name,
                    (SELECT name2 FROM clients WHERE clients.id = client_id) AS client_name2,
                    start_date, end_date, created_at,
                    (SELECT COUNT(*) FROM items WHERE items.project_id = projects.id);
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("description", (object?)body.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("clientId", (object?)body.ClientId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("startDate", (object?)body.StartDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("endDate", (object?)body.EndDate ?? DBNull.Value);

            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.NotFound();
                return Results.Ok(ReadProject(reader));
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Projekt o tej nazwie już istnieje.");
            }
        });

        // DELETE /api/projects/{id} — usuwa projekt razem ze WSZYSTKIMI jego elementami
        // (kaskadowo w bazie: items/item_attachments/item_relations/item_tags mają
        // ON DELETE CASCADE). Pliki fizyczne "głównych" plików elementów (items.file_path)
        // są kasowane z magazynu tak samo, jak przy pełnym usuwaniu pojedynczego elementu
        // (DELETE /api/items/{id}) — kopie załączników/rewizji nie są dziś sprzątane ani tu,
        // ani tam (znane, istniejące ograniczenie, nie nowe).
        app.MapDelete("/api/projects/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var filePaths = new List<string>();
            await using (var selectCmd = new NpgsqlCommand(
                "SELECT file_path FROM items WHERE project_id = @id AND file_path IS NOT NULL;", conn))
            {
                selectCmd.Parameters.AddWithValue("id", id);
                await using var reader = await selectCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    filePaths.Add(reader.GetString(0));
            }

            await using var deleteCmd = new NpgsqlCommand("DELETE FROM projects WHERE id = @id;", conn);
            deleteCmd.Parameters.AddWithValue("id", id);
            var affected = await deleteCmd.ExecuteNonQueryAsync();
            if (affected == 0)
                return Results.NotFound();

            foreach (var path in filePaths)
            {
                try { File.Delete(path); } catch (IOException) { /* magazyn i tak jest sierocy — nie blokujemy usunięcia rekordu */ }
            }

            return Results.Ok();
        });
    }

    private static object ReadProject(NpgsqlDataReader reader, long? itemCount = null) => new
    {
        id = reader.GetGuid(0),
        name = reader.GetString(1),
        description = reader.IsDBNull(2) ? null : reader.GetString(2),
        client = reader.IsDBNull(3) ? null : reader.GetString(3),
        clientId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
        clientName = reader.IsDBNull(5) ? null : reader.GetString(5),
        clientName2 = reader.IsDBNull(6) ? null : reader.GetString(6),
        startDate = reader.IsDBNull(7) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(7),
        endDate = reader.IsDBNull(8) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(8),
        createdAt = reader.GetDateTime(9),
        itemCount = itemCount ?? reader.GetInt64(10)
    };

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);
}

record ProjectRequest(string Name, string? Description, int? ClientId, DateOnly? StartDate, DateOnly? EndDate);
