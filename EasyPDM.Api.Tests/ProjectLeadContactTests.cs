using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Prowadzący projekt (lead_contact_id) -- kontakt klienta wskazany na projekcie, zarówno
// kontakt samego klienta jak i kontakt przypisany do konkretnej Nazwy 2 tego klienta (zob.
// ValidateLeadContactAsync w ProjectEndpoints.cs). Testy pokrywają zarówno "szczęśliwą
// ścieżkę" (oba rodzaje kontaktu akceptowane), jak i odrzucenia (kontakt innego klienta,
// kontakt innej Nazwy 2).
[Collection("EasyPDM database")]
public class ProjectLeadContactTests(EasyPDMWebApplicationFactory factory) : IClassFixture<EasyPDMWebApplicationFactory>
{
    private async Task<HttpClient> AdminClientAsync()
    {
        var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        return client;
    }

    private static async Task<int> CreateClientAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/clients", new { name, location = (string?)null });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private static async Task<int> AddOwnContactAsync(HttpClient client, int clientId, string firstName)
    {
        var response = await client.PostAsJsonAsync($"/api/clients/{clientId}/contacts", new
        {
            firstName,
            lastName = (string?)null,
            phone = (string?)null,
            position = (string?)null,
            email = (string?)null,
            address = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private static async Task<int> AddName2Async(HttpClient client, int clientId, string name2)
    {
        var response = await client.PostAsJsonAsync($"/api/clients/{clientId}/name2", new { name2 });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private static async Task<int> AddName2ContactAsync(HttpClient client, int clientId, int name2Id, string firstName)
    {
        var response = await client.PostAsJsonAsync($"/api/clients/{clientId}/name2/{name2Id}/contacts", new
        {
            firstName,
            lastName = (string?)null,
            phone = (string?)null,
            position = (string?)null,
            email = (string?)null,
            address = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetInt32();
    }

    private static object ProjectBody(string name, int? clientId, int? clientName2Id, int? leadContactId) => new
    {
        name,
        description = (string?)null,
        clientId,
        clientName2Id,
        leadContactId,
        closed = false,
        startDate = (DateOnly?)null,
        endDate = (DateOnly?)null,
    };

    [Fact]
    public async Task Prowadzacym_moze_byc_kontakt_samego_klienta()
    {
        var client = await AdminClientAsync();
        var clientId = await CreateClientAsync(client, $"Klient A {Guid.NewGuid()}");
        var contactId = await AddOwnContactAsync(client, clientId, "Jan");

        var response = await client.PostAsJsonAsync("/api/projects", ProjectBody($"Projekt A {Guid.NewGuid()}", clientId, null, contactId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(contactId, body.GetProperty("leadContactId").GetInt32());
        Assert.Equal("Jan", body.GetProperty("leadContactName").GetString());
    }

    [Fact]
    public async Task Prowadzacym_moze_byc_kontakt_wybranej_nazwy2()
    {
        var client = await AdminClientAsync();
        var clientId = await CreateClientAsync(client, $"Klient B {Guid.NewGuid()}");
        var name2Id = await AddName2Async(client, clientId, "Oddzial B");
        var name2ContactId = await AddName2ContactAsync(client, clientId, name2Id, "Ewa");

        var response = await client.PostAsJsonAsync("/api/projects", ProjectBody($"Projekt B {Guid.NewGuid()}", clientId, name2Id, name2ContactId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(name2ContactId, body.GetProperty("leadContactId").GetInt32());
    }

    [Fact]
    public async Task Kontakt_innego_klienta_jest_odrzucany()
    {
        var client = await AdminClientAsync();
        var clientId = await CreateClientAsync(client, $"Klient C {Guid.NewGuid()}");
        var otherClientId = await CreateClientAsync(client, $"Klient D {Guid.NewGuid()}");
        var otherContactId = await AddOwnContactAsync(client, otherClientId, "Obcy");

        var response = await client.PostAsJsonAsync("/api/projects", ProjectBody($"Projekt C {Guid.NewGuid()}", clientId, null, otherContactId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Kontakt_innej_nazwy2_tego_samego_klienta_jest_odrzucany()
    {
        var client = await AdminClientAsync();
        var clientId = await CreateClientAsync(client, $"Klient E {Guid.NewGuid()}");
        var name2AId = await AddName2Async(client, clientId, "Oddzial E1");
        var name2BId = await AddName2Async(client, clientId, "Oddzial E2");
        var name2AContactId = await AddName2ContactAsync(client, clientId, name2AId, "Ktos");

        // Projekt wskazuje Nazwe2 "E2", ale kontakt nalezy do "E1" -- niedozwolone.
        var response = await client.PostAsJsonAsync("/api/projects", ProjectBody($"Projekt E {Guid.NewGuid()}", clientId, name2BId, name2AContactId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Zmiana_prowadzacego_przez_patch_dziala()
    {
        var client = await AdminClientAsync();
        var clientId = await CreateClientAsync(client, $"Klient F {Guid.NewGuid()}");
        var contactId = await AddOwnContactAsync(client, clientId, "Piotr");

        var createResponse = await client.PostAsJsonAsync("/api/projects", ProjectBody($"Projekt F {Guid.NewGuid()}", clientId, null, null));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = created.GetProperty("id").GetGuid();
        Assert.False(created.TryGetProperty("leadContactId", out var initialLead) && initialLead.ValueKind != JsonValueKind.Null);

        var patchResponse = await client.PatchAsJsonAsync($"/api/projects/{projectId}", ProjectBody($"Projekt F {Guid.NewGuid()}", clientId, null, contactId));

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var patched = await patchResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(contactId, patched.GetProperty("leadContactId").GetInt32());
    }
}
