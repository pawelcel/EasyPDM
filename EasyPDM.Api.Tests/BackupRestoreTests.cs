using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

namespace EasyPDM.Api.Tests;

// Settings -> File storage -> Download backup / Restore from backup: one ZIP with the database
// and every file, restored over a changed server, and onto a server whose storage lives
// elsewhere. Needs pg_dump and pg_restore matching the server (18) on PATH.
//
// pg_dump takes the WHOLE database, not one schema, so these tests run on a database of their
// own (pdm_backup_test) instead of the shared database's pdm_test schema: a backup and restore
// there would dump and rewrite the shared database's public schema too, which the tests never
// touch.
[Collection("EasyPDM database")]
public class BackupRestoreTests
{
    private const string Database = "pdm_backup_test";

    private static string ServerConnection()
    {
        var csb = new NpgsqlConnectionStringBuilder(EasyPDMWebApplicationFactory.ConnectionString) { SearchPath = null };
        return csb.ConnectionString;
    }

    private static async Task<string> FreshDatabaseAsync()
    {
        await using (var conn = new NpgsqlConnection(ServerConnection()))
        {
            await conn.OpenAsync();
            await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Database} WITH (FORCE);", conn))
                await drop.ExecuteNonQueryAsync();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE {Database};", conn))
                await create.ExecuteNonQueryAsync();
        }
        var csb = new NpgsqlConnectionStringBuilder(ServerConnection()) { Database = Database };
        await using (var conn = new NpgsqlConnection(csb.ConnectionString))
        {
            await conn.OpenAsync();
            var schema = await File.ReadAllTextAsync(SchemaSql());
            await using var cmd = new NpgsqlCommand(schema, conn);
            await cmd.ExecuteNonQueryAsync();
        }
        return csb.ConnectionString;
    }

    private static async Task DropDatabaseAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(ServerConnection());
        await conn.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Database} WITH (FORCE);", conn);
        await drop.ExecuteNonQueryAsync();
    }

    private static string SchemaSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "db", "schema.sql")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "db", "schema.sql");
    }

    private static EasyPDMWebApplicationFactory Factory(string connection) =>
        new() { ExtraSettings = new() { ["ConnectionString"] = connection } };

    private static async Task<(Guid ItemId, Guid AttachmentId)> ItemWithFileAsync(HttpClient admin, string name, byte[] bytes)
    {
        var projectId = await admin.CreateProjectAsync($"Backup {Guid.NewGuid()}");
        var itemId = await admin.CreateNodeAsync(projectId, name, "part");
        using var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", name + ".step" } };
        (await admin.PostAsync($"/api/items/{itemId}/attachments", form)).EnsureSuccessStatusCode();
        var atts = await admin.GetFromJsonAsync<JsonElement>($"/api/items/{itemId}/attachments");
        return (itemId, atts.EnumerateArray().Single().GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> RestoreAsync(HttpClient admin, byte[] zip)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(zip), "file", "backup.zip" } };
        var r = await admin.PostAsync("/api/settings/restore", form);
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, text);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.True(body.GetProperty("success").GetBoolean(), body.GetProperty("warnings").GetString());
        return body;
    }

    [Fact]
    public async Task A_backup_brings_back_items_and_files_after_later_changes()
    {
        var connection = await FreshDatabaseAsync();
        try
        {
            await using var factory = Factory(connection);
            using var admin = factory.CreateClient();
            await admin.LoginAsync("admin", "admin");
            var bytes = RandomNumberGenerator.GetBytes(3000);
            var (kept, attachment) = await ItemWithFileAsync(admin, "Kept", bytes);

            var backup = await admin.GetAsync("/api/settings/backup");
            Assert.True(backup.IsSuccessStatusCode, await backup.Content.ReadAsStringAsync());
            var zip = await backup.Content.ReadAsByteArrayAsync();
            using (var archive = new ZipArchive(new MemoryStream(zip)))
            {
                Assert.Contains(archive.Entries, e => e.FullName == "database.dump");
                Assert.Contains(archive.Entries, e => e.FullName.StartsWith("storage/") && e.Length == bytes.Length);
            }

            // Afterwards: the item is deleted and another one appears.
            (await admin.DeleteAsync($"/api/items/{kept}")).EnsureSuccessStatusCode();
            var (later, _) = await ItemWithFileAsync(admin, "Later", bytes);

            await RestoreAsync(admin, zip);

            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/items/{kept}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/items/{later}")).StatusCode);
            Assert.Equal(bytes, await admin.GetByteArrayAsync($"/api/attachments/{attachment}/file"));
        }
        finally { await DropDatabaseAsync(); }
    }

    [Fact]
    public async Task A_backup_restored_on_a_server_with_its_storage_elsewhere_finds_its_files_there()
    {
        var connection = await FreshDatabaseAsync();
        try
        {
            byte[] zip;
            var bytes = RandomNumberGenerator.GetBytes(2048);
            Guid attachment;
            await using (var first = Factory(connection))
            {
                using var admin = first.CreateClient();
                await admin.LoginAsync("admin", "admin");
                (_, attachment) = await ItemWithFileAsync(admin, "Moved", bytes);
                zip = await admin.GetByteArrayAsync("/api/settings/backup");
            }

            await using var second = Factory(connection);   // a new StorageRoot of its own
            using var admin2 = second.CreateClient();
            await admin2.LoginAsync("admin", "admin");
            var body = await RestoreAsync(admin2, zip);
            Assert.True(body.GetProperty("pathsRewritten").GetInt32() >= 1);
            Assert.Equal(0, body.GetProperty("filesMissing").GetInt32());
            // The restored database has the sessions of the backup, not this one: log in again.
            await admin2.LoginAsync("admin", "admin");
            Assert.Equal(bytes, await admin2.GetByteArrayAsync($"/api/attachments/{attachment}/file"));
            Assert.NotEmpty(Directory.EnumerateFiles(second.StorageRoot, "*", SearchOption.AllDirectories));
        }
        finally { await DropDatabaseAsync(); }
    }

    [Fact]
    public async Task Something_that_is_not_an_EasyPDM_backup_is_refused_and_changes_nothing()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");

        using var notZip = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "x.zip" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/settings/restore", notZip)).StatusCode);

        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            archive.CreateEntry("readme.txt");
        using var foreignZip = new MultipartFormDataContent { { new ByteArrayContent(ms.ToArray()), "file", "other.zip" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/settings/restore", foreignZip)).StatusCode);
    }
}
