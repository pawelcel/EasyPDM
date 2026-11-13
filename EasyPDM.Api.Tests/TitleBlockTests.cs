using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// GET /api/items/{id}/title-block: the data a drawing title block is filled from.
[Collection("EasyPDM database")]
public class TitleBlockTests
{
    private static async Task<int> LanguageIdAsync(HttpClient client, string code)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/project-languages");
        return list.EnumerateArray().First(l => l.GetProperty("code").GetString() == code).GetProperty("id").GetInt32();
    }

    private static async Task<HttpClient> UserAsync(EasyPDMWebApplicationFactory factory, HttpClient admin, Guid projectId, string first, string last)
    {
        var username = "tb" + Guid.NewGuid().ToString("N")[..8];
        var id = await admin.CreateUserAsync(username, "haslo123");
        (await admin.PatchAsJsonAsync($"/api/users/{id}", new { firstName = first, lastName = last })).EnsureSuccessStatusCode();
        await admin.GrantProjectAccessAsync(projectId, id);
        var client = factory.CreateClient();
        await client.LoginAsync(username, "haslo123");
        return client;
    }

    private static Task<JsonElement> TitleBlockAsync(HttpClient client, Guid itemId) =>
        client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/title-block");

    [Fact]
    public async Task Title_block_follows_the_project_language_and_the_current_revision()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var pl = await LanguageIdAsync(admin, "PL");
        var en = await LanguageIdAsync(admin, "EN");
        var projectId = await admin.CreateProjectAsync($"Tabelka {Guid.NewGuid()}");
        (await admin.PatchAsJsonAsync($"/api/projects/{projectId}/languages",
            new { mainLanguageId = pl, multilingual = true, languageIds = new[] { en } })).EnsureSuccessStatusCode();

        using var anna = await UserAsync(factory, admin, projectId, "Anna", "Nowak");
        using var jan = await UserAsync(factory, admin, projectId, "Jan", "Kowalski");

        var created = await anna.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new
        {
            name = "Kostka",
            itemType = "part",
            properties = new
            {
                rodzaj = "Wykonywana",
                material = "S355J2",
                mass = "2.4",
                nameTexts = new[] { new { languageId = en, name = "Cube" } },
                processes = new[]
                {
                    new
                    {
                        id = 1, name = "Hartowanie", description = "do 50 HRC",
                        texts = new[]
                        {
                            new { languageId = pl, name = "Hartowanie", description = "do 50 HRC" },
                            new { languageId = en, name = "Hardening", description = "to 50 HRC" },
                        },
                    },
                },
            },
        });
        created.EnsureSuccessStatusCode();
        var itemId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Nothing reviewed or released yet: both names empty, labels already in Polish.
        var tb = await TitleBlockAsync(anna, itemId);
        Assert.Equal("PL", tb.GetProperty("languageCode").GetString());
        Assert.Equal("Narysował", tb.GetProperty("labels").GetProperty("drawnBy").GetString());
        Assert.Equal(JsonValueKind.Null, tb.GetProperty("drawnBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, tb.GetProperty("approvedBy").ValueKind);
        Assert.Equal("Kostka", tb.GetProperty("name").GetString());
        var translation = tb.GetProperty("nameTranslations").EnumerateArray().Single();
        Assert.Equal("EN", translation.GetProperty("languageCode").GetString());
        Assert.Equal("Cube", translation.GetProperty("name").GetString());
        var process = tb.GetProperty("processes").EnumerateArray().Single();
        Assert.Equal("Hartowanie", process.GetProperty("name").GetString());
        Assert.Equal("to 50 HRC", process.GetProperty("translations").EnumerateArray().Single().GetProperty("description").GetString());
        Assert.Equal("S355J2", tb.GetProperty("material").GetString());
        Assert.Equal("2.4", tb.GetProperty("mass").GetString());

        (await anna.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "sprawdzany" })).EnsureSuccessStatusCode();
        // The author's owner lock would keep the reviewer out; hand the item over.
        (await anna.PostAsync($"/api/items/{itemId}/release", null)).EnsureSuccessStatusCode();
        (await jan.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "wydany" })).EnsureSuccessStatusCode();
        tb = await TitleBlockAsync(anna, itemId);
        Assert.Equal("Anna Nowak", tb.GetProperty("drawnBy").GetProperty("name").GetString());
        Assert.Equal("Jan Kowalski", tb.GetProperty("approvedBy").GetProperty("name").GetString());
        Assert.Equal("A", tb.GetProperty("revisionLabel").GetString());
        Assert.Empty(tb.GetProperty("revisions").EnumerateArray());

        // A new revision starts clean: the approval of A does not carry over to B.
        (await jan.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "w_pracy", comment = "Thicker wall" })).EnsureSuccessStatusCode();
        tb = await TitleBlockAsync(anna, itemId);
        Assert.Equal("B", tb.GetProperty("revisionLabel").GetString());
        Assert.Equal(JsonValueKind.Null, tb.GetProperty("drawnBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, tb.GetProperty("approvedBy").ValueKind);
        var revision = tb.GetProperty("revisions").EnumerateArray().Single();
        Assert.Equal("B", revision.GetProperty("revisionLabel").GetString());
        Assert.Equal("Thicker wall", revision.GetProperty("description").GetString());
        Assert.Equal("Jan Kowalski", revision.GetProperty("by").GetString());

        // Translations only on a multilingual project.
        (await admin.PatchAsJsonAsync($"/api/projects/{projectId}/languages",
            new { mainLanguageId = pl, multilingual = false, languageIds = Array.Empty<int>() })).EnsureSuccessStatusCode();
        tb = await TitleBlockAsync(anna, itemId);
        Assert.Empty(tb.GetProperty("nameTranslations").EnumerateArray());
        Assert.Empty(tb.GetProperty("processes").EnumerateArray().Single().GetProperty("translations").EnumerateArray());
    }
}
