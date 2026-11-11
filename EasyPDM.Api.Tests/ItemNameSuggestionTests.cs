using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Podpowiedzi nazw przy tworzeniu elementu: nazwa istniejącego elementu w danym języku i jej
// tłumaczenia. Nazwa elementu jest w głównym języku jego projektu, tłumaczenia w nameTexts.
[Collection("EasyPDM database")]
public class ItemNameSuggestionTests
{
    [Fact]
    public async Task Podpowiada_nazwe_w_jezyku_z_tlumaczeniami()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var languages = await client.GetFromJsonAsync<JsonElement>("/api/project-languages");
        int Lang(string code) => languages.EnumerateArray().Single(l => l.GetProperty("code").GetString() == code).GetProperty("id").GetInt32();
        var en = Lang("EN");
        var pl = Lang("PL");
        var tag = Guid.NewGuid().ToString("N")[..6];

        // Projekt po polsku: nazwa "Kostka" jest polska, angielskie tłumaczenie w nameTexts.
        var projectId = await client.CreateProjectAsync($"Nazwy {tag}");
        (await client.PatchAsJsonAsync($"/api/projects/{projectId}/languages",
            new { mainLanguageId = pl, multilingual = false })).EnsureSuccessStatusCode();
        var item = await client.CreateNodeAsync(projectId, "Kostka " + tag, "part");
        (await client.PatchAsJsonAsync($"/api/items/{item}/properties",
            new { nameTexts = new[] { new { languageId = en, name = "Cube " + tag } } })).EnsureSuccessStatusCode();

        async Task<JsonElement?> FindAsync(int languageId, string name, string itemType = "part")
        {
            var list = await client.GetFromJsonAsync<JsonElement>($"/api/item-names?languageId={languageId}&itemType={itemType}");
            foreach (var entry in list.EnumerateArray())
                if (entry.GetProperty("name").GetString() == name)
                    return entry;
            return null;
        }

        var polish = await FindAsync(pl, "Kostka " + tag);
        Assert.NotNull(polish);
        var texts = polish.Value.GetProperty("texts").EnumerateArray().ToList();
        Assert.Equal(en, Assert.Single(texts).GetProperty("languageId").GetInt32());
        Assert.Equal("Cube " + tag, texts[0].GetProperty("name").GetString());

        // Po angielsku podpowiada "Cube" z polskim tłumaczeniem "Kostka".
        var english = await FindAsync(en, "Cube " + tag);
        Assert.NotNull(english);
        Assert.Equal("Kostka " + tag, Assert.Single(english.Value.GetProperty("texts").EnumerateArray()).GetProperty("name").GetString());

        // Polska nazwa nie trafia do angielskich podpowiedzi, a część do złożeń.
        Assert.Null(await FindAsync(en, "Kostka " + tag));
        Assert.Null(await FindAsync(pl, "Kostka " + tag, "assembly"));
    }
}
