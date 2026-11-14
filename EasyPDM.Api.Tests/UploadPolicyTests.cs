using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Dozwolone typy i limity przesyłanych plików (Ustawienia -> Magazyn plików, UploadPolicy): bez
// reguł każdy plik przechodzi; z regułami każdy endpoint zapisujący plik odrzuca typ spoza listy
// (415) albo plik ponad limit swojego typu (413) i nic wtedy nie zapisuje.
//
// Reguły leżą we wspólnej bazie testowej, więc każdy test czyści je na koniec.
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

    private static Task<HttpResponseMessage> SetRulesAsync(HttpClient admin, params object[] rules)
        => admin.PutAsJsonAsync("/api/settings/upload-rules", new { rules });

    private static async Task<int> AttachmentCountAsync(HttpClient client, Guid itemId)
        => (await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/attachments")).GetArrayLength();

    [Fact]
    public async Task Bez_regul_kazdy_typ_jest_przyjmowany()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var (client, _, partId) = await SetUpAsync(factory);

        (await client.PostAsync($"/api/items/{partId}/attachments", Form("notes.anything", Bytes(10)))).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/items/{partId}/attachments", Form("README", Bytes(10)))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Typ_spoza_listy_jest_odrzucany_i_nic_nie_jest_zapisane()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var (client, projectId, partId) = await SetUpAsync(factory);
        try
        {
            (await SetRulesAsync(client,
                new { extension = "sldprt", maxSizeMb = (int?)null },
                new { extension = " .STEP ", maxSizeMb = (int?)null },
                new { extension = ".pdf", maxSizeMb = (int?)null })).EnsureSuccessStatusCode();

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
        finally
        {
            (await SetRulesAsync(client)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Kazdy_typ_ma_wlasny_limit()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var (client, _, partId) = await SetUpAsync(factory);
        try
        {
            (await SetRulesAsync(client,
                new { extension = ".pdf", maxSizeMb = (int?)1 },
                new { extension = ".step", maxSizeMb = (int?)2 })).EnsureSuccessStatusCode();

            var tooBig = await client.PostAsync($"/api/items/{partId}/attachments", Form("big.pdf", Bytes(1024 * 1024 + 1)));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);
            Assert.Contains(".pdf", await tooBig.Content.ReadAsStringAsync());
            Assert.Equal(0, await AttachmentCountAsync(client, partId));

            // Ten sam rozmiar mieści się w limicie STEP-a.
            (await client.PostAsync($"/api/items/{partId}/attachments", Form("ok.step", Bytes(1024 * 1024 + 1)))).EnsureSuccessStatusCode();
            (await client.PostAsync($"/api/items/{partId}/attachments", Form("ok.pdf", Bytes(1024 * 1024)))).EnsureSuccessStatusCode();
        }
        finally
        {
            (await SetRulesAsync(client)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Cialo_zadania_ponad_najwiekszy_limit_jest_odrzucane_przed_odczytem()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var (client, _, partId) = await SetUpAsync(factory);
        try
        {
            (await SetRulesAsync(client, new { extension = ".step", maxSizeMb = (int?)1 })).EnsureSuccessStatusCode();

            // Największy limit 1 MB + 16 MB zapasu na formularz: 18 MB to więcej niż limit żądania.
            var response = await client.PostAsync($"/api/items/{partId}/attachments", Form("huge.step", Bytes(18 * 1024 * 1024)));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Equal(0, await AttachmentCountAsync(client, partId));
        }
        finally
        {
            (await SetRulesAsync(client)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Ustawienia_zmienia_tylko_administrator_i_sprawdzaja_wpisy()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var (admin, _, _) = await SetUpAsync(factory);
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SetRulesAsync(admin, new { extension = "../x", maxSizeMb = (int?)null })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SetRulesAsync(admin, new { extension = ".pdf", maxSizeMb = (int?)0 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await SetRulesAsync(admin,
                new { extension = "pdf", maxSizeMb = (int?)null }, new { extension = ".PDF", maxSizeMb = (int?)5 })).StatusCode);

            (await SetRulesAsync(admin, new { extension = "STEP", maxSizeMb = (int?)200 })).EnsureSuccessStatusCode();
            var read = await admin.GetFromJsonAsync<JsonElement>("/api/settings/upload-rules");
            var rule = read.GetProperty("rules").EnumerateArray().Single();
            Assert.Equal(".step", rule.GetProperty("extension").GetString());
            Assert.Equal(200, rule.GetProperty("maxSizeMb").GetInt32());
            Assert.Contains(".fcstd", read.GetProperty("cadMacroExtensions").EnumerateArray().Select(e => e.GetString()));

            var userId = await admin.CreateUserAsync("up" + Guid.NewGuid().ToString("N")[..8], "haslo123");
            using var user = factory.CreateClient();
            var name = (await admin.GetFromJsonAsync<JsonElement>("/api/users")).EnumerateArray()
                .First(u => u.GetProperty("id").GetGuid() == userId).GetProperty("username").GetString()!;
            await user.LoginAsync(name, "haslo123");
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/settings/upload-rules")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await SetRulesAsync(user)).StatusCode);
        }
        finally
        {
            (await SetRulesAsync(admin)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Odrzucony_dowod_nie_zostawia_wpisu_weryfikacji_klienta()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var (client, projectId, partId) = await SetUpAsync(factory);
        try
        {
            foreach (var status in new[] { "sprawdzany", "wydany" })
                (await client.PatchAsJsonAsync($"/api/items/{partId}/status", new { status })).EnsureSuccessStatusCode();
            (await SetRulesAsync(client, new { extension = ".pdf", maxSizeMb = (int?)null })).EnsureSuccessStatusCode();

            using var form = new MultipartFormDataContent
            {
                { new StringContent("zweryfikowany"), "result" },
                { new ByteArrayContent(Bytes(10)), "files", "mail.eml" },
            };
            var refused = await client.PostAsync($"/api/projects/{projectId}/items/{partId}/client-verifications", form);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.StatusCode);

            var entries = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/items/{partId}/client-verifications");
            Assert.Equal(0, entries.GetArrayLength());
        }
        finally
        {
            (await SetRulesAsync(client)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public void Rozszerzenia_sa_normalizowane()
    {
        Assert.Equal(".sldprt", UploadPolicy.Normalize("SLDPRT"));
        Assert.Equal(".step", UploadPolicy.Normalize(" .Step "));
        Assert.Null(UploadPolicy.Normalize(""));
        Assert.Null(UploadPolicy.Normalize(".a b"));
        Assert.Null(UploadPolicy.Normalize("../etc"));
    }
}
