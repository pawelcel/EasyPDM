using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Reguła statusu Złożenia względem jego BOM-u (0.4.1): złożenie nie może wyprzedzać swoich
// komponentów. Sprawdzany jest WYŁĄCZNIE jeden poziom w dół — głębiej pilnuje tego ta sama
// reguła zastosowana do podzłożenia, kiedy przychodzi jego kolej.
[Collection("EasyPDM database")]
public class AssemblyStatusRuleTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static async Task<HttpResponseMessage> SetStatusAsync(
        HttpClient client, Guid itemId, string status, bool promoteChildren = false) =>
        await client.PatchAsJsonAsync($"/api/items/{itemId}/status",
            new { status, comment = (string?)null, promoteChildren });

    private static async Task<string?> GetStatusAsync(HttpClient client, Guid itemId)
    {
        var response = await client.GetAsync($"/api/items/{itemId}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("status").GetString();
    }

    private static async Task<JsonElement> PrecheckAsync(HttpClient client, Guid itemId, string target)
    {
        var response = await client.GetAsync($"/api/items/{itemId}/status-precheck?target={target}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // Złożenie z jedną częścią w BOM-ie; część zostaje w zadanym statusie.
    private static async Task<(Guid Assembly, Guid Part)> CreateAssemblyWithPartAsync(
        HttpClient client, Guid projectId, string suffix)
    {
        var assembly = await client.CreateNodeAsync(projectId, $"Złożenie {suffix}", "assembly");
        var part = await client.CreateNodeAsync(projectId, $"Część {suffix}", "part", assembly);
        return (assembly, part);
    }

    [Fact]
    public async Task Sprawdzany_wymaga_komponentow_co_najmniej_sprawdzonych()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Reguła BOM {Guid.NewGuid()}");

        var (assembly, part) = await CreateAssemblyWithPartAsync(client, projectId, "A");

        // Część jest w "w_pracy" — złożenie nie ma prawa przejść na "sprawdzany".
        var refused = await SetStatusAsync(client, assembly, "sprawdzany");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("w_pracy", await GetStatusAsync(client, assembly));
        Assert.Equal("w_pracy", await GetStatusAsync(client, part));

        // Precheck mówi to samo, bez zmieniania czegokolwiek.
        var precheck = await PrecheckAsync(client, assembly, "sprawdzany");
        Assert.False(precheck.GetProperty("ok").GetBoolean());
        Assert.Equal(part, precheck.GetProperty("promotable").EnumerateArray().Single().GetProperty("id").GetGuid());
        Assert.Empty(precheck.GetProperty("subAssemblies").EnumerateArray());
        Assert.Empty(precheck.GetProperty("blocked").EnumerateArray());

        // Ze zgodą: najpierw komponent, potem złożenie — obie zmiany naraz.
        (await SetStatusAsync(client, assembly, "sprawdzany", promoteChildren: true)).EnsureSuccessStatusCode();
        Assert.Equal("sprawdzany", await GetStatusAsync(client, assembly));
        Assert.Equal("sprawdzany", await GetStatusAsync(client, part));
    }

    [Fact]
    public async Task Wydany_wymaga_komponentow_wydanych()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Reguła BOM {Guid.NewGuid()}");

        var (assembly, part) = await CreateAssemblyWithPartAsync(client, projectId, "B");

        // Oba na "sprawdzany" — dla przejścia złożenia na "sprawdzany" to wystarcza...
        (await SetStatusAsync(client, part, "sprawdzany")).EnsureSuccessStatusCode();
        (await SetStatusAsync(client, assembly, "sprawdzany")).EnsureSuccessStatusCode();

        // ...ale na "wydany" już nie: komponent musi być wydany.
        var refused = await SetStatusAsync(client, assembly, "wydany");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("sprawdzany", await GetStatusAsync(client, assembly));

        (await SetStatusAsync(client, assembly, "wydany", promoteChildren: true)).EnsureSuccessStatusCode();
        Assert.Equal("wydany", await GetStatusAsync(client, assembly));
        Assert.Equal("wydany", await GetStatusAsync(client, part));
    }

    [Fact]
    public async Task Podzlozenie_blokuje_i_zgoda_tego_nie_omija()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Reguła BOM {Guid.NewGuid()}");

        var top = await client.CreateNodeAsync(projectId, $"Złożenie górne {Guid.NewGuid()}", "assembly");
        var sub = await client.CreateNodeAsync(projectId, $"Podzłożenie {Guid.NewGuid()}", "assembly", top);
        var leaf = await client.CreateNodeAsync(projectId, $"Część w podzłożeniu {Guid.NewGuid()}", "part", sub);

        var precheck = await PrecheckAsync(client, top, "sprawdzany");
        Assert.False(precheck.GetProperty("ok").GetBoolean());
        Assert.Equal(sub, precheck.GetProperty("subAssemblies").EnumerateArray().Single().GetProperty("id").GetGuid());
        // Część LEŻĄCA GŁĘBIEJ nie pojawia się nigdzie — reguła patrzy tylko jeden poziom w dół.
        Assert.Empty(precheck.GetProperty("promotable").EnumerateArray());

        // Zgoda na pociągnięcie komponentów nie omija podzłożenia — to świadomie osobna decyzja.
        var refused = await SetStatusAsync(client, top, "sprawdzany", promoteChildren: true);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("w_pracy", await GetStatusAsync(client, top));
        Assert.Equal("w_pracy", await GetStatusAsync(client, sub));
        Assert.Equal("w_pracy", await GetStatusAsync(client, leaf));

        // Po ogarnięciu podzłożenia (najpierw jego własna część, potem ono samo) górne przechodzi
        // już bez żadnej zgody — warunek jest spełniony normalnie.
        (await SetStatusAsync(client, sub, "sprawdzany", promoteChildren: true)).EnsureSuccessStatusCode();
        (await SetStatusAsync(client, top, "sprawdzany")).EnsureSuccessStatusCode();
        Assert.Equal("sprawdzany", await GetStatusAsync(client, top));
    }

    [Fact]
    public async Task Anulowany_komponent_blokuje_zamiast_zostac_przywrocony()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Reguła BOM {Guid.NewGuid()}");

        var (assembly, part) = await CreateAssemblyWithPartAsync(client, projectId, "C");

        // Anulować da się wyłącznie z "wydany", więc część musi tam najpierw dojść.
        (await SetStatusAsync(client, part, "sprawdzany")).EnsureSuccessStatusCode();
        (await SetStatusAsync(client, part, "wydany")).EnsureSuccessStatusCode();
        (await SetStatusAsync(client, part, "anulowana")).EnsureSuccessStatusCode();

        var precheck = await PrecheckAsync(client, assembly, "sprawdzany");
        Assert.False(precheck.GetProperty("ok").GetBoolean());
        var blocked = precheck.GetProperty("blocked").EnumerateArray().Single();
        Assert.Equal(part, blocked.GetProperty("id").GetGuid());
        Assert.Equal("anulowana", blocked.GetProperty("reason").GetString());

        // Zgoda nie przywraca anulowanej części do obiegu — to musi być osobna decyzja.
        var refused = await SetStatusAsync(client, assembly, "sprawdzany", promoteChildren: true);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("anulowana", await GetStatusAsync(client, part));
        Assert.Equal("w_pracy", await GetStatusAsync(client, assembly));
    }

    [Fact]
    public async Task Zlozenie_bez_komponentow_i_zwykla_czesc_nie_podlegaja_regule()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Reguła BOM {Guid.NewGuid()}");

        // Puste złożenie — nie ma czego sprawdzać, przechodzi normalnie.
        var empty = await client.CreateNodeAsync(projectId, $"Puste złożenie {Guid.NewGuid()}", "assembly");
        Assert.True((await PrecheckAsync(client, empty, "sprawdzany")).GetProperty("ok").GetBoolean());
        (await SetStatusAsync(client, empty, "sprawdzany")).EnsureSuccessStatusCode();

        // Samodzielna Część nie ma BOM-u — reguła jej nie dotyczy, a precheck odpowiada "można",
        // żeby front mógł go wołać bez rozróżniania rodzaju elementu.
        var part = await client.CreateNodeAsync(projectId, $"Samotna część {Guid.NewGuid()}", "part");
        Assert.True((await PrecheckAsync(client, part, "wydany")).GetProperty("ok").GetBoolean());
        (await SetStatusAsync(client, part, "sprawdzany")).EnsureSuccessStatusCode();
        (await SetStatusAsync(client, part, "wydany")).EnsureSuccessStatusCode();
        Assert.Equal("wydany", await GetStatusAsync(client, part));
    }

    [Fact]
    public async Task Cofniecie_do_pracy_nie_rusza_komponentow()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Reguła BOM {Guid.NewGuid()}");

        var (assembly, part) = await CreateAssemblyWithPartAsync(client, projectId, "D");
        (await SetStatusAsync(client, assembly, "sprawdzany", promoteChildren: true)).EnsureSuccessStatusCode();

        // Powrót złożenia do pracy nie ma powodu ruszać części — reguła działa tylko "w górę".
        (await SetStatusAsync(client, assembly, "w_pracy")).EnsureSuccessStatusCode();
        Assert.Equal("w_pracy", await GetStatusAsync(client, assembly));
        Assert.Equal("sprawdzany", await GetStatusAsync(client, part));
    }
}
