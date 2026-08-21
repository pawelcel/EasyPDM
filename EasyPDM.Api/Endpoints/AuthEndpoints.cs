using System.Security.Cryptography;
using Npgsql;

// Logowanie (nazwa użytkownika + hasło) i sesje. Middleware w Program.cs wymaga poprawnej
// sesji dla WSZYSTKICH ścieżek /api/*, poza /api/auth/login — więc każdy inny endpoint w tym
// API jest automatycznie chroniony, bez potrzeby dopisywania sprawdzenia w każdym z osobna.
static class AuthEndpoints
{
    private const string CookieName = "pdm_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public static void MapAuthEndpoints(this WebApplication app, string connectionString)
    {
        // POST /api/auth/login   body: { "username": "...", "password": "..." }
        app.MapPost("/api/auth/login", async (HttpContext ctx, LoginRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
                return Results.BadRequest("Podaj nazwę użytkownika i hasło.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            CurrentUser user;
            string passwordHash;
            await using (var cmd = new NpgsqlCommand(
                "SELECT id, username, display_name, role, password_hash FROM users WHERE username = @username;", conn))
            {
                cmd.Parameters.AddWithValue("username", body.Username);
                await using var reader = await cmd.ExecuteReaderAsync();
                // Ten sam komunikat dla złego loginu i złego hasła — nie zdradzamy, które z nich.
                if (!await reader.ReadAsync())
                    return Results.BadRequest("Nieprawidłowa nazwa użytkownika lub hasło.");
                user = new CurrentUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                passwordHash = reader.GetString(4);
            }

            if (!PasswordHasher.Verify(body.Password, passwordHash))
                return Results.BadRequest("Nieprawidłowa nazwa użytkownika lub hasło.");

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiresAt = DateTime.UtcNow.Add(SessionLifetime);

            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO sessions (token, user_id, expires_at) VALUES (@token, @userId, @expiresAt);", conn))
            {
                cmd.Parameters.AddWithValue("token", token);
                cmd.Parameters.AddWithValue("userId", user.Id);
                cmd.Parameters.AddWithValue("expiresAt", expiresAt);
                await cmd.ExecuteNonQueryAsync();
            }

            ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Expires = expiresAt,
                Path = "/",
            });

            // sessionToken w treści odpowiedzi (obok ciasteczka) — makro SolidWorks (VBA,
            // MSXML2.XMLHTTP.6.0) nie ma pewnego dostępu do nagłówka Set-Cookie przy
            // logowaniu (znany problem komponentów COM/WinHTTP, filtrujących ten nagłówek
            // ze względów bezpieczeństwa), więc czyta token wprost stąd zamiast z ciasteczka.
            // Ten sam token i tak już trafia do klienta przez ciasteczko — brak dodatkowej
            // ekspozycji.
            return Results.Ok(new
            {
                id = user.Id,
                username = user.Username,
                displayName = user.DisplayName,
                role = user.Role,
                sessionToken = token,
            });
        });

        // POST /api/auth/logout
        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            if (ctx.Request.Cookies.TryGetValue(CookieName, out var token) && !string.IsNullOrEmpty(token))
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("DELETE FROM sessions WHERE token = @token;", conn);
                cmd.Parameters.AddWithValue("token", token);
                await cmd.ExecuteNonQueryAsync();
            }

            ctx.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
            return Results.Ok();
        });

        // GET /api/auth/me — middleware już zagwarantował, że jest zalogowany (inaczej 401
        // zanim ten handler w ogóle by się wykonał).
        app.MapGet("/api/auth/me", (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            return Results.Ok(ToPublicUser(user));
        });

        // GET /api/auth/browser-login?token=...&redirect=...   most token -> ciasteczko dla
        // przeglądarki otwartej przez makro CAD (które ma już własną sesję zapisaną lokalnie,
        // np. w preferencjach FreeCAD) — pozwala otworzyć przeglądarkę systemową od razu
        // zalogowaną, bez ponownego wpisywania hasła. Token to ten sam token sesji, który
        // makro i tak już trzyma jako sekret (por. sessionToken w /auth/login) — most nie
        // wprowadza nowego sekretu, tylko przenosi istniejący z jednego klienta HTTP do
        // drugiego. Wyjątek od middleware w Program.cs (musi działać BEZ ciasteczka).
        app.MapGet("/api/auth/browser-login", async (HttpContext ctx, string? token, string? redirect) =>
        {
            var target = SanitizeRedirect(redirect);

            if (!string.IsNullOrEmpty(token))
            {
                var session = await ValidateToken(connectionString, token);
                if (session is not null)
                {
                    ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
                    {
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax,
                        Expires = session.Value.ExpiresAt,
                        Path = "/",
                    });
                }
            }

            return Results.Redirect(target);
        });

        // PATCH /api/auth/password   body: { "currentPassword": "...", "newPassword": "..." }
        // Każdy zalogowany użytkownik może zmienić WŁASNE hasło (potrzebne zwłaszcza dla
        // domyślnego konta administratora, zasianego z hasłem "admin" przy pierwszym starcie).
        app.MapPatch("/api/auth/password", async (HttpContext ctx, ChangePasswordRequest body) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            if (string.IsNullOrWhiteSpace(body.NewPassword))
                return Results.BadRequest("Nowe hasło nie może być puste.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string currentHash;
            await using (var cmd = new NpgsqlCommand("SELECT password_hash FROM users WHERE id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("id", user.Id);
                currentHash = (string)(await cmd.ExecuteScalarAsync())!;
            }

            if (!PasswordHasher.Verify(body.CurrentPassword, currentHash))
                return Results.BadRequest("Obecne hasło jest nieprawidłowe.");

            var newHash = PasswordHasher.Hash(body.NewPassword);
            await using (var cmd = new NpgsqlCommand("UPDATE users SET password_hash = @hash WHERE id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("hash", newHash);
                cmd.Parameters.AddWithValue("id", user.Id);
                await cmd.ExecuteNonQueryAsync();
            }

            return Results.Ok();
        });
    }

    private static object ToPublicUser(CurrentUser user) => new
    {
        id = user.Id,
        username = user.Username,
        displayName = user.DisplayName,
        role = user.Role,
    };

    // Wywoływane przez middleware w Program.cs dla każdego żądania /api/* (poza login) —
    // odczytuje ciasteczko sesji i zwraca zalogowanego użytkownika albo null.
    public static async Task<CurrentUser?> GetCurrentUser(HttpContext context, string connectionString)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var token) || string.IsNullOrEmpty(token))
            return null;

        var session = await ValidateToken(connectionString, token);
        return session?.User;
    }

    // Współdzielone przez GetCurrentUser (ciasteczko) i /auth/browser-login (token z URL) —
    // ta sama walidacja tokenu sesji, tylko dwa różne miejsca, skąd token przychodzi.
    private static async Task<(CurrentUser User, DateTime ExpiresAt)?> ValidateToken(string connectionString, string token)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = """
            SELECT u.id, u.username, u.display_name, u.role, s.expires_at
            FROM sessions s
            JOIN users u ON u.id = s.user_id
            WHERE s.token = @token AND s.expires_at > now();
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("token", token);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var user = new CurrentUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        return (user, reader.GetDateTime(4));
    }

    // Chroni /auth/browser-login przed open-redirect: apka nie ma routera, więc jedyne
    // sensowne wartości to "/" albo "/?...". Odrzuca "//evil.com" i "/\evil.com" (niektóre
    // przeglądarki traktują wsteczny ukośnik jak "/", czyniąc go protocol-relative) oraz
    // wszystko z ":" w pierwszym segmencie (np. "/javascript:..."). Nieprawidłowa wartość
    // -> fail closed do "/", nie błąd — logowanie ma się udać, tylko bez kontynuacji.
    private static string SanitizeRedirect(string? redirect)
    {
        if (string.IsNullOrEmpty(redirect) || redirect[0] != '/')
            return "/";
        if (redirect.Length > 1 && (redirect[1] == '/' || redirect[1] == '\\'))
            return "/";

        var end = redirect.IndexOfAny(['/', '?'], 1);
        var firstSegment = end < 0 ? redirect[1..] : redirect[1..end];
        return firstSegment.Contains(':') ? "/" : redirect;
    }

    // Współdzielone przez wszystkie endpointy, które chcą sprawdzić "czy to admin" — zamiast
    // każdy plik miał swoją własną kopię tej samej jednolinijkowej metody.
    public static bool IsAdmin(HttpContext ctx) => (ctx.Items["CurrentUser"] as CurrentUser)?.Role == "admin";
}

record CurrentUser(Guid Id, string Username, string DisplayName, string Role);
record LoginRequest(string Username, string Password);
record ChangePasswordRequest(string CurrentPassword, string NewPassword);
