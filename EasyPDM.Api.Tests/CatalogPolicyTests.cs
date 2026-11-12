using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Opcja "katalogi zmienia tylko administrator" (Ustawienia -> Użytkownicy). Domyślnie
// wyłączona: Materiały, Obróbki, Producentów i Klientów zmienia każdy zalogowany, jak dotąd.
// Po włączeniu zwykły użytkownik dalej je czyta i wybiera z nich, ale nie zmienia.
[Collection("EasyPDM database")]
public class CatalogPolicyTests
{
    private static async Task SetPolicyAsync(HttpClient admin, bool adminOnly) =>
        (await admin.PatchAsJsonAsync("/api/settings/catalog-policy", new { adminOnly })).EnsureSuccessStatusCode();

    private static async Task<HttpClient> UserAsync(EasyPDMWebApplicationFactory factory, HttpClient admin)
    {
        var username = "kat" + Guid.NewGuid().ToString("N")[..8];
        await admin.CreateUserAsync(username, "haslo123");
        var user = factory.CreateClient();
        await user.LoginAsync(username, "haslo123");
        return user;
    }

    [Fact]
    public async Task Domyslnie_zwykly_uzytkownik_zmienia_katalogi()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/settings/catalog-policy")).GetProperty("adminOnly").GetBoolean());

        using var user = await UserAsync(factory, admin);
        var created = await user.PostAsJsonAsync("/api/materials", new { name = "Stal " + Guid.NewGuid().ToString("N")[..6] });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task Po_wlaczeniu_katalogi_zmienia_tylko_administrator()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var tag = Guid.NewGuid().ToString("N")[..6];
        var material = await admin.PostAsJsonAsync("/api/materials", new { name = "Alu " + tag });
        var materialId = (await material.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        await SetPolicyAsync(admin, true);
        try
        {
            using var user = await UserAsync(factory, admin);
            // Każdy zalogowany widzi ustawienie -- interfejs chowa u niego przyciski edycji.
            Assert.True((await user.GetFromJsonAsync<JsonElement>("/api/settings/catalog-policy")).GetProperty("adminOnly").GetBoolean());

            // Odczyt zostaje.
            foreach (var path in new[] { "/api/materials", "/api/processes", "/api/manufacturers", "/api/clients" })
                Assert.Equal(HttpStatusCode.OK, (await user.GetAsync(path)).StatusCode);

            // Zapis do każdego z czterech katalogów jest odrzucany, także na ścieżkach zagnieżdżonych.
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/materials", new { name = "X " + tag })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PatchAsJsonAsync($"/api/materials/{materialId}", new { name = "Y " + tag })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/materials/{materialId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/processes",
                new { name = "P " + tag, forParts = true, forAssemblies = false })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync("/api/processes/1/texts", new { texts = Array.Empty<object>() })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/manufacturers", new { name = "M " + tag })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/manufacturers/1/contacts", new { firstName = "A" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/clients", new { name = "K " + tag })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/clients/1/nodes/folder", new { name = "F" })).StatusCode);

            // Administrator zmienia dalej.
            (await admin.PatchAsJsonAsync($"/api/materials/{materialId}", new { name = "Alu2 " + tag })).EnsureSuccessStatusCode();

            // Samo ustawienie zmienia tylko administrator.
            Assert.Equal(HttpStatusCode.Forbidden,
                (await user.PatchAsJsonAsync("/api/settings/catalog-policy", new { adminOnly = false })).StatusCode);
        }
        finally
        {
            await SetPolicyAsync(admin, false);
        }
    }

    // Element dalej zapisuje wybrany materiał i obróbki -- to opis elementu, nie zmiana katalogu.
    [Fact]
    public async Task Blokada_katalogow_nie_przeszkadza_w_opisywaniu_elementow()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Katalogi {Guid.NewGuid()}");
        var username = "opis" + Guid.NewGuid().ToString("N")[..8];
        var userId = await admin.CreateUserAsync(username, "haslo123");
        await admin.GrantProjectAccessAsync(projectId, userId);

        await SetPolicyAsync(admin, true);
        try
        {
            using var user = factory.CreateClient();
            await user.LoginAsync(username, "haslo123");
            var item = await user.CreateNodeAsync(projectId, "Plyta", "part");
            (await user.PatchAsJsonAsync($"/api/items/{item}/properties",
                new { processes = new object[] { new { id = 1, name = "Spawanie", description = "MIG" } } })).EnsureSuccessStatusCode();
        }
        finally
        {
            await SetPolicyAsync(admin, false);
        }
    }
}
