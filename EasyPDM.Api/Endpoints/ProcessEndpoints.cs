using Npgsql;

// Katalog obróbek (menu "Obróbka"). Element wykonywany wybiera z niego obróbki i zapisuje je
// w properties.processes jako {id, name, description} -- zob. migracje 061 i 062. Nazwa nie
// jest unikalna: "Hartowanie" może mieć kilka wariantów różniących się opisem.
//
// Nazwa i opis są w process_texts osobno dla każdego języka dokumentacji (migracja 065).
// Katalog pokazuje i edytuje wersję w domyślnym języku dokumentacji; obróbka bez tłumaczenia
// na ten język pokazuje tekst z innego (languageId mówi, z którego). Pozostałe języki
// uzupełnia okno tłumaczeń przez PUT /api/processes/{id}/texts.
static class ProcessEndpoints
{
    public static void MapProcessEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/processes — cały katalog, alfabetycznie po pokazywanej nazwie, z tekstami we
        // wszystkich językach.
        app.MapGet("/api/processes", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var texts = new Dictionary<int, List<ProcessText>>();
            await using (var textCmd = new NpgsqlCommand("""
                SELECT t.process_id, t.language_id, t.name, t.description
                FROM process_texts t JOIN project_languages l ON l.id = t.language_id
                ORDER BY l.is_default DESC, lower(l.name);
                """, conn))
            await using (var reader = await textCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var processId = reader.GetInt32(0);
                    if (!texts.TryGetValue(processId, out var list))
                        texts[processId] = list = [];
                    list.Add(new ProcessText(reader.GetInt32(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
                }
            }

            var processes = new List<ProcessDto>();
            await using (var cmd = new NpgsqlCommand("SELECT id, for_parts, for_assemblies FROM processes;", conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var id = reader.GetInt32(0);
                    var own = texts.GetValueOrDefault(id) ?? [];
                    // Pierwszy tekst to ten w domyślnym języku, jeśli jest (kolejność zapytania wyżej).
                    var shown = own.FirstOrDefault();
                    processes.Add(new ProcessDto(id, shown?.Name ?? "", shown?.Description, shown?.LanguageId,
                        reader.GetBoolean(1), reader.GetBoolean(2), own));
                }
            }

            return Results.Ok(processes.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
        });

        // POST /api/processes   body: { "name": "Spawanie", "description": "...", "forParts": false, "forAssemblies": true }
        // Nazwa i opis zapisują się w domyślnym języku dokumentacji.
        app.MapPost("/api/processes", async (ProcessRequest body) =>
        {
            var invalid = Validate(body);
            if (invalid is not null)
                return invalid;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            int id;
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO processes (for_parts, for_assemblies) VALUES (@forParts, @forAssemblies) RETURNING id;", conn, tx))
            {
                cmd.Parameters.AddWithValue("forParts", body.ForParts);
                cmd.Parameters.AddWithValue("forAssemblies", body.ForAssemblies);
                id = (int)(await cmd.ExecuteScalarAsync())!;
            }
            await UpsertDefaultTextAsync(conn, tx, id, body);
            await tx.CommitAsync();
            return Results.Created($"/api/processes/{id}", new { id });
        });

        // PATCH /api/processes/{id}   body jak przy POST — zmienia tekst w domyślnym języku
        // i zakres (części/złożenia); tłumaczenia na inne języki zostają.
        // Nie przepisuje elementów: każdy trzyma kopię nazwy i opisu z chwili wyboru, więc
        // element wydany zostaje dokładnie z tą specyfikacją, z którą go wydano.
        app.MapPatch("/api/processes/{id:int}", async (int id, ProcessRequest body) =>
        {
            var invalid = Validate(body);
            if (invalid is not null)
                return invalid;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await using (var cmd = new NpgsqlCommand(
                "UPDATE processes SET for_parts = @forParts, for_assemblies = @forAssemblies WHERE id = @id;", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("forParts", body.ForParts);
                cmd.Parameters.AddWithValue("forAssemblies", body.ForAssemblies);
                if (await cmd.ExecuteNonQueryAsync() == 0)
                    return Results.NotFound();
            }
            await UpsertDefaultTextAsync(conn, tx, id, body);
            await tx.CommitAsync();
            return Results.Ok();
        });

        // PUT /api/processes/{id}/texts   body: { "texts": [{ "languageId": 2, "name": "...", "description": "..." }] }
        // Okno tłumaczeń: zastępuje teksty we wszystkich językach naraz. Język z pustą nazwą
        // znika (brak tłumaczenia), ale co najmniej jeden musi zostać — obróbka bez nazwy
        // nie miałaby jak się pokazać.
        app.MapPut("/api/processes/{id:int}/texts", async (int id, ProcessTextsRequest body) =>
        {
            var texts = (body.Texts ?? [])
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .GroupBy(t => t.LanguageId).Select(g => g.Last())
                .ToList();
            if (texts.Count == 0)
                return Results.BadRequest("Obróbka musi mieć nazwę w co najmniej jednym języku.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using (var check = new NpgsqlCommand("SELECT COUNT(*) FROM project_languages WHERE id = ANY(@ids);", conn))
            {
                check.Parameters.AddWithValue("ids", texts.Select(t => t.LanguageId).ToArray());
                if ((long)(await check.ExecuteScalarAsync())! != texts.Count)
                    return Results.BadRequest("Wybrano język, którego nie ma na liście języków dokumentacji.");
            }

            await using var tx = await conn.BeginTransactionAsync();
            await using (var exists = new NpgsqlCommand("SELECT 1 FROM processes WHERE id = @id;", conn, tx))
            {
                exists.Parameters.AddWithValue("id", id);
                if (await exists.ExecuteScalarAsync() is null)
                    return Results.NotFound();
            }
            await using (var clear = new NpgsqlCommand("DELETE FROM process_texts WHERE process_id = @id;", conn, tx))
            {
                clear.Parameters.AddWithValue("id", id);
                await clear.ExecuteNonQueryAsync();
            }
            foreach (var t in texts)
            {
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO process_texts (process_id, language_id, name, description)
                    VALUES (@id, @languageId, @name, @description);
                    """, conn, tx);
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("languageId", t.LanguageId);
                insert.Parameters.AddWithValue("name", t.Name!.Trim());
                insert.Parameters.AddWithValue("description",
                    string.IsNullOrWhiteSpace(t.Description) ? DBNull.Value : t.Description.Trim());
                await insert.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return Results.Ok();
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

    private static async Task UpsertDefaultTextAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int id, ProcessRequest body)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO process_texts (process_id, language_id, name, description)
            VALUES (@id, default_project_language_id(), @name, @description)
            ON CONFLICT (process_id, language_id) DO UPDATE SET name = EXCLUDED.name, description = EXCLUDED.description;
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", body.Name.Trim());
        cmd.Parameters.AddWithValue("description",
            string.IsNullOrWhiteSpace(body.Description) ? DBNull.Value : body.Description.Trim());
        await cmd.ExecuteNonQueryAsync();
    }

    private static IResult? Validate(ProcessRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Name))
            return Results.BadRequest("Nazwa obróbki nie może być pusta.");
        if (!body.ForParts && !body.ForAssemblies)
            return Results.BadRequest("Obróbka musi dotyczyć części, złożeń albo obu.");
        return null;
    }

    private sealed record ProcessText(int LanguageId, string Name, string? Description);

    private sealed record ProcessDto(int Id, string Name, string? Description, int? LanguageId,
        bool ForParts, bool ForAssemblies, List<ProcessText> Texts);
}

record ProcessRequest(string Name, string? Description, bool ForParts, bool ForAssemblies);

record ProcessTextRequest(int LanguageId, string? Name, string? Description);

record ProcessTextsRequest(ProcessTextRequest[]? Texts);
