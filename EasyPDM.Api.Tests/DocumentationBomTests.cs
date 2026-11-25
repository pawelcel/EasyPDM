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
        Assert.Contains("noga.pdf", both);

        // The BOM alone: no attachments.
        var bomOnly = await ZipEntriesAsync(admin, $"/api/items/{frame}/documentation?bom=full");
        var entry = Assert.Single(bomOnly);
        Assert.StartsWith("BOM_", entry);

        // Without "bom" nothing changes: attachments only.
        var plain = await ZipEntriesAsync(admin, $"/api/items/{frame}/documentation");
        Assert.Equal("noga.pdf", Assert.Single(plain));
    }

    // The archive is named after the item or project it comes from plus the day (yyyy-MM-dd from
    // the browser), and holds every file on one level; a repeated name gets " (2)".
    [Fact]
    public async Task Documentation_zip_is_named_after_its_source_and_flat()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectName = $"Doc name {Guid.NewGuid().ToString("N")[..6]}";
        var projectId = await admin.CreateProjectAsync(projectName);

        async Task<Guid> NodeAsync(string name, string type)
        {
            var r = await admin.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new { name, itemType = type, properties = new { } });
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        var frame = await NodeAsync("Rama", "assembly");
        var leg = await NodeAsync("Noga", "part");
        var rail = await NodeAsync("Belka", "part");
        (await admin.PostAsJsonAsync($"/api/items/{frame}/children", new { childId = leg, quantity = 1 })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/items/{frame}/children", new { childId = rail, quantity = 1 })).EnsureSuccessStatusCode();
        foreach (var item in new[] { leg, rail })
        {
            using var form = new MultipartFormDataContent
            {
                { new ByteArrayContent(Encoding.UTF8.GetBytes("x")), "file", "opis.txt" },
            };
            (await admin.PostAsync($"/api/items/{item}/attachments", form)).EnsureSuccessStatusCode();
        }

        var response = await admin.GetAsync($"/api/items/{frame}/documentation?date=2026-11-25");
        response.EnsureSuccessStatusCode();
        var fileName = response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName!.Trim('"');
        Assert.Matches(@"\(Rama\)\.A 2026-11-25\.zip$", fileName);
        await using (var stream = await response.Content.ReadAsStreamAsync())
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            Assert.Equal(new[] { "opis (2).txt", "opis.txt" }, zip.Entries.Select(e => e.FullName).OrderBy(n => n));

        response = await admin.GetAsync($"/api/projects/{projectId}/documentation?date=11%2F25%2F2026");
        response.EnsureSuccessStatusCode();
        fileName = response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName!.Trim('"');
        // Not year-month-day: the server's own date instead, never the text as sent.
        Assert.Matches($@"^{System.Text.RegularExpressions.Regex.Escape(projectName)} \d{{4}}-\d{{2}}-\d{{2}}\.zip$", fileName);
    }
}
