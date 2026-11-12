using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Ustawienia -> Tagi: administrator widzi wszystkie tagi z liczbą elementów i może usunąć tag
// z całej bazy naraz, także z elementów wydanych. Zwykły użytkownik nie ma do tego dostępu.
[Collection("EasyPDM database")]
public class TagAdminTests
{
    private static async Task<int?> ItemCountAsync(HttpClient admin, string tag)
    {
        var tags = await admin.GetFromJsonAsync<JsonElement>("/api/settings/tags");
        foreach (var t in tags.EnumerateArray())
            if (t.GetProperty("name").GetString() == tag)
                return t.GetProperty("itemCount").GetInt32();
        return null;
    }

    [Fact]
    public async Task Administrator_usuwa_tag_z_calej_bazy()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Tagi admin {Guid.NewGuid()}");
        var tag = "stary" + Guid.NewGuid().ToString("N")[..6];

        var a = await admin.CreateNodeAsync(projectId, "Pierwsza", "part");
        var b = await admin.CreateNodeAsync(projectId, "Druga", "part");
        foreach (var item in new[] { a, b })
            (await admin.PostAsJsonAsync($"/api/items/{item}/tags", new { name = tag })).EnsureSuccessStatusCode();
        // Element wydany: jego tagów nie da się zmienić pojedynczo, ale usunięcie tagu z bazy go obejmuje.
        foreach (var status in new[] { "sprawdzany", "wydany" })
            (await admin.PatchAsJsonAsync($"/api/items/{b}/status", new { status })).EnsureSuccessStatusCode();

        Assert.Equal(2, await ItemCountAsync(admin, tag));

        (await admin.DeleteAsync($"/api/settings/tags/{tag}")).EnsureSuccessStatusCode();

        Assert.Null(await ItemCountAsync(admin, tag));
        Assert.DoesNotContain(tag, await admin.GetFromJsonAsync<List<string>>("/api/tags") ?? []);
        var tagged = await admin.GetFromJsonAsync<JsonElement>($"/api/items?tag={tag}");
        Assert.Equal(0, tagged.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/settings/tags/{tag}")).StatusCode);
    }

    [Fact]
    public async Task Nieuzywany_tag_ma_zero_elementow()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Tagi zero {Guid.NewGuid()}");
        var tag = "pusty" + Guid.NewGuid().ToString("N")[..6];
        var item = await admin.CreateNodeAsync(projectId, "Czesc", "part");
        (await admin.PostAsJsonAsync($"/api/items/{item}/tags", new { name = tag })).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/items/{item}/tags/{tag}")).EnsureSuccessStatusCode();

        Assert.Equal(0, await ItemCountAsync(admin, tag));
    }

    [Fact]
    public async Task Zwykly_uzytkownik_nie_zarzadza_tagami()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "tagi" + Guid.NewGuid().ToString("N")[..8];
        await admin.CreateUserAsync(username, "haslo123");

        using var user = factory.CreateClient();
        await user.LoginAsync(username, "haslo123");
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/settings/tags")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync("/api/settings/tags/cokolwiek")).StatusCode);
    }

    // Ukośnik w nazwie tagu dociera w adresie jako %2F, którego routing nie dekoduje.
    [Fact]
    public async Task Tag_z_ukosnikiem_da_sie_zdjac_i_usunac()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Tagi ukosnik {Guid.NewGuid()}");
        var tag = "a/b " + Guid.NewGuid().ToString("N")[..6];
        var encoded = Uri.EscapeDataString(tag);
        var item = await admin.CreateNodeAsync(projectId, "Czesc", "part");

        (await admin.PostAsJsonAsync($"/api/items/{item}/tags", new { name = tag })).EnsureSuccessStatusCode();
        Assert.Equal(1, await ItemCountAsync(admin, tag));
        (await admin.DeleteAsync($"/api/items/{item}/tags/{encoded}")).EnsureSuccessStatusCode();
        Assert.Equal(0, await ItemCountAsync(admin, tag));

        (await admin.DeleteAsync($"/api/settings/tags/{encoded}")).EnsureSuccessStatusCode();
        Assert.Null(await ItemCountAsync(admin, tag));
    }
}
