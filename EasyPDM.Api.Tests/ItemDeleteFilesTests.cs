using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Całkowite usunięcie elementu (DELETE /api/items/{id}, "Usuń całkowicie") ma zabrać ze sobą
// RÓWNIEŻ pliki z magazynu, nie tylko wiersze. ON DELETE CASCADE czyści bazę, ale plików na
// dysku nie rusza -- endpoint musi więc zebrać ich ścieżki ZANIM skasuje wiersze. Te testy
// pilnują, żeby żadne miejsce, w którym element trzyma pliki, nie zostało pominięte.
[Collection("EasyPDM database")]
public class ItemDeleteFilesTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

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

    private static async Task AttachAsync(HttpClient client, Guid itemId, string? role, string fileName)
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("zawartosc")), "file", fileName },
        };
        if (role is not null)
            form.Add(new StringContent(role), "role");
        (await client.PostAsync($"/api/items/{itemId}/attachments", form)).EnsureSuccessStatusCode();
    }

    // Pliki leżą pod StorageRoot fabryki -- liczymy je wprost na dysku, bo właśnie o to tu
    // chodzi: czy po usunięciu elementu zostaje coś, czego nikt już nigdy nie posprząta.
    private static int StoredFileCount(EasyPDMWebApplicationFactory factory) =>
        Directory.Exists(factory.StorageRoot)
            ? Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Length
            : 0;

    [Fact]
    public async Task Usuniecie_elementu_kasuje_wszystkie_jego_zalaczniki_z_dysku()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Kasowanie {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId);

        var before = StoredFileCount(factory);

        // Wszystkie wyróżnione role naraz plus zwykły załącznik -- komplet tego, co Część
        // potrafi nieść po wysyłce z CAD-a.
        await AttachAsync(client, itemId, "cad", "czesc.sldprt");
        await AttachAsync(client, itemId, "drawing", "rysunek.slddrw");
        await AttachAsync(client, itemId, "pdf", "rysunek.pdf");
        await AttachAsync(client, itemId, "step", "model.step");
        await AttachAsync(client, itemId, "image", "podglad.png");
        await AttachAsync(client, itemId, null, "atest-huty.pdf");

        Assert.Equal(before + 6, StoredFileCount(factory));

        (await client.DeleteAsync($"/api/items/{itemId}")).EnsureSuccessStatusCode();

        // Zero sierot: magazyn wraca do stanu sprzed wgrywania.
        Assert.Equal(before, StoredFileCount(factory));
    }

    [Fact]
    public async Task Usuniecie_projektu_kasuje_jego_zalaczniki_ale_zostawia_pliki_elementow()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Kasowanie projektu {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId);

        var before = StoredFileCount(factory);

        // Załącznik PROJEKTU (oferta) -- znika razem z projektem.
        using (var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("oferta")), "file", "oferta.pdf" },
            { new StringContent("oferta"), "role" },
        })
        {
            (await client.PostAsync($"/api/projects/{projectId}/attachments", form)).EnsureSuccessStatusCode();
        }

        // Załącznik ELEMENTU -- element przeżywa usunięcie projektu (zostaje tylko odpięty),
        // więc jego plik MUSI zostać.
        await AttachAsync(client, itemId, "cad", "czesc.sldprt");

        Assert.Equal(before + 2, StoredFileCount(factory));

        (await client.DeleteAsync($"/api/projects/{projectId}")).EnsureSuccessStatusCode();

        // Znika dokładnie jeden plik: ten należący do projektu.
        Assert.Equal(before + 1, StoredFileCount(factory));

        // Element nadal istnieje, bez projektu -- razem ze swoim plikiem.
        var item = await client.GetAsync($"/api/items/{itemId}");
        item.EnsureSuccessStatusCode();
        var body = await item.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("projectId").ValueKind);
    }

    [Fact]
    public async Task Usuniecie_elementu_kasuje_takze_pliki_weryfikacji_klienta()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Kasowanie {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId);

        var before = StoredFileCount(factory);

        // Weryfikacja klienta ma WŁASNĄ tabelę załączników (item_client_verification_attachments),
        // osobną od item_attachments, i własne pliki w magazynie. W bazie znikają kaskadą przez
        // item_client_verifications, ale pliki trzeba skasować jawnie -- tak samo jak pozostałe.
        foreach (var status in new[] { "sprawdzany", "wydany" })
            (await client.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status })).EnsureSuccessStatusCode();

        using (var form = new MultipartFormDataContent
        {
            { new StringContent("zweryfikowany"), "result" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes("potwierdzenie")), "files", "potwierdzenie.txt" },
        })
        {
            (await client.PostAsync($"/api/projects/{projectId}/items/{itemId}/client-verifications", form))
                .EnsureSuccessStatusCode();
        }

        Assert.Equal(before + 1, StoredFileCount(factory));

        (await client.DeleteAsync($"/api/items/{itemId}")).EnsureSuccessStatusCode();

        Assert.Equal(before, StoredFileCount(factory));
    }
}
