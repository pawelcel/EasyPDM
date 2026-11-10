using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Filtr "Całej bazy" po kilku tagach: element musi mieć wszystkie podane, tak jak inne
// filtry kolejny warunek zawęża wynik. Jeden tag działa jak dotąd.
[Collection("EasyPDM database")]
public class TagFilterTests
{
    private static async Task<List<Guid>> ItemIdsAsync(HttpClient client, string query)
    {
        var items = await client.GetFromJsonAsync<JsonElement>("/api/items" + query);
        return items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    [Fact]
    public async Task Kilka_tagow_pokazuje_elementy_majace_wszystkie()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var projectId = await client.CreateProjectAsync($"Tagi {Guid.NewGuid()}");
        var a = "spaw" + Guid.NewGuid().ToString("N")[..6];
        var b = "lak" + Guid.NewGuid().ToString("N")[..6];

        var both = await client.CreateNodeAsync(projectId, "Oba tagi", "part");
        var onlyA = await client.CreateNodeAsync(projectId, "Tylko pierwszy", "part");
        foreach (var (item, tag) in new[] { (both, a), (both, b), (onlyA, a) })
            (await client.PostAsJsonAsync($"/api/items/{item}/tags", new { name = tag })).EnsureSuccessStatusCode();

        var single = await ItemIdsAsync(client, $"?tag={a}");
        Assert.Contains(both, single);
        Assert.Contains(onlyA, single);

        var all = await ItemIdsAsync(client, $"?tag={a}&tag={b}");
        Assert.Equal(new[] { both }, all);

        // Ten sam tag podany dwa razy to wciąż jeden warunek.
        Assert.Equal(2, (await ItemIdsAsync(client, $"?tag={a}&tag={a}")).Count(id => id == both || id == onlyA));
    }
}
