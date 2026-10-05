using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// "Makro prosi o formularz" — podawane JUŻ OTWARTEJ karcie przeglądarki zamiast otwierania
// nowej na każdy komponent złożenia. Zob. CadRequestStore.cs po powód (kilkadziesiąt okien
// "OK" i kilkadziesiąt kart przy większym złożeniu).
//
// Te testy pilnują tego, na czym stoi zabezpieczenie: makro MUSI umieć odróżnić "ktoś na to
// patrzy" od "przeglądarka jest zamknięta", bo w drugim przypadku musi wrócić do otwierania
// karty — inaczej czekałoby na formularz, którego nikt nigdy nie zobaczy.
[Collection("EasyPDM database")]
public class CadRequestTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static async Task<HttpResponseMessage> PublishAsync(HttpClient client, string ticket, string mode = "create") =>
        await client.PutAsJsonAsync("/api/cad-requests", new
        {
            ticket,
            mode,
            name = "wspornik",
            itemType = "assembly",
            material = "S355",
            documentSize = 12L * 1024 * 1024,
        });

    private static async Task<JsonElement> ReadAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/cad-requests");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("request");
    }

    private static async Task<int> TakenAsync(HttpClient client, string ticket)
    {
        var response = await client.GetAsync($"/api/cad-requests/taken?ticket={ticket}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taken").GetInt32();
    }

    [Fact]
    public async Task Brak_prosby_to_zwykly_stan()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        (await client.DeleteAsync("/api/cad-requests")).EnsureSuccessStatusCode();

        Assert.Equal(JsonValueKind.Null, (await ReadAsync(client)).ValueKind);
    }

    [Fact]
    public async Task Prosba_niesie_wszystko_czego_potrzebuje_formularz()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var ticket = Guid.NewGuid().ToString();
        (await PublishAsync(client, ticket)).EnsureSuccessStatusCode();

        var request = await ReadAsync(client);
        Assert.Equal(ticket, request.GetProperty("ticket").GetString());
        Assert.Equal("create", request.GetProperty("mode").GetString());
        Assert.Equal("wspornik", request.GetProperty("name").GetString());
        // Typ dokumentu jest tu najważniejszy: bez niego okno preselekcjonuje Część i złożenie
        // powstaje jako Część, do której nie da się nic podpiąć w strukturze.
        Assert.Equal("assembly", request.GetProperty("itemType").GetString());
        Assert.False(request.GetProperty("taken").GetBoolean());

        (await client.DeleteAsync("/api/cad-requests")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Dopiero_podjecie_mowi_makru_ze_ktos_patrzy()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var ticket = Guid.NewGuid().ToString();
        (await PublishAsync(client, ticket)).EnsureSuccessStatusCode();

        // Zanim ktokolwiek podejmie — makro ma widzieć 0 i po swoim czasie oczekiwania wrócić
        // do otwierania karty.
        Assert.Equal(0, await TakenAsync(client, ticket));

        (await client.PostAsJsonAsync("/api/cad-requests/take", new { ticket })).EnsureSuccessStatusCode();
        Assert.Equal(1, await TakenAsync(client, ticket));

        // Liczba, nie wartość logiczna w zagnieżdżonym obiekcie: parsery JSON w makrach VBA
        // mają tylko JsonGetString i JsonGetLong.
        (await client.DeleteAsync("/api/cad-requests")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Podjecie_cudzego_biletu_nic_nie_robi()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var ticket = Guid.NewGuid().ToString();
        (await PublishAsync(client, ticket)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/cad-requests/take", new { ticket = Guid.NewGuid().ToString() });
        response.EnsureSuccessStatusCode();
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("matched").GetBoolean());
        Assert.Equal(0, await TakenAsync(client, ticket));

        (await client.DeleteAsync("/api/cad-requests")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Kasowanie_po_bilecie_nie_rusza_prosby_o_nastepny_komponent()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();
        (await PublishAsync(client, first)).EnsureSuccessStatusCode();
        // Makro ruszyło dalej i poprosiło o kolejny komponent, zanim sprzątnęło po poprzednim.
        (await PublishAsync(client, second)).EnsureSuccessStatusCode();

        // Spóźnione sprzątanie po PIERWSZYM bilecie nie może usunąć prośby o drugi — inaczej
        // formularz dla kolejnego komponentu nigdy by się nie pokazał.
        (await client.DeleteAsync($"/api/cad-requests?ticket={first}")).EnsureSuccessStatusCode();

        var request = await ReadAsync(client);
        Assert.Equal(second, request.GetProperty("ticket").GetString());

        (await client.DeleteAsync("/api/cad-requests")).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await ReadAsync(client)).ValueKind);
    }

    [Fact]
    public async Task Odrzuca_bledne_zadania()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/cad-requests", new { ticket = "", mode = "create" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/cad-requests", new { ticket = "abc", mode = "cos-innego" })).StatusCode);
    }
}
