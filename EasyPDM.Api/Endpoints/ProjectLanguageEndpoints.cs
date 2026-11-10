using System.Text.RegularExpressions;
using Npgsql;

// Języki projektu (Ustawienia -> Język -> Języki projektu) -- zob. migracja 063. Listę widzi
// każdy zalogowany (będzie z niej wybierał), zmienia ją tylko administrator, bo to ustawienie
// całej firmy, a nie preferencja jednej osoby. Jeden język jest domyślnym językiem
// dokumentacji; domyślnego nie da się usunąć, więc lista nigdy nie zostaje bez niego.
static partial class ProjectLanguageEndpoints
{
    public static void MapProjectLanguageEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/project-languages — cała lista, alfabetycznie po nazwie.
        app.MapGet("/api/project-languages", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT id, name, code, is_default FROM project_languages ORDER BY lower(name);", conn);
            var languages = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                languages.Add(new
                {
                    id = reader.GetInt32(0),
                    name = reader.GetString(1),
                    code = reader.GetString(2),
                    isDefault = reader.GetBoolean(3),
                });

            return Results.Ok(languages);
        });

        // POST /api/project-languages   body: { "name": "Polski", "code": "PL" }
        app.MapPost("/api/project-languages", async (ProjectLanguageRequest body, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();
            var (name, code, invalid) = Normalize(body);
            if (invalid is not null)
                return invalid;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            // Pierwszy język na pustej liście od razu zostaje domyślnym (lista startuje z English,
            // więc to tylko zabezpieczenie dla bazy, w której domyślnego z jakiegoś powodu brak).
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO project_languages (name, code, is_default)
                VALUES (@name, @code, NOT EXISTS (SELECT 1 FROM project_languages WHERE is_default))
                RETURNING id;
                """, conn);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("code", code);
            try
            {
                var id = (int)(await cmd.ExecuteScalarAsync())!;
                return Results.Created($"/api/project-languages/{id}", new { id });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Conflict();
            }
        });

        // PATCH /api/project-languages/{id}   body jak przy POST.
        app.MapPatch("/api/project-languages/{id:int}", async (int id, ProjectLanguageRequest body, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();
            var (name, code, invalid) = Normalize(body);
            if (invalid is not null)
                return invalid;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE project_languages SET name = @name, code = @code WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("code", code);
            try
            {
                return await cmd.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.Ok();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Conflict();
            }
        });

        // PUT /api/project-languages/{id}/default — ustawia domyślny język dokumentacji. Zdjęcie
        // flagi z poprzedniego i nadanie nowemu w jednej transakcji: częściowy indeks unikalny
        // nie dopuszcza dwóch domyślnych, a w razie błędu nie zostajemy bez żadnego.
        app.MapPut("/api/project-languages/{id:int}/default", async (int id, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            await using (var exists = new NpgsqlCommand("SELECT 1 FROM project_languages WHERE id = @id;", conn, tx))
            {
                exists.Parameters.AddWithValue("id", id);
                if (await exists.ExecuteScalarAsync() is null)
                    return Results.NotFound();
            }
            await using (var clear = new NpgsqlCommand(
                "UPDATE project_languages SET is_default = false WHERE is_default AND id <> @id;", conn, tx))
            {
                clear.Parameters.AddWithValue("id", id);
                await clear.ExecuteNonQueryAsync();
            }
            await using (var set = new NpgsqlCommand("UPDATE project_languages SET is_default = true WHERE id = @id;", conn, tx))
            {
                set.Parameters.AddWithValue("id", id);
                await set.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return Results.Ok();
        });

        // DELETE /api/project-languages/{id} — poza domyślnym: najpierw trzeba wybrać inny.
        app.MapDelete("/api/project-languages/{id:int}", async (int id, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using (var check = new NpgsqlCommand("SELECT is_default FROM project_languages WHERE id = @id;", conn))
            {
                check.Parameters.AddWithValue("id", id);
                var isDefault = await check.ExecuteScalarAsync();
                if (isDefault is null)
                    return Results.NotFound();
                if ((bool)isDefault)
                    return Results.BadRequest("Nie można usunąć domyślnego języka dokumentacji — najpierw wybierz inny.");
            }

            await using var cmd = new NpgsqlCommand("DELETE FROM project_languages WHERE id = @id AND NOT is_default;", conn);
            cmd.Parameters.AddWithValue("id", id);
            return await cmd.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.Ok();
        });
    }

    // Skrót przychodzi tak, jak go wpisano: "pl " to wciąż PL. Wielkie litery wymusza i tak
    // CHECK w bazie, ale tu dostajemy czytelny błąd 400 zamiast 500.
    private static (string Name, string Code, IResult? Invalid) Normalize(ProjectLanguageRequest body)
    {
        var name = (body.Name ?? "").Trim();
        var code = (body.Code ?? "").Trim().ToUpperInvariant();
        if (name.Length == 0)
            return (name, code, Results.BadRequest("Nazwa języka nie może być pusta."));
        if (!CodePattern().IsMatch(code))
            return (name, code, Results.BadRequest("Skrót języka to od 1 do 3 liter A-Z."));
        return (name, code, null);
    }

    [GeneratedRegex("^[A-Z]{1,3}$")]
    private static partial Regex CodePattern();

    private static IResult Conflict() => Results.Conflict("Język o tej nazwie albo tym skrócie już istnieje.");

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);
}

record ProjectLanguageRequest(string? Name, string? Code);
