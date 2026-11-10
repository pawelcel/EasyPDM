using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Katalog obróbek (menu "Obróbka") i ich wybór w elemencie wykonywanym: element trzyma listę
// nazw w properties.processes.
[Collection("EasyPDM database")]
public class ProcessCatalogTests
{
    [Fact]
    public async Task Katalog_obrobek_dodaje_zmienia_i_usuwa()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var name = "Spawanie " + Guid.NewGuid().ToString("N")[..6];

        var created = await client.PostAsJsonAsync("/api/processes",
            new { name, description = "MIG", forParts = false, forAssemblies = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Ta sama nazwa inną wielkością liter to wciąż ta sama obróbka.
        var duplicate = await client.PostAsJsonAsync("/api/processes",
            new { name = name.ToUpperInvariant(), forParts = true, forAssemblies = false });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Obróbka musi czegoś dotyczyć.
        var nothing = await client.PostAsJsonAsync("/api/processes",
            new { name = name + " x", forParts = false, forAssemblies = false });
        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);

        (await client.PatchAsJsonAsync($"/api/processes/{id}",
            new { name, description = "MIG/MAG", forParts = true, forAssemblies = true })).EnsureSuccessStatusCode();
        var listed = (await client.GetFromJsonAsync<JsonElement>("/api/processes"))
            .EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == id);
        Assert.Equal("MIG/MAG", listed.GetProperty("description").GetString());
        Assert.True(listed.GetProperty("forParts").GetBoolean());
        Assert.True(listed.GetProperty("forAssemblies").GetBoolean());

        (await client.DeleteAsync($"/api/processes/{id}")).EnsureSuccessStatusCode();
        Assert.DoesNotContain((await client.GetFromJsonAsync<JsonElement>("/api/processes")).EnumerateArray(),
            p => p.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task Element_zapisuje_kilka_obrobek_albo_zadna()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var projectId = await client.CreateProjectAsync($"Obrobki {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectId, "Wspornik", "part");

        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties",
            new { processes = new[] { "Ciecie laserem", "Giecie" } })).EnsureSuccessStatusCode();
        var item = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}");
        Assert.Equal(new[] { "Ciecie laserem", "Giecie" },
            item.GetProperty("properties").GetProperty("processes").EnumerateArray().Select(x => x.GetString()));

        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties",
            new { processes = Array.Empty<string>() })).EnsureSuccessStatusCode();
        item = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}");
        Assert.Equal(0, item.GetProperty("properties").GetProperty("processes").GetArrayLength());

        // Wyszukiwanie w "Całej bazie" przeszukuje właściwości, więc trafia też po obróbce.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties",
            new { processes = new[] { "Malowanie proszkowe" } })).EnsureSuccessStatusCode();
        var found = await client.GetFromJsonAsync<JsonElement>("/api/items?search=proszkowe");
        Assert.Contains(found.EnumerateArray(), i => i.GetProperty("id").GetGuid() == itemId);
    }
}
