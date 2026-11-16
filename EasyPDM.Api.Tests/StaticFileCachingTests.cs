namespace EasyPDM.Api.Tests;

// The page must be revalidated on every load, or a browser keeps showing the previous release's
// page and bundle for hours after the server is updated; the hashed bundle files may be cached
// for good. A content root of the test's own, so the test does not depend on the committed build.
[Collection("EasyPDM database")]
public class StaticFileCachingTests
{
    [Fact]
    public async Task Page_is_revalidated_and_hashed_assets_are_kept()
    {
        var contentRoot = Directory.CreateTempSubdirectory("easypdm-tests-root-").FullName;
        try
        {
            var assets = Directory.CreateDirectory(Path.Combine(contentRoot, "wwwroot", "assets")).FullName;
            await File.WriteAllTextAsync(Path.Combine(contentRoot, "wwwroot", "index.html"),
                "<!doctype html><script src=\"/assets/index-test.js\"></script>");
            await File.WriteAllTextAsync(Path.Combine(assets, "index-test.js"), "console.log(1)");

            await using var factory = new EasyPDMWebApplicationFactory { ContentRoot = contentRoot };
            using var client = factory.CreateClient();

            foreach (var page in new[] { "/", "/?item=00000000-0000-0000-0000-000000000000", "/index.html" })
            {
                var response = await client.GetAsync(page);
                response.EnsureSuccessStatusCode();
                Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
            }

            var asset = await client.GetAsync("/assets/index-test.js");
            asset.EnsureSuccessStatusCode();
            var cache = asset.Headers.CacheControl;
            Assert.NotNull(cache);
            Assert.True(cache!.Public);
            Assert.Equal(TimeSpan.FromDays(365), cache.MaxAge);
            Assert.Contains("immutable", cache.ToString());
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }
}
