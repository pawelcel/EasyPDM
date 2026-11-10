using Npgsql;

// Katalog obróbek (menu "Obróbka"). Element wykonywany wybiera z niego obróbki i zapisuje je
// w properties.processes jako {id, name, description} -- zob. migracje 061 i 062. Nazwa nie
// jest unikalna: "Hartowanie" może mieć kilka wariantów różniących się opisem.
static class ProcessEndpoints
{
    public static void MapProcessEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/processes — cały katalog, alfabetycznie.
        app.MapGet("/api/processes", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT id, name, description, for_parts, for_assemblies FROM processes ORDER BY lower(name);", conn);
            var processes = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                processes.Add(new
                {
                    id = reader.GetInt32(0),
                    name = reader.GetString(1),
                    description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    forParts = reader.GetBoolean(3),
                    forAssemblies = reader.GetBoolean(4),
                });
            }

            return Results.Ok(processes);
        });

        // POST /api/processes   body: { "name": "Spawanie", "description": "...", "forParts": false, "forAssemblies": true }
        app.MapPost("/api/processes", async (ProcessRequest body) =>
        {
            var invalid = Validate(body);
            if (invalid is not null)
                return invalid;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand("""
                INSERT INTO processes (name, description, for_parts, for_assemblies)
                VALUES (@name, @description, @forParts, @forAssemblies)
                RETURNING id;
                """, conn);
            AddParameters(cmd, body);
            var id = (int)(await cmd.ExecuteScalarAsync())!;
            return Results.Created($"/api/processes/{id}", new { id });
        });

        // PATCH /api/processes/{id}   body jak przy POST.
        // Nie przepisuje elementów: każdy trzyma kopię nazwy i opisu z chwili wyboru, więc
        // element wydany zostaje dokładnie z tą specyfikacją, z którą go wydano.
        app.MapPatch("/api/processes/{id:int}", async (int id, ProcessRequest body) =>
        {
            var invalid = Validate(body);
            if (invalid is not null)
                return invalid;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand("""
                UPDATE processes SET name = @name, description = @description,
                       for_parts = @forParts, for_assemblies = @forAssemblies
                WHERE id = @id;
                """, conn);
            cmd.Parameters.AddWithValue("id", id);
            AddParameters(cmd, body);
            var affected = await cmd.ExecuteNonQueryAsync();
            return affected == 0 ? Results.NotFound() : Results.Ok();
        });

        // DELETE /api/processes/{id} — elementy zachowują swoją kopię nazwy i opisu.
        app.MapDelete("/api/processes/{id:int}", async (int id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand("DELETE FROM processes WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok();
        });
    }

    private static IResult? Validate(ProcessRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return Results.BadRequest("Nazwa obróbki nie może być pusta.");
        if (!body.ForParts && !body.ForAssemblies)
            return Results.BadRequest("Obróbka musi dotyczyć części, złożeń albo obu.");
        return null;
    }

    private static void AddParameters(NpgsqlCommand cmd, ProcessRequest body)
    {
        cmd.Parameters.AddWithValue("name", body.Name.Trim());
        cmd.Parameters.AddWithValue("description",
            string.IsNullOrWhiteSpace(body.Description) ? DBNull.Value : body.Description.Trim());
        cmd.Parameters.AddWithValue("forParts", body.ForParts);
        cmd.Parameters.AddWithValue("forAssemblies", body.ForAssemblies);
    }
}

record ProcessRequest(string Name, string? Description, bool ForParts, bool ForAssemblies);
