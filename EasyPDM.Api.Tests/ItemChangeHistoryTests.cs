using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Historia pokazuje, kto zmienił nazwę, właściwości i tagi elementu, z wartością przed i po;
// niezmieniona wartość (makro CAD wysyłające masę i materiał przy każdym zapisie) nic nie dodaje.
[Collection("EasyPDM database")]
public class ItemChangeHistoryTests
{
    private static async Task<List<JsonElement>> HistoryAsync(HttpClient client, Guid itemId, params string[] types)
    {
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/history");
        return list.EnumerateArray().Where(e => types.Contains(e.GetProperty("type").GetString())).ToList();
    }

    private static string? Str(JsonElement e, string name) =>
        e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : e.GetProperty(name).GetString();

    private static async Task<(HttpClient Client, Guid ItemId)> SetUpAsync(EasyPDMWebApplicationFactory factory, string username)
    {
        var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"History {Guid.NewGuid()}");
        var userId = await admin.CreateUserAsync(username, "Haslo-123!-test");
        await admin.GrantProjectAccessAsync(projectId, userId);

        var user = factory.CreateClient();
        await user.LoginAsync(username, "Haslo-123!-test");
        var itemId = await user.CreateNodeAsync(projectId, "Bracket", "part");
        return (user, itemId);
    }

    [Fact]
    public async Task Zmiana_nazwy_zapisuje_autora_stara_i_nowa_nazwe()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var username = $"renamer{Guid.NewGuid():N}"[..20];
        var (client, itemId) = await SetUpAsync(factory, username);

        (await client.PatchAsJsonAsync($"/api/items/{itemId}/name", new { name = "Bracket left" })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/name", new { name = "Bracket left" })).EnsureSuccessStatusCode();

        var entry = Assert.Single(await HistoryAsync(client, itemId, "name_changed"));
        Assert.Equal($"Jan Testowy {username}", Str(entry, "userDisplayName"));
        Assert.Equal("Bracket", Str(entry, "oldValue"));
        Assert.Equal("Bracket left", Str(entry, "newValue"));
    }

    [Fact]
    public async Task Zmiany_wlasciwosci_zapisuja_sie_raz_na_prawdziwa_zmiane()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var username = $"props{Guid.NewGuid():N}"[..20];
        var (client, itemId) = await SetUpAsync(factory, username);

        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { material = "S235", mass = 1.5 })).EnsureSuccessStatusCode();
        // Same values again, as a macro does on every save: nothing new.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { material = "S235", mass = 1.50 })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { material = "S355" })).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/items/{itemId}/properties/mass")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/items/{itemId}/properties/mass")).EnsureSuccessStatusCode();

        var entries = await HistoryAsync(client, itemId, "property_set", "property_removed");
        var material = entries.Where(e => Str(e, "key") == "material").Select(e => (Str(e, "oldValue"), Str(e, "newValue"))).ToHashSet();
        Assert.Equal(new HashSet<(string?, string?)> { (null, "S235"), ("S235", "S355") }, material);

        var mass = entries.Where(e => Str(e, "key") == "mass").ToList();
        Assert.Equal(2, mass.Count);
        Assert.Contains(mass, e => Str(e, "type") == "property_set" && Str(e, "newValue") == "1.5");
        Assert.Contains(mass, e => Str(e, "type") == "property_removed" && Str(e, "oldValue") == "1.5" && Str(e, "newValue") is null);
        Assert.All(entries, e => Assert.Equal($"Jan Testowy {username}", Str(e, "userDisplayName")));

        // The price is recorded; priceBy, the signature the server stamps beside it, is not.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { price = 12.5 })).EnsureSuccessStatusCode();
        var priced = await HistoryAsync(client, itemId, "property_set");
        Assert.Contains(priced, e => Str(e, "key") == "price" && Str(e, "newValue") == "12.5");
        Assert.DoesNotContain(priced, e => Str(e, "key") == "priceBy");
    }

    [Fact]
    public async Task Dodanie_i_zdjecie_tagu_sa_zapisane_a_powtorki_nie()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var username = $"tags{Guid.NewGuid():N}"[..20];
        var (client, itemId) = await SetUpAsync(factory, username);

        (await client.PostAsJsonAsync($"/api/items/{itemId}/tags", new { name = "to-check" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/items/{itemId}/tags", new { name = "to-check" })).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/items/{itemId}/tags/to-check")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/items/{itemId}/tags/to-check")).EnsureSuccessStatusCode();

        var added = Assert.Single(await HistoryAsync(client, itemId, "tag_added"));
        Assert.Equal("to-check", Str(added, "newValue"));
        Assert.Equal($"Jan Testowy {username}", Str(added, "userDisplayName"));
        var removed = Assert.Single(await HistoryAsync(client, itemId, "tag_removed"));
        Assert.Equal("to-check", Str(removed, "oldValue"));
    }

    [Fact]
    public async Task Usuniecie_tagu_z_calej_bazy_zostawia_wpis_na_kazdym_elemencie()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var username = $"tagdel{Guid.NewGuid():N}"[..20];
        var (client, itemId) = await SetUpAsync(factory, username);
        var tag = $"gone-{Guid.NewGuid():N}"[..12];
        (await client.PostAsJsonAsync($"/api/items/{itemId}/tags", new { name = tag })).EnsureSuccessStatusCode();

        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        (await admin.DeleteAsync($"/api/settings/tags/{tag}")).EnsureSuccessStatusCode();

        var removed = Assert.Single(await HistoryAsync(client, itemId, "tag_removed"));
        Assert.Equal(tag, Str(removed, "oldValue"));
    }
}
