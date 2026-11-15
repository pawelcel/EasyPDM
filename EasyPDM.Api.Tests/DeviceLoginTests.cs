using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// A program signs in by being approved in the browser (DeviceLoginEndpoints): it never sees the
// password, the token goes only to the program that started the request, and it is an ordinary
// macro token, listed and revocable like any other.
[Collection("EasyPDM database")]
public class DeviceLoginTests
{
    private static async Task<JsonElement> StartAsync(HttpClient program, string client = "EasyPDM companion")
    {
        var r = await program.PostAsJsonAsync("/api/auth/device/start", new { client });
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> PollAsync(HttpClient program, string deviceCode) =>
        program.PostAsJsonAsync("/api/auth/device/token", new { deviceCode });

    [Fact]
    public async Task Approved_in_the_browser_the_program_gets_a_macro_token_once()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var program = factory.CreateClient();
        var start = await StartAsync(program);
        var deviceCode = start.GetProperty("deviceCode").GetString()!;
        var userCode = start.GetProperty("userCode").GetString()!;
        Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}$", userCode);
        Assert.Equal($"/?deviceLogin={userCode}", start.GetProperty("verificationPath").GetString());

        Assert.Equal(HttpStatusCode.Accepted, (await PollAsync(program, deviceCode)).StatusCode);

        var browser = factory.CreateClient();
        await browser.LoginAsync("admin", "admin");
        var shown = await browser.GetFromJsonAsync<JsonElement>($"/api/auth/device/{userCode.ToLowerInvariant()}");
        Assert.Equal("EasyPDM companion", shown.GetProperty("client").GetString());
        (await browser.PostAsync($"/api/auth/device/{userCode}/approve", null)).EnsureSuccessStatusCode();

        var granted = await PollAsync(program, deviceCode);
        granted.EnsureSuccessStatusCode();
        var body = await granted.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("sessionToken").GetString()!;
        Assert.StartsWith("pdmm_", token);
        Assert.Equal("admin", body.GetProperty("username").GetString());

        // Handed out once; the token works and is listed among the macro logins.
        Assert.Equal(HttpStatusCode.Gone, (await PollAsync(program, deviceCode)).StatusCode);
        var signedIn = factory.CreateClient();
        signedIn.DefaultRequestHeaders.Add("Cookie", $"pdm_session={token}");
        (await signedIn.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();
        var tokens = await browser.GetStringAsync("/api/macro-tokens");
        Assert.Contains("EasyPDM companion", tokens);
    }

    [Fact]
    public async Task Refused_in_the_browser_the_program_is_told_and_gets_nothing()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var program = factory.CreateClient();
        var start = await StartAsync(program);
        var browser = factory.CreateClient();
        await browser.LoginAsync("admin", "admin");
        (await browser.PostAsync($"/api/auth/device/{start.GetProperty("userCode").GetString()}/deny", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await PollAsync(program, start.GetProperty("deviceCode").GetString()!)).StatusCode);
    }

    [Fact]
    public async Task Approving_needs_a_logged_in_person_and_a_wrong_device_code_gets_nothing()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        var program = factory.CreateClient();
        var start = await StartAsync(program);
        var userCode = start.GetProperty("userCode").GetString()!;

        Assert.Equal(HttpStatusCode.Unauthorized, (await program.PostAsync($"/api/auth/device/{userCode}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await program.GetAsync($"/api/auth/device/{userCode}")).StatusCode);

        var browser = factory.CreateClient();
        await browser.LoginAsync("admin", "admin");
        (await browser.PostAsync($"/api/auth/device/{userCode}/approve", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Gone, (await PollAsync(program, new string('0', 64))).StatusCode);
        // Answered requests cannot be approved again.
        Assert.Equal(HttpStatusCode.NotFound, (await browser.PostAsync($"/api/auth/device/{userCode}/approve", null)).StatusCode);
    }
}
