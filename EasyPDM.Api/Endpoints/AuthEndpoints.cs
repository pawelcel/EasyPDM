using System.Collections.Concurrent;
using System.Security.Cryptography;
using Npgsql;

// Logowanie (nazwa użytkownika + hasło) i sesje. Middleware w Program.cs wymaga poprawnej
// sesji dla WSZYSTKICH ścieżek /api/*, poza /api/auth/login — więc każdy inny endpoint w tym
// API jest automatycznie chroniony, bez potrzeby dopisywania sprawdzenia w każdym z osobna.
static class AuthEndpoints
{
    private const string CookieName = "pdm_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    // Prosta ochrona przed brute-force na /auth/login: w pamięci procesu (jeden node
    // API, więc nie trzeba rozproszonego stanu), licznik nieudanych prób per nazwa
    // użytkownika (case-insensitive, tak jak samo pole username w bazie). Po
    // MaxFailedAttempts kolejnych porażkach z rzędu, konto jest tymczasowo blokowane na
    // LockoutDuration -- reset licznika przy udanym logowaniu. Świadomie per-username, nie
    // per-IP (atakujący łatwo rotuje IP, nazwa użytkownika jest tym, co faktycznie chroni
    // przed odgadnięciem domyślnego hasła "admin"/"admin").
    private static readonly ConcurrentDictionary<string, LoginAttemptState> FailedAttempts =
        new(StringComparer.OrdinalIgnoreCase);
    private const int MaxFailedAttempts = 10;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private sealed class LoginAttemptState
    {
        public int Count;
        public DateTime LockedUntil;
        public DateTime LastFailure;
    }

    // Powyżej tylu wpisów stare, już nieblokujące są wyrzucane — bez tego próby logowania na
    // losowe nazwy użytkowników powiększały słownik w nieskończoność.
    private const int MaxTrackedUsernames = 10_000;

    private static void RecordFailedLogin(string username)
    {
        var now = DateTime.UtcNow;
        var state = FailedAttempts.GetOrAdd(username, _ => new LoginAttemptState());
        lock (state)
        {
            // Licznik dotyczy prób "z rzędu": po przerwie dłuższej niż czas blokady zaczyna od
            // zera. Wcześniej pomyłki sumowały się bez końca i dziesiąta literówka po miesiącach
            // blokowała konto tak samo jak dziesięć prób w minutę.
            if (now - state.LastFailure > LockoutDuration)
                state.Count = 0;
            state.Count++;
            state.LastFailure = now;
            if (state.Count >= MaxFailedAttempts)
                state.LockedUntil = now.Add(LockoutDuration);
        }

        if (FailedAttempts.Count > MaxTrackedUsernames)
        {
            foreach (var (name, entry) in FailedAttempts)
            {
                bool stale;
                lock (entry) { stale = entry.LockedUntil < now && now - entry.LastFailure > LockoutDuration; }
                if (stale)
                    FailedAttempts.TryRemove(name, out _);
            }
        }
    }

    // Unieważnia sesje użytkownika po zmianie hasła, poza sesją, z której zmieniono hasło
    // (exceptToken; null = wszystkie). Bez tego przejęta sesja działała jeszcze do 30 dni mimo
    // zmiany hasła — a zmiana hasła to zwykle właśnie reakcja na podejrzenie przejęcia konta.
    public static async Task RevokeSessionsAsync(NpgsqlConnection conn, Guid userId, string? exceptToken)
    {
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM sessions WHERE user_id = @userId AND token IS DISTINCT FROM @except;", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.Add(new NpgsqlParameter("except", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)exceptToken ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync();
        // Tokeny makr też — po zmianie hasła makra logują się nowym (zob. migracja 071).
        await using var macroCmd = new NpgsqlCommand("DELETE FROM macro_tokens WHERE user_id = @userId;", conn);
        macroCmd.Parameters.AddWithValue("userId", userId);
        await macroCmd.ExecuteNonQueryAsync();
    }

    public static string? CurrentSessionToken(HttpContext ctx) =>
        ctx.Request.Cookies.TryGetValue(CookieName, out var token) && !string.IsNullOrEmpty(token) ? token : null;

    // Sprawdza nazwę użytkownika i hasło z blokadą po serii pomyłek. Wspólne dla logowania
    // przeglądarki i makr — ta sama ochrona przed zgadywaniem haseł na obu drogach.
    private static async Task<(CurrentUser? User, IResult? Error)> VerifyCredentialsAsync(
        NpgsqlConnection conn, string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (null, Results.BadRequest("Enter a username and password."));

        if (FailedAttempts.TryGetValue(username, out var attemptState))
        {
            DateTime lockedUntil;
            lock (attemptState) { lockedUntil = attemptState.LockedUntil; }
            if (lockedUntil > DateTime.UtcNow)
                return (null, Results.Text(
                    "Too many failed login attempts. Try again in a few minutes.",
                    statusCode: StatusCodes.Status429TooManyRequests));
        }

        CurrentUser user;
        string passwordHash;
        await using (var cmd = new NpgsqlCommand(
            "SELECT id, username, display_name, role, password_hash FROM users WHERE username = @username;", conn))
        {
            cmd.Parameters.AddWithValue("username", username);
            await using var reader = await cmd.ExecuteReaderAsync();
            // Ten sam komunikat dla złego loginu i złego hasła — nie zdradzamy, które z nich.
            if (!await reader.ReadAsync())
            {
                RecordFailedLogin(username);
                return (null, Results.BadRequest("Incorrect username or password."));
            }
            user = new CurrentUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
            passwordHash = reader.GetString(4);
        }

        if (!PasswordHasher.Verify(password, passwordHash))
        {
            RecordFailedLogin(username);
            return (null, Results.BadRequest("Incorrect username or password."));
        }

        FailedAttempts.TryRemove(username, out _);
        return (user, null);
    }

    // Ciasteczko sesji przeglądarki — logowanie hasłem i logowanie jednokrotne (OidcSignIn).
    internal static void AppendSessionCookie(HttpContext ctx, string token, DateTime expiresAt) =>
        ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt,
            Path = "/",
        });

    // Nowa sesja przeglądarki dla użytkownika (logowanie i most z makra do przeglądarki).
    internal static async Task<(string Token, DateTime ExpiresAt)> CreateSessionAsync(NpgsqlConnection conn, Guid userId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.Add(SessionLifetime);

        // Wygasłe sesje nigdy nie były usuwane — tabela tylko rosła. Logowanie jest rzadkie,
        // więc to dobry moment na sprzątanie, bez osobnej usługi w tle.
        await using (var cleanup = new NpgsqlCommand("DELETE FROM sessions WHERE expires_at < now();", conn))
            await cleanup.ExecuteNonQueryAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO sessions (token, user_id, expires_at) VALUES (@token, @userId, @expiresAt);", conn);
        cmd.Parameters.AddWithValue("token", token);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("expiresAt", expiresAt);
        await cmd.ExecuteNonQueryAsync();
        return (token, expiresAt);
    }

    private static readonly TimeSpan MacroTokenLifetime = TimeSpan.FromDays(365);

    // Tokeny makr trzymane są w bazie tylko jako skrót SHA-256 (zob. migracja 071).
    internal static string HashMacroToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    // Użytkownik dla tokenu makra (null = nieznany albo wygasły). Znacznik ostatniego użycia
    // odświeżany najwyżej co 5 minut — makro wysyła dziesiątki zapytań na jedną wysyłkę, a lista w
    // ustawieniach potrzebuje tylko przybliżonego "kiedy ostatnio".
    private static async Task<CurrentUser?> ValidateMacroTokenAsync(NpgsqlConnection conn, string token)
    {
        await using var cmd = new NpgsqlCommand("""
            WITH hit AS (
                UPDATE macro_tokens SET last_used_at = now()
                WHERE token_hash = @hash AND expires_at > now() AND last_used_at < now() - interval '5 minutes'
                RETURNING user_id
            )
            SELECT u.id, u.username, u.display_name, u.role
            FROM macro_tokens m JOIN users u ON u.id = m.user_id
            WHERE m.token_hash = @hash AND m.expires_at > now();
            """, conn);
        cmd.Parameters.AddWithValue("hash", HashMacroToken(token));
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new CurrentUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
            : null;
    }

    public static void MapAuthEndpoints(this WebApplication app, string connectionString, BrowserBridgeTicketStore ticketStore)
    {
        // POST /api/auth/login   body: { "username": "...", "password": "..." }
        app.MapPost("/api/auth/login", async (HttpContext ctx, LoginRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var (verified, error) = await VerifyCredentialsAsync(conn, body.Username, body.Password);
            if (verified is null)
                return error!;
            var user = verified;

            var (token, expiresAt) = await CreateSessionAsync(conn, user.Id);
            AppendSessionCookie(ctx, token, expiresAt);

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
        // POST /api/auth/macro-login   body: { username, password, client }
        // Logowanie makra CAD: wydaje osobny token makra (zob. migracja 071), widoczny i
        // odwoływalny w Ustawienia -> Użytkownicy, zamiast sesji przeglądarki. Odpowiedź ma ten
        // sam kształt co /auth/login — token w polu "sessionToken" i w ciasteczku pdm_session —
        // żeby makra (także VBA, które czyta pole z JSON-a) zmieniły tylko adres logowania.
        // Ciasteczko to tylko nośnik dla makra; przeglądarka i tak loguje się przez /auth/login.
        app.MapPost("/api/auth/macro-login", async (HttpContext ctx, MacroLoginRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var (user, error) = await VerifyCredentialsAsync(conn, body.Username, body.Password);
            if (user is null)
                return error!;

            var token = "pdmm_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var expiresAt = DateTime.UtcNow.Add(MacroTokenLifetime);
            var client = string.IsNullOrWhiteSpace(body.Client) ? "CAD macro" : body.Client.Trim();
            if (client.Length > 100) client = client[..100];

            await using (var cleanup = new NpgsqlCommand("DELETE FROM macro_tokens WHERE expires_at < now();", conn))
                await cleanup.ExecuteNonQueryAsync();
            await using (var cmd = new NpgsqlCommand("""
                INSERT INTO macro_tokens (user_id, token_hash, client, expires_at)
                VALUES (@userId, @hash, @client, @expiresAt);
                """, conn))
            {
                cmd.Parameters.AddWithValue("userId", user.Id);
                cmd.Parameters.AddWithValue("hash", HashMacroToken(token));
                cmd.Parameters.AddWithValue("client", client);
                cmd.Parameters.AddWithValue("expiresAt", expiresAt);
                await cmd.ExecuteNonQueryAsync();
            }

            ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Expires = expiresAt,
                Path = "/",
            });

            return Results.Ok(new
            {
                id = user.Id,
                username = user.Username,
                displayName = user.DisplayName,
                role = user.Role,
                sessionToken = token,
                macroToken = token,
            });
        });

        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            if (ctx.Request.Cookies.TryGetValue(CookieName, out var token) && !string.IsNullOrEmpty(token))
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("DELETE FROM sessions WHERE token = @token;", conn);
                cmd.Parameters.AddWithValue("token", token);
                await cmd.ExecuteNonQueryAsync();
                // Wylogowanie makra (ten sam endpoint, ten sam nośnik) odwołuje jego token.
                await using var macroCmd = new NpgsqlCommand("DELETE FROM macro_tokens WHERE token_hash = @hash;", conn);
                macroCmd.Parameters.AddWithValue("hash", HashMacroToken(token));
                await macroCmd.ExecuteNonQueryAsync();
            }

            ctx.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
            return Results.Ok();
        });

        // POST /api/auth/browser-bridge-ticket — zamienia sesję, którą makro CAD trzyma
        // (nagłówek Cookie skonstruowany ręcznie, zob. get_session_token() w
        // EasyPDMUpload.FCMacro) na jednorazowy, krótkotrwały bilet do otwarcia przeglądarki
        // (GET /browser-login niżej), zamiast wysyłać tam wprost 30-dniowy token sesji.
        // Chronione zwykłym middleware sesji (NIE ma go na liście wyjątków w Program.cs) —
        // tylko już zalogowany klient (makro z ważnym tokenem) może sobie wystawić bilet.
        app.MapPost("/api/auth/browser-bridge-ticket", (HttpContext ctx) =>
        {
            if (!ctx.Request.Cookies.TryGetValue(CookieName, out var token) || string.IsNullOrEmpty(token))
                return Results.Unauthorized();

            var ticket = ticketStore.Issue(token);
            return Results.Ok(new { ticket });
        });

        // GET /api/auth/me — middleware już zagwarantował, że jest zalogowany (inaczej 401
        // zanim ten handler w ogóle by się wykonał).
        app.MapGet("/api/auth/me", (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            return Results.Ok(ToPublicUser(user));
        });

        // GET /api/auth/browser-login?ticket=...&redirect=...   most bilet -> ciasteczko dla
        // przeglądarki otwartej przez makro CAD (które ma już własną sesję zapisaną lokalnie,
        // np. w preferencjach FreeCAD) — pozwala otworzyć przeglądarkę systemową od razu
        // zalogowaną, bez ponownego wpisywania hasła. "ticket" to jednorazowy, krótkotrwały
        // bilet z POST /browser-bridge-ticket wyżej, NIE sam token sesji -- dzięki temu
        // prawdziwy, 30-dniowy sekret nigdy nie ląduje w URL-u otwieranym w przeglądarce (a
        // więc ani w jej historii, ani w ewentualnych logach) i nawet powtórne otwarcie tego
        // samego URL-a nic już nie daje (bilet zużywa się przy pierwszym użyciu). Wyjątek od
        // middleware w Program.cs (musi działać BEZ ciasteczka).
        app.MapGet("/api/auth/browser-login", async (HttpContext ctx, string? ticket, string? redirect) =>
        {
            var target = SanitizeRedirect(redirect);

            var token = string.IsNullOrEmpty(ticket) ? null : ticketStore.TryConsume(ticket);
            // Bilet wydany makru (token makra) zakłada przeglądarce WŁASNĄ sesję — token makra
            // nie trafia do przeglądarki, a wylogowanie przeglądarki nie wylogowuje makra.
            if (token is not null && token.StartsWith("pdmm_", StringComparison.Ordinal))
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                var macroUser = await ValidateMacroTokenAsync(conn, token);
                token = null;
                if (macroUser is not null)
                {
                    var (browserToken, browserExpires) = await CreateSessionAsync(conn, macroUser.Id);
                    ctx.Response.Cookies.Append(CookieName, browserToken, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = ctx.Request.IsHttps,
                        SameSite = SameSiteMode.Lax,
                        Expires = browserExpires,
                        Path = "/",
                    });
                }
            }
            if (token is not null)
            {
                var session = await ValidateToken(connectionString, token);
                if (session is not null)
                {
                    ctx.Response.Cookies.Append(CookieName, token, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = ctx.Request.IsHttps,
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
                return Results.BadRequest("The new password cannot be empty.");
            if (PasswordPolicy.Validate(body.NewPassword) is { } passwordError)
                return Results.BadRequest(passwordError);

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string currentHash;
            await using (var cmd = new NpgsqlCommand("SELECT password_hash FROM users WHERE id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("id", user.Id);
                currentHash = (string)(await cmd.ExecuteScalarAsync())!;
            }

            if (!PasswordHasher.Verify(body.CurrentPassword, currentHash))
                return Results.BadRequest("The current password is incorrect.");

            var newHash = PasswordHasher.Hash(body.NewPassword);
            await using (var cmd = new NpgsqlCommand("UPDATE users SET password_hash = @hash WHERE id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("hash", newHash);
                cmd.Parameters.AddWithValue("id", user.Id);
                await cmd.ExecuteNonQueryAsync();
            }
            // Bieżąca sesja zostaje (użytkownik właśnie z niej zmienił hasło), pozostałe —
            // inne przeglądarki, makra CAD — muszą zalogować się nowym hasłem.
            await RevokeSessionsAsync(conn, user.Id, CurrentSessionToken(ctx));

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
        if (session is not null)
            return session.Value.User;
        if (!token.StartsWith("pdmm_", StringComparison.Ordinal))
            return null;
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        return await ValidateMacroTokenAsync(conn, token);
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
    // Znaki sterujące, spacje i wsteczne ukośniki gdziekolwiek też dają "/": przeglądarki
    // usuwają z adresu tabulatory i nowe linie, więc "/\t/evil.com" zamieniłoby się w "//evil.com".
    internal static string SanitizeRedirect(string? redirect)
    {
        if (string.IsNullOrEmpty(redirect) || redirect[0] != '/')
            return "/";
        if (redirect.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\'))
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
record MacroLoginRequest(string? Username, string? Password, string? Client);
record ChangePasswordRequest(string CurrentPassword, string NewPassword);
