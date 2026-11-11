using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Ustawienia -> Firma: nazwa i logo do tabelki rysunkowej. Odczyt dla każdego zalogowanego,
// zapis dla administratora; logo tylko PNG/JPEG, rozpoznawane po zawartości.
[Collection("EasyPDM database")]
public class CompanyProfileTests
{
    // Najmniejszy poprawny PNG 1x1.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAwS2OUAAAAABJRU5ErkJggg==");

    private static MultipartFormDataContent FileForm(byte[] bytes, string fileName, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    [Fact]
    public async Task Administrator_ustawia_nazwe_i_logo()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");

        (await admin.PatchAsJsonAsync("/api/settings/company", new { name = " Przykładowa Firma " })).EnsureSuccessStatusCode();
        (await admin.PutAsync("/api/settings/company/logo", FileForm(Png, "logo.png", "image/png"))).EnsureSuccessStatusCode();

        var company = await admin.GetFromJsonAsync<JsonElement>("/api/settings/company");
        Assert.Equal("Przykładowa Firma", company.GetProperty("name").GetString());
        Assert.True(company.GetProperty("hasLogo").GetBoolean());

        var logo = await admin.GetAsync("/api/settings/company/logo");
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await logo.Content.ReadAsByteArrayAsync());

        (await admin.DeleteAsync("/api/settings/company/logo")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/settings/company/logo")).StatusCode);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/settings/company")).GetProperty("hasLogo").GetBoolean());
    }

    [Fact]
    public async Task Logo_musi_byc_obrazem_PNG_albo_JPEG()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");

        // SVG udający PNG — rozpoznawany po zawartości, więc odrzucony.
        var svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsync("/api/settings/company/logo", FileForm(svg, "logo.png", "image/png"))).StatusCode);
    }

    [Fact]
    public async Task Zwykly_uzytkownik_czyta_ale_nie_zmienia()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var username = "firma" + Guid.NewGuid().ToString("N")[..8];
        await admin.CreateUserAsync(username, "haslo123");

        using var user = factory.CreateClient();
        await user.LoginAsync(username, "haslo123");
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/settings/company")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PatchAsJsonAsync("/api/settings/company", new { name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.PutAsync("/api/settings/company/logo", FileForm(Png, "logo.png", "image/png"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync("/api/settings/company/logo")).StatusCode);
    }

    [Fact]
    public async Task Czyszczenie_bazy_usuwa_dane_firmy_tylko_na_zyczenie()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        (await admin.PatchAsJsonAsync("/api/settings/company", new { name = "Do skasowania" })).EnsureSuccessStatusCode();
        (await admin.PutAsync("/api/settings/company/logo", FileForm(Png, "logo.png", "image/png"))).EnsureSuccessStatusCode();

        // Starszy klient bez pola "company" nie rusza danych firmy.
        (await admin.PostAsJsonAsync("/api/settings/storage/clear-database",
            new { projects = false, materials = true, manufacturers = false, clients = false })).EnsureSuccessStatusCode();
        Assert.Equal("Do skasowania", (await admin.GetFromJsonAsync<JsonElement>("/api/settings/company")).GetProperty("name").GetString());

        (await admin.PostAsJsonAsync("/api/settings/storage/clear-database",
            new { projects = false, materials = false, manufacturers = false, clients = false, company = true })).EnsureSuccessStatusCode();
        var company = await admin.GetFromJsonAsync<JsonElement>("/api/settings/company");
        Assert.Equal(JsonValueKind.Null, company.GetProperty("name").ValueKind);
        Assert.False(company.GetProperty("hasLogo").GetBoolean());
    }
}
