using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Języki we właściwościach projektu: każdy projekt ma język główny (na start domyślny język
// dokumentacji z ustawień), a wielojęzyczny ma dodatkowo listę języków, na której zawsze jest
// też główny. Użytego w projekcie języka nie da się usunąć z ustawień.
[Collection("EasyPDM database")]
public class ProjectLanguageSettingsTests
{
    private static async Task<JsonElement> ProjectAsync(HttpClient client, Guid id)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/projects");
        return list.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
    }

    private static async Task<int> LanguageIdAsync(HttpClient client, string code)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/project-languages");
        return list.EnumerateArray().Single(l => l.GetProperty("code").GetString() == code).GetProperty("id").GetInt32();
    }

    private static async Task<int> DefaultLanguageIdAsync(HttpClient client)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/project-languages");
        return list.EnumerateArray().Single(l => l.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetInt32();
    }

    private static int[] Ids(JsonElement project) =>
        project.GetProperty("languageIds").EnumerateArray().Select(e => e.GetInt32()).Order().ToArray();

    [Fact]
    public async Task Nowy_projekt_ma_domyslny_jezyk_i_nie_jest_wielojezyczny()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Jezyki {Guid.NewGuid()}");

        var project = await ProjectAsync(admin, projectId);
        Assert.Equal(await DefaultLanguageIdAsync(admin), project.GetProperty("mainLanguageId").GetInt32());
        Assert.False(project.GetProperty("multilingual").GetBoolean());
        Assert.Empty(Ids(project));
    }

    [Fact]
    public async Task Wielojezyczny_projekt_zawsze_ma_jezyk_glowny_na_liscie()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Jezyki {Guid.NewGuid()}");
        var en = await LanguageIdAsync(admin, "EN");
        var pl = await LanguageIdAsync(admin, "PL");

        // Główny polski, na liście tylko angielski -- polski dokłada się sam.
        (await admin.PatchAsJsonAsync($"/api/projects/{projectId}/languages",
            new { mainLanguageId = pl, multilingual = true, languageIds = new[] { en } })).EnsureSuccessStatusCode();
        var project = await ProjectAsync(admin, projectId);
        Assert.Equal(pl, project.GetProperty("mainLanguageId").GetInt32());
        Assert.True(project.GetProperty("multilingual").GetBoolean());
        Assert.Equal(new[] { en, pl }.Order().ToArray(), Ids(project));

        // Wyłączenie wielojęzyczności czyści listę, język główny zostaje.
        (await admin.PatchAsJsonAsync($"/api/projects/{projectId}/languages",
            new { mainLanguageId = pl, multilingual = false, languageIds = new[] { en } })).EnsureSuccessStatusCode();
        project = await ProjectAsync(admin, projectId);
        Assert.Equal(pl, project.GetProperty("mainLanguageId").GetInt32());
        Assert.False(project.GetProperty("multilingual").GetBoolean());
        Assert.Empty(Ids(project));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/projects/{projectId}/languages",
            new { mainLanguageId = pl, multilingual = true, languageIds = new[] { int.MaxValue } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsJsonAsync($"/api/projects/{Guid.NewGuid()}/languages",
            new { mainLanguageId = pl, multilingual = false })).StatusCode);
    }

    [Fact]
    public async Task Jezyka_uzytego_w_projekcie_nie_da_sie_usunac()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Jezyki {Guid.NewGuid()}");
        var main = await DefaultLanguageIdAsync(admin);
        var created = await admin.PostAsJsonAsync("/api/project-languages",
            new { name = "Język " + Guid.NewGuid().ToString("N")[..6], code = "Q" + (char)('A' + Random.Shared.Next(26)) + (char)('A' + Random.Shared.Next(26)) });
        var extra = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        (await admin.PatchAsJsonAsync($"/api/projects/{projectId}/languages",
            new { mainLanguageId = main, multilingual = true, languageIds = new[] { extra } })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/project-languages/{extra}")).StatusCode);

        (await admin.PatchAsJsonAsync($"/api/projects/{projectId}/languages",
            new { mainLanguageId = main, multilingual = false })).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/project-languages/{extra}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Zwykly_uzytkownik_nie_zmienia_jezykow_projektu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Jezyki {Guid.NewGuid()}");
        var username = "jp" + Guid.NewGuid().ToString("N")[..8];
        var userId = await admin.CreateUserAsync(username, "haslo123");
        await admin.GrantProjectAccessAsync(projectId, userId);

        using var user = factory.CreateClient();
        await user.LoginAsync(username, "haslo123");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PatchAsJsonAsync($"/api/projects/{projectId}/languages",
            new { mainLanguageId = await DefaultLanguageIdAsync(admin), multilingual = true })).StatusCode);
    }
}
