using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Katalog obróbek (menu "Obróbka") i ich wybór w elemencie wykonywanym: element trzyma listę
// nazw w properties.processes.
[Collection("EasyPDM database")]
public class ProcessCatalogTests
{
    [Fact]
    public async Task Katalog_obrobek_dodaje_zmienia_i_usuwa()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var name = "Spawanie " + Guid.NewGuid().ToString("N")[..6];

        var created = await client.PostAsJsonAsync("/api/processes",
            new { name, description = "MIG", forParts = false, forAssemblies = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Ta sama nazwa może wystąpić drugi raz -- inny wariant tego samego procesu.
        var variant = await client.PostAsJsonAsync("/api/processes",
            new { name, description = "TIG", forParts = true, forAssemblies = false });
        Assert.Equal(HttpStatusCode.Created, variant.StatusCode);

        // Obróbka musi czegoś dotyczyć.
        var nothing = await client.PostAsJsonAsync("/api/processes",
            new { name = name + " x", forParts = false, forAssemblies = false });
        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);

        (await client.PatchAsJsonAsync($"/api/processes/{id}",
            new { name, description = "MIG/MAG", forParts = true, forAssemblies = true })).EnsureSuccessStatusCode();
        var listed = (await client.GetFromJsonAsync<JsonElement>("/api/processes"))
            .EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == id);
        Assert.Equal("MIG/MAG", listed.GetProperty("description").GetString());
        Assert.True(listed.GetProperty("forParts").GetBoolean());
        Assert.True(listed.GetProperty("forAssemblies").GetBoolean());

        (await client.DeleteAsync($"/api/processes/{id}")).EnsureSuccessStatusCode();
        Assert.DoesNotContain((await client.GetFromJsonAsync<JsonElement>("/api/processes")).EnumerateArray(),
            p => p.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task Element_zapisuje_kilka_obrobek_albo_zadna()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        var projectId = await client.CreateProjectAsync($"Obrobki {Guid.NewGuid()}");
        var itemId = await client.CreateNodeAsync(projectId, "Wspornik", "part");

        // Dwa warianty tej samej nazwy rozróżnia identyfikator; opis jest kopią z chwili wyboru.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties", new
        {
            processes = new object[]
            {
                new { id = 10, name = "Hartowanie", description = "50 HRC" },
                new { id = 11, name = "Hartowanie", description = "indukcyjne" },
            },
        })).EnsureSuccessStatusCode();
        var item = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}");
        Assert.Equal(new[] { "50 HRC", "indukcyjne" },
            item.GetProperty("properties").GetProperty("processes").EnumerateArray()
                .Select(x => x.GetProperty("description").GetString()));

        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties",
            new { processes = Array.Empty<string>() })).EnsureSuccessStatusCode();
        item = await client.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}");
        Assert.Equal(0, item.GetProperty("properties").GetProperty("processes").GetArrayLength());

        // Wyszukiwanie w "Całej bazie" przeszukuje właściwości, więc trafia też po obróbce.
        (await client.PatchAsJsonAsync($"/api/items/{itemId}/properties",
            new { processes = new object[] { new { id = 12, name = "Malowanie proszkowe", description = (string?)null } } })).EnsureSuccessStatusCode();
        var found = await client.GetFromJsonAsync<JsonElement>("/api/items?search=proszkowe");
        Assert.Contains(found.EnumerateArray(), i => i.GetProperty("id").GetGuid() == itemId);
    }
}

// Czyszczenie bazy w Ustawieniach obejmuje katalog obróbek jako osobną kategorię — osobna
// klasa, bo kasuje cały katalog, a baza jest wspólna dla testów jednej klasy.
[Collection("EasyPDM database")]
public class ProcessClearDatabaseTests
{
    [Fact]
    public async Task Czyszczenie_bazy_usuwa_katalog_obrobek_tylko_na_zyczenie()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync("admin", "admin");
        (await client.PostAsJsonAsync("/api/processes",
            new { name = "Giecie", forParts = true, forAssemblies = false })).EnsureSuccessStatusCode();
        // Inne klasy testowe w tej samej bazie mogły już dodać obróbki -- liczy się, że
        // katalog nie jest pusty i nie zmienia się po czyszczeniu bez tej kategorii.
        var before = (await client.GetFromJsonAsync<JsonElement>("/api/processes")).GetArrayLength();
        Assert.True(before >= 1);

        // Starszy klient bez pola "processes" nie rusza katalogu.
        (await client.PostAsJsonAsync("/api/settings/storage/clear-database",
            new { projects = false, materials = true, manufacturers = false, clients = false })).EnsureSuccessStatusCode();
        Assert.Equal(before, (await client.GetFromJsonAsync<JsonElement>("/api/processes")).GetArrayLength());

        (await client.PostAsJsonAsync("/api/settings/storage/clear-database",
            new { projects = false, materials = false, manufacturers = false, clients = false, processes = true })).EnsureSuccessStatusCode();
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/processes")).GetArrayLength());
    }
}

// Nazwa i opis obróbki w każdym języku dokumentacji: katalog pokazuje wersję w domyślnym
// języku, a po jego zmianie przełącza się na nowy (z wersją z innego języka, gdy brak).
[Collection("EasyPDM database")]
public class ProcessTranslationTests
{
    private static async Task<int> LanguageIdAsync(HttpClient client, string code)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/project-languages");
        return list.EnumerateArray().Single(l => l.GetProperty("code").GetString() == code).GetProperty("id").GetInt32();
    }

    private static async Task<JsonElement> ProcessAsync(HttpClient client, int id)
    {
        var list = await client.GetFromJsonAsync<JsonElement>("/api/processes");
        return list.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task Katalog_pokazuje_wersje_w_domyslnym_jezyku()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var en = await LanguageIdAsync(admin, "EN");
        var pl = await LanguageIdAsync(admin, "PL");
        var tag = Guid.NewGuid().ToString("N")[..6];

        // Nowa obróbka trafia do domyślnego języka (English).
        var created = await admin.PostAsJsonAsync("/api/processes",
            new { name = "Hardening " + tag, description = "to 50 HRC", forParts = true, forAssemblies = false });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var process = await ProcessAsync(admin, id);
        Assert.Equal(en, process.GetProperty("languageId").GetInt32());
        Assert.Single(process.GetProperty("texts").EnumerateArray());

        (await admin.PutAsJsonAsync($"/api/processes/{id}/texts", new
        {
            texts = new object[]
            {
                new { languageId = en, name = "Hardening " + tag, description = "to 50 HRC" },
                new { languageId = pl, name = "Hartowanie " + tag, description = "do 50 HRC" },
            }
        })).EnsureSuccessStatusCode();

        try
        {
            (await admin.PutAsync($"/api/project-languages/{pl}/default", null)).EnsureSuccessStatusCode();
            process = await ProcessAsync(admin, id);
            Assert.Equal("Hartowanie " + tag, process.GetProperty("name").GetString());
            Assert.Equal("do 50 HRC", process.GetProperty("description").GetString());

            // Edycja z katalogu zmienia tekst w bieżącym domyślnym języku, angielski zostaje.
            (await admin.PatchAsJsonAsync($"/api/processes/{id}",
                new { name = "Hartowanie " + tag, description = "do 55 HRC", forParts = true, forAssemblies = false })).EnsureSuccessStatusCode();
            process = await ProcessAsync(admin, id);
            var texts = process.GetProperty("texts").EnumerateArray().ToList();
            Assert.Equal("do 55 HRC", texts.Single(t => t.GetProperty("languageId").GetInt32() == pl).GetProperty("description").GetString());
            Assert.Equal("to 50 HRC", texts.Single(t => t.GetProperty("languageId").GetInt32() == en).GetProperty("description").GetString());

            // Usunięcie polskiego tłumaczenia: katalog (domyślny polski) pokazuje angielską wersję.
            (await admin.PutAsJsonAsync($"/api/processes/{id}/texts", new
            {
                texts = new object[] { new { languageId = en, name = "Hardening " + tag, description = "to 50 HRC" }, new { languageId = pl, name = "" } }
            })).EnsureSuccessStatusCode();
            process = await ProcessAsync(admin, id);
            Assert.Equal("Hardening " + tag, process.GetProperty("name").GetString());
            Assert.Equal(en, process.GetProperty("languageId").GetInt32());
        }
        finally
        {
            (await admin.PutAsync($"/api/project-languages/{en}/default", null)).EnsureSuccessStatusCode();
        }

        // Nie da się zostawić obróbki bez żadnej nazwy.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/processes/{id}/texts",
            new { texts = new object[] { new { languageId = en, name = " " } } })).StatusCode);
    }

    [Fact]
    public async Task Nie_da_sie_usunac_jezyka_z_jedyna_nazwa_obrobki()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var en = await LanguageIdAsync(admin, "EN");
        var code = "X" + (char)('A' + Random.Shared.Next(26)) + (char)('A' + Random.Shared.Next(26));
        var langResponse = await admin.PostAsJsonAsync("/api/project-languages", new { name = "Język " + Guid.NewGuid().ToString("N")[..6], code });
        var lang = (await langResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var created = await admin.PostAsJsonAsync("/api/processes",
            new { name = "Proces " + Guid.NewGuid().ToString("N")[..6], forParts = true, forAssemblies = false });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        (await admin.PutAsJsonAsync($"/api/processes/{id}/texts",
            new { texts = new object[] { new { languageId = lang, name = "Tylko tu" } } })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/project-languages/{lang}")).StatusCode);

        // Z tłumaczeniem w innym języku usunięcie przechodzi, a tekst w usuniętym znika.
        (await admin.PutAsJsonAsync($"/api/processes/{id}/texts", new
        {
            texts = new object[] { new { languageId = lang, name = "Tylko tu" }, new { languageId = en, name = "Only here" } }
        })).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/project-languages/{lang}")).EnsureSuccessStatusCode();
        var process = await ProcessAsync(admin, id);
        Assert.Equal("Only here", process.GetProperty("name").GetString());
        Assert.Single(process.GetProperty("texts").EnumerateArray());
    }
}
