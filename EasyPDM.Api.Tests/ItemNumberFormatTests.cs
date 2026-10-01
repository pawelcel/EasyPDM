using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Format numeru elementu: literowy prefiks rodzaju plus dopełnienie zerami do zadanej
// szerokości. OBA są zamrażane na elemencie przy jego tworzeniu i nie działają wstecz --
// numer elementu jest jednocześnie nazwą jego pliku na dysku, a tej nie da się przeliczyć.
// Serwer skleja z tego gotowe "itemNumberLabel" -- jedno miejsce dla frontendu i trzech makr
// CAD, które dotąd składały nazwę pliku z samej liczby, pomijając prefiks.
[Collection("EasyPDM database")]
public class ItemNumberFormatTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static async Task SetDigitsAsync(HttpClient client, int digits)
    {
        var response = await client.PatchAsJsonAsync("/api/settings/item-number-format", new { digits });
        response.EnsureSuccessStatusCode();
    }

    private static async Task SetWithNameAsync(HttpClient client, bool withName)
    {
        var response = await client.PatchAsJsonAsync("/api/settings/item-number-format", new { withName });
        response.EnsureSuccessStatusCode();
    }

    private static async Task SetPrefixAsync(HttpClient client, string rodzaj, string? prefix)
    {
        var response = await client.PatchAsJsonAsync(
            $"/api/settings/item-number-prefixes/{Uri.EscapeDataString(rodzaj)}", new { prefix });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> GetItemAsync(HttpClient client, Guid itemId)
    {
        var response = await client.GetAsync($"/api/items/{itemId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Etykieta_laczy_prefiks_z_dopelnieniem()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Format numeru {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Klienta", "C");
        await SetDigitsAsync(client, 4);

        // CreateNodeAsync tworzy Część rodzaju "Klienta", więc prefiks zadziała.
        var itemId = await client.CreateNodeAsync(projectId, $"Wspornik {Guid.NewGuid()}", "part");

        var item = await GetItemAsync(client, itemId);
        var number = item.GetProperty("itemNumber").GetInt32();
        Assert.Equal("C", item.GetProperty("itemNumberPrefix").GetString());
        Assert.Equal($"C{number.ToString().PadLeft(4, '0')}", item.GetProperty("itemNumberLabel").GetString());

        // Sam numer w bazie pozostaje liczbą — dopełnienie jest wyłącznie formatem.
        Assert.True(number > 0);
    }

    [Fact]
    public async Task Ani_dopelnienie_ani_prefiks_nie_dzialaja_wstecz()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Format numeru {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Klienta", null);
        await SetDigitsAsync(client, 0);
        var itemId = await client.CreateNodeAsync(projectId, $"Tuleja {Guid.NewGuid()}", "part");
        var number = (await GetItemAsync(client, itemId)).GetProperty("itemNumber").GetInt32();

        // Bez prefiksu i bez dopełnienia: sama liczba.
        Assert.Equal(number.ToString(), (await GetItemAsync(client, itemId)).GetProperty("itemNumberLabel").GetString());

        // Oba ustawienia zamrażane są na elemencie przy jego tworzeniu, więc zmiana któregokolwiek
        // NIE rusza tego, co już istnieje. To nie jest kosmetyka: makro CAD zapisało plik na dysku
        // pod nazwą zbudowaną z tego numeru, a pliku nikt wstecz nie przemianuje.
        await SetDigitsAsync(client, 6);
        await SetPrefixAsync(client, "Klienta", "X");

        var after = await GetItemAsync(client, itemId);
        Assert.True(after.GetProperty("itemNumberPrefix").ValueKind == JsonValueKind.Null);
        Assert.Equal(number.ToString(), after.GetProperty("itemNumberLabel").GetString());

        // Element utworzony PO zmianie dostaje już nowy format.
        var freshId = await client.CreateNodeAsync(projectId, $"Panewka {Guid.NewGuid()}", "part");
        var fresh = await GetItemAsync(client, freshId);
        var freshNumber = fresh.GetProperty("itemNumber").GetInt32();
        Assert.Equal($"X{freshNumber.ToString().PadLeft(6, '0')}", fresh.GetProperty("itemNumberLabel").GetString());

        await SetDigitsAsync(client, 0);
        await SetPrefixAsync(client, "Klienta", null);
    }

    [Fact]
    public async Task Numer_dluzszy_niz_ustawienie_nie_jest_przycinany()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Format numeru {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Klienta", null);
        await SetDigitsAsync(client, 1);
        var itemId = await client.CreateNodeAsync(projectId, $"Sworzen {Guid.NewGuid()}", "part");

        var item = await GetItemAsync(client, itemId);
        var number = item.GetProperty("itemNumber").GetInt32();
        // Ustawienie to MINIMALNA szerokość, nie format o stałej długości.
        Assert.Equal(number.ToString(), item.GetProperty("itemNumberLabel").GetString());

        await SetDigitsAsync(client, 0);
    }

    [Fact]
    public async Task Bilet_tworzenia_niesie_etykiete_dla_makr_CAD()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Format numeru {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Klienta", "C");
        await SetDigitsAsync(client, 4);

        // Makro CAD otwiera przeglądarkę z biletem i odpytuje go o nazwę, pod którą ma zapisać
        // plik — to JEDYNE miejsce, z którego tę nazwę bierze.
        var ticket = Guid.NewGuid();
        var createResponse = await client.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new
        {
            name = $"Rama {Guid.NewGuid()}",
            itemType = "part",
            properties = new { rodzaj = "Klienta" },
            parentId = (Guid?)null,
            ticket,
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var createdNumber = created.GetProperty("itemNumber").GetInt32();
        Assert.Equal($"C{createdNumber.ToString().PadLeft(4, '0')}", created.GetProperty("itemNumberLabel").GetString());

        var ticketResponse = await client.GetAsync($"/api/create-tickets/{ticket}");
        Assert.Equal(HttpStatusCode.OK, ticketResponse.StatusCode);
        var ticketBody = await ticketResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"C{createdNumber.ToString().PadLeft(4, '0')}", ticketBody.GetProperty("itemNumberLabel").GetString());

        await SetDigitsAsync(client, 0);
        await SetPrefixAsync(client, "Klienta", null);
    }

    [Fact]
    public async Task Nazwa_elementu_da_sie_wylaczyc_z_nazwy_rekordu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Format numeru {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Klienta", "C");
        await SetDigitsAsync(client, 4);

        // Domyślnie nazwa wchodzi: "C0001(Wspornik)".
        await SetWithNameAsync(client, true);
        var withNameId = await client.CreateNodeAsync(projectId, "Wspornik", "part");
        var withNameItem = await GetItemAsync(client, withNameId);
        var withNameNumber = withNameItem.GetProperty("itemNumber").GetInt32();
        Assert.Equal($"C{withNameNumber.ToString().PadLeft(4, '0')}(Wspornik)",
            withNameItem.GetProperty("recordName").GetString());

        // Po wyłączeniu zostaje sam numer.
        await SetWithNameAsync(client, false);
        var bareId = await client.CreateNodeAsync(projectId, "Tuleja", "part");
        var bareItem = await GetItemAsync(client, bareId);
        var bareNumber = bareItem.GetProperty("itemNumber").GetInt32();
        Assert.Equal($"C{bareNumber.ToString().PadLeft(4, '0')}", bareItem.GetProperty("recordName").GetString());

        // Element utworzony WCZEŚNIEJ zachowuje swoją postać — ustawienie jest zamrażane na
        // elemencie, tak samo jak prefiks i dopełnienie.
        Assert.Equal($"C{withNameNumber.ToString().PadLeft(4, '0')}(Wspornik)",
            (await GetItemAsync(client, withNameId)).GetProperty("recordName").GetString());

        await SetWithNameAsync(client, true);
        await SetDigitsAsync(client, 0);
        await SetPrefixAsync(client, "Klienta", null);
    }

    [Fact]
    public async Task Zapis_jednego_ustawienia_nie_rusza_drugiego()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        await SetDigitsAsync(client, 5);
        await SetWithNameAsync(client, false);

        // Dwie osobne sekcje w interfejsie zapisują się niezależnie — PATCH z samym "withName"
        // nie może wyzerować liczby cyfr ani odwrotnie.
        var format = await (await client.GetAsync("/api/settings/item-number-format"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, format.GetProperty("digits").GetInt32());
        Assert.False(format.GetProperty("withName").GetBoolean());

        await SetDigitsAsync(client, 0);
        format = await (await client.GetAsync("/api/settings/item-number-format"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(format.GetProperty("withName").GetBoolean());

        await SetWithNameAsync(client, true);
    }

    [Fact]
    public async Task Ustawienie_odrzuca_wartosci_spoza_zakresu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PatchAsJsonAsync("/api/settings/item-number-format", new { digits = -1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PatchAsJsonAsync("/api/settings/item-number-format", new { digits = 11 })).StatusCode);

        // Zakres brzegowy musi przechodzić.
        await SetDigitsAsync(client, 10);
        var format = await (await client.GetAsync("/api/settings/item-number-format"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, format.GetProperty("digits").GetInt32());

        await SetDigitsAsync(client, 0);
    }
}
