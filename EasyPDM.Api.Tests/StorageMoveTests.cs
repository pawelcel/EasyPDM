using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// Settings -> File storage -> Change location (POST /api/settings/storage/move): files moved
// along stay reachable from the items that hold them, and the new location is remembered in
// appsettings.Local.json -- in the test's own content root, never in the repository's.
[Collection("EasyPDM database")]
public class StorageMoveTests
{
    private static string RepoLocalSettings()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EasyPDM.Api", "EasyPDM.Api.csproj")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "EasyPDM.Api", "appsettings.Local.json");
    }

    private static string Fingerprint(string path) =>
        File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "absent";

    private static async Task<(Guid ItemId, Guid AttachmentId)> ItemWithFileAsync(HttpClient admin, byte[] bytes)
    {
        var projectId = await admin.CreateProjectAsync($"Move {Guid.NewGuid()}");
        var itemId = await admin.CreateNodeAsync(projectId, "Shaft", "part");
        using var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "shaft.step" } };
        (await admin.PostAsync($"/api/items/{itemId}/attachments", form)).EnsureSuccessStatusCode();
        var atts = await admin.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/attachments");
        return (itemId, atts.EnumerateArray().Single().GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Moving_with_the_files_keeps_every_file_reachable_and_remembers_the_place()
    {
        var repoSettings = RepoLocalSettings();
        var before = Fingerprint(repoSettings);
        var contentRoot = Directory.CreateTempSubdirectory("easypdm-tests-root-").FullName;
        var target = Path.Combine(Path.GetTempPath(), "easypdm-tests-moved-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var factory = new EasyPDMWebApplicationFactory { ContentRoot = contentRoot };
            using var admin = factory.CreateClient();
            await admin.LoginAsync("admin", "admin");
            var bytes = RandomNumberGenerator.GetBytes(4096);
            var (itemId, attachmentId) = await ItemWithFileAsync(admin, bytes);

            var moved = await admin.PostAsJsonAsync("/api/settings/storage/move", new { newPath = target, migrateExisting = true });
            moved.EnsureSuccessStatusCode();
            var body = await moved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("migratedFiles").GetInt32() >= 1);

            Assert.Equal(bytes, await admin.GetByteArrayAsync($"/api/attachments/{attachmentId}/file"));
            Assert.False(Directory.Exists(factory.StorageRoot));
            Assert.NotEmpty(Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories));

            var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(contentRoot, "appsettings.Local.json")));
            Assert.Equal(Path.GetFullPath(target), saved.RootElement.GetProperty("StorageRoot").GetString());

            // A file uploaded afterwards lands in the new place too.
            using var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "shaft2.step" } };
            (await admin.PostAsync($"/api/items/{itemId}/attachments", form)).EnsureSuccessStatusCode();
            Assert.Contains(Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories), f => f.EndsWith(".step"));
        }
        finally
        {
            Directory.Delete(contentRoot, true);
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
        Assert.Equal(before, Fingerprint(repoSettings));
    }

    [Fact]
    public async Task A_relative_or_identical_location_is_refused()
    {
        var contentRoot = Directory.CreateTempSubdirectory("easypdm-tests-root-").FullName;
        try
        {
            await using var factory = new EasyPDMWebApplicationFactory { ContentRoot = contentRoot };
            using var admin = factory.CreateClient();
            await admin.LoginAsync("admin", "admin");
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,
                (await admin.PostAsJsonAsync("/api/settings/storage/move", new { newPath = "relative/dir", migrateExisting = true })).StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,
                (await admin.PostAsJsonAsync("/api/settings/storage/move", new { newPath = factory.StorageRoot, migrateExisting = true })).StatusCode);
            Assert.False(File.Exists(Path.Combine(contentRoot, "appsettings.Local.json")));
        }
        finally { Directory.Delete(contentRoot, true); }
    }
}
