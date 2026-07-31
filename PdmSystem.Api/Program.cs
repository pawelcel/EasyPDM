var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string connectionString = app.Configuration["ConnectionString"]
    ?? throw new InvalidOperationException("Brak ConnectionString w appsettings.json");

// Katalog, w którym API fizycznie trzyma wgrane pliki (ręczne dodawanie elementów).
// Ścieżka względna liczona jest od katalogu aplikacji, żeby działało niezależnie od tego,
// z jakiego katalogu roboczego uruchomisz `dotnet run`.
string storageRoot = app.Configuration["StorageRoot"] ?? "storage";
if (!Path.IsPathRooted(storageRoot))
    storageRoot = Path.Combine(AppContext.BaseDirectory, storageRoot);
Directory.CreateDirectory(storageRoot);

// Serwuje wwwroot/index.html pod adresem "/" oraz zbudowany frontend.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapProjectEndpoints(connectionString);
app.MapItemEndpoints(connectionString, storageRoot);
app.MapTagEndpoints(connectionString);
app.MapPropertyEndpoints(connectionString);
app.MapStructureEndpoints(connectionString);
app.MapMaterialEndpoints(connectionString);
app.MapAttachmentEndpoints(connectionString, storageRoot);
app.MapConfigEndpoints(storageRoot);

app.Run();
