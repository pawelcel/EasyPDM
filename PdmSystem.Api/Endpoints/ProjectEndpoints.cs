using Npgsql;

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
                SELECT p.id, p.name, p.description, p.created_at, COUNT(i.id) AS item_count
                FROM projects p
                LEFT JOIN items i ON i.project_id = p.id
                GROUP BY p.id
                ORDER BY p.name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    id = reader.GetGuid(0),
                    name = reader.GetString(1),
                    description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    createdAt = reader.GetDateTime(3),
                    itemCount = reader.GetInt64(4)
                });
            }
            return Results.Ok(result);
        });

        // POST /api/projects   body: { "name": "...", "description": "..." }
        app.MapPost("/api/projects", async (ProjectRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa projektu nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                INSERT INTO projects (name, description) VALUES (@name, @description)
                RETURNING id, name, description, created_at;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("description", (object?)body.Description ?? DBNull.Value);

            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                await reader.ReadAsync();
                return Results.Ok(new
                {
                    id = reader.GetGuid(0),
                    name = reader.GetString(1),
                    description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    createdAt = reader.GetDateTime(3),
                    itemCount = 0
                });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Projekt o tej nazwie już istnieje.");
            }
        });
    }
}

record ProjectRequest(string Name, string? Description);
