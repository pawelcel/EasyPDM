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
            if (!AuthEndpoints.IsAdmin(ctx))
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
            if (!AuthEndpoints.IsAdmin(ctx))
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
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            byte[] bytes;
            try
            {
                bytes = await CreateBackupZipAsync(connectionString, storage);
            }
            catch (BackupFailedException ex)
            {
                return Results.Problem(ex.Message);
            }

            var fileName = $"pdm-backup-{DateTime.Now:yyyy-MM-dd_HHmm}.zip";
            return Results.File(bytes, "application/zip", fileName);
        });

        // GET /api/settings/backup-schedule — bieżący harmonogram automatycznej kopii zapasowej.
        app.MapGet("/api/settings/backup-schedule", async (HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            return Results.Ok(await GetBackupScheduleAsync(conn));
        });

        // PATCH /api/settings/backup-schedule   body: { enabled, frequency, dayOfWeek, dayOfMonth, hour, minute }
        app.MapPatch("/api/settings/backup-schedule", async (HttpContext ctx, BackupScheduleRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            if (body.Frequency != "daily" && body.Frequency != "weekly" && body.Frequency != "monthly")
                return Results.BadRequest("Nieprawidłowa częstotliwość — dozwolone: 'daily', 'weekly', 'monthly'.");
            if (body.Frequency == "weekly" && (body.DayOfWeek is null || body.DayOfWeek is < 0 or > 6))
                return Results.BadRequest("Dla częstotliwości 'weekly' wymagany jest 'dayOfWeek' w zakresie 0-6.");
            if (body.Frequency == "monthly" && (body.DayOfMonth is null || body.DayOfMonth is < 1 or > 31))
                return Results.BadRequest("Dla częstotliwości 'monthly' wymagany jest 'dayOfMonth' w zakresie 1-31.");
            if (body.Hour is < 0 or > 23)
                return Results.BadRequest("Godzina musi być w zakresie 0-23.");
            if (body.Minute is < 0 or > 59)
                return Results.BadRequest("Minuta musi być w zakresie 0-59.");
            if (body.RetentionCount is < 1 or > 365)
                return Results.BadRequest("Liczba przechowywanych kopii musi być w zakresie 1-365.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                UPDATE backup_schedule SET
                    enabled = @enabled, frequency = @frequency,
                    day_of_week = @dayOfWeek, day_of_month = @dayOfMonth,
                    hour = @hour, minute = @minute, retention_count = @retentionCount
                WHERE id = true;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("enabled", body.Enabled);
            cmd.Parameters.AddWithValue("frequency", body.Frequency);
            cmd.Parameters.AddWithValue("dayOfWeek", (object?)body.DayOfWeek ?? DBNull.Value);
            cmd.Parameters.AddWithValue("dayOfMonth", (object?)body.DayOfMonth ?? DBNull.Value);
            cmd.Parameters.AddWithValue("hour", body.Hour);
            cmd.Parameters.AddWithValue("minute", body.Minute);
            cmd.Parameters.AddWithValue("retentionCount", body.RetentionCount);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok(await GetBackupScheduleAsync(conn));
        });

        // POST /api/settings/restore   multipart/form-data: file (ZIP z GET /settings/backup)
        // Odtwarza bazę (pg_restore --clean, więc nadpisuje WSZYSTKIE bieżące dane) i podmienia
        // cały magazyn plików na ten z kopii. To NIEODWRACALNA operacja — frontend pokazuje
        // ostrzeżenie przed wywołaniem. Po przywróceniu baza jest inna niż ta, z którą łączy
        // się pula połączeń Npgsql, więc czyścimy pule, żeby kolejne zapytania od razu
        // dostały świeże połączenia zamiast (potencjalnie nieaktualnych) z cache'u.
        app.MapPost("/api/settings/restore", async (HttpContext ctx, HttpRequest request) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            if (!request.HasFormContentType)
                return Results.BadRequest("Oczekiwano danych multipart/form-data.");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest("Brak pliku kopii zapasowej w polu 'file'.");

            var tempDir = Path.Combine(Path.GetTempPath(), $"pdm_restore_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                var zipPath = Path.Combine(tempDir, "upload.zip");
                await using (var stream = File.Create(zipPath))
                    await file.CopyToAsync(stream);

                var extractDir = Path.Combine(tempDir, "extracted");
                try
                {
                    ZipFile.ExtractToDirectory(zipPath, extractDir);
                }
                catch (InvalidDataException)
                {
                    return Results.BadRequest("Przesłany plik nie jest poprawnym archiwum ZIP.");
                }

                var dumpPath = Path.Combine(extractDir, "database.dump");
                if (!File.Exists(dumpPath))
                {
                    return Results.BadRequest(
                        "Plik ZIP nie zawiera database.dump — to nie jest kopia zapasowa EasyPDM.");
                }

                var csb = new NpgsqlConnectionStringBuilder(connectionString);
                var psi = new ProcessStartInfo
                {
                    FileName = "pg_restore",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("-h");
                psi.ArgumentList.Add(csb.Host ?? "localhost");
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add((csb.Port == 0 ? 5432 : csb.Port).ToString());
                // Tabele w tej bazie należą do "postgres" (schemat był zakładany tą rolą —
                // ten sam powód, dla którego część migracji w db/migrations/ wymaga
                // uruchomienia jako "postgres", nie zwykłego pdm_user z connection stringa).
                // --clean musi więc łączyć się jako właściciel, inaczej DROP/CREATE każdej
                // tabeli kończy się błędem "must be owner of table" i restore nic nie zmienia.
                psi.ArgumentList.Add("-U");
                psi.ArgumentList.Add("postgres");
                psi.ArgumentList.Add("-d");
                psi.ArgumentList.Add(csb.Database ?? "");
                psi.ArgumentList.Add("--clean");
                psi.ArgumentList.Add("--if-exists");
                psi.ArgumentList.Add(dumpPath);

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("Nie udało się uruchomić pg_restore.");
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                // pg_restore nie jest tu uruchamiany w jednej transakcji — pojedyncze,
                // nieszkodliwe błędy (np. brak uprawnień do rozszerzenia, które i tak już
                // istnieje) nie przerywają reszty odtwarzania, więc niezerowy kod zwracamy
                // jako ostrzeżenie, a nie twardy błąd blokujący dalsze kroki.
                NpgsqlConnection.ClearAllPools();

                var filesRestored = 0;
                var storageBackupDir = Path.Combine(extractDir, "storage");
                if (Directory.Exists(storageBackupDir))
                {
                    if (Directory.Exists(storage.Path))
                    {
                        foreach (var entry in Directory.EnumerateFileSystemEntries(storage.Path))
                        {
                            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                            else File.Delete(entry);
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory(storage.Path);
                    }
                    filesRestored = CopyDirectoryRecursive(storageBackupDir, storage.Path);
                }

                return Results.Ok(new { success = process.ExitCode == 0, warnings = stderr, filesRestored });
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
            }
        });
    }

    // Wspólna logika pg_dump + spakowanie magazynu plików w ZIP — używana zarówno przez
    // GET /api/settings/backup (pobranie ręczne, zwraca do przeglądarki), jak i przez
    // ScheduledBackupService (kopia automatyczna wg harmonogramu, zapisywana na dysku).
    internal static async Task<byte[]> CreateBackupZipAsync(string connectionString, StorageSettings storage)
    {
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
            // --no-owner: kto odtworzy bazę (patrz pg_restore niżej) i tak będzie
            // właścicielem nowo tworzonych tabel. GRANT-y (np. dla pdm_user) NIE są tu
            // pomijane — to nie są uprawnienia domyślne, więc bez nich po restore appka
            // straciłaby dostęp do własnych tabel.
            psi.ArgumentList.Add("--no-owner");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(dumpPath);
            psi.Environment["PGPASSWORD"] = csb.Password ?? "";

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Nie udało się uruchomić pg_dump.");
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new BackupFailedException($"pg_dump zakończył się błędem: {stderr}");

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

            return await File.ReadAllBytesAsync(zipPath);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }

    internal static async Task<BackupSchedule> GetBackupScheduleAsync(NpgsqlConnection conn)
    {
        const string sql = """
            SELECT enabled, frequency, day_of_week, day_of_month, hour, minute, last_run_at, retention_count
            FROM backup_schedule WHERE id = true;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new BackupSchedule(
            Enabled: reader.GetBoolean(0),
            Frequency: reader.GetString(1),
            DayOfWeek: reader.IsDBNull(2) ? null : reader.GetInt32(2),
            DayOfMonth: reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Hour: reader.GetInt32(4),
            Minute: reader.GetInt32(5),
            LastRunAt: reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            RetentionCount: reader.GetInt32(7));
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

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);
}

// Rzucany, gdy samo pg_dump zawiedzie (odróżniony od zwykłych wyjątków I/O, żeby
// GET /api/settings/backup mógł zwrócić Results.Problem zamiast 500 z nieczytelnym stosem).
class BackupFailedException(string message) : Exception(message);

record MoveStorageRequest(string NewPath, bool MigrateExisting);

record BackupSchedule(
    bool Enabled, string Frequency, int? DayOfWeek, int? DayOfMonth, int Hour, int Minute, DateTime? LastRunAt,
    int RetentionCount);

record BackupScheduleRequest(
    bool Enabled, string Frequency, int? DayOfWeek, int? DayOfMonth, int Hour, int Minute, int RetentionCount);
