using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Pomocnicze rozszerzenia współdzielone przez testy — logowanie, tworzenie danych
// testowych, wyciąganie pojedynczych pól z odpowiedzi JSON.
internal static class TestClientHelpers
{
    public static async Task LoginAsync(this HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
    }

    public static async Task<Guid> CreateProjectAsync(this HttpClient adminClient, string name)
    {
        var response = await adminClient.PostAsJsonAsync("/api/projects", new { name, description = (string?)null, client = (string?)null, startDate = (DateOnly?)null, endDate = (DateOnly?)null });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateNodeAsync(this HttpClient adminClient, Guid projectId, string name, string itemType, Guid? parentId = null)
    {
        var response = await adminClient.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new
        {
            name,
            itemType,
            properties = itemType == "part" ? new { rodzaj = "Klienta" } : null,
            parentId,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateUserAsync(this HttpClient adminClient, string username, string password, string role = "user")
    {
        var response = await adminClient.PostAsJsonAsync("/api/users", new
        {
            username,
            password,
            displayName = username,
            email = (string?)null,
            role,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    public static async Task GrantProjectAccessAsync(this HttpClient adminClient, Guid projectId, Guid userId)
    {
        var response = await adminClient.PostAsync($"/api/projects/{projectId}/users/{userId}", content: null);
        response.EnsureSuccessStatusCode();
    }
}
