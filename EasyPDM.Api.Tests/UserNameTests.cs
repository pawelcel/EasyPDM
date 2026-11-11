using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Imię i nazwisko użytkownika (do tabelki rysunkowej): wymagane przy zakładaniu konta, do
// uzupełnienia na starszych kontach, nie do wyczyszczenia.
[Collection("EasyPDM database")]
public class UserNameTests
{
    private static async Task<JsonElement> UserAsync(HttpClient admin, Guid id)
    {
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/users");
        return list.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Nowe_konto_wymaga_imienia_i_nazwiska()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "imie" + Guid.NewGuid().ToString("N")[..8];

        var missing = await admin.PostAsJsonAsync("/api/users",
            new { username, password = "haslo123", displayName = "Ktoś", role = "user", firstName = "Anna" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        var created = await admin.PostAsJsonAsync("/api/users",
            new { username, password = "haslo123", displayName = "Ktoś", role = "user", firstName = " Anna ", lastName = "Nowak" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var user = await UserAsync(admin, id);
        Assert.Equal("Anna", user.GetProperty("firstName").GetString());
        Assert.Equal("Nowak", user.GetProperty("lastName").GetString());
    }

    [Fact]
    public async Task Imie_i_nazwisko_da_sie_zmienic_ale_nie_wyczyscic()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var id = await admin.CreateUserAsync("zmiana" + Guid.NewGuid().ToString("N")[..8], "haslo123");

        (await admin.PatchAsJsonAsync($"/api/users/{id}", new { firstName = "Piotr", lastName = "Kowalski" })).EnsureSuccessStatusCode();
        var user = await UserAsync(admin, id);
        Assert.Equal("Piotr", user.GetProperty("firstName").GetString());
        Assert.Equal("Kowalski", user.GetProperty("lastName").GetString());

        // Zmiana samej roli nie rusza imienia i nazwiska.
        (await admin.PatchAsJsonAsync($"/api/users/{id}", new { role = "user" })).EnsureSuccessStatusCode();
        Assert.Equal("Kowalski", (await UserAsync(admin, id)).GetProperty("lastName").GetString());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PatchAsJsonAsync($"/api/users/{id}", new { lastName = " " })).StatusCode);
    }
}
