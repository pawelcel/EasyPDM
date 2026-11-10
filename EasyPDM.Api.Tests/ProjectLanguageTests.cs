using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Ustawienia -> Język -> Języki projektu: lista języków dokumentacji z pełną nazwą i skrótem
// (1-3 wielkie litery). Zmienia ją administrator, odczytać może każdy zalogowany.
[Collection("EasyPDM database")]
public class ProjectLanguageTests
{
    // Unikalny skrót na przebieg: trzy litery z losowej liczby, żeby testy się nie gryzły.
    private static string RandomCode()
    {
        var n = Random.Shared.Next(26 * 26 * 26);
        return new string([(char)('A' + n % 26), (char)('A' + n / 26 % 26), (char)('A' + n / 676)]);
    }

    private static async Task<JsonElement?> FindAsync(HttpClient client, int id)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/project-languages");
        foreach (var l in list.EnumerateArray())
            if (l.GetProperty("id").GetInt32() == id)
                return l;
        return null;
    }

    [Fact]
    public async Task Administrator_dodaje_zmienia_i_usuwa_jezyk()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var name = "Język " + Guid.NewGuid().ToString("N")[..6];
        var code = RandomCode();

        // Skrót wpisany małymi literami i ze spacjami zapisuje się wielkimi.
        var created = await admin.PostAsJsonAsync("/api/project-languages", new { name, code = " " + code.ToLowerInvariant() + " " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(code, (await FindAsync(admin, id))!.Value.GetProperty("code").GetString());

        // Ten sam skrót albo ta sama nazwa (bez względu na wielkość liter) to konflikt.
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/project-languages", new { name = name + " 2", code })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsJsonAsync("/api/project-languages", new { name = name.ToUpperInvariant(), code = RandomCode() })).StatusCode);

        var newName = name + " zmieniony";
        (await admin.PatchAsJsonAsync($"/api/project-languages/{id}", new { name = newName, code })).EnsureSuccessStatusCode();
        Assert.Equal(newName, (await FindAsync(admin, id))!.Value.GetProperty("name").GetString());

        (await admin.DeleteAsync($"/api/project-languages/{id}")).EnsureSuccessStatusCode();
        Assert.Null(await FindAsync(admin, id));
    }

    [Fact]
    public async Task Na_start_jest_English_i_Polish_a_domyslny_English()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");

        var list = (await admin.GetFromJsonAsync<JsonElement>("/api/project-languages")).EnumerateArray().ToList();
        var en = list.Single(l => l.GetProperty("code").GetString() == "EN");
        var pl = list.Single(l => l.GetProperty("code").GetString() == "PL");
        Assert.Equal("English", en.GetProperty("name").GetString());
        Assert.Equal("Polish", pl.GetProperty("name").GetString());
        Assert.True(en.GetProperty("isDefault").GetBoolean());
        Assert.Single(list, l => l.GetProperty("isDefault").GetBoolean());
    }

    [Fact]
    public async Task Domyslny_jezyk_zmienia_sie_i_nie_da_sie_go_usunac()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var created = await admin.PostAsJsonAsync("/api/project-languages",
            new { name = "Język " + Guid.NewGuid().ToString("N")[..6], code = RandomCode() });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.False((await FindAsync(admin, id))!.Value.GetProperty("isDefault").GetBoolean());

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/project-languages");
        var previous = list.EnumerateArray().Single(l => l.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetInt32();
        try
        {
            (await admin.PutAsync($"/api/project-languages/{id}/default", null)).EnsureSuccessStatusCode();
            Assert.True((await FindAsync(admin, id))!.Value.GetProperty("isDefault").GetBoolean());
            Assert.False((await FindAsync(admin, previous))!.Value.GetProperty("isDefault").GetBoolean());

            Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/project-languages/{id}")).StatusCode);
            Assert.NotNull(await FindAsync(admin, id));
        }
        finally
        {
            (await admin.PutAsync($"/api/project-languages/{previous}/default", null)).EnsureSuccessStatusCode();
        }
        (await admin.DeleteAsync($"/api/project-languages/{id}")).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCD")]
    [InlineData("P1")]
    [InlineData("ŁÓ")]
    public async Task Skrot_to_od_jednej_do_trzech_liter(string code)
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");

        var response = await admin.PostAsJsonAsync("/api/project-languages", new { name = "Zły " + Guid.NewGuid(), code });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Zwykly_uzytkownik_widzi_liste_ale_jej_nie_zmienia()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "jezyk" + Guid.NewGuid().ToString("N")[..8];
        await admin.CreateUserAsync(username, "haslo123");

        using var user = factory.CreateClient();
        await user.LoginAsync(username, "haslo123");
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/project-languages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.PostAsJsonAsync("/api/project-languages", new { name = "Czeski", code = "CZ" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync("/api/project-languages/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsync("/api/project-languages/1/default", null)).StatusCode);
    }
}
