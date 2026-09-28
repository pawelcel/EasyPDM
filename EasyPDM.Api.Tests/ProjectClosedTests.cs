using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Zamykanie/otwieranie projektu przez PATCH /api/projects/{id}/closed — osobny endpoint od
// zapisu całego formularza właściwości, bo przycisk stoi w belce nad drzewem, POZA tym
// formularzem. Gdyby wysyłał cały obiekt projektu, kliknięcie zaraz po edycji pola cofałoby
// tę edycję (blur pola i ten PATCH to dwa równoległe zapisy o niegwarantowanej kolejności).
[Collection("EasyPDM database")]
public class ProjectClosedTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static async Task<JsonElement> GetProjectAsync(HttpClient client, Guid projectId)
    {
        var response = await client.GetAsync("/api/projects");
        response.EnsureSuccessStatusCode();
        var all = await response.Content.ReadFromJsonAsync<JsonElement>();
        return all.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == projectId);
    }

    [Fact]
    public async Task Zamkniecie_i_otwarcie_przestawia_wylacznie_flage()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var name = $"Projekt zamykanie {Guid.NewGuid()}";
        var createResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            name,
            description = "opis do zachowania",
            clientId = (int?)null,
            clientName2Id = (int?)null,
            leadContactId = (int?)null,
            closed = false,
            startDate = (DateOnly?)null,
            endDate = (DateOnly?)null,
        });
        createResponse.EnsureSuccessStatusCode();
        var projectId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var closeResponse = await client.PatchAsJsonAsync($"/api/projects/{projectId}/closed", new { closed = true });
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        var closed = await GetProjectAsync(client, projectId);
        Assert.True(closed.GetProperty("closed").GetBoolean());
        // Reszta właściwości musi przetrwać nietknięta — to jest cały powód osobnego endpointu.
        Assert.Equal(name, closed.GetProperty("name").GetString());
        Assert.Equal("opis do zachowania", closed.GetProperty("description").GetString());

        (await client.PatchAsJsonAsync($"/api/projects/{projectId}/closed", new { closed = false }))
            .EnsureSuccessStatusCode();
        Assert.False((await GetProjectAsync(client, projectId)).GetProperty("closed").GetBoolean());
    }

    [Fact]
    public async Task Zwykly_uzytkownik_nie_moze_zamknac_projektu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var adminClient = factory.CreateClient();
        await adminClient.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await adminClient.CreateProjectAsync($"Projekt zamykanie uprawnienia {Guid.NewGuid()}");
        var username = $"zwykly{Guid.NewGuid():N}"[..20];
        var userId = await adminClient.CreateUserAsync(username, "haslo123");
        await adminClient.GrantProjectAccessAsync(projectId, userId);

        using var userClient = factory.CreateClient();
        await userClient.LoginAsync(username, "haslo123");

        var response = await userClient.PatchAsJsonAsync($"/api/projects/{projectId}/closed", new { closed = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Nieistniejacy_projekt_zwraca_404()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var response = await client.PatchAsJsonAsync($"/api/projects/{Guid.NewGuid()}/closed", new { closed = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
