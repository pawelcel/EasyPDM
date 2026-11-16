using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// POST /api/items/cad-versions tells a CAD client whether its local copy of a component is the
// item's current CAD file: by the stamp the client wrote into the file, or by the file's hash
// for files without one -- also when a later upload of the same revision replaced the older file.
[Collection("EasyPDM database")]
public class CadVersionTests
{
    private static async Task<Guid> CreatePartAsync(HttpClient client, Guid projectId)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new
        {
            name = $"Element {Guid.NewGuid()}",
            itemType = "part",
            properties = new { rodzaj = "Wykonywana" },
            parentId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task UploadCadAsync(HttpClient client, Guid itemId, string fileName, string content, string? contentVersion)
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", fileName },
            { new StringContent("cad"), "role" },
        };
        if (contentVersion is not null)
            form.Add(new StringContent(contentVersion), "contentVersion");
        (await client.PostAsync($"/api/items/{itemId}/attachments", form)).EnsureSuccessStatusCode();
    }

    private static string Sha256(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static async Task<JsonElement> CheckAsync(HttpClient client, Guid itemId, string? contentVersion = null, string? fileHash = null)
    {
        var response = await client.PostAsJsonAsync("/api/items/cad-versions",
            new { components = new[] { new { itemId, contentVersion, fileHash } } });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())[0];
    }

    [Fact]
    public async Task Local_copy_is_current_older_or_unknown()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var itemId = await CreatePartAsync(client, await client.CreateProjectAsync($"Versions {Guid.NewGuid()}"));

        Assert.Equal("none", (await CheckAsync(client, itemId, "v1")).GetProperty("local").GetString());

        // Same revision uploaded twice: the second replaces the first attachment, but the first
        // stays known as an older version.
        await UploadCadAsync(client, itemId, "10(Kolumna).A.FCStd", "first", "v1");
        await UploadCadAsync(client, itemId, "10(Kolumna).A.FCStd", "second", "v2");

        var current = await CheckAsync(client, itemId, "v2");
        Assert.Equal("current", current.GetProperty("local").GetString());
        Assert.Equal("A", current.GetProperty("current").GetProperty("revisionLabel").GetString());
        Assert.Equal("w_pracy", current.GetProperty("status").GetString());

        var older = await CheckAsync(client, itemId, "v1");
        Assert.Equal("older", older.GetProperty("local").GetString());
        Assert.Equal("10(Kolumna).A.FCStd", older.GetProperty("localVersion").GetProperty("fileName").GetString());
        Assert.False(string.IsNullOrEmpty(older.GetProperty("current").GetProperty("uploadedBy").GetString()));

        // The stamp decides when there is one: a re-saved file (other hash) with the current
        // stamp is still current, and an unseen stamp is unknown even with a matching hash.
        Assert.Equal("current", (await CheckAsync(client, itemId, "v2", Sha256("re-saved"))).GetProperty("local").GetString());
        Assert.Equal("unknown", (await CheckAsync(client, itemId, "never-uploaded", Sha256("second"))).GetProperty("local").GetString());

        // Without a stamp, the hash.
        Assert.Equal("current", (await CheckAsync(client, itemId, null, Sha256("second").ToLowerInvariant())).GetProperty("local").GetString());
        Assert.Equal("older", (await CheckAsync(client, itemId, null, Sha256("first"))).GetProperty("local").GetString());
        Assert.Equal("unknown", (await CheckAsync(client, itemId, null, Sha256("changed locally"))).GetProperty("local").GetString());
        Assert.Equal("unknown", (await CheckAsync(client, itemId)).GetProperty("local").GetString());
    }

    [Fact]
    public async Task Unknown_items_and_malformed_stamps()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var itemId = await CreatePartAsync(client, await client.CreateProjectAsync($"Versions {Guid.NewGuid()}"));

        Assert.False((await CheckAsync(client, Guid.NewGuid(), "v1")).GetProperty("found").GetBoolean());

        // A stamp in another form is not kept, so the file is known by its hash only.
        await UploadCadAsync(client, itemId, "11(Wal).A.FCStd", "content", "not a <stamp>");
        Assert.Equal("unknown", (await CheckAsync(client, itemId, "not a <stamp>")).GetProperty("local").GetString());
        Assert.Equal("current", (await CheckAsync(client, itemId, null, Sha256("content"))).GetProperty("local").GetString());

        using var anonymous = factory.CreateClient();
        var refused = await anonymous.PostAsJsonAsync("/api/items/cad-versions", new { components = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }
}
