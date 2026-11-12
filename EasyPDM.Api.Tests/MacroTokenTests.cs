using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Tokeny makr CAD: osobne od sesji przeglądarki, na liście w ustawieniach, odwoływalne,
// unieważniane zmianą hasła; most do przeglądarki zakłada przeglądarce własną sesję.
[Collection("EasyPDM database")]
public class MacroTokenTests
{
    private static HttpClient MacroClient(EasyPDMWebApplicationFactory factory, string token)
    {
        // Makro wysyła token tym samym ciasteczkiem co dotąd; HttpClient fabryki nie trzyma ciasteczek
        // sam z siebie między odpowiedzią logowania a nowym klientem, więc nagłówek ustawiamy ręcznie.
        var client = factory.CreateClient(new() { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"pdm_session={token}");
        return client;
    }

    private static async Task<string> MacroLoginAsync(EasyPDMWebApplicationFactory factory, string username, string password, string client)
    {
        using var anon = factory.CreateClient(new() { HandleCookies = false });
        var response = await anon.PostAsJsonAsync("/api/auth/macro-login", new { username, password, client });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("sessionToken").GetString()!;
        Assert.Equal(token, body.GetProperty("macroToken").GetString());
        Assert.StartsWith("pdmm_", token);
        return token;
    }

    [Fact]
    public async Task Makro_loguje_sie_osobnym_tokenem_widocznym_i_odwolywalnym()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "makro" + Guid.NewGuid().ToString("N")[..8];
        await admin.CreateUserAsync(username, "haslo123");

        var token = await MacroLoginAsync(factory, username, "haslo123", "FreeCAD Upload");
        using var macro = MacroClient(factory, token);
        Assert.Equal(username, (await macro.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("username").GetString());

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/macro-tokens");
        var entry = list.EnumerateArray().Single(t => t.GetProperty("username").GetString() == username);
        Assert.Equal("FreeCAD Upload", entry.GetProperty("client").GetString());

        // Zwykły użytkownik widzi tylko swoje.
        using var userBrowser = factory.CreateClient();
        await userBrowser.LoginAsync(username, "haslo123");
        Assert.Single((await userBrowser.GetFromJsonAsync<JsonElement>("/api/macro-tokens")).EnumerateArray());

        (await admin.DeleteAsync($"/api/macro-tokens/{entry.GetProperty("id").GetGuid()}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await macro.GetAsync("/api/auth/me")).StatusCode);
        // Odwołanie tokenu makra nie wylogowuje przeglądarki.
        Assert.Equal(HttpStatusCode.OK, (await userBrowser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Zmiana_hasla_i_wylogowanie_odwoluja_token_makra()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "makro" + Guid.NewGuid().ToString("N")[..8];
        var userId = await admin.CreateUserAsync(username, "haslo123");

        var first = await MacroLoginAsync(factory, username, "haslo123", "SolidWorks Upload");
        using (var macro = MacroClient(factory, first))
        {
            (await macro.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await macro.GetAsync("/api/auth/me")).StatusCode);
        }

        var second = await MacroLoginAsync(factory, username, "haslo123", "Inventor Download");
        (await admin.PatchAsJsonAsync($"/api/users/{userId}", new { password = "noweHaslo1" })).EnsureSuccessStatusCode();
        using var revoked = MacroClient(factory, second);
        Assert.Equal(HttpStatusCode.Unauthorized, (await revoked.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Most_do_przegladarki_z_makra_zaklada_osobna_sesje()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "most" + Guid.NewGuid().ToString("N")[..8];
        await admin.CreateUserAsync(username, "haslo123");
        var token = await MacroLoginAsync(factory, username, "haslo123", "FreeCAD Upload");

        using var macro = MacroClient(factory, token);
        var ticket = (await (await macro.PostAsync("/api/auth/browser-bridge-ticket", null)).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("ticket").GetString();

        using var browser = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        var bridge = await browser.GetAsync($"/api/auth/browser-login?ticket={ticket}&redirect=/");
        var cookie = bridge.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("pdm_session="));
        var browserToken = cookie["pdm_session=".Length..cookie.IndexOf(';')];
        Assert.False(browserToken.StartsWith("pdmm_"));

        using var browserSession = MacroClient(factory, browserToken);
        Assert.Equal(username, (await browserSession.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("username").GetString());
    }
}
