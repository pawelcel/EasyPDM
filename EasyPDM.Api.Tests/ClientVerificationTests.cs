using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Weryfikacja klienta — wpisy akceptacji/uwag dla WYDANEJ Części/Złożenia, prowadzone
// osobno dla każdego projektu. Testy pilnują trzech rzeczy, na których stoi cała funkcja:
// tylko status "wydany", izolacja między projektami i zapamiętanie rewizji.
[Collection("EasyPDM database")]
public class ClientVerificationTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static async Task ReleaseItemAsync(HttpClient client, Guid itemId)
    {
        foreach (var status in new[] { "sprawdzany", "wydany" })
        {
            var response = await client.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status });
            response.EnsureSuccessStatusCode();
        }
    }

    // result = null → wpis bez rozstrzygnięcia, czyli "w trakcie weryfikacji" (pole w ogóle
    // nie leci w formularzu, dokładnie jak z okna, gdzie żaden wynik nie jest zaznaczony).
    private static async Task<HttpResponseMessage> AddVerificationAsync(
        HttpClient client, Guid projectId, Guid itemId, string? result,
        string? comment = null, string? fileContent = null)
    {
        var form = new MultipartFormDataContent();
        if (result is not null)
            form.Add(new StringContent(result), "result");
        if (comment is not null)
            form.Add(new StringContent(comment), "comment");
        if (fileContent is not null)
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(fileContent)), "files", "potwierdzenie.txt");

        return await client.PostAsync($"/api/projects/{projectId}/items/{itemId}/client-verifications", form);
    }

    private static async Task<JsonElement> GetVerificationsAsync(HttpClient client, Guid projectId, Guid itemId)
    {
        var response = await client.GetAsync($"/api/projects/{projectId}/items/{itemId}/client-verifications");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Weryfikuje się to, co klient faktycznie dostał — element w pracy nie ma jeszcze czego
    // akceptować. Front ukrywa przycisk, ale backend nie może na tym polegać.
    [Fact]
    public async Task Element_w_pracy_nie_przyjmuje_weryfikacji()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt weryfikacja status {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectId, "Czesc w pracy", "part");

        var response = await AddVerificationAsync(client, projectId, itemId, "zweryfikowany");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Kolejne_wpisy_z_zalacznikiem_trafiaja_na_liste_najnowsze_pierwsze()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt weryfikacja lista {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectId, "Czesc wydana", "part");
        await ReleaseItemAsync(client, itemId);

        (await AddVerificationAsync(client, projectId, itemId, "do_poprawy", "Uwagi klienta", "tresc maila"))
            .EnsureSuccessStatusCode();
        (await AddVerificationAsync(client, projectId, itemId, "zweryfikowany", "Poprawione"))
            .EnsureSuccessStatusCode();

        var entries = await GetVerificationsAsync(client, projectId, itemId);

        Assert.Equal(2, entries.GetArrayLength());
        // Najnowszy pierwszy.
        Assert.Equal("zweryfikowany", entries[0].GetProperty("result").GetString());
        Assert.Equal("do_poprawy", entries[1].GetProperty("result").GetString());
        // Załącznik trzyma się swojego wpisu, nie wszystkich.
        Assert.Empty(entries[0].GetProperty("attachments").EnumerateArray());
        var attachment = Assert.Single(entries[1].GetProperty("attachments").EnumerateArray().ToList());
        Assert.Equal("potwierdzenie.txt", attachment.GetProperty("fileName").GetString());

        // Załącznik faktycznie da się pobrać, z oryginalną treścią.
        var download = await client.GetAsync(
            $"/api/client-verification-attachments/{attachment.GetProperty("id").GetGuid()}/download");
        download.EnsureSuccessStatusCode();
        Assert.Equal("tresc maila", await download.Content.ReadAsStringAsync());
    }

    // Sedno funkcji: ta sama Część użyta w drugim projekcie ma WŁASNĄ weryfikację — bo
    // akceptuje ją inny klient. Wpisy nie mogą "iść za elementem".
    [Fact]
    public async Task Weryfikacja_nie_przechodzi_do_innego_projektu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectA = await client.CreateProjectAsync($"Projekt weryfikacja A {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectA, "Czesc wspoldzielona", "part");
        await ReleaseItemAsync(client, itemId);
        (await AddVerificationAsync(client, projectA, itemId, "zweryfikowany", "Akceptacja klienta A"))
            .EnsureSuccessStatusCode();

        // Ten sam element wpięty jako komponent złożenia w DRUGIM projekcie.
        var projectB = await client.CreateProjectAsync($"Projekt weryfikacja B {Guid.NewGuid()}");
        var assemblyB = await client.CreateNodeAsync(projectB, "Zlozenie B", "assembly");
        (await client.PostAsJsonAsync($"/api/items/{assemblyB}/children", new { childId = itemId, quantity = 1 }))
            .EnsureSuccessStatusCode();

        Assert.Equal(1, (await GetVerificationsAsync(client, projectA, itemId)).GetArrayLength());
        Assert.Equal(0, (await GetVerificationsAsync(client, projectB, itemId)).GetArrayLength());

        // To samo w podsumowaniu zasilającym znaczniki w drzewku.
        var summaryB = await client.GetAsync($"/api/projects/{projectB}/client-verifications");
        summaryB.EnsureSuccessStatusCode();
        Assert.Equal(0, (await summaryB.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength());
    }

    // Wpis zapamiętuje rewizję, której dotyczył — po wydaniu nowej widać, że stara akceptacja
    // nie mówi nic o tym, co element ma teraz.
    [Fact]
    public async Task Wpis_zapamietuje_rewizje_z_chwili_dodania()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt weryfikacja rewizja {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectId, "Czesc rewizja", "part");
        await ReleaseItemAsync(client, itemId);
        (await AddVerificationAsync(client, projectId, itemId, "zweryfikowany")).EnsureSuccessStatusCode();

        // Powrót do pracy podnosi rewizję; po ponownym wydaniu dochodzi kolejny wpis.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "w_pracy", comment = "zmiana" }))
            .EnsureSuccessStatusCode();
        await ReleaseItemAsync(client, itemId);
        (await AddVerificationAsync(client, projectId, itemId, "zweryfikowany")).EnsureSuccessStatusCode();

        var entries = await GetVerificationsAsync(client, projectId, itemId);
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Equal(2, entries[0].GetProperty("revisionNumber").GetInt32());
        Assert.Equal(1, entries[1].GetProperty("revisionNumber").GetInt32());
    }

    // Wpis bez wybranego wyniku to "w trakcie weryfikacji" — rzecz poszła do klienta i
    // czekamy. W oknie nie ma domyślnie zaznaczonego wyniku, więc to najczęstszy pierwszy wpis.
    [Fact]
    public async Task Wpis_bez_wyniku_oznacza_weryfikacje_w_toku()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt weryfikacja w toku {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectId, "Czesc u klienta", "part");
        await ReleaseItemAsync(client, itemId);

        var response = await AddVerificationAsync(client, projectId, itemId, result: null, comment: "Wyslane do klienta");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var entries = await GetVerificationsAsync(client, projectId, itemId);
        var entry = entries[0];
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("result").ValueKind);
        Assert.Equal("Wyslane do klienta", entry.GetProperty("comment").GetString());

        // Ten sam brak wyniku musi przejść też przez podsumowanie zasilające znaczniki.
        var summary = await client.GetAsync($"/api/projects/{projectId}/client-verifications");
        summary.EnsureSuccessStatusCode();
        var rows = await summary.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("result").ValueKind);
    }

    // Zestawienie w panelu projektu rozbija elementy na trzy tabele wg ostatniego wyniku,
    // więc podsumowanie musi nieść też dane samego elementu (numer, nazwa, jego AKTUALNA
    // rewizja) — inaczej nie dałoby się wypisać wiersza ani odróżnić wyniku dla bieżącej
    // wersji od takiego, który został przy poprzedniej.
    [Fact]
    public async Task Podsumowanie_niesie_dane_elementu_i_jego_aktualna_rewizje()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt zestawienie {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectId, "Czesc zestawienie", "part");
        await ReleaseItemAsync(client, itemId);
        (await AddVerificationAsync(client, projectId, itemId, "do_poprawy")).EnsureSuccessStatusCode();

        // Nowa rewizja PO wpisie — wynik zostaje przy rewizji 1, element jest już na 2.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "w_pracy", comment = "poprawki" }))
            .EnsureSuccessStatusCode();
        await ReleaseItemAsync(client, itemId);

        var response = await client.GetAsync($"/api/projects/{projectId}/client-verifications");
        response.EnsureSuccessStatusCode();
        var row = (await response.Content.ReadFromJsonAsync<JsonElement>())[0];

        Assert.Equal("Czesc zestawienie", row.GetProperty("fileName").GetString());
        Assert.True(row.GetProperty("itemNumber").GetInt32() > 0);
        Assert.Equal(1, row.GetProperty("revisionNumber").GetInt32());
        Assert.Equal(2, row.GetProperty("itemRevisionNumber").GetInt32());
    }

    [Fact]
    public async Task Nieznany_wynik_jest_odrzucany()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt weryfikacja zly wynik {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectId, "Czesc zly wynik", "part");
        await ReleaseItemAsync(client, itemId);

        var response = await AddVerificationAsync(client, projectId, itemId, "cokolwiek");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Podając cudze id elementu nie da się dopisać weryfikacji "do" projektu, w którym ten
    // element w ogóle nie występuje.
    [Fact]
    public async Task Element_spoza_projektu_jest_odrzucany()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectWithItem = await client.CreateProjectAsync($"Projekt weryfikacja wlasciwy {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectWithItem, "Czesc obca", "part");
        await ReleaseItemAsync(client, itemId);

        var unrelatedProject = await client.CreateProjectAsync($"Projekt weryfikacja obcy {Guid.NewGuid()}");

        var response = await AddVerificationAsync(client, unrelatedProject, itemId, "zweryfikowany");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
