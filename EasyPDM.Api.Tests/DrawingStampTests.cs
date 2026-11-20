using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EasyPDM.Api.Tests;

// Sending an item for review stamps "drawn by" into its drawing PDF and releasing it stamps
// "approved by" -- but only a PDF the macros marked as exported from an EasyPDM sheet
// (sheetLayout), and the stamped file replaces the original.
[Collection("EasyPDM database")]
public class DrawingStampTests
{
    private static byte[] BlankPdf(double widthMm, double heightMm)
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Width = PdfSharp.Drawing.XUnit.FromMillimeter(widthMm);
        page.Height = PdfSharp.Drawing.XUnit.FromMillimeter(heightMm);
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private static async Task<(HttpClient Client, Guid ItemId)> PartWithPdfAsync(EasyPDMWebApplicationFactory factory, byte[] pdf, string? sheetLayout)
    {
        var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var projectId = await client.CreateProjectAsync($"Stamp {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectId, $"Plate {Guid.NewGuid():N}"[..14], "part");
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(pdf), "file", "drawing.pdf" },
            { new StringContent("pdf"), "role" },
        };
        if (sheetLayout is not null)
            form.Add(new StringContent(sheetLayout), "sheetLayout");
        (await client.PostAsync($"/api/items/{itemId}/attachments", form)).EnsureSuccessStatusCode();
        return (client, itemId);
    }

    private static async Task<byte[]> DownloadPdfAsync(HttpClient client, Guid itemId)
    {
        var attachments = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/attachments");
        var pdf = attachments.EnumerateArray().Single(a => a.GetProperty("role").GetString() == "pdf");
        return await client.GetByteArrayAsync($"/api/attachments/{pdf.GetProperty("id").GetGuid()}/file");
    }

    private static async Task ReleaseAsync(HttpClient client, Guid itemId)
    {
        foreach (var status in new[] { "sprawdzany", "wydany" })
            (await client.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Releasing_stamps_an_EasyPDM_sheet_PDF_and_replaces_it()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var original = BlankPdf(420, 297);
        var (client, itemId) = await PartWithPdfAsync(factory, original, "easypdm-A3");

        Assert.Equal(original, await DownloadPdfAsync(client, itemId));

        // In review: "drawn by" goes in.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "sprawdzany" })).EnsureSuccessStatusCode();
        var reviewed = await DownloadPdfAsync(client, itemId);
        Assert.NotEqual(original, reviewed);

        // Released: "approved by" joins it.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "wydany" })).EnsureSuccessStatusCode();
        var stamped = await DownloadPdfAsync(client, itemId);
        Assert.NotEqual(reviewed, stamped);
        // The sheet's lettering is embedded for the stamp, and the file still opens as one page.
        Assert.Contains("osifont", Encoding.Latin1.GetString(stamped));
        using var doc = PdfReader.Open(new MemoryStream(stamped), PdfDocumentOpenMode.Import);
        Assert.Equal(1, doc.PageCount);

        // The stored size follows the new file.
        var attachments = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/attachments");
        var pdf = attachments.EnumerateArray().Single(a => a.GetProperty("role").GetString() == "pdf");
        Assert.Equal(stamped.Length, pdf.GetProperty("fileSize").GetInt64());
    }

    // The stamp rewrites the PDF under the same attachment id, so the web app's preview needs
    // something that changes with the file to reload it, and browsers must not reuse the old copy.
    [Fact]
    public async Task A_stamped_PDF_gets_a_new_version_and_is_not_cached()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var (client, itemId) = await PartWithPdfAsync(factory, BlankPdf(420, 297), "easypdm-A3");

        async Task<JsonElement> PdfEntryAsync()
        {
            var attachments = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/attachments");
            return attachments.EnumerateArray().Single(a => a.GetProperty("role").GetString() == "pdf");
        }

        var before = await PdfEntryAsync();
        var versionBefore = before.GetProperty("version").GetString();
        Assert.False(string.IsNullOrEmpty(versionBefore));

        (await client.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "sprawdzany" })).EnsureSuccessStatusCode();
        var reviewed = await PdfEntryAsync();
        Assert.Equal(before.GetProperty("id").GetGuid(), reviewed.GetProperty("id").GetGuid());
        Assert.NotEqual(versionBefore, reviewed.GetProperty("version").GetString());

        using var response = await client.GetAsync($"/api/attachments/{reviewed.GetProperty("id").GetGuid()}/file");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.NotNull(response.Content.Headers.LastModified);
    }

    [Fact]
    public async Task Releasing_again_without_a_new_PDF_does_not_stamp_it_twice()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var (client, itemId) = await PartWithPdfAsync(factory, BlankPdf(420, 297), "easypdm-A3");
        await ReleaseAsync(client, itemId);
        var stamped = await DownloadPdfAsync(client, itemId);

        (await client.PatchAsJsonAsync($"/api/items/{itemId}/status",
            new { status = "w_pracy", comment = "Thicker wall" })).EnsureSuccessStatusCode();
        await ReleaseAsync(client, itemId);

        Assert.Equal(stamped, await DownloadPdfAsync(client, itemId));
    }

    [Fact]
    public async Task A_PDF_not_marked_as_an_EasyPDM_sheet_is_left_alone()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var original = BlankPdf(420, 297);
        var (client, itemId) = await PartWithPdfAsync(factory, original, null);
        await ReleaseAsync(client, itemId);
        Assert.Equal(original, await DownloadPdfAsync(client, itemId));
    }

    [Fact]
    public async Task A_page_of_another_size_is_not_stamped()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var original = BlankPdf(297, 210);
        var (client, itemId) = await PartWithPdfAsync(factory, original, "easypdm-A3");
        await ReleaseAsync(client, itemId);
        Assert.Equal(original, await DownloadPdfAsync(client, itemId));
    }
}
