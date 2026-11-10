using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Nazwa wchodzi do nazwy pliku CAD ("C0005(płyta).A.sldprt"), więc tak jak rodzaju nie da
// się jej zmienić, gdy element ma plik w wyróżnionym polu. Element utworzony bez nazwy w
// nazwie rekordu ("C0005.A.sldprt") zmianę nazwy dalej przyjmuje.
[Collection("EasyPDM database")]
public class NameLockTests
{
    private static async Task<Guid> CreatePartAsync(HttpClient client, Guid projectId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new
        {
            name,
            itemType = "part",
            properties = new { rodzaj = "Wykonywana" },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task UploadCadAsync(HttpClient client, Guid itemId, string fileName)
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("cad")), "file", fileName },
            { new StringContent("cad"), "role" },
        };
        (await client.PostAsync($"/api/items/{itemId}/attachments", form)).EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> GetItemAsync(HttpClient client, Guid itemId) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}");

    private static Task<HttpResponseMessage> RenameAsync(HttpClient client, Guid itemId, string name) =>
        client.PatchAsJsonAsync($"/api/items/{itemId}/name", new { name });

    [Fact]
    public async Task Nazwy_nie_da_sie_zmienic_po_dodaniu_pliku_CAD()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var projectId = await client.CreateProjectAsync($"Nazwa {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId, "Plyta");

        // Bez pliku w wyróżnionym polu pomyłkę w nazwie da się poprawić.
        Assert.False((await GetItemAsync(client, itemId)).GetProperty("nameLocked").GetBoolean());
        (await RenameAsync(client, itemId, "Plyta dolna")).EnsureSuccessStatusCode();

        await UploadCadAsync(client, itemId, "1(Plyta dolna).A.FCStd");
        Assert.True((await GetItemAsync(client, itemId)).GetProperty("nameLocked").GetBoolean());

        var refused = await RenameAsync(client, itemId, "Plyta gorna");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("Plyta dolna", (await GetItemAsync(client, itemId)).GetProperty("fileName").GetString());

        // Ponowne przysłanie tej samej nazwy nie jest zmianą.
        (await RenameAsync(client, itemId, "Plyta dolna")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Bez_nazwy_w_nazwie_rekordu_zmiana_nazwy_dalej_dziala()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        (await client.PatchAsJsonAsync("/api/settings/item-number-format", new { withName = false })).EnsureSuccessStatusCode();
        try
        {
            var projectId = await client.CreateProjectAsync($"Bez nazwy {Guid.NewGuid()}");
            var itemId = await CreatePartAsync(client, projectId, "Wspornik");
            await UploadCadAsync(client, itemId, "1.A.FCStd");

            Assert.False((await GetItemAsync(client, itemId)).GetProperty("nameLocked").GetBoolean());
            (await RenameAsync(client, itemId, "Wspornik lewy")).EnsureSuccessStatusCode();
        }
        finally
        {
            // Baza jest wspólna dla testów tej klasy -- ustawienie nie może przeciekać dalej.
            (await client.PatchAsJsonAsync("/api/settings/item-number-format", new { withName = true })).EnsureSuccessStatusCode();
        }
    }
}
