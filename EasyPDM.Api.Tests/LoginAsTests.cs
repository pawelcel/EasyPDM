using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// "Zaloguj jako": administrator pracuje w tej samej sesji jako zwykły użytkownik, z dokładnie
// jego uprawnieniami, a każdy start, każda zmiana i każdy koniec trafiają do dziennika.
[Collection("EasyPDM database")]
public class LoginAsTests
{
    private const string Password = "Haslo-123!-test";

    private static string NewName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private static async Task<JsonElement[]> LogFor(HttpClient admin, Guid targetId)
    {
        var lines = await admin.GetFromJsonAsync<JsonElement>("/api/auth/login-as/log?limit=1000");
        return lines.EnumerateArray().Where(l => l.GetProperty("targetId").GetGuid() == targetId).Reverse().ToArray();
    }

    [Fact]
    public async Task Administrator_widzi_to_co_uzytkownik_i_wraca_a_dziennik_wszystko_zapisuje()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var mine = await admin.CreateProjectAsync(NewName("la-mine"));
        var other = await admin.CreateProjectAsync(NewName("la-other"));
        var username = NewName("la");
        var userId = await admin.CreateUserAsync(username, Password);
        await admin.GrantProjectAccessAsync(mine, userId);

        var people = await admin.GetFromJsonAsync<JsonElement>("/api/auth/login-as/people");
        Assert.Contains(people.EnumerateArray(), p => p.GetProperty("id").GetGuid() == userId);
        Assert.DoesNotContain(people.EnumerateArray(), p => p.GetProperty("username").GetString() == "admin");
        var access = await admin.GetFromJsonAsync<JsonElement>($"/api/auth/login-as/people/{userId}");
        Assert.Equal(1, access.GetProperty("projectCount").GetInt32());

        using var browser = factory.CreateClient();
        await browser.LoginAsync("admin", "admin");
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsJsonAsync("/api/auth/login-as", new { userId })).StatusCode);

        var me = await browser.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(userId, me.GetProperty("id").GetGuid());
        Assert.Equal("user", me.GetProperty("role").GetString());
        Assert.Equal("Administrator", me.GetProperty("loginAs").GetProperty("adminName").GetString());

        // Uprawnienia użytkownika: tylko jego projekt, żadnych tras administratora.
        var projects = await browser.GetFromJsonAsync<JsonElement>("/api/projects");
        var ids = projects.EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(mine, ids);
        Assert.DoesNotContain(other, ids);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/users")).StatusCode);
        var refused = await browser.PostAsJsonAsync("/api/projects", new { name = NewName("la-new") });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // To, co tylko sam użytkownik może zrobić, zostaje zablokowane.
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PatchAsJsonAsync("/api/auth/password",
            new { currentPassword = Password, newPassword = "Inne-Haslo-456!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsJsonAsync("/api/auth/login-as", new { userId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/auth/login-as/log")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync("/api/auth/login-as/back", null)).StatusCode);
        var back = await browser.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("admin", back.GetProperty("username").GetString());
        Assert.Equal(JsonValueKind.Null, back.GetProperty("loginAs").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsync("/api/auth/login-as/back", null)).StatusCode);

        var log = await LogFor(admin, userId);
        Assert.Equal(["start", "change", "change", "change", "end"], log.Select(l => l.GetProperty("kind").GetString()));
        Assert.Equal("POST /api/projects 403", log[1].GetProperty("detail").GetString());
        Assert.Equal("back", log[^1].GetProperty("detail").GetString());
        Assert.Equal($"Jan Testowy {username}", log[0].GetProperty("targetName").GetString());
    }

    [Fact]
    public async Task Nie_mozna_wybrac_administratora_siebie_ani_zaczac_bez_roli_administratora()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var otherAdmin = await admin.CreateUserAsync(NewName("la-adm"), Password, "admin");
        var username = NewName("la");
        var userId = await admin.CreateUserAsync(username, Password);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/auth/login-as", new { userId = otherAdmin })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/auth/login-as", new { userId = me.GetProperty("id").GetGuid() })).StatusCode);

        using var plain = factory.CreateClient();
        await plain.LoginAsync(username, Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsJsonAsync("/api/auth/login-as", new { userId = otherAdmin })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync("/api/auth/login-as/people")).StatusCode);
    }

    [Fact]
    public async Task Wylogowanie_w_trakcie_konczy_cala_sesje()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var userId = await admin.CreateUserAsync(NewName("la"), Password);

        using var browser = factory.CreateClient(new() { HandleCookies = false });
        var login = await browser.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionToken").GetString();
        browser.DefaultRequestHeaders.Add("Cookie", "pdm_session=" + token);
        await browser.PostAsJsonAsync("/api/auth/login-as", new { userId });

        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync("/api/auth/logout", null)).StatusCode);

        // Ten sam token nie loguje już nikogo: ani użytkownika, ani administratora.
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
        var log = await LogFor(admin, userId);
        Assert.Equal("logged_out", log[^1].GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Odebranie_roli_administratorowi_konczy_jego_sesje_jako_ktos_inny()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var adminName = NewName("la-adm");
        var secondAdmin = await admin.CreateUserAsync(adminName, Password, "admin");
        var userId = await admin.CreateUserAsync(NewName("la"), Password);

        using var browser = factory.CreateClient();
        await browser.LoginAsync(adminName, Password);
        await browser.PostAsJsonAsync("/api/auth/login-as", new { userId });
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/auth/me")).StatusCode);

        var demote = await admin.PatchAsJsonAsync($"/api/users/{secondAdmin}", new { role = "user" });
        demote.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
        var log = await LogFor(admin, userId);
        Assert.Equal("expired", log[^1].GetProperty("detail").GetString());

        // Przywrócenie roli nie wskrzesza tamtej sesji: została usunięta.
        (await admin.PatchAsJsonAsync($"/api/users/{secondAdmin}", new { role = "admin" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Zmiana_hasla_administratora_konczy_jego_sesje_jako_ktos_inny()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var adminName = NewName("la-adm");
        await admin.CreateUserAsync(adminName, Password, "admin");
        var userId = await admin.CreateUserAsync(NewName("la"), Password);

        using var switched = factory.CreateClient();
        await switched.LoginAsync(adminName, Password);
        await switched.PostAsJsonAsync("/api/auth/login-as", new { userId });

        using var own = factory.CreateClient();
        await own.LoginAsync(adminName, Password);
        (await own.PatchAsJsonAsync("/api/auth/password", new { currentPassword = Password, newPassword = "Nowe-Haslo-789!" }))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await switched.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await own.GetAsync("/api/auth/me")).StatusCode);
    }
}
