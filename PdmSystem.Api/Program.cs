using Npgsql;

var builder = WebApplication.CreateBuilder(args);
// Lokalne, prawdziwe dane dostępowe (hasło do bazy itd.) trzymane POZA repozytorium —
// appsettings.json ma tylko placeholder, appsettings.Local.json (gitignored) go nadpisuje.
// Zob. appsettings.Local.json.example i "Jak uruchomić" w README.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
// Bezpieczne na każdej platformie: aktywuje się TYLKO, gdy proces faktycznie działa pod
// Windows Service Control Manager (instalator Windows rejestruje go jako usługę) — na
// Linux/macOS/Dockerze/zwykłym "dotnet run" to zwykły no-op.
builder.Host.UseWindowsService();
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
var storage = new StorageSettings(storageRoot);
string appSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");

// Katalog na automatyczne kopie zapasowe (Ustawienia -> Magazyn plików -> Automatyczna kopia)
// — celowo NIEZALEŻNY od StorageRoot: gdyby leżał wewnątrz magazynu plików, każda kolejna
// automatyczna kopia pakowałaby też wszystkie poprzednie kopie (rosnąca w nieskończoność
// zawartość ZIP-a) i zniekształcałaby statystyki plików pokazywane w Ustawieniach.
string backupRoot = app.Configuration["BackupRoot"] ?? "backups";
if (!Path.IsPathRooted(backupRoot))
    backupRoot = Path.Combine(AppContext.BaseDirectory, backupRoot);

// Katalog na logi programu (Ustawienia -> Logi). Podpięty pod ILoggerFactory od razu na
// starcie, żeby przechwycić też komunikaty z samego rozruchu (np. EnsureDefaultAdminAsync
// poniżej, komunikaty Microsoft.Hosting.Lifetime).
string logRoot = app.Configuration["LogRoot"] ?? "logs";
if (!Path.IsPathRooted(logRoot))
    logRoot = Path.Combine(AppContext.BaseDirectory, logRoot);
app.Services.GetRequiredService<ILoggerFactory>().AddProvider(new FileLoggerProvider(logRoot));

await EnsureDefaultAdminAsync(connectionString);

// Serwuje wwwroot/index.html pod adresem "/" oraz zbudowany frontend.
app.UseDefaultFiles();
app.UseStaticFiles();

// Każda ścieżka /api/* (poza logowaniem) wymaga poprawnej sesji — reszta endpointów nie
// musi tego sprawdzać samodzielnie. Zalogowany użytkownik trafia do HttpContext.Items pod
// kluczem "CurrentUser" (typu CurrentUser, zob. AuthEndpoints.cs), skąd mogą go odczytać
// poszczególne handlery (np. do sprawdzenia roli administratora).
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") &&
        !context.Request.Path.StartsWithSegments("/api/auth/login"))
    {
        var user = await AuthEndpoints.GetCurrentUser(context, connectionString);
        if (user is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Nie zalogowano.");
            return;
        }

        context.Items["CurrentUser"] = user;
    }

    await next();
});

app.MapAuthEndpoints(connectionString);
app.MapUserEndpoints(connectionString);
app.MapProjectEndpoints(connectionString);
app.MapProjectAccessEndpoints(connectionString);
app.MapItemEndpoints(connectionString, storage);
app.MapTagEndpoints(connectionString);
app.MapPropertyEndpoints(connectionString);
app.MapStructureEndpoints(connectionString);
app.MapBomEndpoints(connectionString);
app.MapDocumentationEndpoints(connectionString);
app.MapHistoryEndpoints(connectionString);
app.MapMaterialEndpoints(connectionString);
app.MapManufacturerEndpoints(connectionString);
app.MapSavedFilterEndpoints(connectionString);
app.MapAttachmentEndpoints(connectionString, storage);
app.MapConfigEndpoints(storage);
app.MapSettingsEndpoints(connectionString, storage, appSettingsPath);
app.MapLogEndpoints(logRoot);

// Bez rejestracji w DI (patrz komentarz w ScheduledBackupService.cs) — uruchamiane ręcznie,
// z tokenem powiązanym z zamykaniem aplikacji, żeby pętla zatrzymała się razem z serwerem.
_ = new ScheduledBackupService(connectionString, storage, backupRoot, app.Logger)
    .RunAsync(app.Lifetime.ApplicationStopping);

app.Run();

// Jeśli tabela users jest pusta (świeża instalacja), zakłada domyślne konto administratora.
// Hasło trzeba zahaszować kodem — dlatego to tutaj, nie w migracji SQL.
static async Task EnsureDefaultAdminAsync(string connectionString)
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    await using (var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM users;", conn))
    {
        var count = (long)(await countCmd.ExecuteScalarAsync())!;
        if (count > 0)
            return;
    }

    const string sql = """
        INSERT INTO users (username, display_name, password_hash, role)
        VALUES ('admin', 'Administrator', @hash, 'admin');
        """;
    await using var insertCmd = new NpgsqlCommand(sql, conn);
    insertCmd.Parameters.AddWithValue("hash", PasswordHasher.Hash("admin"));
    await insertCmd.ExecuteNonQueryAsync();

    Console.WriteLine(
        "Utworzono domyślne konto administratora — login: admin, hasło: admin. " +
        "Zmień hasło zaraz po pierwszym zalogowaniu.");
}
