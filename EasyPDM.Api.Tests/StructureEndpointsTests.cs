using System.Net;
using System.Net.Http.Json;

namespace EasyPDM.Api.Tests;

[Collection("EasyPDM database")]
public class StructureEndpointsTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    [Fact]
    public async Task Dodanie_dziecka_tworzy_relacje_widoczna_w_bom()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync("Projekt BOM");
        var assemblyId = await client.CreateNodeAsync(projectId, "Zlozenie glowne", "assembly");
        var partId = await client.CreateNodeAsync(projectId, "Czesc", "part");

        var response = await client.PostAsJsonAsync($"/api/items/{assemblyId}/children", new { childId = partId, quantity = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var relations = await client.GetAsync($"/api/projects/{projectId}/relations");
        Assert.Equal(HttpStatusCode.OK, relations.StatusCode);
        var relationsText = await relations.Content.ReadAsStringAsync();
        Assert.Contains(partId.ToString(), relationsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Proba_utworzenia_cyklu_w_strukturze_jest_odrzucana()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync("Projekt cykl");
        var top = await client.CreateNodeAsync(projectId, "Zlozenie A", "assembly");
        var middle = await client.CreateNodeAsync(projectId, "Zlozenie B", "assembly");

        // A -> B (poprawne)
        var first = await client.PostAsJsonAsync($"/api/items/{top}/children", new { childId = middle, quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // B -> A zamknęłoby pętlę — musi zostać odrzucone.
        var cyclic = await client.PostAsJsonAsync($"/api/items/{middle}/children", new { childId = top, quantity = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, cyclic.StatusCode);
    }

    [Fact]
    public async Task Element_nie_moze_byc_podelementem_samego_siebie()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync("Projekt samo-cykl");
        var assemblyId = await client.CreateNodeAsync(projectId, "Zlozenie", "assembly");

        var response = await client.PostAsJsonAsync($"/api/items/{assemblyId}/children", new { childId = assemblyId, quantity = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
