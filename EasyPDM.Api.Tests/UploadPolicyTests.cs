using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Limity typu i rozmiaru przesyłanych plików (UploadPolicy): wyłączone bez ustawień; po ustawieniu
// każdy endpoint zapisujący plik odrzuca typ spoza listy (415) albo plik ponad limit (413) i nic
// wtedy nie zapisuje.
[Collection("EasyPDM database")]
public class UploadPolicyTests
{
    private static MultipartFormDataContent Form(string fileName, byte[] content)
        => new() { { new ByteArrayContent(content), "file", fileName } };

    private static byte[] Bytes(int count) => Enumerable.Repeat((byte)'x', count).ToArray();

    private static async Task<(HttpClient Client, Guid ProjectId, Guid PartId)> SetUpAsync(EasyPDMWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var projectId = await client.CreateProjectAsync($"Uploads {Guid.NewGuid()}");
        var partId = await client.CreateNodeAsync(projectId, $"Part {Guid.NewGuid()}", "part");
        return (client, projectId, partId);
    }

    private static async Task<int> AttachmentCountAsync(HttpClient client, Guid itemId)
        => (await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/attachments")).GetArrayLength();

    [Fact]
    public async Task Bez_ustawien_kazdy_typ_jest_przyjmowany()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var (client, _, partId) = await SetUpAsync(factory);

        (await client.PostAsync($"/api/items/{partId}/attachments", Form("notes.anything", Bytes(10)))).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/items/{partId}/attachments", Form("README", Bytes(10)))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Typ_spoza_listy_jest_odrzucany_i_nic_nie_jest_zapisane()
    {
        await using var factory = new EasyPDMWebApplicationFactory
        {
            ExtraSettings = { ["Uploads:AllowedExtensions"] = "sldprt, .STEP,.pdf" },
        };
        var (client, projectId, partId) = await SetUpAsync(factory);

        var refused = await client.PostAsync($"/api/items/{partId}/attachments", Form("payload.exe", Bytes(10)));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.StatusCode);
        Assert.Contains(".exe", await refused.Content.ReadAsStringAsync());
        Assert.Equal(0, await AttachmentCountAsync(client, partId));

        // Wielkość liter bez znaczenia, kropka w ustawieniu opcjonalna.
        (await client.PostAsync($"/api/items/{partId}/attachments", Form("BRACKET.SLDPRT", Bytes(10)))).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/items/{partId}/attachments", Form("bracket.step", Bytes(10)))).EnsureSuccessStatusCode();

        // Plik bez rozszerzenia i inne endpointy zapisujące pliki.
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await client.PostAsync($"/api/projects/{projectId}/items", Form("Makefile", Bytes(10)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await client.PostAsync($"/api/projects/{projectId}/attachments", Form("page.html", Bytes(10)))).StatusCode);

        var clientResponse = await client.PostAsJsonAsync("/api/clients", new { name = $"Client {Guid.NewGuid()}" });
        clientResponse.EnsureSuccessStatusCode();
        var clientId = (await clientResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await client.PostAsync($"/api/clients/{clientId}/nodes/file", Form("tool.ps1", Bytes(10)))).StatusCode);
    }

    [Fact]
    public async Task Plik_ponad_limit_jest_odrzucany()
    {
        await using var factory = new EasyPDMWebApplicationFactory
        {
            ExtraSettings = { ["Uploads:MaxFileSizeMb"] = "1" },
        };
        var (client, _, partId) = await SetUpAsync(factory);

        var tooBig = await client.PostAsync($"/api/items/{partId}/attachments", Form("big.step", Bytes(1024 * 1024 + 1)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
        Assert.Equal(0, await AttachmentCountAsync(client, partId));

        (await client.PostAsync($"/api/items/{partId}/attachments", Form("ok.step", Bytes(1024 * 1024)))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Cialo_zadania_ponad_limit_jest_odrzucane_przed_odczytem()
    {
        await using var factory = new EasyPDMWebApplicationFactory
        {
            ExtraSettings = { ["Uploads:MaxFileSizeMb"] = "1" },
        };
        var (client, _, partId) = await SetUpAsync(factory);

        // Limit 1 MB + 16 MB zapasu na formularz: 18 MB to więcej niż limit żądania.
        var response = await client.PostAsync($"/api/items/{partId}/attachments", Form("huge.step", Bytes(18 * 1024 * 1024)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, await AttachmentCountAsync(client, partId));
    }

    [Fact]
    public void Polityka_normalizuje_wpisy_i_odrzuca_pusta_liste()
    {
        var policy = new UploadPolicy(["SLDPRT", " .Step ", ""], 5);
        Assert.Contains(".sldprt", policy.AllowedExtensions!);
        Assert.Contains(".STEP", policy.AllowedExtensions!);
        Assert.Equal(5L * 1024 * 1024, policy.MaxFileSizeBytes);

        var open = new UploadPolicy(null, null);
        Assert.Null(open.AllowedExtensions);
        Assert.Null(open.MaxFileSizeBytes);

        Assert.Throws<InvalidOperationException>(() => new UploadPolicy([" ", ""], 5));
        Assert.Throws<InvalidOperationException>(() => new UploadPolicy([".pdf"], 0));
    }
}
