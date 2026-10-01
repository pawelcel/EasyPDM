using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Poprawka pomyłki w rodzaju: zmiana rodzaju przelicza prefiks numeru, ale TYLKO dopóki
// element nie ma pliku w żadnym z czterech wyróżnionych pól (cad, drawing, pdf, step). Te
// nazwy buduje makro z numeru elementu, więc po ich wgraniu zmiana rodzaju jest ODRZUCANA --
// rodzaj rozjechany z numerem byłby gorszy niż brak możliwości poprawki. Zwykłe załączniki
// zachowują własną nazwę i niczego nie blokują.
[Collection("EasyPDM database")]
public class ItemKindPrefixTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

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

    private static async Task<Guid> CreatePartAsync(HttpClient client, Guid projectId, string rodzaj)
    {
        var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/nodes", new
        {
            name = $"Element {Guid.NewGuid()}",
            itemType = "part",
            properties = new { rodzaj },
            parentId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task AttachFileAsync(HttpClient client, Guid itemId, string? role, string fileName)
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("zawartosc")), "file", fileName },
        };
        if (role is not null)
            form.Add(new StringContent(role), "role");
        (await client.PostAsync($"/api/items/{itemId}/attachments", form)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Zmiana_rodzaju_przelicza_prefiks_dopoki_nie_ma_zalacznikow()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Prefiks rodzaju {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Wykonywana", "W");
        await SetPrefixAsync(client, "Zakupowa", "Z");

        var itemId = await CreatePartAsync(client, projectId, "Wykonywana");
        var number = (await GetItemAsync(client, itemId)).GetProperty("itemNumber").GetInt32();
        Assert.Equal("W", (await GetItemAsync(client, itemId)).GetProperty("itemNumberPrefix").GetString());

        // Pomyłka w rodzaju, poprawiona zanim cokolwiek zostało wgrane -- prefiks nadąża.
        var patch = await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { rodzaj = "Zakupowa" });
        patch.EnsureSuccessStatusCode();
        Assert.True((await patch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("prefixRecalculated").GetBoolean());

        var after = await GetItemAsync(client, itemId);
        Assert.Equal("Z", after.GetProperty("itemNumberPrefix").GetString());
        // Sam numer się NIE zmienia -- przelicza się wyłącznie prefiks.
        Assert.Equal(number, after.GetProperty("itemNumber").GetInt32());
        Assert.Equal($"Z{number}", after.GetProperty("itemNumberLabel").GetString());

        await SetPrefixAsync(client, "Wykonywana", null);
        await SetPrefixAsync(client, "Zakupowa", null);
    }

    [Theory]
    [InlineData("cad", "czesc.sldprt")]
    [InlineData("drawing", "rysunek.slddrw")]
    [InlineData("pdf", "podglad.pdf")]
    [InlineData("step", "model.step")]
    public async Task Plik_w_wyroznionym_polu_uniemozliwia_zmiane_rodzaju(string role, string fileName)
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Prefiks rodzaju {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Wykonywana", "W");
        await SetPrefixAsync(client, "Zakupowa", "Z");

        var itemId = await CreatePartAsync(client, projectId, "Wykonywana");
        await AttachFileAsync(client, itemId, role, fileName);

        // Od tej chwili numer elementu siedzi w nazwie wgranego pliku, więc zmiana rodzaju
        // (a przez to prefiksu) jest odrzucana w całości.
        var patch = await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { rodzaj = "Zakupowa" });
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);

        var after = await GetItemAsync(client, itemId);
        Assert.Equal("W", after.GetProperty("itemNumberPrefix").GetString());
        // Rodzaj zostaje nietknięty -- odrzucamy całe żądanie, nie tylko jego część.
        Assert.Equal("Wykonywana", after.GetProperty("properties").GetProperty("rodzaj").GetString());
        Assert.True(after.GetProperty("kindLocked").GetBoolean());

        await SetPrefixAsync(client, "Wykonywana", null);
        await SetPrefixAsync(client, "Zakupowa", null);
    }

    [Fact]
    public async Task Zwykly_zalacznik_nie_blokuje_prefiksu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Prefiks rodzaju {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Wykonywana", "W");
        await SetPrefixAsync(client, "Zakupowa", "Z");

        var itemId = await CreatePartAsync(client, projectId, "Wykonywana");
        // Atest wgrany ręcznie przez przeglądarkę: zachowuje własną nazwę, numer elementu
        // nigdzie w niej nie występuje -- nie ma więc czego chronić.
        await AttachFileAsync(client, itemId, role: null, fileName: "atest-huty.pdf");

        var patch = await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { rodzaj = "Zakupowa" });
        patch.EnsureSuccessStatusCode();
        Assert.True((await patch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("prefixRecalculated").GetBoolean());
        var afterOrdinary = await GetItemAsync(client, itemId);
        Assert.Equal("Z", afterOrdinary.GetProperty("itemNumberPrefix").GetString());
        Assert.False(afterOrdinary.GetProperty("kindLocked").GetBoolean());

        await SetPrefixAsync(client, "Wykonywana", null);
        await SetPrefixAsync(client, "Zakupowa", null);
    }

    [Fact]
    public async Task Ten_sam_rodzaj_przyslany_ponownie_nie_jest_odrzucany()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Prefiks rodzaju {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Wykonywana", "W");
        var itemId = await CreatePartAsync(client, projectId, "Wykonywana");
        await AttachFileAsync(client, itemId, role: "cad", fileName: "czesc.sldprt");

        // Blokujemy RZECZYWISTĄ zmianę, nie samą obecność pola w żądaniu -- zapis formularza,
        // który przysyła niezmieniony rodzaj razem z innym polem, musi przejść.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties",
            new { rodzaj = "Wykonywana", material = "S355" })).EnsureSuccessStatusCode();

        var after = await GetItemAsync(client, itemId);
        Assert.Equal("Wykonywana", after.GetProperty("properties").GetProperty("rodzaj").GetString());
        Assert.Equal("S355", after.GetProperty("properties").GetProperty("material").GetString());

        await SetPrefixAsync(client, "Wykonywana", null);
    }

    [Fact]
    public async Task Rodzaj_bez_przypisanego_prefiksu_czysci_prefiks()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Prefiks rodzaju {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Wykonywana", "W");
        await SetPrefixAsync(client, "Normalia", null);

        var itemId = await CreatePartAsync(client, projectId, "Wykonywana");
        Assert.Equal("W", (await GetItemAsync(client, itemId)).GetProperty("itemNumberPrefix").GetString());

        // Rodzaj bez mapowania w ustawieniach -> element wraca do samego numeru.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { rodzaj = "Normalia" }))
            .EnsureSuccessStatusCode();

        var after = await GetItemAsync(client, itemId);
        Assert.True(after.GetProperty("itemNumberPrefix").ValueKind == JsonValueKind.Null);
        Assert.Equal(after.GetProperty("itemNumber").GetInt32().ToString(),
            after.GetProperty("itemNumberLabel").GetString());

        await SetPrefixAsync(client, "Wykonywana", null);
    }

    [Fact]
    public async Task Zmiana_innej_wlasciwosci_nie_rusza_prefiksu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);
        var projectId = await client.CreateProjectAsync($"Prefiks rodzaju {Guid.NewGuid()}");

        await SetPrefixAsync(client, "Wykonywana", "W");
        var itemId = await CreatePartAsync(client, projectId, "Wykonywana");

        // Prefiks przelicza wyłącznie zmiana rodzaju -- zapis dowolnego innego pola go nie dotyka,
        // nawet gdy element nie ma jeszcze żadnego załącznika.
        var patch = await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new { material = "S235" });
        patch.EnsureSuccessStatusCode();
        Assert.False((await patch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("prefixRecalculated").GetBoolean());
        Assert.Equal("W", (await GetItemAsync(client, itemId)).GetProperty("itemNumberPrefix").GetString());

        await SetPrefixAsync(client, "Wykonywana", null);
    }
}
