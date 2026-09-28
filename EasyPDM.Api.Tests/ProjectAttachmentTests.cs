using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Załączniki PROJEKTU — dokumenty całego zlecenia (oferta, potwierdzenie przyjęcia zlecenia,
// pozostałe), niezależne od załączników pojedynczych elementów.
[Collection("EasyPDM database")]
public class ProjectAttachmentTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid projectId, string fileName, string content, string? role)
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", fileName },
        };
        if (role is not null)
            form.Add(new StringContent(role), "role");
        return await client.PostAsync($"/api/projects/{projectId}/attachments", form);
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, Guid projectId)
    {
        var response = await client.GetAsync($"/api/projects/{projectId}/attachments");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Trzy_kategorie_zalacznikow_trafiaja_na_liste_z_wlasna_rola()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt dokumenty {Guid.NewGuid()}");

        (await UploadAsync(client, projectId, "oferta.pdf", "tresc oferty", "oferta")).EnsureSuccessStatusCode();
        (await UploadAsync(client, projectId, "zlecenie.pdf", "potwierdzenie", "zlecenie")).EnsureSuccessStatusCode();
        (await UploadAsync(client, projectId, "notatka.txt", "ustalenia", null)).EnsureSuccessStatusCode();

        var list = await ListAsync(client, projectId);
        var byRole = list.EnumerateArray().ToDictionary(
            a => a.GetProperty("role").ValueKind == JsonValueKind.Null ? "" : a.GetProperty("role").GetString()!,
            a => a.GetProperty("fileName").GetString());

        Assert.Equal("oferta.pdf", byRole["oferta"]);
        Assert.Equal("zlecenie.pdf", byRole["zlecenie"]);
        Assert.Equal("notatka.txt", byRole[""]);
    }

    // Wyróżnione role dopuszczają WIELE plików (inaczej niż 'pdf'/'step' przy elemencie):
    // oferta bywa poprawiana i wysyłana ponownie, a nowa wersja nie kasuje poprzedniej.
    [Fact]
    public async Task Kolejna_oferta_nie_zastepuje_poprzedniej()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt oferty {Guid.NewGuid()}");
        (await UploadAsync(client, projectId, "oferta-v1.pdf", "wersja 1", "oferta")).EnsureSuccessStatusCode();
        (await UploadAsync(client, projectId, "oferta-v2.pdf", "wersja 2", "oferta")).EnsureSuccessStatusCode();

        var offers = (await ListAsync(client, projectId)).EnumerateArray()
            .Where(a => a.GetProperty("role").GetString() == "oferta")
            .Select(a => a.GetProperty("fileName").GetString())
            .ToList();

        Assert.Equal(2, offers.Count);
        Assert.Contains("oferta-v1.pdf", offers);
        Assert.Contains("oferta-v2.pdf", offers);
    }

    [Fact]
    public async Task Zalacznik_da_sie_pobrac_i_usunac()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt pobieranie {Guid.NewGuid()}");
        var upload = await UploadAsync(client, projectId, "oferta.txt", "tresc do pobrania", "oferta");
        upload.EnsureSuccessStatusCode();
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var download = await client.GetAsync($"/api/project-attachments/{id}/download");
        download.EnsureSuccessStatusCode();
        Assert.Equal("tresc do pobrania", await download.Content.ReadAsStringAsync());

        var delete = await client.DeleteAsync($"/api/project-attachments/{id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.Equal(0, (await ListAsync(client, projectId)).GetArrayLength());
    }

    [Fact]
    public async Task Nieznana_rola_jest_odrzucana()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt zla rola {Guid.NewGuid()}");

        var response = await UploadAsync(client, projectId, "plik.txt", "tresc", "cokolwiek");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Oferty i potwierdzenia mówią o warunkach handlowych, więc — inaczej niż "Cała baza",
    // świadomie otwarty katalog części — widzi je tylko ktoś z dostępem do projektu.
    [Fact]
    public async Task Uzytkownik_bez_dostepu_do_projektu_nie_widzi_zalacznikow()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var adminClient = factory.CreateClient();
        await adminClient.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await adminClient.CreateProjectAsync($"Projekt prywatny {Guid.NewGuid()}");
        (await UploadAsync(adminClient, projectId, "oferta.pdf", "poufne warunki", "oferta")).EnsureSuccessStatusCode();

        var username = $"bezdostepu{Guid.NewGuid():N}"[..20];
        await adminClient.CreateUserAsync(username, "haslo123");

        using var userClient = factory.CreateClient();
        await userClient.LoginAsync(username, "haslo123");

        var response = await userClient.GetAsync($"/api/projects/{projectId}/attachments");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
