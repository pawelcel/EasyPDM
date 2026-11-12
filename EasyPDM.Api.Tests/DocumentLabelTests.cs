using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Podpisy w dokumentach (Ustawienia -> Język): nagłówki CSV brane z ustawień, z wbudowanymi
// tłumaczeniami PL/EN/DE i angielskim dla pozostałych języków.
[Collection("EasyPDM database")]
public class DocumentLabelTests
{
    private static async Task<int> LanguageIdAsync(HttpClient client, string code)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/project-languages");
        return list.EnumerateArray().Single(l => l.GetProperty("code").GetString() == code).GetProperty("id").GetInt32();
    }

    private static async Task<string> CsvHeaderAsync(HttpClient client, Guid itemId)
    {
        var bytes = await client.GetByteArrayAsync($"/api/items/{itemId}/bom/csv");
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n")[0];
    }

    [Fact]
    public async Task Naglowki_CSV_bierze_z_ustawien_i_wraca_do_wbudowanych()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var pl = await LanguageIdAsync(admin, "PL");
        var projectId = await admin.CreateProjectAsync($"Podpisy {Guid.NewGuid()}");
        (await admin.PatchAsJsonAsync($"/api/projects/{projectId}/languages", new { mainLanguageId = pl, multilingual = false })).EnsureSuccessStatusCode();
        var assembly = await admin.CreateNodeAsync(projectId, "Rama", "assembly");

        Assert.StartsWith("L.p.;Nazwa;Ilość;", await CsvHeaderAsync(admin, assembly));

        try
        {
            (await admin.PutAsJsonAsync($"/api/document-labels/{pl}",
                new { labels = new Dictionary<string, string> { ["csv.name"] = "Nazwa elementu" } })).EnsureSuccessStatusCode();
            Assert.StartsWith("L.p.;Nazwa elementu;Ilość;", await CsvHeaderAsync(admin, assembly));

            var labels = await admin.GetFromJsonAsync<JsonElement>($"/api/document-labels/{pl}");
            var name = labels.EnumerateArray().Single(l => l.GetProperty("key").GetString() == "csv.name");
            Assert.True(name.GetProperty("custom").GetBoolean());
            Assert.Equal("Nazwa", name.GetProperty("defaultText").GetString());
        }
        finally
        {
            // Pusty tekst przywraca podpis wbudowany.
            (await admin.PutAsJsonAsync($"/api/document-labels/{pl}",
                new { labels = new Dictionary<string, string> { ["csv.name"] = "" } })).EnsureSuccessStatusCode();
        }
        Assert.StartsWith("L.p.;Nazwa;Ilość;", await CsvHeaderAsync(admin, assembly));
    }

    [Fact]
    public async Task Jezyk_bez_wbudowanych_tlumaczen_ma_angielskie_i_wlasne()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var code = "Z" + (char)('A' + Random.Shared.Next(26)) + (char)('A' + Random.Shared.Next(26));
        var created = await admin.PostAsJsonAsync("/api/project-languages", new { name = "Czeski " + Guid.NewGuid().ToString("N")[..6], code });
        var lang = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var projectId = await admin.CreateProjectAsync($"Podpisy cz {Guid.NewGuid()}");
        (await admin.PatchAsJsonAsync($"/api/projects/{projectId}/languages", new { mainLanguageId = lang, multilingual = false })).EnsureSuccessStatusCode();
        var assembly = await admin.CreateNodeAsync(projectId, "Rama", "assembly");

        Assert.StartsWith("No.;Name;Quantity;", await CsvHeaderAsync(admin, assembly));
        (await admin.PutAsJsonAsync($"/api/document-labels/{lang}",
            new { labels = new Dictionary<string, string> { ["csv.no"] = "Poř.", ["csv.name"] = "Název", ["csv.quantity"] = "Množství" } })).EnsureSuccessStatusCode();
        Assert.StartsWith("Poř.;Název;Množství;Material;", await CsvHeaderAsync(admin, assembly));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/document-labels/{lang}",
            new { labels = new Dictionary<string, string> { ["csv.nieznany"] = "X" } })).StatusCode);
    }

    [Fact]
    public async Task Zwykly_uzytkownik_czyta_ale_nie_zmienia_podpisow()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var en = await LanguageIdAsync(admin, "EN");
        var username = "podpisy" + Guid.NewGuid().ToString("N")[..8];
        await admin.CreateUserAsync(username, "haslo123");

        using var user = factory.CreateClient();
        await user.LoginAsync(username, "haslo123");
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/document-labels/{en}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync($"/api/document-labels/{en}",
            new { labels = new Dictionary<string, string> { ["csv.name"] = "X" } })).StatusCode);
    }
}
