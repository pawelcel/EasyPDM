using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Poprawki z przeglądu kodu 2026-11-12: wyszukiwanie po wartościach, unieważnianie sesji,
// dostęp do projektu rodzica, kształt obróbek i tłumaczeń, tagi przy czyszczeniu bazy,
// nagłówki bezpieczeństwa.
[Collection("EasyPDM database")]
public class ReviewFixesTests
{
    private static async Task<List<Guid>> SearchAsync(HttpClient client, string search)
    {
        var items = await client.GetFromJsonAsync<JsonElement>($"/api/items?search={Uri.EscapeDataString(search)}");
        return items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    [Fact]
    public async Task Wyszukiwanie_trafia_w_wartosci_a_nie_w_nazwy_kluczy()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var projectId = await client.CreateProjectAsync($"Szukanie {Guid.NewGuid()}");
        var tag = Guid.NewGuid().ToString("N")[..8];
        var item = await client.CreateNodeAsync(projectId, "Plyta", "part");
        (await client.PatchAsJsonAsync($"/api/items/{item}/properties", new
        {
            processes = new object[] { new { id = 1, name = "Spawanie" + tag, description = "opis" + tag, texts = new object[] { new { languageId = 1, name = "Welding" + tag } } } },
        })).EnsureSuccessStatusCode();

        // Wartości, także zagnieżdżone (obróbka i jej tłumaczenie), są znajdowane...
        Assert.Contains(item, await SearchAsync(client, "spawanie" + tag));
        Assert.Contains(item, await SearchAsync(client, "WELDING" + tag));
        Assert.Contains(item, await SearchAsync(client, "opis" + tag));
        // ...a nazwy kluczy nie: "languageId" było dawniej trafieniem w każdy element z obróbką.
        Assert.DoesNotContain(item, await SearchAsync(client, "languageId"));
        Assert.DoesNotContain(item, await SearchAsync(client, "description"));
    }

    [Fact]
    public async Task Zmiana_hasla_unieważnia_inne_sesje()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "sesje" + Guid.NewGuid().ToString("N")[..8];
        var userId = await admin.CreateUserAsync(username, "haslo123");

        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        await first.LoginAsync(username, "haslo123");
        await second.LoginAsync(username, "haslo123");

        // Własna zmiana hasła: sesja, z której zmieniono, zostaje; druga wygasa.
        (await first.PatchAsJsonAsync("/api/auth/password", new { currentPassword = "haslo123", newPassword = "nowe4567" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/auth/me")).StatusCode);

        // Hasło ustawione przez administratora wylogowuje użytkownika wszędzie.
        (await admin.PatchAsJsonAsync($"/api/users/{userId}", new { password = "admin789" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Nie_da_sie_utworzyc_elementu_pod_rodzicem_z_cudzego_projektu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var foreignProject = await admin.CreateProjectAsync($"Cudzy {Guid.NewGuid()}");
        var foreignAssembly = await admin.CreateNodeAsync(foreignProject, "Zlozenie", "assembly");
        var username = "obcy" + Guid.NewGuid().ToString("N")[..8];
        await admin.CreateUserAsync(username, "haslo123");

        using var user = factory.CreateClient();
        await user.LoginAsync(username, "haslo123");
        // Element bez projektu przechodzi sprawdzenie projektu zawsze — rodzic musi być sprawdzony osobno.
        var response = await user.PostAsJsonAsync("/api/nodes",
            new { name = "Wrzutka", itemType = "part", properties = new { rodzaj = "Wykonywana" }, parentId = foreignAssembly });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var relations = await admin.GetFromJsonAsync<JsonElement>($"/api/projects/{foreignProject}/relations");
        Assert.DoesNotContain(relations.EnumerateArray(), r => r.GetProperty("parentId").GetGuid() == foreignAssembly);
    }

    [Theory]
    [InlineData("{\"nameTexts\":\"Cube\"}")]
    [InlineData("{\"nameTexts\":[{\"languageId\":\"1\",\"name\":\"Cube\"}]}")]
    [InlineData("{\"nameTexts\":[{\"languageId\":1,\"name\":\" \"}]}")]
    [InlineData("{\"processes\":{\"name\":\"Spawanie\"}}")]
    [InlineData("{\"processes\":[{\"description\":\"bez nazwy\"}]}")]
    [InlineData("{\"processes\":[{\"name\":\"Spawanie\",\"texts\":[{\"languageId\":1}]}]}")]
    public async Task Zly_ksztalt_obrobek_i_tlumaczen_jest_odrzucany(string json)
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var projectId = await client.CreateProjectAsync($"Ksztalt {Guid.NewGuid()}");
        var item = await client.CreateNodeAsync(projectId, "Plyta", "part");

        var response = await client.PatchAsync($"/api/items/{item}/properties",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Poprawne_i_stare_formaty_obrobek_przechodza()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var projectId = await client.CreateProjectAsync($"Ksztalt ok {Guid.NewGuid()}");
        var item = await client.CreateNodeAsync(projectId, "Plyta", "part");

        (await client.PatchAsJsonAsync($"/api/items/{item}/properties", new
        {
            // Zwykły napis to zapis sprzed wariantów — nadal dozwolony.
            processes = new object[] { "Gięcie", new { id = (int?)null, name = "Spawanie", description = (string?)null } },
            nameTexts = new[] { new { languageId = 1, name = "Plate" } },
        })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Czyszczenie_projektow_usuwa_nieuzywane_tagi()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Tagi czyszczenie {Guid.NewGuid()}");
        var item = await admin.CreateNodeAsync(projectId, "Plyta", "part");
        var tag = "znika" + Guid.NewGuid().ToString("N")[..6];
        (await admin.PostAsJsonAsync($"/api/items/{item}/tags", new { name = tag })).EnsureSuccessStatusCode();

        (await admin.PostAsJsonAsync("/api/settings/storage/clear-database",
            new { projects = true, materials = false, manufacturers = false, clients = false })).EnsureSuccessStatusCode();

        Assert.DoesNotContain(tag, await admin.GetFromJsonAsync<List<string>>("/api/tags") ?? []);
    }

    [Fact]
    public async Task Odpowiedzi_maja_naglowki_bezpieczenstwa()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Nowe_haslo_musi_miec_co_najmniej_8_znakow()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "haslo" + Guid.NewGuid().ToString("N")[..8];

        var tooShort = await admin.PostAsJsonAsync("/api/users",
            new { username, password = "krotkie", displayName = "X", firstName = "A", lastName = "B", role = "user" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var userId = await admin.CreateUserAsync(username, "haslo123");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/users/{userId}", new { password = "1234567" })).StatusCode);
        // Zmiana innych pól bez hasła nie podlega regule.
        (await admin.PatchAsJsonAsync($"/api/users/{userId}", new { displayName = "Nowa" })).EnsureSuccessStatusCode();

        using var user = factory.CreateClient();
        await user.LoginAsync(username, "haslo123");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await user.PatchAsJsonAsync("/api/auth/password", new { currentPassword = "haslo123", newPassword = "abc" })).StatusCode);
    }

    // Struktura wydanego złożenia jest zamrożona jak jego właściwości; wydany element dalej
    // można dopiąć jako komponent do złożenia w pracy.
    [Fact]
    public async Task Struktury_wydanego_zlozenia_nie_da_sie_zmienic()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"BOM wydany {Guid.NewGuid()}");
        var released = await admin.CreateNodeAsync(projectId, "Rama", "assembly");
        var part = await admin.CreateNodeAsync(projectId, "Plyta", "part");
        var extra = await admin.CreateNodeAsync(projectId, "Sruba", "part");
        (await admin.PostAsJsonAsync($"/api/items/{released}/children", new { childId = part })).EnsureSuccessStatusCode();
        foreach (var status in new[] { "sprawdzany", "wydany" })
            (await admin.PatchAsJsonAsync($"/api/items/{part}/status", new { status })).EnsureSuccessStatusCode();
        foreach (var status in new[] { "sprawdzany", "wydany" })
            (await admin.PatchAsJsonAsync($"/api/items/{released}/status", new { status })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/items/{released}/children", new { childId = extra })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/items/{released}/children/{part}/position", new { position = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/items/{released}/children/{part}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/projects/{projectId}/nodes",
            new { name = "Nowa", itemType = "part", properties = new { rodzaj = "Wykonywana" }, parentId = released })).StatusCode);

        // Wydane złożenie jako komponent nowego złożenia w pracy — dozwolone.
        var parent = await admin.CreateNodeAsync(projectId, "Linia", "assembly");
        (await admin.PostAsJsonAsync($"/api/items/{parent}/children", new { childId = released })).EnsureSuccessStatusCode();

        // Po powrocie do "W pracy" struktura znów się zmienia.
        (await admin.PatchAsJsonAsync($"/api/items/{released}/status", new { status = "w_pracy" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/items/{released}/children", new { childId = extra })).EnsureSuccessStatusCode();
    }
}
