using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
}
