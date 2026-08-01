using Npgsql;

// Tworzenie, edycja i usuwanie projektów — wyłącznie dla administratora (ten sam wzorzec
// sprawdzania roli co w UserEndpoints/ItemEndpoints). Sam odczyt (GET) jest dostępny dla
// każdego zalogowanego użytkownika.
static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/projects — lista projektów z liczbą elementów w każdym.
        app.MapGet("/api/projects", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT p.id, p.name, p.description, p.client, p.start_date, p.end_date,
                       p.created_at, COUNT(i.id) AS item_count
                FROM projects p
                LEFT JOIN items i ON i.project_id = p.id
                GROUP BY p.id
                ORDER BY p.name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(ReadProject(reader));
            return Results.Ok(result);
        });

        // POST /api/projects   body: { name, description?, client?, startDate?, endDate? }
        app.MapPost("/api/projects", async (HttpContext ctx, ProjectRequest body) =>
        {
            if (!IsAdmin(ctx))
                return Forbidden();
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa projektu nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                INSERT INTO projects (name, description, client, start_date, end_date)
                VALUES (@name, @description, @client, @startDate, @endDate)
                RETURNING id, name, description, client, start_date, end_date, created_at;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("description", (object?)body.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("client", (object?)body.Client ?? DBNull.Value);
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
            if (!IsAdmin(ctx))
                return Forbidden();
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa projektu nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                UPDATE projects SET
                    name = @name,
                    description = @description,
                    client = @client,
                    start_date = @startDate,
                    end_date = @endDate
                WHERE id = @id
                RETURNING id, name, description, client, start_date, end_date, created_at,
                    (SELECT COUNT(*) FROM items WHERE items.project_id = projects.id);
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("description", (object?)body.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("client", (object?)body.Client ?? DBNull.Value);
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
            if (!IsAdmin(ctx))
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
        startDate = reader.IsDBNull(4) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(4),
        endDate = reader.IsDBNull(5) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(5),
        createdAt = reader.GetDateTime(6),
        itemCount = itemCount ?? reader.GetInt64(7)
    };

    private static bool IsAdmin(HttpContext ctx) => (ctx.Items["CurrentUser"] as CurrentUser)?.Role == "admin";

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);
}

record ProjectRequest(string Name, string? Description, string? Client, DateOnly? StartDate, DateOnly? EndDate);
