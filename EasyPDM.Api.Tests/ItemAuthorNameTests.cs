using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// The item panel names who did each thing next to it -- uploaded a file, opened a revision,
// entered the price -- by first and last name, not only in the item's History.
[Collection("EasyPDM database")]
public class ItemAuthorNameTests
{
    [Fact]
    public async Task File_revision_and_price_carry_the_authors_full_name()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "autor" + Guid.NewGuid().ToString("N")[..8];
        var userId = await admin.CreateUserAsync(username, "haslo123");
        (await admin.PatchAsJsonAsync($"/api/users/{userId}", new { firstName = "Anna", lastName = "Nowak" })).EnsureSuccessStatusCode();
        var projectId = await admin.CreateProjectAsync($"Autor {Guid.NewGuid()}");
        await admin.GrantProjectAccessAsync(projectId, userId);

        using var client = factory.CreateClient();
        await client.LoginAsync(username, "haslo123");
        var created = await client.PostAsJsonAsync($"/api/projects/{projectId}/nodes",
            new { name = "Kostka", itemType = "part", properties = new { rodzaj = "Wykonywana" } });
        created.EnsureSuccessStatusCode();
        var itemId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("cad")), "file", "1(Kostka).A.FCStd" },
            { new StringContent("cad"), "role" },
        })
        {
            (await client.PostAsync($"/api/items/{itemId}/attachments", form)).EnsureSuccessStatusCode();
        }
        var attachments = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/attachments");
        Assert.Equal("Anna Nowak", attachments.EnumerateArray().Single().GetProperty("uploadedBy").GetString());

        // The name comes from the session; one sent in the request is ignored.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties",
            new { price = 12.5, priceDate = "2026-11-12", priceBy = "Someone Else" })).EnsureSuccessStatusCode();
        var item = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}");
        Assert.Equal("Anna Nowak", item.GetProperty("properties").GetProperty("priceBy").GetString());

        foreach (var status in new[] { "sprawdzany", "wydany" })
            (await client.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/status",
            new { status = "w_pracy", comment = "Thicker wall" })).EnsureSuccessStatusCode();
        var revisions = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/revisions");
        var revision = revisions.EnumerateArray().Single();
        Assert.Equal("Thicker wall", revision.GetProperty("comment").GetString());
        Assert.Equal("Anna Nowak", revision.GetProperty("createdBy").GetString());

        // The History list names people the same way.
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/history");
        Assert.All(history.EnumerateArray(), e => Assert.Equal("Anna Nowak", e.GetProperty("userDisplayName").GetString()));
    }
}
