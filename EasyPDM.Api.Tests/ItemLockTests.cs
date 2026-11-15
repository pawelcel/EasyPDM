using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// The owner lock (POST /api/items/{id}/lock and /release): while someone holds an item, nobody
// else may edit it or take it over -- except an administrator, who takes it over under their own
// name (never acting as the owner), for an absent colleague. Released items are never locked.
[Collection("EasyPDM database")]
public class ItemLockTests
{
    private static async Task<(HttpClient Client, Guid Id)> UserAsync(EasyPDMWebApplicationFactory factory, HttpClient admin, Guid projectId)
    {
        var name = "u" + Guid.NewGuid().ToString("N")[..10];
        var id = await admin.CreateUserAsync(name, "haslo1234");
        await admin.GrantProjectAccessAsync(projectId, id);
        var client = factory.CreateClient();
        await client.LoginAsync(name, "haslo1234");
        return (client, id);
    }

    private static async Task<JsonElement> ItemAsync(HttpClient c, Guid itemId) =>
        await c.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}");

    private static Task<HttpResponseMessage> EditAsync(HttpClient c, Guid itemId, string material) =>
        c.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { material });

    [Fact]
    public async Task Only_the_owner_edits_a_locked_item_and_an_admin_takes_it_over_under_their_own_name()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Lock {Guid.NewGuid()}");
        var (owner, ownerId) = await UserAsync(factory, admin, projectId);
        var (other, _) = await UserAsync(factory, admin, projectId);
        var itemId = await owner.CreateNodeAsync(projectId, "Bracket", "part");

        (await owner.PostAsync($"/api/items/{itemId}/lock", null)).EnsureSuccessStatusCode();
        var locked = await ItemAsync(owner, itemId);
        Assert.True(locked.GetProperty("ownerLocked").GetBoolean());
        Assert.Equal(ownerId, locked.GetProperty("ownerId").GetGuid());

        // Someone else can neither take it, nor free it, nor edit it; the owner can edit.
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync($"/api/items/{itemId}/lock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync($"/api/items/{itemId}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await EditAsync(other, itemId, "S235")).StatusCode);
        (await EditAsync(owner, itemId, "S355")).EnsureSuccessStatusCode();

        // An administrator takes the lock over and becomes the owner, as themselves.
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");
        (await admin.PostAsync($"/api/items/{itemId}/lock", null)).EnsureSuccessStatusCode();
        Assert.Equal(me.GetProperty("id").GetGuid(), (await ItemAsync(admin, itemId)).GetProperty("ownerId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await EditAsync(owner, itemId, "S235")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/items/{itemId}/release", null)).StatusCode);

        // Freed, anyone with access may take it.
        (await admin.PostAsync($"/api/items/{itemId}/release", null)).EnsureSuccessStatusCode();
        Assert.False((await ItemAsync(other, itemId)).GetProperty("ownerLocked").GetBoolean());
        (await other.PostAsync($"/api/items/{itemId}/lock", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_released_item_cannot_be_locked()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Lock {Guid.NewGuid()}");
        var itemId = await admin.CreateNodeAsync(projectId, "Plate", "part");
        foreach (var status in new[] { "sprawdzany", "wydany" })
            (await admin.PatchAsJsonAsync($"/api/items/{itemId}/status", new { status })).EnsureSuccessStatusCode();

        Assert.False((await ItemAsync(admin, itemId)).GetProperty("ownerLocked").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/items/{itemId}/lock", null)).StatusCode);
    }
}
