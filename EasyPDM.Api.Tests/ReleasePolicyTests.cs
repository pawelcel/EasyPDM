using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Opcja "status Wydany nadaje tylko administrator" (Ustawienia -> Użytkownicy). Domyślnie
// wyłączona: autor sam wydaje swoją część, jak dotąd. Po włączeniu zwykły użytkownik może
// przekazać element do sprawdzenia, ale wydać go musi administrator.
[Collection("EasyPDM database")]
public class ReleasePolicyTests
{
    private static async Task SetPolicyAsync(HttpClient admin, bool adminOnly) =>
        (await admin.PatchAsJsonAsync("/api/settings/release-policy", new { adminOnly })).EnsureSuccessStatusCode();

    private static async Task<(HttpClient Author, Guid ItemId)> AuthorWithItemInReviewAsync(
        EasyPDMWebApplicationFactory factory, HttpClient admin)
    {
        var projectId = await admin.CreateProjectAsync($"Wydanie {Guid.NewGuid()}");
        var username = "autor" + Guid.NewGuid().ToString("N")[..8];
        var userId = await admin.CreateUserAsync(username, "haslo123");
        await admin.GrantProjectAccessAsync(projectId, userId);

        var author = factory.CreateClient();
        await author.LoginAsync(username, "haslo123");
        var itemId = await author.CreateNodeAsync(projectId, "Plyta", "part");
        (await author.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "sprawdzany" }))
            .EnsureSuccessStatusCode();
        return (author, itemId);
    }

    [Fact]
    public async Task Domyslnie_autor_sam_wydaje_element()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");

        var policy = await admin.GetFromJsonAsync<JsonElement>("/api/settings/release-policy");
        Assert.False(policy.GetProperty("adminOnly").GetBoolean());

        var (author, itemId) = await AuthorWithItemInReviewAsync(factory, admin);
        using (author)
            (await author.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "wydany" }))
                .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Po_wlaczeniu_wydaje_tylko_administrator()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        await SetPolicyAsync(admin, true);
        try
        {
            var (author, itemId) = await AuthorWithItemInReviewAsync(factory, admin);
            using (author)
            {
                // Ustawienie widzi każdy zalogowany -- interfejs wyszarza u niego przycisk.
                var policy = await author.GetFromJsonAsync<JsonElement>("/api/settings/release-policy");
                Assert.True(policy.GetProperty("adminOnly").GetBoolean());

                var refused = await author.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "wydany" });
                Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

                // Cofnięcie do pracy nadal wolno -- reguła dotyczy tylko wydania.
                (await author.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "w_pracy" }))
                    .EnsureSuccessStatusCode();
                (await author.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "sprawdzany" }))
                    .EnsureSuccessStatusCode();
            }

            (await admin.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status = "wydany" }))
                .EnsureSuccessStatusCode();
        }
        finally
        {
            // Baza jest wspólna dla testów tej klasy -- ustawienie nie może przeciekać dalej.
            await SetPolicyAsync(admin, false);
        }
    }

    [Fact]
    public async Task Ustawienie_zmienia_tylko_administrator()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "user" + Guid.NewGuid().ToString("N")[..8];
        await admin.CreateUserAsync(username, "haslo123");

        using var user = factory.CreateClient();
        await user.LoginAsync(username, "haslo123");
        var response = await user.PatchAsJsonAsync("/api/settings/release-policy", new { adminOnly = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
