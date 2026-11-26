using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// DELETE /api/notifications: clears the caller's whole list at once, and only theirs.
[Collection("EasyPDM database")]
public class NotificationDeleteAllTests
{
    private static async Task<int> CountAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        return body.GetProperty("items").GetArrayLength();
    }

    [Fact]
    public async Task Delete_all_clears_only_the_callers_notifications()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");

        async Task<HttpClient> UserAsync()
        {
            var name = "nd" + Guid.NewGuid().ToString("N")[..8];
            var id = await admin.CreateUserAsync(name, "haslo123");
            foreach (var _ in new[] { 1, 2 })
                await admin.GrantProjectAccessAsync(await admin.CreateProjectAsync($"Notif {Guid.NewGuid()}"), id);
            var client = factory.CreateClient();
            await client.LoginAsync(name, "haslo123");
            return client;
        }

        using var anna = await UserAsync();
        using var jan = await UserAsync();
        Assert.True(await CountAsync(anna) >= 2);
        var janBefore = await CountAsync(jan);
        Assert.True(janBefore >= 2);

        (await anna.DeleteAsync("/api/notifications")).EnsureSuccessStatusCode();

        Assert.Equal(0, await CountAsync(anna));
        Assert.Equal(janBefore, await CountAsync(jan));
    }
}
