using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// "My work": progress reports on assigned assemblies, deleting the latest one in time, and the
// project lead's review.
[Collection("EasyPDM database")]
public class MyWorkTests
{
    [Fact]
    public async Task Progress_reports_are_written_deleted_in_time_and_reviewed_by_the_lead()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Postep {Guid.NewGuid()}");

        async Task<(Guid Id, HttpClient Client)> UserAsync()
        {
            var name = "mw" + Guid.NewGuid().ToString("N")[..8];
            var id = await admin.CreateUserAsync(name, "haslo123");
            await admin.GrantProjectAccessAsync(projectId, id);
            var c = factory.CreateClient();
            await c.LoginAsync(name, "haslo123");
            return (id, c);
        }
        var (lead, leadClient) = await UserAsync();
        var (ewa, ewaClient) = await UserAsync();
        var (jan, janClient) = await UserAsync();

        var r = await admin.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new { name = "Rama", itemType = "assembly", properties = new { } });
        r.EnsureSuccessStatusCode();
        var frame = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await admin.PutAsJsonAsync($"/api/projects/{projectId}/lead-user", new { userId = lead })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/projects/{projectId}/team", new { userId = ewa, role = "Konstrukcja" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/projects/{projectId}/team", new { userId = jan, role = "Rysunki" })).EnsureSuccessStatusCode();
        (await leadClient.PostAsJsonAsync($"/api/items/{frame}/assignees", new { userId = ewa })).EnsureSuccessStatusCode();

        // Only someone assigned reports; percent within 0..100.
        Assert.Equal(HttpStatusCode.Forbidden, (await janClient.PostAsJsonAsync($"/api/items/{frame}/progress", new { note = "x", percent = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ewaClient.PostAsJsonAsync($"/api/items/{frame}/progress", new { note = "x", percent = 120 })).StatusCode);
        async Task<long> ReportAsync(string note, int percent)
        {
            var res = await ewaClient.PostAsJsonAsync($"/api/items/{frame}/progress", new { note, percent });
            res.EnsureSuccessStatusCode();
            return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        }
        var first = await ReportAsync("Szkielet ramy", 30);
        var second = await ReportAsync("Mocowania silnika", 60);

        // Ewa sees the project and the assembly; only her latest entry can be deleted.
        var mine = await ewaClient.GetFromJsonAsync<JsonElement>("/api/my-work");
        var project = mine.GetProperty("projects").EnumerateArray().Single(p => p.GetProperty("projectId").GetGuid() == projectId);
        Assert.False(project.GetProperty("isLead").GetBoolean());
        var assembly = project.GetProperty("assemblies").EnumerateArray().Single();
        Assert.True(assembly.GetProperty("canReport").GetBoolean());
        var entries = assembly.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(new[] { 30, 60 }, entries.Select(e => e.GetProperty("percent").GetInt32()));
        Assert.Equal(new[] { false, true }, entries.Select(e => e.GetProperty("canDelete").GetBoolean()));
        Assert.Equal(HttpStatusCode.Conflict, (await ewaClient.DeleteAsync($"/api/progress/{first}")).StatusCode);
        (await ewaClient.DeleteAsync($"/api/progress/{second}")).EnsureSuccessStatusCode();

        // Jan, on the team but not assigned, sees the project without the assembly.
        var jans = await janClient.GetFromJsonAsync<JsonElement>("/api/my-work");
        Assert.Empty(jans.GetProperty("projects").EnumerateArray().Single(p => p.GetProperty("projectId").GetGuid() == projectId)
            .GetProperty("assemblies").EnumerateArray());

        // The lead sees it and reviews: remarks need a comment; Ewa is told.
        var leads = await leadClient.GetFromJsonAsync<JsonElement>("/api/my-work");
        Assert.True(leads.GetProperty("projects").EnumerateArray().Single(p => p.GetProperty("projectId").GetGuid() == projectId).GetProperty("isLead").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await janClient.PutAsJsonAsync($"/api/progress/{first}/review", new { status = "accepted" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leadClient.PutAsJsonAsync($"/api/progress/{first}/review", new { status = "remarks", comment = "" })).StatusCode);
        (await leadClient.PutAsJsonAsync($"/api/progress/{first}/review", new { status = "remarks", comment = "Dodaj wzmocnienia" })).EnsureSuccessStatusCode();
        var notes = (await ewaClient.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("type").GetString() == "progress_reviewed").ToList();
        Assert.Single(notes);
        Assert.Equal("remarks", notes[0].GetProperty("data").GetProperty("status").GetString());

        // A reviewed entry can no longer be deleted; with 0 minutes allowed, none can.
        var third = await ReportAsync("Wzmocnienia", 70);
        Assert.Equal(HttpStatusCode.Forbidden, (await ewaClient.PatchAsJsonAsync("/api/settings/progress-policy", new { deleteMinutes = 0 })).StatusCode);
        (await admin.PatchAsJsonAsync("/api/settings/progress-policy", new { deleteMinutes = 0 })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await ewaClient.DeleteAsync($"/api/progress/{third}")).StatusCode);
        (await admin.PatchAsJsonAsync("/api/settings/progress-policy", new { deleteMinutes = 15 })).EnsureSuccessStatusCode();
        (await leadClient.PutAsJsonAsync($"/api/progress/{third}/review", new { status = "accepted" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await ewaClient.DeleteAsync($"/api/progress/{third}")).StatusCode);
    }
}
