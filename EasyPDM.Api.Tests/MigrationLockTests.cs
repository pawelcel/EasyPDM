using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace EasyPDM.Api.Tests;

// Blokada migracji (pg_advisory_lock) należy do sesji PostgreSQL, a połączenie z puli zachowuje
// sesję po zamknięciu. Po starcie programu blokada musi więc być zwolniona jawnie — inaczej
// kolejny start w tym samym procesie (albo druga instancja) czeka na nią do limitu czasu.
[Collection("EasyPDM database")]
public class MigrationLockTests
{
    private const long AdvisoryLockKey = 875_142_001;

    [Fact]
    public async Task Po_starcie_blokada_migracji_jest_zwolniona()
    {
        await using var factory = new EasyPDMWebApplicationFactory();
        using var client = factory.CreateClient(); // schemat na miejscu

        // Wprost, bez reszty startu programu: inaczej następne zapytanie przy starcie wypożycza
        // to samo połączenie z puli, Npgsql je czyści i blokada znika przypadkiem.
        var runner = typeof(Program).Assembly.GetTypes().Single(t => t.Name == "MigrationRunner");
        var apply = runner.GetMethod("ApplyPendingMigrationsAsync", BindingFlags.Public | BindingFlags.Static)!;
        await (Task)apply.Invoke(null, [EasyPDMWebApplicationFactory.ConnectionString, NullLogger.Instance])!;

        // Osobna sesja spoza puli: gdyby blokadę trzymało bezczynne połączenie w puli,
        // pg_try_advisory_lock zwróciłoby false.
        var csb = new NpgsqlConnectionStringBuilder(EasyPDMWebApplicationFactory.ConnectionString) { Pooling = false };
        await using var conn = new NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key);", conn);
        cmd.Parameters.AddWithValue("key", AdvisoryLockKey);
        var acquired = (bool)(await cmd.ExecuteScalarAsync())!;
        Assert.True(acquired);

        await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key);", conn);
        unlock.Parameters.AddWithValue("key", AdvisoryLockKey);
        await unlock.ExecuteNonQueryAsync();
    }
}
