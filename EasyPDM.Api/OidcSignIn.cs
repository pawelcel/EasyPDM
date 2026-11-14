using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Npgsql;

// Logowanie jednokrotne (single sign-on) przez dostawcę OpenID Connect (Keycloak, Microsoft Entra
// ID, Authentik, Google Workspace…), obok logowania hasłem. Włączone, gdy ustawiono Oidc:Authority:
//
//   Oidc:Authority        adres wystawcy, np. https://sso.example.com/realms/main
//   Oidc:ClientId         klient tej aplikacji u dostawcy (poufny, code flow z PKCE)
//   Oidc:ClientSecret
//   Oidc:RolesClaim       claim z listą ról użytkownika (domyślnie "roles")
//   Oidc:RequiredRoles    role, z których dowolna wpuszcza; bez nich nikt nie wejdzie przez SSO,
//                         chyba że Oidc:AllowAllUsers = true (każdy użytkownik dostawcy — przy
//                         dostawcy publicznym, np. kontach Google, to każdy w internecie)
//   Oidc:AdminRoles       role dające rolę administratora EasyPDM (domyślnie żadna: administratora
//                         daje się świadomie, nie przez ogólną nazwę roli u dostawcy)
//   Oidc:LocalOnlyUsers   loginy, na które SSO nigdy nie loguje (domyślnie "admin": lokalne konto
//                         awaryjne zostaje tylko na hasło)
//   Oidc:AutoRedirect     true = strona logowania od razu przekierowuje do dostawcy (domyślnie
//                         false: przycisk logowania przez SSO nad formularzem hasła)
//
// GET /api/auth/oidc/start?redirect=/... wysyła przeglądarkę do dostawcy; ten wraca na
// /api/auth/oidc/callback, gdzie użytkownik jest odnajdywany po identyfikatorze u dostawcy
// (wystawca + sub) albo zakładany przy pierwszym logowaniu i dostaje to samo ciasteczko
// pdm_session co po logowaniu hasłem. Rolę EasyPDM ustalają role od dostawcy przy każdym
// logowaniu. Lokalne konto o tym samym loginie nigdy nie zostaje przejęte.
static class OidcSignIn
{
    public const string Scheme = "oidc";
    private const string ExternalCookieScheme = "oidc-external";
    private const string CallbackPath = "/api/auth/oidc/callback";

    public static bool IsEnabled(IConfiguration config) => !string.IsNullOrWhiteSpace(config["Oidc:Authority"]);

    // Rejestrowane zawsze, konfigurowane przy pierwszym użyciu (żeby widzieć też ustawienia
    // dodane przez host testowy).
    public static void AddOidcSignIn(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(ExternalCookieScheme)
            // Niesie tylko odpowiedź dostawcy w obrębie callbacku; nic się na nie nie loguje.
            .AddCookie(ExternalCookieScheme)
            .AddOpenIdConnect(Scheme, _ => { });
        builder.Services.AddOptions<OpenIdConnectOptions>(Scheme).Configure<IConfiguration>((options, config) =>
        {
            options.SignInScheme = ExternalCookieScheme;
            // Wyłączone: wartości zastępcze przechodzą walidację opcji; bez trasy /start nic nie
            // wywołuje tego schematu, więc nigdy nie są używane.
            options.Authority = IsEnabled(config) ? config["Oidc:Authority"] : "https://sso-off.invalid";
            options.ClientId = IsEnabled(config) ? config["Oidc:ClientId"] : "sso-off";
            options.ClientSecret = config["Oidc:ClientSecret"];
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.CallbackPath = CallbackPath;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.Events = new OpenIdConnectEvents
            {
                OnTicketReceived = async ctx =>
                {
                    ctx.HandleResponse();
                    var configuration = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                    var connectionString = configuration["ConnectionString"]!;
                    var outcome = await SignInAsync(connectionString, ctx.Principal!, OidcSettings.From(configuration));
                    if (outcome.User is null)
                    {
                        ctx.Response.Redirect($"/?sso-error={Uri.EscapeDataString(outcome.Refusal!)}");
                        return;
                    }

                    await using var conn = new NpgsqlConnection(connectionString);
                    await conn.OpenAsync();
                    var (token, expiresAt) = await AuthEndpoints.CreateSessionAsync(conn, outcome.User.Id);
                    AuthEndpoints.AppendSessionCookie(ctx.HttpContext, token, expiresAt);
                    ctx.Response.Redirect(AuthEndpoints.SanitizeRedirect(ctx.Properties?.RedirectUri));
                },
                OnRemoteFailure = ctx =>
                {
                    ctx.HandleResponse();
                    ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("OidcSignIn").LogWarning(ctx.Failure, "Single sign-on failed.");
                    ctx.Response.Redirect("/?sso-error=failed");
                    return Task.CompletedTask;
                },
            };
        });
    }

    public static void MapOidcEndpoints(this WebApplication app)
    {
        // GET /api/auth/config — co ma pokazać strona logowania. Bez sesji.
        app.MapGet("/api/auth/config", (IConfiguration config) => Results.Ok(new
        {
            sso = IsEnabled(config),
            ssoAutoRedirect = IsEnabled(config) && config.GetValue<bool>("Oidc:AutoRedirect"),
        }));

        if (!IsEnabled(app.Configuration))
            return;

        // GET /api/auth/oidc/start?redirect=/...
        app.MapGet("/api/auth/oidc/start", (string? redirect) =>
            Results.Challenge(new AuthenticationProperties { RedirectUri = AuthEndpoints.SanitizeRedirect(redirect) }, [Scheme]));
    }

    // Użytkownik dostawcy zmapowany na użytkownika EasyPDM: odnaleziony po identyfikatorze albo
    // założony, rola z ról dostawcy. Refusal to krótki powód dla strony logowania, gdy nie wolno
    // wejść.
    internal static async Task<(CurrentUser? User, string? Refusal)> SignInAsync(
        string connectionString, ClaimsPrincipal principal, OidcSettings settings)
    {
        var username = (principal.FindFirst("preferred_username")?.Value ?? "").Trim().ToLowerInvariant();
        if (username.Length == 0)
            return (null, "no-username");
        if (settings.LocalOnlyUsers.Contains(username))
            return (null, "local-only");

        // Wystawca + sub: ten sam sub u innego dostawcy to inna osoba.
        var issuer = principal.FindFirst("iss")?.Value?.Trim() ?? "";
        var sub = principal.FindFirst("sub")?.Value?.Trim() ?? "";
        if (sub.Length == 0)
            return (null, "no-username");
        var subject = issuer.Length == 0 ? sub : $"{issuer}|{sub}";

        var roles = principal.FindAll(settings.RolesClaim).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        var allowed = settings.RequiredRoles.Count > 0 ? settings.RequiredRoles.Overlaps(roles) : settings.AllowAllUsers;
        if (!allowed)
        {
            // Dostawca odebrał dostęp: sesje i tokeny makr tej osoby przestają działać od razu,
            // a nie dopiero po 30 dniach, i nie zostaje jej rola administratora.
            await RevokeAsync(connectionString, subject);
            return (null, "no-access");
        }

        var role = settings.AdminRoles.Overlaps(roles) ? "admin" : "user";
        var firstName = Claim(principal, "given_name");
        var lastName = Claim(principal, "family_name");
        var displayName = Claim(principal, "name")
            ?? (firstName is not null || lastName is not null ? $"{firstName} {lastName}".Trim() : username);
        var email = Claim(principal, "email");

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Ten sam użytkownik dostawcy co poprzednio: nazwa, e-mail i rola idą za dostawcą.
        await using (var update = new NpgsqlCommand("""
            UPDATE users SET display_name = @displayName, email = COALESCE(@email, email),
                first_name = COALESCE(@firstName, first_name), last_name = COALESCE(@lastName, last_name),
                role = @role
            WHERE oidc_subject = @subject
            RETURNING id, username, display_name, role;
            """, conn))
        {
            AddProfile(update, displayName, email, firstName, lastName, role, subject);
            await using var reader = await update.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                return (new CurrentUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)), null);
        }

        // Pierwsze logowanie: nowy użytkownik z hasłem, którego nikt nie zna. Lokalnego konta o tym
        // samym loginie nie przejmujemy: takie logowanie jest odrzucane, a administrator decyduje
        // (zmienia login lokalnego konta albo łączy je, ustawiając mu oidc_subject).
        await using var insert = new NpgsqlCommand("""
            INSERT INTO users (username, display_name, email, first_name, last_name, password_hash, role, oidc_subject)
            VALUES (@username, @displayName, @email, @firstName, @lastName, @hash, @role, @subject)
            ON CONFLICT DO NOTHING
            RETURNING id, username, display_name, role;
            """, conn);
        AddProfile(insert, displayName, email, firstName, lastName, role, subject);
        insert.Parameters.AddWithValue("username", username);
        insert.Parameters.AddWithValue("hash", PasswordHasher.Hash(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))));
        await using var inserted = await insert.ExecuteReaderAsync();
        if (!await inserted.ReadAsync())
            return (null, "local-account");
        return (new CurrentUser(inserted.GetGuid(0), inserted.GetString(1), inserted.GetString(2), inserted.GetString(3)), null);
    }

    private static async Task RevokeAsync(string connectionString, string subject)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        Guid? userId;
        await using (var cmd = new NpgsqlCommand("UPDATE users SET role = 'user' WHERE oidc_subject = @subject RETURNING id;", conn))
        {
            cmd.Parameters.AddWithValue("subject", subject);
            userId = await cmd.ExecuteScalarAsync() as Guid?;
        }
        if (userId is Guid id)
            await AuthEndpoints.RevokeSessionsAsync(conn, id, null);
    }

    private static string? Claim(ClaimsPrincipal principal, string type)
    {
        var value = principal.FindFirst(type)?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static void AddProfile(NpgsqlCommand cmd, string displayName, string? email, string? firstName, string? lastName, string role, string subject)
    {
        cmd.Parameters.AddWithValue("displayName", displayName);
        cmd.Parameters.AddWithValue("email", (object?)email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("firstName", (object?)firstName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lastName", (object?)lastName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("role", role);
        cmd.Parameters.AddWithValue("subject", subject);
    }
}

sealed record OidcSettings(string RolesClaim, IReadOnlySet<string> RequiredRoles, bool AllowAllUsers, IReadOnlySet<string> AdminRoles, IReadOnlySet<string> LocalOnlyUsers)
{
    public static OidcSettings From(IConfiguration config)
    {
        static HashSet<string> List(string? text, string fallback, StringComparer comparer) =>
            (text ?? fallback).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).ToHashSet(comparer);

        return new OidcSettings(
            string.IsNullOrWhiteSpace(config["Oidc:RolesClaim"]) ? "roles" : config["Oidc:RolesClaim"]!,
            List(config["Oidc:RequiredRoles"], "", StringComparer.Ordinal),
            config.GetValue<bool>("Oidc:AllowAllUsers"),
            List(config["Oidc:AdminRoles"], "", StringComparer.Ordinal),
            List(config["Oidc:LocalOnlyUsers"], "admin", StringComparer.OrdinalIgnoreCase));
    }
}
