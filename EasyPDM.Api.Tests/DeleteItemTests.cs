using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// "Usuń całkowicie" (DELETE /api/items/{id}) na Złożeniu -- co dokładnie znika razem z nim,
// a co zostaje. Logika "descendants/survivors" w ItemEndpoints.cs jest nietrywialnym,
// rekurencyjnym SQL-em, a do tej pory nie miała żadnego pokrycia testami.
[Collection("EasyPDM database")]
public class DeleteItemTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static async Task<bool> ItemExistsAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"/api/items/{id}");
        return response.StatusCode == HttpStatusCode.OK;
    }

    // Komponent używany TAKŻE w innym złożeniu (poza kasowanym poddrzewem) musi przeżyć --
    // inaczej usunięcie jednego złożenia cicho psułoby BOM zupełnie innego.
    [Fact]
    public async Task Komponent_uzyty_w_innym_zlozeniu_przezywa_usuniecie()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt usuwanie wspoldzielony {Guid.NewGuid()}");
        var szuflada = await client.CreateNodeAsync(projectId, "Szuflada", "assembly");
        var szafka = await client.CreateNodeAsync(projectId, "Szafka", "assembly");
        var plyta = await client.CreateNodeAsync(projectId, "Plyta", "part");

        (await client.PostAsJsonAsync($"/api/items/{szuflada}/children", new { childId = plyta, quantity = 1 }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/items/{szafka}/children", new { childId = plyta, quantity = 1 }))
            .EnsureSuccessStatusCode();

        var deleted = await client.DeleteAsync($"/api/items/{szuflada}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.False(await ItemExistsAsync(client, szuflada));
        Assert.True(await ItemExistsAsync(client, plyta));
        Assert.True(await ItemExistsAsync(client, szafka));
    }

    // Komponent, który NIE jest nigdzie indziej użyty. Część/Złożenie to samodzielny byt
    // katalogowy (własny numer, rewizja, historia, załączniki) -- usunięcie złożenia, w
    // którym akurat był użyty, nie może go kasować razem z nim.
    [Fact]
    public async Task Komponent_uzyty_tylko_tutaj_tez_przezywa_usuniecie_zlozenia()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt usuwanie wylaczny {Guid.NewGuid()}");
        var szuflada = await client.CreateNodeAsync(projectId, "Szuflada solo", "assembly");
        var plyta = await client.CreateNodeAsync(projectId, "Plyta solo", "part");

        (await client.PostAsJsonAsync($"/api/items/{szuflada}/children", new { childId = plyta, quantity = 1 }))
            .EnsureSuccessStatusCode();

        var deleted = await client.DeleteAsync($"/api/items/{szuflada}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.False(await ItemExistsAsync(client, szuflada));
        Assert.True(await ItemExistsAsync(client, plyta));
    }

    // Zagnieżdżenie: usunięcie górnego złożenia nie może kasować ani pod-złożenia, ani
    // jego własnych części.
    [Fact]
    public async Task Usuniecie_gornego_zlozenia_nie_kasuje_podzlozenia_ani_jego_czesci()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt usuwanie zagniezdzone {Guid.NewGuid()}");
        var komoda = await client.CreateNodeAsync(projectId, "Komoda", "assembly");
        var szuflada = await client.CreateNodeAsync(projectId, "Szuflada zagniezdzona", "assembly");
        var plyta = await client.CreateNodeAsync(projectId, "Plyta zagniezdzona", "part");

        (await client.PostAsJsonAsync($"/api/items/{komoda}/children", new { childId = szuflada, quantity = 1 }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/items/{szuflada}/children", new { childId = plyta, quantity = 1 }))
            .EnsureSuccessStatusCode();

        var deleted = await client.DeleteAsync($"/api/items/{komoda}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.False(await ItemExistsAsync(client, komoda));
        Assert.True(await ItemExistsAsync(client, szuflada));
        Assert.True(await ItemExistsAsync(client, plyta));
    }

    // Folder to czysty kontener bez numeru/rewizji/historii -- tu kaskada ZOSTAJE, bo
    // "usuń folder" bez usunięcia jego zawartości zostawiałoby ją bez żadnego miejsca w
    // strukturze. Test pilnuje, żeby zmiana zachowania dla Części/Złożeń tego nie ruszyła.
    [Fact]
    public async Task Usuniecie_folderu_kasuje_jego_zawartosc()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Projekt usuwanie folder {Guid.NewGuid()}");
        var folder = await client.CreateNodeAsync(projectId, "Folder do usuniecia", "folder");
        var podfolder = await client.CreateNodeAsync(projectId, "Podfolder", "folder", folder);

        var deleted = await client.DeleteAsync($"/api/items/{folder}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.False(await ItemExistsAsync(client, folder));
        Assert.False(await ItemExistsAsync(client, podfolder));
    }
}
