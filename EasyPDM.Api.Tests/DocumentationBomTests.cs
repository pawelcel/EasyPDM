using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// GET /api/items/{id}/documentation?bom=...: an assembly's documentation ZIP can carry its BOM
// CSVs next to the attachments, and asking for the BOM alone must not pull in every attachment
// (no "ext" otherwise means all of them).
[Collection("EasyPDM database")]
public class DocumentationBomTests
{
    private static async Task<List<string>> ZipEntriesAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    [Fact]
    public async Task Documentation_zip_includes_the_selected_bom_csvs()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Doc BOM {Guid.NewGuid()}");

        async Task<Guid> NodeAsync(string name, string type)
        {
            var r = await admin.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new { name, itemType = type, properties = new { } });
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        var frame = await NodeAsync("Rama", "assembly");
        var leg = await NodeAsync("Noga", "part");
        (await admin.PostAsJsonAsync($"/api/items/{frame}/children", new { childId = leg, quantity = 4 })).EnsureSuccessStatusCode();
        using (var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("rysunek")), "file", "noga.pdf" },
        })
            (await admin.PostAsync($"/api/items/{leg}/attachments", form)).EnsureSuccessStatusCode();

        var both = await ZipEntriesAsync(admin, $"/api/items/{frame}/documentation?ext=pdf&bom=full&bom=aggregated");
        Assert.Equal(3, both.Count);
        Assert.Contains(both, e => e.StartsWith("BOM_") && !e.StartsWith("BOM_zsumowany_") && e.EndsWith(".csv"));
        Assert.Contains(both, e => e.StartsWith("BOM_zsumowany_") && e.EndsWith(".csv"));
        Assert.Contains(both, e => e.EndsWith("/noga.pdf"));

        // The BOM alone: no attachments.
        var bomOnly = await ZipEntriesAsync(admin, $"/api/items/{frame}/documentation?bom=full");
        var entry = Assert.Single(bomOnly);
        Assert.StartsWith("BOM_", entry);

        // Without "bom" nothing changes: attachments only.
        var plain = await ZipEntriesAsync(admin, $"/api/items/{frame}/documentation");
        Assert.Equal("noga.pdf", Path.GetFileName(Assert.Single(plain)));
    }
}
