using Npgsql;

// Zarządzanie kontami — wyłącznie dla administratora. Zwykły użytkownik (rola "user") nie
// może dodawać/edytować/usuwać kont — sprawdzane tutaj przez IsAdmin, niezależnie od tego,
// że middleware w Program.cs już wymaga samego zalogowania na każdej ścieżce /api/*.
static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/users
        app.MapGet("/api/users", async (HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "SELECT id, username, display_name, email, role FROM users ORDER BY username;";
            await using var cmd = new NpgsqlCommand(sql, conn);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    id = reader.GetGuid(0),
                    username = reader.GetString(1),
                    displayName = reader.GetString(2),
                    email = reader.IsDBNull(3) ? null : reader.GetString(3),
                    role = reader.GetString(4),
                });
            }

            return Results.Ok(result);
        });

        // POST /api/users   body: { username, password, displayName, email?, role }
        app.MapPost("/api/users", async (HttpContext ctx, CreateUserRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            if (string.IsNullOrWhiteSpace(body.Username))
                return Results.BadRequest("Nazwa użytkownika jest wymagana.");
            if (string.IsNullOrWhiteSpace(body.Password))
                return Results.BadRequest("Hasło jest wymagane.");
            if (string.IsNullOrWhiteSpace(body.DisplayName))
                return Results.BadRequest("Wyświetlana nazwa jest wymagana.");
            if (body.Role != "admin" && body.Role != "user")
                return Results.BadRequest("Nieprawidłowa rola — dozwolone: 'admin', 'user'.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var userId = Guid.NewGuid();
            const string sql = """
                INSERT INTO users (id, username, display_name, email, password_hash, role)
                VALUES (@id, @username, @displayName, @email, @hash, @role);
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("username", body.Username.Trim());
            cmd.Parameters.AddWithValue("displayName", body.DisplayName.Trim());
            cmd.Parameters.AddWithValue("email", (object?)body.Email?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("hash", PasswordHasher.Hash(body.Password));
            cmd.Parameters.AddWithValue("role", body.Role);

            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (PostgresException e) when (e.SqlState == "23505")
            {
                return Results.Conflict("Użytkownik o tej nazwie już istnieje.");
            }

            return Results.Created($"/api/users/{userId}", new { id = userId });
        });

        // PATCH /api/users/{id}   body: { displayName?, email?, role?, password? }
        app.MapPatch("/api/users/{id:guid}", async (Guid id, HttpContext ctx, UpdateUserRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();
            if (body.Role is not null && body.Role != "admin" && body.Role != "user")
                return Results.BadRequest("Nieprawidłowa rola — dozwolone: 'admin', 'user'.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // Nie pozwalamy odebrać roli administratora ostatniemu adminowi w systemie —
            // inaczej nikt nie mógłby już zarządzać użytkownikami.
            if (body.Role == "user" && await IsLastAdmin(conn, id))
                return Results.BadRequest("Nie można odebrać roli administratora ostatniemu adminowi.");

            const string sql = """
                UPDATE users SET
                    display_name = COALESCE(@displayName, display_name),
                    email = CASE WHEN @emailProvided THEN @email ELSE email END,
                    role = COALESCE(@role, role),
                    password_hash = COALESCE(@hash, password_hash)
                WHERE id = @id;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("displayName", (object?)body.DisplayName?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("emailProvided", body.Email is not null);
            cmd.Parameters.AddWithValue("email", (object?)body.Email?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("role", (object?)body.Role ?? DBNull.Value);
            cmd.Parameters.AddWithValue(
                "hash", string.IsNullOrEmpty(body.Password) ? DBNull.Value : PasswordHasher.Hash(body.Password));
            var affected = await cmd.ExecuteNonQueryAsync();

            return affected == 0 ? Results.NotFound() : Results.Ok();
        });

        // DELETE /api/users/{id}
        app.MapDelete("/api/users/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (await IsLastAdmin(conn, id))
                return Results.BadRequest("Nie można usunąć ostatniego administratora.");

            await using var cmd = new NpgsqlCommand("DELETE FROM users WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("id", id);
            var affected = await cmd.ExecuteNonQueryAsync();

            return affected == 0 ? Results.NotFound() : Results.Ok();
        });
    }

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);

    // Sprawdza, czy usunięcie/zdegradowanie danego użytkownika zostawiłoby system bez
    // ŻADNEGO administratora — tylko wtedy, gdy TEN użytkownik sam jest adminem.
    private static async Task<bool> IsLastAdmin(NpgsqlConnection conn, Guid userId)
    {
        // COALESCE(..., false) na pierwszym warunku -- bez tego, dla nieistniejącego @id
        // podzapytanie "role" daje SQL NULL, a "NULL = 'admin'" to NULL (nie false), więc
        // całe wyrażenie (NULL AND ...) wraca jako NULL zamiast booleana -- ExecuteScalarAsync
        // zwróciłby wtedy DBNull.Value, a rzutowanie (bool) niżej rzucałoby InvalidCastException
        // zamiast zwrócić poprawne "nie, to nie jest ostatni admin" dla nieistniejącego usera.
        const string sql = """
            SELECT COALESCE((SELECT role FROM users WHERE id = @id) = 'admin', false)
               AND (SELECT COUNT(*) FROM users WHERE role = 'admin') <= 1;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
}

record CreateUserRequest(string Username, string Password, string DisplayName, string? Email, string Role);
record UpdateUserRequest(string? DisplayName, string? Email, string? Role, string? Password);
