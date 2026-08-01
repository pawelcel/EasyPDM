using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

// Ustawienia administracyjne: lokalizacja magazynu plików (StorageRoot) i kopia zapasowa
// całego systemu (magazyn + baza PostgreSQL). Wyłącznie dla administratora.
static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(
        this WebApplication app, string connectionString, StorageSettings storage, string appSettingsPath)
    {
        // GET /api/settings/storage — bieżąca lokalizacja magazynu + podstawowe statystyki.
        app.MapGet("/api/settings/storage", (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx))
                return Forbidden();

            var path = storage.Path;
            long fileCount = 0;
            long totalSizeBytes = 0;
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    fileCount++;
                    try { totalSizeBytes += new FileInfo(file).Length; } catch (IOException) { }
                }
            }

            return Results.Ok(new { path, fileCount, totalSizeBytes });
        });

        // POST /api/settings/storage/move   body: { newPath, migrateExisting }
        // migrateExisting = false: zmienia tylko lokalizację dla NOWYCH plików — istniejące
        // zostają tam, gdzie są (każdy element i tak trzyma pełną, niezależną ścieżkę do
        // swojego pliku, więc to samo w sobie nic nie psuje).
        // migrateExisting = true: kopiuje WSZYSTKIE istniejące pliki do nowej lokalizacji,
        // przepisuje ścieżki w bazie (items/item_attachments) i dopiero po potwierdzonym
        // sukcesie kasuje stare pliki.
        app.MapPost("/api/settings/storage/move", async (HttpContext ctx, MoveStorageRequest body) =>
        {
            if (!IsAdmin(ctx))
                return Forbidden();

            if (string.IsNullOrWhiteSpace(body.NewPath))
                return Results.BadRequest("Nowa ścieżka nie może być pusta.");
            if (!Path.IsPathRooted(body.NewPath))
                return Results.BadRequest("Ścieżka musi być bezwzględna (np. /mnt/dysk/magazyn).");

            var oldPath = Path.GetFullPath(storage.Path);
            var newPath = Path.GetFullPath(body.NewPath);

            if (string.Equals(oldPath, newPath, StringComparison.Ordinal))
                return Results.BadRequest("Nowa ścieżka jest taka sama jak obecna.");

            try
            {
                Directory.CreateDirectory(newPath);
                var probe = Path.Combine(newPath, $".pdm_write_test_{Guid.NewGuid():N}");
                await File.WriteAllTextAsync(probe, "");
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Results.BadRequest($"Nie można zapisać w podanej lokalizacji: {ex.Message}");
            }

            var migratedFiles = 0;
            if (body.MigrateExisting && Directory.Exists(oldPath))
            {
                migratedFiles = CopyDirectoryRecursive(oldPath, newPath);

                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                await using var tx = await conn.BeginTransactionAsync();

                await RewriteFilePathsAsync(conn, tx, "items", oldPath, newPath);
                await RewriteFilePathsAsync(conn, tx, "item_attachments", oldPath, newPath);

                await tx.CommitAsync();

                // Stare pliki kasujemy DOPIERO po udanym skopiowaniu i przepisaniu bazy —
                // jeśli cokolwiek zawiedzie wcześniej, oryginał zostaje nietknięty.
                try { Directory.Delete(oldPath, recursive: true); } catch (IOException) { }
            }

            storage.Path = newPath;
            await PersistStorageRootAsync(appSettingsPath, newPath);

            return Results.Ok(new { path = newPath, migratedFiles });
        });

        // GET /api/settings/backup — pg_dump bazy + magazyn plików spakowane w jeden ZIP.
        app.MapGet("/api/settings/backup", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx))
                return Forbidden();

            var tempDir = Path.Combine(Path.GetTempPath(), $"pdm_backup_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                var dumpPath = Path.Combine(tempDir, "database.dump");
                var csb = new NpgsqlConnectionStringBuilder(connectionString);

                var psi = new ProcessStartInfo
                {
                    FileName = "pg_dump",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("-h");
                psi.ArgumentList.Add(csb.Host ?? "localhost");
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add((csb.Port == 0 ? 5432 : csb.Port).ToString());
                psi.ArgumentList.Add("-U");
                psi.ArgumentList.Add(csb.Username ?? "");
                psi.ArgumentList.Add("-d");
                psi.ArgumentList.Add(csb.Database ?? "");
                psi.ArgumentList.Add("-F");
                psi.ArgumentList.Add("c");
                psi.ArgumentList.Add("-f");
                psi.ArgumentList.Add(dumpPath);
                psi.Environment["PGPASSWORD"] = csb.Password ?? "";

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("Nie udało się uruchomić pg_dump.");
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                    return Results.Problem($"pg_dump zakończył się błędem: {stderr}");

                var zipPath = Path.Combine(tempDir, "backup.zip");
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(dumpPath, "database.dump");
                    if (Directory.Exists(storage.Path))
                    {
                        foreach (var file in Directory.EnumerateFiles(storage.Path, "*", SearchOption.AllDirectories))
                        {
                            var relative = Path.GetRelativePath(storage.Path, file).Replace('\\', '/');
                            zip.CreateEntryFromFile(file, $"storage/{relative}");
                        }
                    }
                }

                var bytes = await File.ReadAllBytesAsync(zipPath);
                var fileName = $"pdm-backup-{DateTime.Now:yyyy-MM-dd_HHmm}.zip";
                return Results.File(bytes, "application/zip", fileName);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
            }
        });
    }

    private static int CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        var count = 0;
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destPath = Path.Combine(destDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, overwrite: true);
            count++;
        }
        return count;
    }

    // "table" jest zawsze jedną z dwóch literalnych, stałych wartości podanych niżej w tym
    // pliku (nigdy danymi od użytkownika), więc budowanie z niej SQL-a przez interpolację
    // jest tu bezpieczne.
    private static async Task RewriteFilePathsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string table, string oldRoot, string newRoot)
    {
        var rows = new List<(Guid Id, string OldPath)>();
        await using (var selectCmd = new NpgsqlCommand($"SELECT id, file_path FROM {table} WHERE file_path IS NOT NULL;", conn, tx))
        {
            await using var reader = await selectCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        foreach (var (id, oldFilePath) in rows)
        {
            if (!oldFilePath.StartsWith(oldRoot, StringComparison.Ordinal))
                continue;

            var newFilePath = newRoot + oldFilePath[oldRoot.Length..];
            await using var updateCmd = new NpgsqlCommand($"UPDATE {table} SET file_path = @newPath WHERE id = @id;", conn, tx);
            updateCmd.Parameters.AddWithValue("newPath", newFilePath);
            updateCmd.Parameters.AddWithValue("id", id);
            await updateCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task PersistStorageRootAsync(string appSettingsPath, string newPath)
    {
        var json = await File.ReadAllTextAsync(appSettingsPath);
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("Niepoprawny appsettings.json.");
        node["StorageRoot"] = newPath;
        await File.WriteAllTextAsync(appSettingsPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool IsAdmin(HttpContext ctx) => (ctx.Items["CurrentUser"] as CurrentUser)?.Role == "admin";

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);
}

record MoveStorageRequest(string NewPath, bool MigrateExisting);
