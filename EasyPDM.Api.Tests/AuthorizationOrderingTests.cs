using System.Net;
using System.Net.Http.Json;

namespace EasyPDM.Api.Tests;

// Regresja dla znaleziska z przeglądu kodu w tej samej sesji: kilka endpointów sprawdzało
// sprawy biznesowe (dozwolone przejście statusu, blokadę właściciela) PRZED sprawdzeniem
// dostępu do projektu, więc użytkownik bez dostępu dostawał 400 z treścią zdradzającą stan
// elementu zamiast 403 "Brak dostępu do tego projektu." — zob. ItemEndpoints.cs (PATCH
// /status i inne). Ten test pilnuje, żeby ta poprawka się nie cofnęła.
[Collection("EasyPDM database")]
public class AuthorizationOrderingTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    [Fact]
    public async Task Status_endpoint_zwraca_403_dla_uzytkownika_bez_dostepu_do_projektu_zamiast_zdradzac_stan_elementu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var adminClient = factory.CreateClient();
        await adminClient.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await adminClient.CreateProjectAsync("Projekt tajny");
        var itemId = await adminClient.CreateNodeAsync(projectId, "Zlozenie", "assembly");

        // Użytkownik istnieje, ale NIE ma przypisanego dostępu do "Projekt tajny".
        await adminClient.CreateUserAsync("outsider", "haslo123");

        using var outsiderClient = factory.CreateClient();
        await outsiderClient.LoginAsync("outsider", "haslo123");

        // "sprawdzany" to poprawne przejście z "w_pracy" — gdyby dostęp do projektu był
        // sprawdzany PO walidacji przejścia (dawny błąd), dostalibyśmy 200 albo 400 z
        // treścią o przejściu statusu, nie 403.
        var response = await outsiderClient.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "sprawdzany" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("dostępu", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Status_endpoint_dziala_dla_uzytkownika_z_przyznanym_dostepem()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var adminClient = factory.CreateClient();
        await adminClient.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await adminClient.CreateProjectAsync("Projekt otwarty");
        var itemId = await adminClient.CreateNodeAsync(projectId, "Zlozenie", "assembly");
        // Nowo utworzony element jest domyślnie zablokowany przez twórcę (owner_locked) —
        // niezależny mechanizm od dostępu do projektu; zwalniamy go, żeby ten test faktycznie
        // sprawdzał TYLKO dostęp do projektu, a nie przy okazji blokadę właściciela.
        await adminClient.PostAsync($"/api/items/{itemId}/release", content: null);

        var userId = await adminClient.CreateUserAsync("insider", "haslo123");
        await adminClient.GrantProjectAccessAsync(projectId, userId);

        using var insiderClient = factory.CreateClient();
        await insiderClient.LoginAsync("insider", "haslo123");

        var response = await insiderClient.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "sprawdzany" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Nazwa_endpoint_zwraca_403_zamiast_zdradzac_blokade_przed_sprawdzeniem_dostepu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var adminClient = factory.CreateClient();
        await adminClient.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await adminClient.CreateProjectAsync("Projekt tajny 2");
        var itemId = await adminClient.CreateNodeAsync(projectId, "Czesc", "part");
        // Podnosimy status na "sprawdzany", żeby element był "zablokowany" (IsLocked) —
        // dawny błąd zdradzałby akurat ten fakt w treści 400 przed sprawdzeniem dostępu.
        await adminClient.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "sprawdzany" });

        await adminClient.CreateUserAsync("outsider2", "haslo123");
        using var outsiderClient = factory.CreateClient();
        await outsiderClient.LoginAsync("outsider2", "haslo123");

        var response = await outsiderClient.PatchAsJsonAsync($"/api/items/{itemId}/name", new { name = "Nowa nazwa" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
