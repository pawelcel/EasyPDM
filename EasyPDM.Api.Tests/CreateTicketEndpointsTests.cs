using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Mechanizm "biletu" tworzenia (zob. CreateTicketStore.cs) — pozwala makru CAD, które
// otworzyło przeglądarkę zamiast pokazywać własny dialog, odpytywać o wynik utworzenia
// elementu przez POST /nodes wywołany z tej przeglądarki.
[Collection("EasyPDM database")]
public class CreateTicketEndpointsTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    [Fact]
    public async Task Nieznany_lub_oczekujacy_bilet_zwraca_202()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var response = await client.GetAsync($"/api/create-tickets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Ticket_w_POST_nodes_udostepnia_wynik_przez_GET_create_tickets()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync("Projekt biletowy");
        var ticket = Guid.NewGuid();

        var createResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new
        {
            name = "Czesc.SLDPRT",
            itemType = "part",
            properties = new { rodzaj = "Klienta" },
            parentId = (Guid?)null,
            ticket,
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var expectedItemId = created.GetProperty("id").GetGuid();
        var expectedItemNumber = created.GetProperty("itemNumber").GetInt32();

        var pollResponse = await client.GetAsync($"/api/create-tickets/{ticket}");

        Assert.Equal(HttpStatusCode.OK, pollResponse.StatusCode);
        var body = await pollResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedItemId, body.GetProperty("itemId").GetGuid());
        Assert.Equal(expectedItemNumber, body.GetProperty("itemNumber").GetInt32());
        Assert.Equal("Czesc.SLDPRT", body.GetProperty("name").GetString());
        Assert.False(body.GetProperty("existing").GetBoolean());
    }

    [Fact]
    public async Task Attach_existing_dopelnia_bilet_bez_tworzenia_elementu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync("Projekt dogrywany");
        var existingItemId = await client.CreateNodeAsync(projectId, "Zlozenie.SLDASM", "assembly");
        var ticket = Guid.NewGuid();

        var attachResponse = await client.PostAsJsonAsync($"/api/create-tickets/{ticket}/attach-existing", new
        {
            itemId = existingItemId,
            exportStep = false,
        });

        Assert.Equal(HttpStatusCode.OK, attachResponse.StatusCode);

        var pollResponse = await client.GetAsync($"/api/create-tickets/{ticket}");
        Assert.Equal(HttpStatusCode.OK, pollResponse.StatusCode);
        var body = await pollResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(existingItemId, body.GetProperty("itemId").GetGuid());
        Assert.True(body.GetProperty("existing").GetBoolean());
        Assert.False(body.GetProperty("exportStep").GetBoolean());
    }

    [Fact]
    public async Task Attach_existing_z_nieznanym_elementem_zwraca_404()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var response = await client.PostAsJsonAsync($"/api/create-tickets/{Guid.NewGuid()}/attach-existing", new
        {
            itemId = Guid.NewGuid(),
            exportStep = (bool?)null,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task POST_nodes_bez_ticketu_nie_zaklada_wpisu_w_store()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync("Projekt bez biletu");
        await client.CreateNodeAsync(projectId, "Czesc.SLDPRT", "part");

        var pollResponse = await client.GetAsync($"/api/create-tickets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Accepted, pollResponse.StatusCode);
    }
}
