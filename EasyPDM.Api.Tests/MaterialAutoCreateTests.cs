using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Materiał przysłany przez makro CAD bierze się z dokumentu, a nie z listy wyboru, więc
// katalog może go jeszcze nie znać. Serwer zakłada go wtedy sam -- inaczej element miałby
// materiał, którego nie da się ani wybrać przy następnej edycji, ani użyć jako filtr.
[Collection("EasyPDM database")]
public class MaterialAutoCreateTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static async Task<List<string>> MaterialNamesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/materials");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().Select(m => m.GetProperty("name").GetString() ?? "").ToList();
    }

    private static async Task<Guid> CreatePartAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new
        {
            name = $"Element {Guid.NewGuid()}",
            itemType = "part",
            properties = new { rodzaj = "Wykonywana" },
            parentId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Nieznany_material_trafia_do_katalogu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Materiały {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId);

        var name = $"Stal {Guid.NewGuid():N}";
        Assert.DoesNotContain(name, await MaterialNamesAsync(client));

        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { material = name }))
            .EnsureSuccessStatusCode();

        // Katalog ma teraz ten materiał...
        Assert.Contains(name, await MaterialNamesAsync(client));
        // ...a element faktycznie go niesie.
        var item = await (await client.GetAsync($"/api/items/{itemId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(name, item.GetProperty("properties").GetProperty("material").GetString());
    }

    [Fact]
    public async Task Znany_material_nie_jest_duplikowany()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Materiały {Guid.NewGuid()}");

        var name = $"Stal {Guid.NewGuid():N}";
        (await client.PostAsJsonAsync("/api/materials", new { name, group = "Stale", subgroup = (string?)null }))
            .EnsureSuccessStatusCode();

        var first = await CreatePartAsync(client, projectId);
        var second = await CreatePartAsync(client, projectId);
        (await client.PatchAsJsonAsync($"/api/items/{first}/properties", new { material = name })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/items/{second}/properties", new { material = name })).EnsureSuccessStatusCode();

        // Jeden wpis mimo dwóch zapisów — i grupa nadana ręcznie NIE zostaje zdeptana.
        var materials = await (await client.GetAsync("/api/materials")).Content.ReadFromJsonAsync<JsonElement>();
        var matching = materials.EnumerateArray().Where(m => m.GetProperty("name").GetString() == name).ToList();
        Assert.Single(matching);
        Assert.Equal("Stale", matching[0].GetProperty("group").GetString());
    }

    [Fact]
    public async Task Pusty_material_niczego_nie_zaklada()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Materiały {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId);

        var before = (await MaterialNamesAsync(client)).Count;
        // Wyczyszczenie pola (pusty string) to normalna edycja, nie powód do zakładania wpisu.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { material = "" }))
            .EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { material = "   " }))
            .EnsureSuccessStatusCode();

        Assert.Equal(before, (await MaterialNamesAsync(client)).Count);
    }
}
