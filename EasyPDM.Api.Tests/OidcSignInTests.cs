using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace EasyPDM.Api.Tests;

// Logowanie jednokrotne mapuje użytkownika dostawcy na użytkownika EasyPDM: zakładany przy
// pierwszym logowaniu, rola z ról dostawcy przy każdym logowaniu, odmowa bez wymaganej roli,
// nigdy jako lokalne konto awaryjne ani przejęcie lokalnego konta o tym samym loginie.
[Collection("EasyPDM database")]
public class OidcSignInTests
{
    private static readonly OidcSettings Settings = OidcSettings.From(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Oidc:RequiredRoles"] = "app_easypdm",
            ["Oidc:AdminRoles"] = "admin",
        })
        .Build());

    private static ClaimsPrincipal PrincipalFrom(string issuer, string username, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new("iss", issuer),
            new("sub", $"sub-{username.ToLowerInvariant()}"),
            new("preferred_username", username),
            new("name", $"Name of {username}"),
            new("given_name", "Anna"),
            new("family_name", "Nowak"),
        };
        claims.AddRange(roles.Select(r => new Claim("roles", r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal Principal(string username, params string[] roles)
        => PrincipalFrom("https://idp.example.com/realms/test", username, roles);

    private static string NewUsername() => $"sso{Guid.NewGuid():N}"[..20];

    private static Task<(CurrentUser? User, string? Refusal)> SignIn(ClaimsPrincipal principal)
        => OidcSignIn.SignInAsync(EasyPDMWebApplicationFactory.ConnectionString, principal, Settings);

    [Fact]
    public async Task Pierwsze_logowanie_zaklada_uzytkownika_a_rola_idzie_za_dostawca()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        _ = factory.Server;
        var username = NewUsername();

        var (user, refusal) = await SignIn(Principal(username.ToUpperInvariant(), "app_easypdm"));
        Assert.Null(refusal);
        Assert.Equal(username, user!.Username);
        Assert.Equal("user", user.Role);
        Assert.Equal($"Name of {username.ToUpperInvariant()}", user.DisplayName);

        var (again, _) = await SignIn(Principal(username, "app_easypdm", "admin"));
        Assert.Equal(user.Id, again!.Id);
        Assert.Equal("admin", again.Role);

        var (demoted, _) = await SignIn(Principal(username, "app_easypdm"));
        Assert.Equal("user", demoted!.Role);

        // Imię i nazwisko (tabelka rysunkowa) przychodzą od dostawcy.
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var users = await admin.GetFromJsonAsync<JsonElement>("/api/users");
        var row = users.EnumerateArray().Single(u => u.GetProperty("username").GetString() == username);
        Assert.Equal("Anna", row.GetProperty("firstName").GetString());
        Assert.Equal("Nowak", row.GetProperty("lastName").GetString());
    }

    [Fact]
    public async Task Ten_sam_sub_u_innego_dostawcy_to_inna_osoba()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        _ = factory.Server;
        var username = NewUsername();

        var (first, _) = await SignIn(PrincipalFrom("https://idp.example.com/realms/test", username, "app_easypdm"));
        Assert.NotNull(first);

        // Inny wystawca, ten sam sub i login: nie wchodzi na konto pierwszego.
        var (other, refusal) = await SignIn(PrincipalFrom("https://other.example.com", username, "app_easypdm"));
        Assert.Null(other);
        Assert.Equal("local-account", refusal);
    }

    [Fact]
    public async Task Bez_wymaganej_roli_i_jako_lokalny_admin_odmowa()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        _ = factory.Server;

        var (noRole, refusal) = await SignIn(Principal(NewUsername(), "admin"));
        Assert.Null(noRole);
        Assert.Equal("no-access", refusal);

        var (admin, adminRefusal) = await SignIn(Principal("Admin", "app_easypdm", "admin"));
        Assert.Null(admin);
        Assert.Equal("local-only", adminRefusal);
    }

    [Fact]
    public async Task Lokalne_konto_o_tym_samym_loginie_nie_zostaje_przejete()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = NewUsername();
        await admin.CreateUserAsync(username, "Haslo-123!-test");

        var (user, refusal) = await SignIn(Principal(username, "app_easypdm", "admin"));

        Assert.Null(user);
        Assert.Equal("local-account", refusal);
        using var local = factory.CreateClient();
        await local.LoginAsync(username, "Haslo-123!-test");
        var me = await local.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("user", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Bez_wymaganych_rol_SSO_nikogo_nie_wpuszcza_i_nie_daje_administratora()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        _ = factory.Server;
        var defaults = OidcSettings.From(new ConfigurationBuilder().Build());

        // Domyślnie zamknięte: bez Oidc:RequiredRoles nikt nie wchodzi.
        var (refused, refusal) = await OidcSignIn.SignInAsync(EasyPDMWebApplicationFactory.ConnectionString, Principal(NewUsername(), "admin"), defaults);
        Assert.Null(refused);
        Assert.Equal("no-access", refusal);

        // Oidc:AllowAllUsers otwiera świadomie; ogólna rola "admin" u dostawcy nie daje administratora.
        var open = defaults with { AllowAllUsers = true };
        var (user, _) = await OidcSignIn.SignInAsync(EasyPDMWebApplicationFactory.ConnectionString, Principal(NewUsername(), "admin"), open);
        Assert.Equal("user", user!.Role);
    }

    [Fact]
    public async Task Odebranie_dostepu_u_dostawcy_konczy_sesje_przy_kolejnej_probie()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        _ = factory.Server;
        var username = NewUsername();
        var (user, _) = await SignIn(Principal(username, "app_easypdm", "admin"));

        string token;
        await using (var conn = new Npgsql.NpgsqlConnection(EasyPDMWebApplicationFactory.ConnectionString))
        {
            await conn.OpenAsync();
            (token, _) = await AuthEndpoints.CreateSessionAsync(conn, user!.Id);
        }
        using var browser = factory.CreateClient();
        browser.DefaultRequestHeaders.Add("Cookie", "pdm_session=" + token);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/auth/me")).StatusCode);

        var (gone, refusal) = await SignIn(Principal(username, "admin"));
        Assert.Null(gone);
        Assert.Equal("no-access", refusal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Uzytkownik_SSO_nie_loguje_sie_haslem()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        _ = factory.Server;
        var username = NewUsername();
        await SignIn(Principal(username, "app_easypdm"));

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username, password = "anything-at-all" });

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Strona_logowania_wie_czy_SSO_jest_wlaczone()
    {
        await using (var off = new EasyPDMWebApplicationFactory())
        {
            var body = await off.CreateClient().GetFromJsonAsync<JsonElement>("/api/auth/config");
            Assert.False(body.GetProperty("sso").GetBoolean());
            Assert.False(body.GetProperty("ssoAutoRedirect").GetBoolean());
            // Bez Oidc:Authority trasy startu nie ma: nic nie przekierowuje do dostawcy.
            var start = await off.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/api/auth/oidc/start");
            Assert.NotEqual(HttpStatusCode.Redirect, start.StatusCode);
        }

        await using var on = new EasyPDMWebApplicationFactory
        {
            ExtraSettings =
            {
                ["Oidc:Authority"] = "https://idp.example.com/realms/test",
                ["Oidc:ClientId"] = "easypdm",
                ["Oidc:ClientSecret"] = "x",
                ["Oidc:AutoRedirect"] = "true",
            },
        };
        var onBody = await on.CreateClient().GetFromJsonAsync<JsonElement>("/api/auth/config");
        Assert.True(onBody.GetProperty("sso").GetBoolean());
        Assert.True(onBody.GetProperty("ssoAutoRedirect").GetBoolean());
    }

    [Theory]
    [InlineData("/?item=1", "/?item=1")]
    [InlineData("//evil.com", "/")]
    [InlineData("/\\evil.com", "/")]
    [InlineData("/\t/evil.com", "/")]
    [InlineData("/ /evil.com", "/")]
    [InlineData("/javascript:alert(1)", "/")]
    [InlineData("https://evil.com", "/")]
    public void Przekierowanie_po_logowaniu_zostaje_w_aplikacji(string redirect, string expected)
        => Assert.Equal(expected, AuthEndpoints.SanitizeRedirect(redirect));
}
