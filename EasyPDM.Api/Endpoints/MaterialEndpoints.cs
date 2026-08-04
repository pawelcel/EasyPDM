using Npgsql;

static class MaterialEndpoints
{
    public static void MapMaterialEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/materials — katalog materiałów do wyboru w Części. "group"/"subgroup" to
        // pola czysto porządkowe/filtrujące w tym katalogu — nigdy nie trafiają do właściwości
        // Części (Część zapisuje tylko nazwę materiału jako wolny tekst).
        app.MapGet("/api/materials", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT id, name, group_name, subgroup_name FROM materials ORDER BY name;", conn);
            var materials = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                materials.Add(new
                {
                    id = reader.GetInt32(0),
                    name = reader.GetString(1),
                    group = reader.IsDBNull(2) ? null : reader.GetString(2),
                    subgroup = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }

            return Results.Ok(materials);
        });

        // POST /api/materials   body: { "name": "Stal S235", "group": "Stal", "subgroup": "Węglowe" }
        app.MapPost("/api/materials", async (MaterialRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa materiału nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                INSERT INTO materials (name, group_name, subgroup_name)
                VALUES (@name, @group, @subgroup)
                RETURNING id;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("group", (object?)NullIfBlank(body.Group) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("subgroup", (object?)NullIfBlank(body.Subgroup) ?? DBNull.Value);

            try
            {
                var id = (int)(await cmd.ExecuteScalarAsync())!;
                return Results.Created($"/api/materials/{id}", new { id });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Materiał o tej nazwie już istnieje.");
            }
        });

        // PATCH /api/materials/{id}   body: { "name": "...", "group": "...", "subgroup": "..." }
        app.MapPatch("/api/materials/{id:int}", async (int id, MaterialRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa materiału nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                UPDATE materials SET name = @name, group_name = @group, subgroup_name = @subgroup
                WHERE id = @id;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("group", (object?)NullIfBlank(body.Group) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("subgroup", (object?)NullIfBlank(body.Subgroup) ?? DBNull.Value);

            try
            {
                var affected = await cmd.ExecuteNonQueryAsync();
                return affected == 0 ? Results.NotFound() : Results.Ok();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Materiał o tej nazwie już istnieje.");
            }
        });

        // DELETE /api/materials/{id}
        app.MapDelete("/api/materials/{id:int}", async (int id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand("DELETE FROM materials WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

record MaterialRequest(string Name, string? Group, string? Subgroup);
