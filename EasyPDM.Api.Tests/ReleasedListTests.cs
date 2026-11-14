using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Wydane zestawienie zostaje takie, jak w chwili wydania: część, która wraca do pracy (nowa
// rewizja, nowa nazwa, inny materiał), nie zmienia list, które wydano z jej poprzednią rewizją.
[Collection("EasyPDM database")]
public class ReleasedListTests
{
    private static async Task SetStatusAsync(HttpClient client, Guid itemId, string status, bool promoteChildren = false) =>
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/status",
            new { status, comment = (string?)null, promoteChildren })).EnsureSuccessStatusCode();

    private static async Task ReleaseAsync(HttpClient client, Guid itemId)
    {
        await SetStatusAsync(client, itemId, "sprawdzany", promoteChildren: true);
        await SetStatusAsync(client, itemId, "wydany", promoteChildren: true);
    }

    private static async Task<JsonElement[]> ReleaseLinesAsync(HttpClient client, Guid itemId)
    {
        var body = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/released-list");
        return body.GetProperty("lines").EnumerateArray().ToArray();
    }

    private static async Task<int?> RevisionAsync(HttpClient client, Guid itemId)
    {
        var item = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}");
        return item.GetProperty("revisionNumber").ValueKind == JsonValueKind.Null ? null : item.GetProperty("revisionNumber").GetInt32();
    }

    private static async Task<HttpClient> AdminAsync(EasyPDMWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        return client;
    }

    [Fact]
    public async Task Czesc_wracajaca_do_pracy_nie_zmienia_wydanego_zlozenia()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = await AdminAsync(factory);
        var projectId = await client.CreateProjectAsync($"Wydane {Guid.NewGuid()}");
        var assembly = await client.CreateNodeAsync(projectId, "Rama", "assembly");
        var part = await client.CreateNodeAsync(projectId, "Wspornik", "part", assembly);
        (await client.PatchAsJsonAsync($"/api/items/{part}/properties", new { material = "S235" })).EnsureSuccessStatusCode();
        await ReleaseAsync(client, assembly);
        var releasedRevision = await RevisionAsync(client, part);

        // Część wraca do pracy: nowa rewizja, nowa nazwa, inny materiał.
        await SetStatusAsync(client, part, "w_pracy");
        (await client.PatchAsJsonAsync($"/api/items/{part}/name", new { name = "Wspornik nowy" })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/items/{part}/properties", new { material = "S355" })).EnsureSuccessStatusCode();
        Assert.NotEqual(releasedRevision, await RevisionAsync(client, part));

        var line = Assert.Single(await ReleaseLinesAsync(client, assembly));
        Assert.Equal(part, line.GetProperty("itemId").GetGuid());
        Assert.Equal("Wspornik", line.GetProperty("fileName").GetString());
        Assert.Equal("S235", line.GetProperty("properties").GetProperty("material").GetString());
        Assert.Equal(releasedRevision, line.GetProperty("revisionNumber").ValueKind == JsonValueKind.Null ? null : line.GetProperty("revisionNumber").GetInt32());

        // CSV mówi to samo co ekran.
        var csv = Encoding.UTF8.GetString(await client.GetByteArrayAsync($"/api/items/{assembly}/bom/csv"));
        Assert.Contains("S235", csv);
        Assert.DoesNotContain("S355", csv);
        Assert.DoesNotContain("Wspornik nowy", csv);

        // Złożenie w pracy czyta zestawienie na żywo.
        await SetStatusAsync(client, assembly, "w_pracy");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/items/{assembly}/released-list")).StatusCode);
        var live = Encoding.UTF8.GetString(await client.GetByteArrayAsync($"/api/items/{assembly}/bom/csv"));
        Assert.Contains("S355", live);
    }

    [Fact]
    public async Task Wydane_podzlozenie_trzyma_swoja_liste_w_wydanym_zlozeniu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = await AdminAsync(factory);
        var projectId = await client.CreateProjectAsync($"Wydane {Guid.NewGuid()}");
        var top = await client.CreateNodeAsync(projectId, "Maszyna", "assembly");
        var sub = await client.CreateNodeAsync(projectId, "Napęd", "assembly", top);
        var shaft = await client.CreateNodeAsync(projectId, "Wał", "part", sub);
        await ReleaseAsync(client, sub);
        await ReleaseAsync(client, top);

        // Podzłożenie dostaje nową rewizję z dodatkową częścią i znów jest wydane.
        await SetStatusAsync(client, sub, "w_pracy");
        var key = await client.CreateNodeAsync(projectId, "Wpust", "part", sub);
        await ReleaseAsync(client, sub);

        var topLines = await ReleaseLinesAsync(client, top);
        Assert.Equal([sub, shaft], topLines.Select(l => l.GetProperty("itemId").GetGuid()));
        Assert.Equal([1, 2], topLines.Select(l => l.GetProperty("depth").GetInt32()));

        var subLines = await ReleaseLinesAsync(client, sub);
        Assert.Equal(new HashSet<Guid> { shaft, key }, subLines.Select(l => l.GetProperty("itemId").GetGuid()).ToHashSet());

        // /bom (depth >= 2) wydanego złożenia też nie widzi nowej części.
        var nested = await client.GetFromJsonAsync<JsonElement>($"/api/items/{top}/bom");
        Assert.DoesNotContain(nested.EnumerateArray(), e => e.GetProperty("itemId").GetGuid() == key);
    }

    [Fact]
    public async Task Elementu_z_wydanej_listy_nie_mozna_usunac()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = await AdminAsync(factory);
        var projectId = await client.CreateProjectAsync($"Wydane {Guid.NewGuid()}");
        var assembly = await client.CreateNodeAsync(projectId, "Stół", "assembly");
        var part = await client.CreateNodeAsync(projectId, "Noga", "part", assembly);
        await ReleaseAsync(client, assembly);

        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/items/{part}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/items/{part}")).StatusCode);

        // Całe złożenie razem z częścią można usunąć: jego wydana lista znika razem z nim.
        Assert.True((await client.DeleteAsync($"/api/items/{assembly}")).IsSuccessStatusCode);
    }
}
