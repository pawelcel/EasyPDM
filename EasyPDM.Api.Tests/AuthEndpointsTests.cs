using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EasyPDM.Api.Tests;

[Collection("EasyPDM database")]
public class AuthEndpointsTests
{
    // EnsureDefaultAdminAsync (Program.cs) zasiewa to konto automatycznie, gdy "users" jest
    // puste — DatabaseFixture resetuje schemat przed każdą klasą testową, więc zawsze istnieje.
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    [Fact]
    public async Task Login_z_poprawnym_haslem_zwraca_uzytkownika_i_ustawia_sesje()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = AdminUsername, password = AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("admin", body.GetProperty("role").GetString());

        // WebApplicationFactory.CreateClient() domyślnie obsługuje ciasteczka między
        // żądaniami tego samego klienta (HandleCookies=true) — kolejne żądanie tym samym
        // "client" powinno być już zalogowane, bez ręcznego doklejania nagłówka Cookie.
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Login_z_blednym_haslem_jest_odrzucany()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = AdminUsername, password = "zle-haslo" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Auth_me_bez_sesji_zwraca_401()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_uniewaznia_sesje_po_stronie_serwera()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/login", new { username = AdminUsername, password = AdminPassword });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        var meAfterLogout = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);
    }

    // Most token->ciasteczko dla przeglądarki otwieranej przez makro CAD (zob.
    // AuthEndpoints.cs, GET /api/auth/browser-login) — token to sessionToken zwracany w
    // treści /auth/login, ten sam sekret, który makro i tak już trzyma lokalnie.
    [Fact]
    public async Task Browser_login_z_poprawnym_tokenem_ustawia_sesje_i_przekierowuje()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var loginClient = factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/login", new { username = AdminUsername, password = AdminPassword });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("sessionToken").GetString();

        using var browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await browserClient.GetAsync($"/api/auth/browser-login?token={token}&redirect=%2F%3Ffoo%3D1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/?foo=1", response.Headers.Location?.OriginalString);

        // Ciasteczko ustawione przy przekierowaniu -> kolejne żądanie tym samym klientem
        // (HandleCookies domyślnie true, niezależnie od AllowAutoRedirect) ma być zalogowane.
        var me = await browserClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Browser_login_z_bledym_tokenem_przekierowuje_bez_ustawienia_sesji()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await browserClient.GetAsync("/api/auth/browser-login?token=nieistniejacy&redirect=%2F");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var me = await browserClient.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("/javascript:alert(1)")]
    public async Task Browser_login_odrzuca_niebezpieczny_redirect(string maliciousRedirect)
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var loginClient = factory.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/auth/login", new { username = AdminUsername, password = AdminPassword });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("sessionToken").GetString();

        using var browserClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await browserClient.GetAsync($"/api/auth/browser-login?token={token}&redirect={Uri.EscapeDataString(maliciousRedirect)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }
}
