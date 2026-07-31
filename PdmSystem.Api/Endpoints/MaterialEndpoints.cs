using Npgsql;

static class MaterialEndpoints
{
    public static void MapMaterialEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/materials — katalog materiałów do wyboru w Części. "group" to pole czysto
        // porządkowe/filtrujące w tym katalogu — nigdy nie trafia do właściwości Części.
        app.MapGet("/api/materials", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT name, group_name FROM materials ORDER BY name;", conn);
            var materials = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                materials.Add(new
                {
                    name = reader.GetString(0),
                    group = reader.IsDBNull(1) ? null : reader.GetString(1)
                });
            }

            return Results.Ok(materials);
        });

        // POST /api/materials   body: { "name": "Stal S235", "group": "Stale" }
        app.MapPost("/api/materials", async (MaterialRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa materiału nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                INSERT INTO materials (name, group_name) VALUES (@name, @group)
                ON CONFLICT (name) DO UPDATE SET group_name = EXCLUDED.group_name;
                """;
            string? group = string.IsNullOrWhiteSpace(body.Group) ? null : body.Group.Trim();

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("group", (object?)group ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // DELETE /api/materials/{name}
        app.MapDelete("/api/materials/{name}", async (string name) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand("DELETE FROM materials WHERE name = @name;", conn);
            cmd.Parameters.AddWithValue("name", name);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });
    }
}

record MaterialRequest(string Name, string? Group);
