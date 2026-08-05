using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

[Collection("EasyPDM database")]
public class BomEndpointsTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    // zlozenie1 -> zlozenie2 (ilość 3) -> czesc2 (ilość 2) — czesc2 jest widoczna w BOM-ie
    // zlozenie1 tylko na drugim poziomie (depth 2), z SUROWĄ ilością z jej bezpośredniej
    // relacji (2, NIE 3*2=6 — to rozróżnienie surowa/rozwinięta ilość jest tym, co
    // "/bom" (widok ekranowy) i "/bom/aggregated-csv" (zsumowany eksport) celowo różni).
    private static async Task<(Guid projectId, Guid zlozenie1, Guid zlozenie2, Guid czesc2)> BuildNestedAssemblyAsync(HttpClient client)
    {
        var projectId = await client.CreateProjectAsync("Projekt BOM zaglebiony");
        var zlozenie1 = await client.CreateNodeAsync(projectId, "Zlozenie1", "assembly");
        var zlozenie2 = await client.CreateNodeAsync(projectId, "Zlozenie2", "assembly");

        var createCzesc = await client.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new
        {
            name = "Czesc2",
            itemType = "part",
            properties = new
            {
                rodzaj = "Zakupowa",
                material = "Stal nierdzewna",
                manufacturer = "ACME",
                orderNumber = "ORD-1",
                orderNumber2 = "ORD-2",
            },
            parentId = (Guid?)null,
        });
        createCzesc.EnsureSuccessStatusCode();
        var czesc2Body = await createCzesc.Content.ReadFromJsonAsync<JsonElement>();
        var czesc2 = czesc2Body.GetProperty("id").GetGuid();

        var addZlozenie2 = await client.PostAsJsonAsync($"/api/items/{zlozenie1}/children", new { childId = zlozenie2, quantity = 3 });
        addZlozenie2.EnsureSuccessStatusCode();

        var addCzesc2 = await client.PostAsJsonAsync($"/api/items/{zlozenie2}/children", new { childId = czesc2, quantity = 2 });
        addCzesc2.EnsureSuccessStatusCode();

        return (projectId, zlozenie1, zlozenie2, czesc2);
    }

    [Fact]
    public async Task Bom_zwraca_element_zaglebiony_na_drugim_poziomie_z_surowa_iloscia()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var (_, zlozenie1, _, czesc2) = await BuildNestedAssemblyAsync(client);

        var response = await client.GetAsync($"/api/items/{zlozenie1}/bom");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, entries.GetArrayLength());

        var entry = entries[0];
        Assert.Equal(czesc2, entry.GetProperty("itemId").GetGuid());
        Assert.Equal(2, entry.GetProperty("depth").GetInt32());
        Assert.Equal(2m, entry.GetProperty("quantity").GetDecimal());

        var path = entry.GetProperty("path").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        Assert.Equal(new[] { 1, 1 }, path);
    }

    [Fact]
    public async Task Bom_pomija_bezposrednie_dzieci_bo_te_pokazuje_juz_drzewo_projektu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var (_, _, zlozenie2, czesc2) = await BuildNestedAssemblyAsync(client);

        var response = await client.GetAsync($"/api/items/{zlozenie2}/bom");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entries = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, entries.GetArrayLength());

        // czesc2 istnieje jako bezpośrednie dziecko zlozenie2 (depth 1) — celowo poza
        // wynikiem "/bom", bo to już pokazuje tree.childrenOf po stronie frontendu.
        _ = czesc2;
    }

    [Fact]
    public async Task Bom_zwraca_404_dla_nieistniejacego_elementu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var response = await client.GetAsync($"/api/items/{Guid.NewGuid()}/bom");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var csvResponse = await client.GetAsync($"/api/items/{Guid.NewGuid()}/bom/csv");
        Assert.Equal(HttpStatusCode.NotFound, csvResponse.StatusCode);
    }

    [Fact]
    public async Task Bom_csv_zawiera_naglowek_bom_utf8_i_zaglebiony_wiersz_z_zlozonym_lp()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var (_, zlozenie1, _, _) = await BuildNestedAssemblyAsync(client);

        var response = await client.GetAsync($"/api/items/{zlozenie1}/bom/csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("L.p.;Nazwa;Ilość;Materiał;Producent;Numer zamówieniowy 1;Numer zamówieniowy 2", lines[0]);

        // zlozenie2 to L.p. "1" (pierwsze dziecko zlozenie1), czesc2 pod nim to "1.1".
        Assert.Contains(lines, l => l.StartsWith("1;", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("1.1;", StringComparison.Ordinal) && l.Contains("Stal nierdzewna") && l.Contains("ACME"));
    }

    [Fact]
    public async Task Bom_aggregated_csv_sumuje_rozwinieta_ilosc_przez_cały_lancuch()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var (_, zlozenie1, _, _) = await BuildNestedAssemblyAsync(client);

        var response = await client.GetAsync($"/api/items/{zlozenie1}/bom/aggregated-csv");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        var czescLine = Assert.Single(lines, l => l.Contains("Czesc2"));
        var fields = czescLine.Split(';');
        // zlozenie1 -> zlozenie2 x3 -> czesc2 x2 = 6 sztuk czesc2 łącznie (rozwinięta ilość),
        // w odróżnieniu od surowej ilości "2" widocznej w "/bom" i "/bom/csv".
        Assert.Equal("6", fields[1]);
        Assert.Equal("Stal nierdzewna", fields[2]);
        Assert.Equal("ACME", fields[3]);
    }
}
