using Microsoft.Extensions.Configuration;
using PdmSystem.Core.Services;
using PdmSystem.Core.Services.Adapters;

// UWAGA: ten skaner na dzień dzisiejszy NIE jest zgodny ze schematem bazy
// po migracji 002 (items.project_id NOT NULL) — insert się nie powiedzie,
// dopóki nie ustalimy, jak wyniki skanu mają mapować się na projekt.
// Zaparkowane świadomie, patrz README.

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

string connectionString = config["ConnectionString"]
    ?? throw new InvalidOperationException("Brak ConnectionString w appsettings.json");

var scanFolders = config.GetSection("ScanFolders").Get<string[]>() ?? [];
var extensions = config.GetSection("FileExtensions").Get<string[]>()
    ?? [".fcstd"];

if (scanFolders.Length == 0)
{
    Console.WriteLine("Brak skonfigurowanych folderów do skanowania (ScanFolders w appsettings.json).");
    return;
}

var adapters = new List<ICadAdapter>
{
    new FreeCadAdapter(),
    // new SolidWorksAdapter(), // zaparkowane pod przyszłą Fazę integracji z SolidWorks
};

var runner = new ScanRunner(extensions, adapters, connectionString, log: Console.WriteLine);
var result = await runner.RunAsync(scanFolders);

Console.WriteLine();
Console.WriteLine($"Zakończono. Przetworzono: {result.Processed}, błędów: {result.Errors}.");
