using Npgsql;

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
app.MapItemEndpoints(connectionString, storageRoot);
app.MapTagEndpoints(connectionString);
app.MapPropertyEndpoints(connectionString);
app.MapStructureEndpoints(connectionString);
app.MapBomEndpoints(connectionString);
app.MapMaterialEndpoints(connectionString);
app.MapAttachmentEndpoints(connectionString, storageRoot);
app.MapConfigEndpoints(storageRoot);

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
