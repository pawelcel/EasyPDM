using System.Reflection;
using Npgsql;

// Automatyczne stosowanie migracji przy KAŻDYM starcie programu — działa identycznie
// niezależnie od sposobu wdrożenia (Docker, Linux/Windows jako usługa, zwykłe "dotnet run"),
// więc aktualizacja już zainstalowanego programu sprowadza się do podmiany plików i
// restartu, bez ręcznego odpalania psql. Migracje są wbudowane w sam plik wykonywalny jako
// embedded resources (zob. PdmSystem.Api.csproj) — nie zależą od tego, czy folder
// db/migrations/ został skądś skopiowany obok.
static class MigrationRunner
{
    // Nadany przez <EmbeddedResource ... LinkBase="Migrations" /> w połączeniu
    // z RootNamespace "PdmSystem.Api" w .csproj — potwierdzone odczytem
    // Assembly.GetManifestResourceNames() na realnie zbudowanym DLL-u.
    private const string ResourcePrefix = "PdmSystem.Api.Migrations.";

    public static async Task ApplyPendingMigrationsAsync(string connectionString, ILogger logger)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

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
                "MigrationRunner: tabela 'users' nie istnieje — pomijam automatyczne migracje " +
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
                "MigrationRunner: pierwsza kontrola po wprowadzeniu śledzenia migracji — " +
                "oznaczam {Count} już znanych migracji jako zastosowane bez ich ponownego wykonywania.",
                migrations.Count);
            foreach (var (_, fileName) in migrations)
                await MarkAppliedAsync(conn, fileName);
            return;
        }

        foreach (var (resourceName, fileName) in migrations)
        {
            if (applied.Contains(fileName))
                continue;

            logger.LogInformation("MigrationRunner: stosuję {FileName}...", fileName);

            // Każdy plik migracji sam zarządza własną transakcją (BEGIN;...COMMIT;) — nie
            // owijamy tego dodatkowo transakcją ADO.NET, żeby uniknąć zagnieżdżenia.
            await using (var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Brak zasobu {resourceName}."))
            using (var streamReader = new StreamReader(stream))
            {
                var sql = await streamReader.ReadToEndAsync();
                await using var cmd = new NpgsqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();
            }

            await MarkAppliedAsync(conn, fileName);
            logger.LogInformation("MigrationRunner: zastosowano {FileName}.", fileName);
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
