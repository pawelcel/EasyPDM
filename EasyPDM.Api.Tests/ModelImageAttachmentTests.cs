using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Podgląd modelu to zrzut PNG zrobiony przez makro CAD przy wysyłce (rola "image"), a nie
// plik STEP renderowany w przeglądarce. Zrzut jest ściśle związany ze STEP-em: powstaje
// razem z nim i bez niego nie ma czego przedstawiać, więc usunięcie STEP-a musi go zabrać
// ze sobą -- inaczej w bazie i w magazynie zostawałyby obrazki, których nic już nie
// wyświetla ani nie da się z niczym powiązać.
[Collection("EasyPDM database")]
public class ModelImageAttachmentTests
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

    private static async Task<Guid> AttachAsync(HttpClient client, Guid itemId, string role, string fileName)
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("zawartosc")), "file", fileName },
            { new StringContent(role), "role" },
        };
        var response = await client.PostAsync($"/api/items/{itemId}/attachments", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<List<(Guid Id, string? Role)>> AttachmentsAsync(HttpClient client, Guid itemId)
    {
        var response = await client.GetAsync($"/api/items/{itemId}/attachments");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray()
            .Select(a => (a.GetProperty("id").GetGuid(),
                a.GetProperty("role").ValueKind == JsonValueKind.Null ? null : a.GetProperty("role").GetString()))
            .ToList();
    }

    [Fact]
    public async Task Rola_image_jest_przyjmowana()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Zrzut {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId);

        await AttachAsync(client, itemId, "image", "podglad.png");

        var attachments = await AttachmentsAsync(client, itemId);
        Assert.Single(attachments, a => a.Role == "image");
    }

    [Fact]
    public async Task Nowy_zrzut_zastepuje_poprzedni()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Zrzut {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId);

        // Zrzut przedstawia BIEŻĄCĄ postać modelu, więc jest jednoslotowy jak pdf/step:
        // kolejna wysyłka zastępuje poprzedni, zamiast odkładać go obok na zawsze.
        await AttachAsync(client, itemId, "image", "podglad-1.png");
        await AttachAsync(client, itemId, "image", "podglad-2.png");

        var images = (await AttachmentsAsync(client, itemId)).Where(a => a.Role == "image").ToList();
        Assert.Single(images);
    }

    [Fact]
    public async Task Usuniecie_STEP_kasuje_takze_zrzut()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Zrzut {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId);

        var stepId = await AttachAsync(client, itemId, "step", "model.step");
        await AttachAsync(client, itemId, "image", "podglad.png");

        (await client.DeleteAsync($"/api/attachments/{stepId}")).EnsureSuccessStatusCode();

        // Zrzut istniał wyłącznie po to, żeby pokazać ten model -- bez STEP-a jest sierotą.
        var attachments = await AttachmentsAsync(client, itemId);
        Assert.DoesNotContain(attachments, a => a.Role == "step");
        Assert.DoesNotContain(attachments, a => a.Role == "image");
    }

    [Fact]
    public async Task Usuniecie_PDF_nie_rusza_zrzutu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Zrzut {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId);

        var pdfId = await AttachAsync(client, itemId, "pdf", "rysunek.pdf");
        await AttachAsync(client, itemId, "image", "podglad.png");

        // Kasowanie kaskadowe dotyczy WYŁĄCZNIE STEP-a -- rysunek PDF jest niezależnym
        // wyborem i nie ma nic wspólnego z podglądem modelu.
        (await client.DeleteAsync($"/api/attachments/{pdfId}")).EnsureSuccessStatusCode();

        var attachments = await AttachmentsAsync(client, itemId);
        Assert.Single(attachments, a => a.Role == "image");
    }

    [Fact]
    public async Task Nieznana_rola_jest_odrzucana()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Zrzut {Guid.NewGuid()}");
        var itemId = await CreatePartAsync(client, projectId);

        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("zawartosc")), "file", "cos.png" },
            { new StringContent("thumbnail"), "role" },
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsync($"/api/items/{itemId}/attachments", form)).StatusCode);
    }
}
