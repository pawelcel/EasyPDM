using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// POST /api/nodes -- backend checkboxa "Dodaj do projektu" w AddNodeDialog (zob. jego
// komentarz we froncie). Tworzy element z project_id = NULL, opcjonalnie od razu jako
// dziecko w BOM-ie (parentId) -- dokładnie ten kształt, którego potrzebuje makro CAD przy
// automatycznym tworzeniu nowych komponentów złożenia, żeby NIE zaśmiecały drzewa żadnego
// projektu, będąc widoczne tylko jako pozycje BOM-u rodzica.
[Collection("EasyPDM database")]
public class CreateNodeWithoutProjectTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    [Fact]
    public async Task Tworzy_element_bez_projektu_widoczny_tylko_przez_cala_baze()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var response = await client.PostAsJsonAsync("/api/nodes", new
        {
            name = "Czesc bez projektu",
            itemType = "part",
            properties = new { rodzaj = "Klienta" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var itemId = created.GetProperty("id").GetGuid();

        // "Cała baza" (GET /api/items bez projectId) musi go znaleźć mimo braku projektu --
        // to jedyne miejsce, gdzie taki element jest w ogóle widoczny.
        var wholeDb = await client.GetAsync("/api/items");
        Assert.Equal(HttpStatusCode.OK, wholeDb.StatusCode);
        var items = await wholeDb.Content.ReadFromJsonAsync<JsonElement>();
        var match = items.EnumerateArray().FirstOrDefault(i => i.GetProperty("id").GetGuid() == itemId);
        Assert.True(match.ValueKind == JsonValueKind.Object, "Element bez projektu powinien być widoczny w \"Cała baza\".");
        Assert.True(match.GetProperty("projectId").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Element_bez_projektu_z_parentId_trafia_do_bom_rodzica()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync("Projekt zlozenia");
        var assemblyId = await client.CreateNodeAsync(projectId, "Zlozenie", "assembly");

        var response = await client.PostAsJsonAsync("/api/nodes", new
        {
            name = "Nowy komponent",
            itemType = "part",
            properties = new { rodzaj = "Klienta" },
            parentId = assemblyId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var childId = created.GetProperty("id").GetGuid();

        // Mimo braku projektu, komponent musi być widoczny w BOM-ie rodzica -- item_relations
        // jest niezależne od project_id (współdzielony komponent może już dziś należeć do
        // innego projektu niż jego rodzic).
        var relations = await client.GetAsync($"/api/projects/{projectId}/relations");
        Assert.Equal(HttpStatusCode.OK, relations.StatusCode);
        var relationsText = await relations.Content.ReadAsStringAsync();
        Assert.Contains(childId.ToString(), relationsText, StringComparison.OrdinalIgnoreCase);
    }
}
