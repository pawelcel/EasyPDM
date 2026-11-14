using System.Reflection;
using Npgsql;

// Automatyczne stosowanie migracji przy KAŻDYM starcie programu — działa identycznie
// niezależnie od sposobu wdrożenia (Docker, Linux/Windows jako usługa, zwykłe "dotnet run"),
// więc aktualizacja już zainstalowanego programu sprowadza się do podmiany plików i
// restartu, bez ręcznego odpalania psql. Migracje są wbudowane w sam plik wykonywalny jako
// embedded resources (zob. EasyPDM.Api.csproj) — nie zależą od tego, czy folder
// db/migrations/ został skądś skopiowany obok.
static class MigrationRunner
{
    // Nadany przez <EmbeddedResource ... LinkBase="Migrations" /> w połączeniu
    // z RootNamespace "EasyPDM.Api" w .csproj — potwierdzone odczytem
    // Assembly.GetManifestResourceNames() na realnie zbudowanym DLL-u.
    private const string ResourcePrefix = "EasyPDM.Api.Migrations.";

    // Dowolna stała liczba — advisory lock w PostgreSQL to tylko numeryczny klucz, nie musi
    // nic znaczyć poza tym, że jest unikalny w obrębie tej aplikacji i stały między wersjami.
    private const long AdvisoryLockKey = 875_142_001;

    public static async Task ApplyPendingMigrationsAsync(string connectionString, ILogger logger)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Blokada na poziomie SESJI PostgreSQL — chroni przed dwiema instancjami programu
        // (np. rolling restart, albo przypadkowe podwójne uruchomienie) próbującymi
        // zastosować tę samą migrację naraz. Zamknięcie połączenia jej NIE zwalnia: pula
        // zachowuje sesję, a Npgsql czyści ją dopiero przy następnym wypożyczeniu tego
        // połączenia. Do tego czasu każdy, kto prosi o blokadę na innym połączeniu, czeka —
        // dlatego zwalniamy ją ręcznie w finally.
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(@key);", conn))
        {
            lockCmd.Parameters.AddWithValue("key", AdvisoryLockKey);
            await lockCmd.ExecuteNonQueryAsync();
        }

        try
        {
            await ApplyUnderLockAsync(conn, logger);
        }
        finally
        {
            await using var unlockCmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@key);", conn);
            unlockCmd.Parameters.AddWithValue("key", AdvisoryLockKey);
            await unlockCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task ApplyUnderLockAsync(NpgsqlConnection conn, ILogger logger)
    {
        // Samonaprawiające się — działa też na instalacjach sprzed wprowadzenia tego
        // mechanizmu (migracja 027 tworzy tę samą tabelę, ale istniejąca baza mogła jej
        // jeszcze nie dostać, jeśli ktoś pominął ręczne stosowanie migracji do tej pory).
        await using (var createCmd = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                filename TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """, conn))
        {
            await createCmd.ExecuteNonQueryAsync();
        }

        // Baza w ogóle nie ma jeszcze podstawowego schematu (świeży, pusty PostgreSQL) — to
        // zadanie db/schema.sql / instalatora, nie tego mechanizmu (migracje 002+ zakładają,
        // że tabele bazowe typu "users" już istnieją).
        if (!await TableExistsAsync(conn, "users"))
        {
            logger.LogWarning(
                "MigrationRunner: table 'users' does not exist — skipping automatic migrations " +
                "(najpierw zastosuj db/schema.sql).");
            return;
        }

        var applied = new HashSet<string>();
        await using (var selectCmd = new NpgsqlCommand("SELECT filename FROM schema_migrations;", conn))
        await using (var reader = await selectCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                applied.Add(reader.GetString(0));
        }

        var assembly = Assembly.GetExecutingAssembly();
        var migrations = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(n => (ResourceName: n, FileName: n[ResourcePrefix.Length..]))
            .OrderBy(m => m.FileName, StringComparer.Ordinal)
            .ToList();

        // Baza już ma pełny, aktualny na dzień jej założenia schemat (świeża instalacja przez
        // db/schema.sql — zob. tam — albo istniejąca instalacja migrowana ręcznie do tej
        // pory), ale to pierwszy raz, kiedy widzi tabelę schema_migrations — oznacz WSZYSTKIE
        // aktualnie znane migracje jako już zastosowane, NIE wykonując ich ponownie. Od tego
        // momentu każda NOWA migracja (dodana w przyszłej wersji programu) zostanie faktycznie
        // wykonana przy najbliższym starcie.
        if (applied.Count == 0 && migrations.Count > 0)
        {
            logger.LogInformation(
                "MigrationRunner: first check since migration tracking was introduced — " +
                "marking {Count} known migrations as applied without running them again.",
                migrations.Count);
            foreach (var (_, fileName) in migrations)
                await MarkAppliedAsync(conn, fileName);
            return;
        }

        foreach (var (resourceName, fileName) in migrations)
        {
            if (applied.Contains(fileName))
                continue;

            logger.LogInformation("MigrationRunner: applying {FileName}...", fileName);

            try
            {
                // Każdy plik migracji sam zarządza własną transakcją (BEGIN;...COMMIT;) — nie
                // owijamy tego dodatkowo transakcją ADO.NET, żeby uniknąć zagnieżdżenia.
                await using (var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Missing resource {resourceName}."))
                using (var streamReader = new StreamReader(stream))
                {
                    var sql = await streamReader.ReadToEndAsync();
                    await using var cmd = new NpgsqlCommand(sql, conn);
                    await cmd.ExecuteNonQueryAsync();
                }

                await MarkAppliedAsync(conn, fileName);
                logger.LogInformation("MigrationRunner: zastosowano {FileName}.", fileName);
            }
            catch (Exception ex)
            {
                // Program CELOWO nie startuje dalej na niespójnym schemacie (rzucamy dalej) —
                // ale zamiast surowego wyjątku Npgsql, zostawiamy jasną wskazówkę. Jeśli
                // migracja NIE jest idempotentna (np. ADD COLUMN bez IF NOT EXISTS) i proces
                // padł PO faktycznym COMMIT tej migracji, ale PRZED zapisaniem postępu do
                // schema_migrations, kolejny start powtórzy TĘ SAMĄ migrację i dostanie ten
                // sam błąd w pętli — trzeba wtedy ręcznie sprawdzić, czy zmiana z tego pliku
                // faktycznie już jest w bazie, i jeśli tak, ręcznie dopisać wiersz do
                // schema_migrations zamiast próbować wykonać SQL drugi raz.
                logger.LogError(ex,
                    "MigrationRunner: migration {FileName} failed — the program will NOT " +
                    "start with a possibly inconsistent schema. If the change from this file " +
                    "is in fact already in the database (the process may have died AFTER running it, BEFORE recording " +
                    "progress), check by hand and add the row to schema_migrations instead of " +
                    "retrying the same SQL.",
                    fileName);
                throw;
            }
        }
    }

    private static async Task MarkAppliedAsync(NpgsqlConnection conn, string fileName)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO schema_migrations (filename) VALUES (@filename) ON CONFLICT DO NOTHING;", conn);
        cmd.Parameters.AddWithValue("filename", fileName);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string tableName)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @name;", conn);
        cmd.Parameters.AddWithValue("name", tableName);
        return await cmd.ExecuteScalarAsync() is not null;
    }
}
