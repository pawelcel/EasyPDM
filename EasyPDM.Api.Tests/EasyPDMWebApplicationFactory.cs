using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EasyPDM.Api.Tests;

// Uruchamia CAŁĄ aplikację (Program.cs, łącznie z MigrationRunner/EnsureDefaultAdminAsync)
// przeciwko prawdziwej bazie PostgreSQL — osobny schemat ("pdm_test", nie "public"), żeby
// nie dotykać realnych danych deweloperskich w tej samej instancji Postgresa. Connection
// string bierzemy ze zmiennej środowiskowej EASYPDM_TEST_CONNECTION_STRING (ustawianej w CI
// przez usługę postgres), z sensownym domyślnym dla lokalnego uruchomienia na tej samej
// maszynie deweloperskiej, na której powstał ten projekt.
public sealed class EasyPDMWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestSchema = "pdm_test";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("EASYPDM_TEST_CONNECTION_STRING")
        ?? $"Host=localhost;Port=5432;Database=pdm;Username=pdm_user;Password=haslo;Search Path={TestSchema}";

    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "easypdm-tests-storage-" + Guid.NewGuid().ToString("N"));
    public string BackupRoot { get; } = Path.Combine(Path.GetTempPath(), "easypdm-tests-backups-" + Guid.NewGuid().ToString("N"));
    public string LogRoot { get; } = Path.Combine(Path.GetTempPath(), "easypdm-tests-logs-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionString"] = ConnectionString,
                ["StorageRoot"] = StorageRoot,
                ["BackupRoot"] = BackupRoot,
                ["LogRoot"] = LogRoot,
            });
        });
    }

    // Zeruje schemat testowy do stanu "świeżo po db/schema.sql" — wywoływane RAZ na klasę
    // testową (zob. DatabaseFixture) przed pierwszym utworzeniem fabryki/hosta, żeby
    // MigrationRunner (uruchamiany przy starcie w Program.cs) zastał tabelę "users" i resztę
    // schematu już na miejscu.
    public static async Task ResetDatabaseAsync()
    {
        var csb = new NpgsqlConnectionStringBuilder(ConnectionString);
        var searchPath = csb.SearchPath ?? TestSchema;

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using (var dropCmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{searchPath}\" CASCADE;", conn))
            await dropCmd.ExecuteNonQueryAsync();
        await using (var createCmd = new NpgsqlCommand($"CREATE SCHEMA \"{searchPath}\";", conn))
            await createCmd.ExecuteNonQueryAsync();

        var schemaSqlPath = FindSchemaSqlPath();
        var schemaSql = await File.ReadAllTextAsync(schemaSqlPath);
        await using var schemaCmd = new NpgsqlCommand(schemaSql, conn);
        await schemaCmd.ExecuteNonQueryAsync();
    }

    // db/schema.sql leży w korzeniu repo, kilka poziomów wyżej od katalogu, w którym
    // faktycznie uruchamiane są testy (bin/Debug/net10.0/) — szukamy w górę drzewa
    // katalogów, zamiast zakładać sztywną liczbę "..".
    private static string FindSchemaSqlPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "db", "schema.sql");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Nie znaleziono db/schema.sql — uruchom testy z checkoutu repo EasyPDM.");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            TryDeleteDirectory(StorageRoot);
            TryDeleteDirectory(BackupRoot);
            TryDeleteDirectory(LogRoot);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

// xUnit "collection fixture" — jeden reset bazy dla WSZYSTKICH klas testowych w kolekcji
// "EasyPDM database" (zob. [Collection] na klasach testowych), zamiast per-klasę: testy w
// tej samej kolekcji NIE działają równolegle względem siebie (xUnit domyślnie serializuje
// klasy w tej samej kolekcji), co jest tu celowe — dzielą tę samą, mutowalną bazę.
public sealed class DatabaseFixture : IAsyncLifetime
{
    public async Task InitializeAsync() => await EasyPDMWebApplicationFactory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition("EasyPDM database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
