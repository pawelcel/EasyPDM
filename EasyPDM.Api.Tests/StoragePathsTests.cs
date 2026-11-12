using Npgsql;

namespace EasyPDM.Api.Tests;

// Przepisywanie bezwzględnych ścieżek plików po przeniesieniu magazynu albo przywróceniu kopii
// na serwerze z innym katalogiem magazynu (zob. StoragePaths).
public class StoragePathsRelocateTests
{
    private static Func<string, bool> Existing(params string[] paths)
    {
        var set = paths.Select(p => p.Replace('\\', '/')).ToHashSet();
        return p => set.Contains(p.Replace('\\', '/'));
    }

    [Fact]
    public void Znany_dawny_katalog_podmienia_poczatek_sciezki()
    {
        var result = StoragePaths.Relocate("/srv/old/storage/p1/a.step", "/srv/old/storage", "/data/storage", _ => false);
        Assert.Equal(Path.Combine("/data/storage", "p1", "a.step"), result);
    }

    [Fact]
    public void Dawny_katalog_pasuje_tylko_na_granicy_katalogu()
    {
        // "/srv/storage" nie jest początkiem "/srv/storage2/..." — to inny katalog.
        var result = StoragePaths.Relocate("/srv/storage2/p1/a.step", "/srv/storage", "/data/storage", _ => false);
        Assert.Null(result);
    }

    [Fact]
    public void Sciezka_z_Windows_trafia_do_magazynu_na_Linuksie()
    {
        var result = StoragePaths.Relocate(@"C:\PDM\storage\attachments\i1\x.pdf", @"C:\PDM\storage\", "/var/lib/easypdm/storage", _ => false);
        Assert.Equal(Path.Combine("/var/lib/easypdm/storage", "attachments", "i1", "x.pdf"), result);
    }

    [Fact]
    public void Bez_dawnego_katalogu_plik_jest_szukany_po_koncowce_sciezki()
    {
        // Kopia sprzed zapisywania katalogu magazynu: początek ścieżki jest nieznany.
        var target = Path.Combine("/data/storage", "clients", "7", "n1.dwg");
        var result = StoragePaths.Relocate("/home/kto/stary/magazyn/clients/7/n1.dwg", null, "/data/storage", Existing(target));
        Assert.Equal(target, result);
    }

    [Fact]
    public void Nieznaleziony_plik_zostaje_bez_zmian()
    {
        Assert.Null(StoragePaths.Relocate("/gdzies/p1/brak.step", null, "/data/storage", _ => false));
    }
}

[Collection("EasyPDM database")]
public class StoragePathsDatabaseTests
{
    // Wszystkie tabele ze ścieżkami są przepisywane — także załączniki projektu, które
    // przenoszenie magazynu wcześniej pomijało.
    [Fact]
    public async Task Przepisuje_sciezki_w_elementach_i_zalacznikach_projektu()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var admin = factory.CreateClient();
        await admin.LoginAsync("admin", "admin");
        var projectId = await admin.CreateProjectAsync($"Magazyn {Guid.NewGuid()}");

        var tag = Guid.NewGuid().ToString("N");
        var oldRoot = $"/stary/magazyn/{tag}";
        var newRoot = Path.Combine(Path.GetTempPath(), $"pdm_relocate_{tag}");
        var itemPath = $"{oldRoot}/{projectId}/{tag}.step";
        var attachmentPath = $"{oldRoot}/project-attachments/{projectId}/{tag}.pdf";

        await using var conn = new NpgsqlConnection(EasyPDMWebApplicationFactory.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        Guid itemId, attachmentId;
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO items (project_id, item_type, file_name, file_path) VALUES (@p, 'file', 'a.step', @path) RETURNING id;", conn, tx))
        {
            cmd.Parameters.AddWithValue("p", projectId);
            cmd.Parameters.AddWithValue("path", itemPath);
            itemId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO project_attachments (project_id, file_name, file_path) VALUES (@p, 'oferta.pdf', @path) RETURNING id;", conn, tx))
        {
            cmd.Parameters.AddWithValue("p", projectId);
            cmd.Parameters.AddWithValue("path", attachmentPath);
            attachmentId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        await StoragePaths.RelocateAllAsync(conn, tx, oldRoot, newRoot, _ => false);

        async Task<string> PathOf(string table, Guid id)
        {
            await using var cmd = new NpgsqlCommand($"SELECT file_path FROM {table} WHERE id = @id;", conn, tx);
            cmd.Parameters.AddWithValue("id", id);
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        Assert.Equal(Path.Combine(newRoot, projectId.ToString(), $"{tag}.step"), await PathOf("items", itemId));
        Assert.Equal(Path.Combine(newRoot, "project-attachments", projectId.ToString(), $"{tag}.pdf"), await PathOf("project_attachments", attachmentId));

        await tx.RollbackAsync();
    }
}
