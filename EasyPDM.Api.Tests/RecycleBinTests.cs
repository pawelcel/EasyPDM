using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EasyPDM.Api.Tests;

// The recycle bin (BinEndpoints.cs, RecycleBin.cs): a deleted item leaves every screen but keeps
// its rows and files, comes back whole on restore, and is gone for good only on an admin's purge.
[Collection("EasyPDM database")]
public class RecycleBinTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private static int StoredFileCount(EasyPDMWebApplicationFactory factory) =>
        Directory.Exists(factory.StorageRoot)
            ? Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Length
            : 0;

    private static async Task AttachAsync(HttpClient client, Guid itemId, string? role, string fileName)
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("content of " + fileName)), "file", fileName },
        };
        if (role is not null)
            form.Add(new StringContent(role), "role");
        (await client.PostAsync($"/api/items/{itemId}/attachments", form)).EnsureSuccessStatusCode();
    }

    private static async Task<string> TextAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<List<Guid>> ChildIdsAsync(HttpClient client, Guid parentId)
    {
        var children = await client.GetFromJsonAsync<JsonElement>($"/api/items/{parentId}/children");
        return children.EnumerateArray().Select(c => c.GetProperty("item").GetProperty("id").GetGuid()).OrderBy(g => g).ToList();
    }

    private static async Task<JsonElement?> BinEntryAsync(HttpClient client, Guid itemId)
    {
        var bin = await client.GetFromJsonAsync<JsonElement>("/api/bin");
        foreach (var entry in bin.EnumerateArray())
            if (entry.GetProperty("itemId").GetGuid() == itemId)
                return entry;
        return null;
    }

    [Fact]
    public async Task A_deleted_assembly_waits_in_the_bin_and_comes_back_with_files_relations_and_history()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Bin restore {Guid.NewGuid()}");
        var folder = await client.CreateNodeAsync(projectId, "Frames", "folder");
        var assembly = await client.CreateNodeAsync(projectId, "Frame", "assembly", folder);
        var part = await client.CreateNodeAsync(projectId, "Rail", "part");
        (await client.PostAsJsonAsync($"/api/items/{assembly}/children", new { childId = part, quantity = 2 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/items/{assembly}/tags", new { name = "bin-test" })).EnsureSuccessStatusCode();
        await AttachAsync(client, assembly, "cad", "frame.sldasm");
        await AttachAsync(client, assembly, null, "notes.pdf");

        var detailBefore = await TextAsync(client, $"/api/items/{assembly}");
        var attachmentsBefore = await TextAsync(client, $"/api/items/{assembly}/attachments");
        var historyBefore = await TextAsync(client, $"/api/items/{assembly}/history");
        var bomBefore = await TextAsync(client, $"/api/items/{assembly}/children");
        var filesBefore = StoredFileCount(factory);
        Assert.True(filesBefore >= 2);

        var deleted = await client.DeleteAsync($"/api/items/{assembly}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        // Gone from every screen, but its files stay on disk and the bin lists it.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/items/{assembly}")).StatusCode);
        Assert.DoesNotContain(assembly, await ChildIdsAsync(client, folder));
        Assert.Equal(filesBefore, StoredFileCount(factory));
        var entry = await BinEntryAsync(client, assembly);
        Assert.NotNull(entry);
        Assert.Equal("assembly", entry.Value.GetProperty("itemType").GetString());
        Assert.Equal("Frames", entry.Value.GetProperty("location").GetString());
        Assert.False(entry.Value.GetProperty("needsFolder").GetBoolean());
        // The component is a catalog item of its own and was never deleted.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/items/{part}")).StatusCode);

        var restored = await client.PostAsJsonAsync($"/api/bin/{entry.Value.GetProperty("id").GetGuid()}/restore", new { });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);

        // Back in its folder, with the same record, files, bill of materials and history.
        Assert.Contains(assembly, await ChildIdsAsync(client, folder));
        Assert.Equal(detailBefore, await TextAsync(client, $"/api/items/{assembly}"));
        Assert.Equal(attachmentsBefore, await TextAsync(client, $"/api/items/{assembly}/attachments"));
        Assert.Equal(historyBefore, await TextAsync(client, $"/api/items/{assembly}/history"));
        Assert.Equal(bomBefore, await TextAsync(client, $"/api/items/{assembly}/children"));
        Assert.Equal(filesBefore, StoredFileCount(factory));
        Assert.Null(await BinEntryAsync(client, assembly));

        // The files really are the ones that were there: they download again.
        var attachments = JsonDocument.Parse(attachmentsBefore).RootElement;
        foreach (var attachment in attachments.EnumerateArray())
        {
            var download = await client.GetAsync($"/api/attachments/{attachment.GetProperty("id").GetGuid()}/file");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        }
    }

    [Fact]
    public async Task Only_an_admin_purges_and_a_purge_removes_the_entry_and_its_files_for_good()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await admin.CreateProjectAsync($"Bin purge {Guid.NewGuid()}");
        var filesBefore = StoredFileCount(factory);
        var part = await admin.CreateNodeAsync(projectId, "Bracket", "part");
        await AttachAsync(admin, part, "cad", "bracket.sldprt");
        await AttachAsync(admin, part, "step", "bracket.step");
        var filesWithPart = StoredFileCount(factory);
        Assert.Equal(filesBefore + 2, filesWithPart);

        (await admin.DeleteAsync($"/api/items/{part}")).EnsureSuccessStatusCode();
        var entryId = (await BinEntryAsync(admin, part))!.Value.GetProperty("id").GetGuid();

        // A person who did not delete it neither sees it in the bin nor can purge or restore it.
        var username = $"binuser{Guid.NewGuid():N}"[..20];
        var userId = await admin.CreateUserAsync(username, "Bin-user-pass-1");
        await admin.GrantProjectAccessAsync(projectId, userId);
        using var user = factory.CreateClient();
        await user.LoginAsync(username, "Bin-user-pass-1");
        Assert.Null(await BinEntryAsync(user, part));
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/bin/{entryId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync("/api/bin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync($"/api/bin/{entryId}/restore", new { })).StatusCode);
        Assert.NotNull(await BinEntryAsync(admin, part));
        Assert.Equal(filesWithPart, StoredFileCount(factory));

        var purged = await admin.DeleteAsync($"/api/bin/{entryId}");
        Assert.Equal(HttpStatusCode.OK, purged.StatusCode);

        Assert.Null(await BinEntryAsync(admin, part));
        Assert.Equal(filesBefore, StoredFileCount(factory));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/bin/{entryId}/restore", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{part}")).StatusCode);
    }

    [Fact]
    public async Task An_item_whose_folder_is_gone_asks_for_a_folder_and_restores_into_the_one_chosen()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Bin folder gone {Guid.NewGuid()}");
        var oldFolder = await client.CreateNodeAsync(projectId, "Old", "folder");
        var newFolder = await client.CreateNodeAsync(projectId, "New", "folder");
        var part = await client.CreateNodeAsync(projectId, "Clip", "part", oldFolder);

        (await client.DeleteAsync($"/api/items/{part}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/items/{oldFolder}")).EnsureSuccessStatusCode();

        var entry = (await BinEntryAsync(client, part))!.Value;
        Assert.True(entry.GetProperty("needsFolder").GetBoolean());
        var entryId = entry.GetProperty("id").GetGuid();

        var asked = await client.PostAsJsonAsync($"/api/bin/{entryId}/restore", new { });
        Assert.Equal(HttpStatusCode.Conflict, asked.StatusCode);
        Assert.True((await asked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("needsFolder").GetBoolean());

        var restored = await client.PostAsJsonAsync($"/api/bin/{entryId}/restore", new { folderId = newFolder });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Contains(part, await ChildIdsAsync(client, newFolder));

        // The emptied folder is still in the bin, and restores back to the project root.
        var folderEntry = (await BinEntryAsync(client, oldFolder))!.Value;
        Assert.False(folderEntry.GetProperty("needsFolder").GetBoolean());
        (await client.PostAsJsonAsync($"/api/bin/{folderEntry.GetProperty("id").GetGuid()}/restore", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/items/{oldFolder}")).StatusCode);
    }

    // A released item carries the most rows: stage history, revisions, attachments. All of it must
    // come back unchanged.
    [Fact]
    public async Task A_released_part_comes_back_with_its_stage_history_and_revisions()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient();
        await client.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await client.CreateProjectAsync($"Bin released {Guid.NewGuid()}");
        var part = await client.CreateNodeAsync(projectId, "Plate", "part");
        await AttachAsync(client, part, "cad", "plate.sldprt");
        (await client.PatchAsJsonAsync($"/api/items/{part}/status", new { status = "sprawdzany" })).EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/items/{part}/status", new { status = "wydany" })).EnsureSuccessStatusCode();

        var urls = new[] { "", "/history", "/revisions", "/attachments", "/title-block" }.Select(u => $"/api/items/{part}{u}").ToList();
        var before = new List<string>();
        foreach (var url in urls)
            before.Add(await TextAsync(client, url));
        Assert.Contains("wydany", before[0]);

        (await client.DeleteAsync($"/api/items/{part}")).EnsureSuccessStatusCode();
        var entryId = (await BinEntryAsync(client, part))!.Value.GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/bin/{entryId}/restore", new { })).EnsureSuccessStatusCode();

        for (var i = 0; i < urls.Count; i++)
            Assert.Equal(before[i], await TextAsync(client, urls[i]));
    }

    // Only admins delete today, but the restore rule is already the wider one: the person who
    // deleted an item may put it back only into a project they have access to.
    [Fact]
    public async Task The_person_who_deleted_it_restores_it_only_into_a_project_they_may_open()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync(AdminUsername, AdminPassword);

        var projectId = await admin.CreateProjectAsync($"Bin deleter {Guid.NewGuid()}");
        var part = await admin.CreateNodeAsync(projectId, "Hinge", "part");
        (await admin.DeleteAsync($"/api/items/{part}")).EnsureSuccessStatusCode();
        var entryId = (await BinEntryAsync(admin, part))!.Value.GetProperty("id").GetGuid();

        var username = $"deleter{Guid.NewGuid():N}"[..20];
        var userId = await admin.CreateUserAsync(username, "Bin-user-pass-1");
        await using (var conn = new Npgsql.NpgsqlConnection(EasyPDMWebApplicationFactory.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("UPDATE recycle_bin SET deleted_by = @user WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("user", userId);
            cmd.Parameters.AddWithValue("id", entryId);
            await cmd.ExecuteNonQueryAsync();
        }
        using var user = factory.CreateClient();
        await user.LoginAsync(username, "Bin-user-pass-1");

        // Theirs, so they see it; no access to the project, so it stays in the bin.
        Assert.NotNull(await BinEntryAsync(user, part));
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync($"/api/bin/{entryId}/restore", new { })).StatusCode);
        Assert.NotNull(await BinEntryAsync(admin, part));

        await admin.GrantProjectAccessAsync(projectId, userId);
        (await user.PostAsJsonAsync($"/api/bin/{entryId}/restore", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/items/{part}")).StatusCode);
    }
}
