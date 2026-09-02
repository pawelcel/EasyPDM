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
            // inaczej nikt nie mógłby już zarządzać użytkownikami. Warunek w WHERE (nie osobne
            // sprawdzenie przed UPDATE-em) zamyka wyścig: dwóch adminów degradujących się
            // niemal jednocześnie (albo degradujący się nawzajem) czytaliby "liczba adminów"
            // z osobnych zapytań chwilę wcześniej i oboje mogliby przejść starą, nieatomową
            // wersję tego sprawdzenia, zostawiając system bez żadnego administratora.
            // "@role IS DISTINCT FROM 'user'" (NULL-safe) — bez tego NULL (czyli "nie zmieniaj
            // roli") w porównaniu z 'user' dawałoby SQL NULL zamiast true, więc zwykłe zmiany
            // nazwy/e-maila/hasła (bez dotykania roli) byłyby błędnie blokowane dla adminów.
            const string sql = """
                UPDATE users SET
                    display_name = COALESCE(@displayName, display_name),
                    email = CASE WHEN @emailProvided THEN @email ELSE email END,
                    role = COALESCE(@role, role),
                    password_hash = COALESCE(@hash, password_hash)
                WHERE id = @id
                  AND (@role IS DISTINCT FROM 'user' OR role <> 'admin'
                       OR (SELECT COUNT(*) FROM users WHERE role = 'admin') > 1);
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

            if (affected == 0)
            {
                // 0 wierszy — albo user nie istnieje, albo (gdy body.Role == "user") to był
                // ostatni admin. Osobny odczyt tylko po to, żeby zwrócić właściwy komunikat.
                await using var existsCmd = new NpgsqlCommand("SELECT 1 FROM users WHERE id = @id;", conn);
                existsCmd.Parameters.AddWithValue("id", id);
                if (await existsCmd.ExecuteScalarAsync() is not null)
                    return Results.BadRequest("Nie można odebrać roli administratora ostatniemu adminowi.");
                return Results.NotFound();
            }

            // Pomiń, gdy admin zmienia WŁASNE hasło tędy — nie ma sensu informować kogoś o
            // czymś, co sam właśnie zrobił.
            var actor = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!string.IsNullOrEmpty(body.Password) && actor.Id != id)
                await Notifications.NotifyAsync(conn, app.Logger, id, "password_changed", new { });

            return Results.Ok();
        });

        // DELETE /api/users/{id}
        app.MapDelete("/api/users/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // Atomowe — ten sam wyścig i to samo uzasadnienie co w PATCH powyżej (zob.
            // komentarz tam): warunek "czy to ostatni admin" musi siedzieć w samym WHERE, nie
            // w osobnym sprawdzeniu przed DELETE.
            const string sql = """
                DELETE FROM users
                WHERE id = @id
                  AND (role <> 'admin' OR (SELECT COUNT(*) FROM users WHERE role = 'admin') > 1);
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            var affected = await cmd.ExecuteNonQueryAsync();

            if (affected > 0)
                return Results.Ok();

            await using var existsCmd = new NpgsqlCommand("SELECT 1 FROM users WHERE id = @id;", conn);
            existsCmd.Parameters.AddWithValue("id", id);
            if (await existsCmd.ExecuteScalarAsync() is not null)
                return Results.BadRequest("Nie można usunąć ostatniego administratora.");
            return Results.NotFound();
        });
    }

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);
}

record CreateUserRequest(string Username, string Password, string DisplayName, string? Email, string Role);
record UpdateUserRequest(string? DisplayName, string? Email, string? Role, string? Password);
