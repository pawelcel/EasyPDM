using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// /api/projects/{id}/contacts: the people on the client's side involved in a project, each with
// a role, picked from the same contacts as the project lead.
[Collection("EasyPDM database")]
public class ProjectContactTests
{
    private static async Task<int> PostIdAsync(HttpClient client, string url, object body)
    {
        var r = await client.PostAsJsonAsync(url, body);
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static object Project(string name, int? clientId, int? name2Id) => new
    {
        name, description = (string?)null, clientId, clientName2Id = name2Id, leadContactId = (int?)null,
        closed = false, startDate = (DateOnly?)null, endDate = (DateOnly?)null,
    };

    [Fact]
    public async Task People_on_the_client_side_get_a_role_and_follow_the_projects_client()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");

        var clientId = await PostIdAsync(admin, "/api/clients", new { name = $"Klient C {Guid.NewGuid()}", location = (string?)null });
        var otherClientId = await PostIdAsync(admin, "/api/clients", new { name = $"Klient D {Guid.NewGuid()}", location = (string?)null });
        var name2Id = await PostIdAsync(admin, $"/api/clients/{clientId}/name2", new { name2 = "Oddzial C" });
        var anna = await PostIdAsync(admin, $"/api/clients/{clientId}/contacts", new { firstName = "Anna", lastName = "Zakupy" });
        var piotr = await PostIdAsync(admin, $"/api/clients/{clientId}/name2/{name2Id}/contacts", new { firstName = "Piotr", lastName = "Odbior" });
        var obcy = await PostIdAsync(admin, $"/api/clients/{otherClientId}/contacts", new { firstName = "Obcy", lastName = "Kontakt" });

        var r = await admin.PostAsJsonAsync("/api/projects", Project($"Projekt C {Guid.NewGuid()}", clientId, name2Id));
        r.EnsureSuccessStatusCode();
        var projectBody = await r.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = projectBody.GetProperty("id").GetGuid();
        var projectName = projectBody.GetProperty("name").GetString()!;

        (await admin.PostAsJsonAsync($"/api/projects/{projectId}/contacts", new { contactId = anna, role = "Zakupy" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/projects/{projectId}/contacts", new { contactId = piotr, role = " Odbiór techniczny " })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/projects/{projectId}/contacts", new { contactId = anna, role = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/projects/{projectId}/contacts", new { contactId = obcy, role = "x" })).StatusCode);

        (await admin.PatchAsJsonAsync($"/api/projects/{projectId}/contacts/{anna}", new { role = "Zakupy i logistyka" })).EnsureSuccessStatusCode();
        var list = await admin.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/contacts");
        var roles = list.EnumerateArray().ToDictionary(c => c.GetProperty("contactId").GetInt32(), c => c.GetProperty("role").GetString());
        Assert.Equal("Zakupy i logistyka", roles[anna]);
        Assert.Equal("Odbiór techniczny", roles[piotr]);

        // A user with access sees them but cannot change them.
        var userName = "pc" + Guid.NewGuid().ToString("N")[..8];
        var userId = await admin.CreateUserAsync(userName, "haslo123");
        await admin.GrantProjectAccessAsync(projectId, userId);
        using var user = factory.CreateClient();
        await user.LoginAsync(userName, "haslo123");
        Assert.Equal(2, (await user.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/contacts")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/projects/{projectId}/contacts/{anna}")).StatusCode);

        // Dropping the project's Name 2 drops the person who belongs to it; the client's own stays.
        (await admin.PatchAsJsonAsync($"/api/projects/{projectId}", Project(projectName, clientId, null))).EnsureSuccessStatusCode();
        list = await admin.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/contacts");
        Assert.Equal(new[] { anna }, list.EnumerateArray().Select(c => c.GetProperty("contactId").GetInt32()));

        (await admin.DeleteAsync($"/api/projects/{projectId}/contacts/{anna}")).EnsureSuccessStatusCode();
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/contacts")).GetArrayLength());
    }
}
