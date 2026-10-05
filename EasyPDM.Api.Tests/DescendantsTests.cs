using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// GET /items/{id}/descendants — element i całe jego poddrzewo spłaszczone jednym zapytaniem.
// Istnieje dla listy postępu przy POBIERANIU: makro schodzi poziom po poziomie i w momencie
// startu nie wie, ile plików będzie, więc bez tego pasek postępu kłamałby.
[Collection("EasyPDM database")]
public class DescendantsTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static async Task<Guid> CreateAsync(HttpClient client, Guid projectId, string name, string itemType)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new
        {
            name,
            itemType,
            properties = new { rodzaj = "Wykonywana" },
            parentId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task LinkAsync(HttpClient client, Guid parent, Guid child) =>
        (await client.PostAsJsonAsync($"/api/items/{parent}/children", new { childId = child, quantity = 1 }))
            .EnsureSuccessStatusCode();

    private static async Task<List<string>> DescendantsAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/items/{id}/descendants");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().Select(e => e.GetProperty("recordName").GetString() ?? "").ToList();
    }

    [Fact]
    public async Task Zwraca_element_i_cale_poddrzewo_bez_duplikatow()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Poddrzewo {Guid.NewGuid()}");

        var frame = await CreateAsync(client, projectId, "frame", "assembly");
        var sub = await CreateAsync(client, projectId, "sub", "assembly");
        var plate = await CreateAsync(client, projectId, "plate", "part");
        var pin = await CreateAsync(client, projectId, "pin", "part");

        await LinkAsync(client, frame, sub);
        await LinkAsync(client, sub, plate);
        await LinkAsync(client, sub, pin);
        // Ta sama część użyta DWA razy w różnych miejscach drzewa — makro pobiera ją raz
        // (pilnuje tego słownik "seen"), więc i tu ma wystąpić raz, inaczej licznik
        // "x z y" nigdy nie doszedłby do końca.
        await LinkAsync(client, frame, plate);

        var names = await DescendantsAsync(client, frame);
        Assert.Equal(4, names.Count);
        Assert.Single(names, n => n.Contains("plate"));
        Assert.Contains(names, n => n.Contains("frame"));
        Assert.Contains(names, n => n.Contains("pin"));
    }

    [Fact]
    public async Task Element_bez_dzieci_zwraca_sam_siebie()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Poddrzewo {Guid.NewGuid()}");

        var lone = await CreateAsync(client, projectId, "wspornik", "part");

        // Pobranie pojedynczej części to też bieg — lista ma mieć jedną pozycję, nie zero.
        var names = await DescendantsAsync(client, lone);
        Assert.Single(names);
        Assert.Contains("wspornik", names[0]);
    }
}
