using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// The company's side of a project (lead and team with roles) and who works on its assemblies.
[Collection("EasyPDM database")]
public class ProjectTeamTests
{
    [Fact]
    public async Task Project_team_and_assembly_assignees_follow_access_and_the_team()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Zespol {Guid.NewGuid()}");

        async Task<(Guid Id, HttpClient Client)> UserAsync(bool access)
        {
            var name = "pt" + Guid.NewGuid().ToString("N")[..8];
            var id = await admin.CreateUserAsync(name, "haslo123");
            if (access) await admin.GrantProjectAccessAsync(projectId, id);
            var c = factory.CreateClient();
            await c.LoginAsync(name, "haslo123");
            return (id, c);
        }
        var (lead, leadClient) = await UserAsync(true);
        var (ewa, ewaClient) = await UserAsync(true);
        var (outsider, _) = await UserAsync(false);

        async Task<Guid> AssemblyAsync(string name)
        {
            var r = await admin.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new { name, itemType = "assembly", properties = new { } });
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        var frame = await AssemblyAsync("Rama");
        var gearbox = await AssemblyAsync("Przekladnia");

        // Lead and team only from users who can see the project.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/projects/{projectId}/lead-user", new { userId = outsider })).StatusCode);
        (await admin.PutAsJsonAsync($"/api/projects/{projectId}/lead-user", new { userId = lead })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/projects/{projectId}/team", new { userId = outsider, role = "x" })).StatusCode);
        (await admin.PostAsJsonAsync($"/api/projects/{projectId}/team", new { userId = ewa, role = "Konstrukcja" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/projects/{projectId}/team", new { userId = ewa, role = "Technologia" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/projects/{projectId}/team", new { userId = ewa, role = "Konstrukcja" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ewaClient.PostAsJsonAsync($"/api/projects/{projectId}/team", new { userId = ewa, role = "Szef" })).StatusCode);

        var team = await ewaClient.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/team");
        Assert.Equal(lead, team.GetProperty("leadUserId").GetGuid());
        Assert.Equal(2, team.GetProperty("members").GetArrayLength());

        // Only the project's lead hands out the work: not a team member, not an administrator.
        Assert.Equal(HttpStatusCode.Forbidden, (await ewaClient.PostAsJsonAsync($"/api/items/{frame}/assignees", new { userId = ewa })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/items/{frame}/assignees", new { userId = ewa })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leadClient.PostAsJsonAsync($"/api/items/{frame}/assignees", new { userId = outsider })).StatusCode);
        (await leadClient.PostAsJsonAsync($"/api/items/{frame}/assignees", new { userId = ewa })).EnsureSuccessStatusCode();
        (await leadClient.PostAsJsonAsync($"/api/items/{frame}/assignees", new { userId = lead })).EnsureSuccessStatusCode();
        (await leadClient.PostAsJsonAsync($"/api/items/{gearbox}/assignees", new { userId = ewa })).EnsureSuccessStatusCode();   // one person, two assemblies
        (await leadClient.PutAsJsonAsync($"/api/items/{frame}/assignees/{ewa}/lead", new { isLead = true })).EnsureSuccessStatusCode();
        (await leadClient.PutAsJsonAsync($"/api/items/{frame}/assignees/{lead}/lead", new { isLead = true })).EnsureSuccessStatusCode();

        // Ewa is told about both assemblies; the lead, who assigned themselves, is not.
        async Task<List<JsonElement>> AssignedNotesAsync(HttpClient c) =>
            (await c.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items").EnumerateArray()
                .Where(n => n.GetProperty("type").GetString() == "assembly_assigned").ToList();
        var notes = await AssignedNotesAsync(ewaClient);
        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.GetProperty("itemId").GetGuid() == frame && n.GetProperty("data").GetProperty("itemLabel").GetString()!.Contains("Rama"));
        Assert.Empty(await AssignedNotesAsync(leadClient));

        var frameAssignees = await ewaClient.GetFromJsonAsync<JsonElement>($"/api/items/{frame}/assignees");
        Assert.False(frameAssignees.GetProperty("canEdit").GetBoolean());
        var leads = frameAssignees.GetProperty("assignees").EnumerateArray().Where(a => a.GetProperty("isLead").GetBoolean()).Select(a => a.GetProperty("userId").GetGuid());
        Assert.Equal(new[] { lead }, leads);   // only one lead at a time

        // Ewa loses access: off the team and off both assemblies.
        (await admin.DeleteAsync($"/api/projects/{projectId}/users/{ewa}")).EnsureSuccessStatusCode();
        team = await admin.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/team");
        Assert.Equal(0, team.GetProperty("members").GetArrayLength());
        var gearboxAssignees = await admin.GetFromJsonAsync<JsonElement>($"/api/items/{gearbox}/assignees");
        Assert.Equal(0, gearboxAssignees.GetProperty("assignees").GetArrayLength());
        var adminView = await admin.GetFromJsonAsync<JsonElement>($"/api/items/{frame}/assignees");
        Assert.Equal(1, adminView.GetProperty("assignees").GetArrayLength());
        Assert.False(adminView.GetProperty("canEdit").GetBoolean());

        // Clearing the lead takes them off the assembly too (they were assigned as the lead only).
        (await admin.PutAsJsonAsync($"/api/projects/{projectId}/lead-user", new { userId = (Guid?)null })).EnsureSuccessStatusCode();
        Assert.Equal(0, (await admin.GetFromJsonAsync<JsonElement>($"/api/items/{frame}/assignees")).GetProperty("assignees").GetArrayLength());
    }
}
