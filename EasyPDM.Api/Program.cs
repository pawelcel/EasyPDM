using Microsoft.AspNetCore.Http.Features;
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
// Kestrel domyślnie odcina żądania powyżej ~30 MB (MaxRequestBodySize) -- za mało dla
// realnych plików CAD (złożenia/eksporty STEP potrafią ważyć grubo ponad 100 MB,
// potwierdzone w praktyce: upload 133 MB kończył się 413 przy uploadzie z makra
// SolidWorks). Limit wyłączony całkowicie -- to zaufane, wewnętrzne narzędzie firmowe za
// logowaniem, nie publiczny serwis, więc nie ma sensu zgadywać arbitralnego górnego limitu,
// który i tak kiedyś okaże się za mały dla jakiegoś większego złożenia.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
// ReadFormAsync() (used by the attachment-upload endpoint) has its OWN separate default
// limit (128 MB, MultipartBodyLengthLimit) independent of Kestrel's -- raising only the
// Kestrel one above would still 413 on anything bigger than 128 MB. Same reasoning as
// Kestrel's limit: no sane fixed cap for arbitrary CAD files, so it's removed too.
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = long.MaxValue);
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
// CELOWO appsettings.Local.json, NIE appsettings.json — "Zmień lokalizację" (Ustawienia ->
// Magazyn plików) zapisuje tu nowy StorageRoot na przyszłość (po restarcie). Local.json jest
// dodany do konfiguracji (linia 8 wyżej) PO WSZYSTKICH domyślnych źródłach ASP.NET Core
// (appsettings.json, appsettings.{Environment}.json, zmienne środowiskowe) — więc ma
// najwyższy priorytet WSZĘDZIE (Windows: appsettings.Production.json pisane raz przez
// instalator; Linux/Docker: zmienna środowiskowa StorageRoot z pliku usługi/obrazu). Zapis
// do samego appsettings.json byłby cicho nadpisywany przez którekolwiek z tamtych przy
// każdym kolejnym starcie aplikacji — zmiana lokalizacji "wracałaby" do starej ścieżki po
// restarcie usługi/kontenera, mimo że baza i pliki są już w nowym miejscu.
string appSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json");

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

// Stosuje wszystkie jeszcze nie zastosowane migracje (db/migrations/, wbudowane w plik
// wykonywalny) PRZED czymkolwiek innym, co dotyka bazy — dzięki temu aktualizacja programu
// (Docker/Linux/Windows) sprowadza się do podmiany plików i restartu.
await MigrationRunner.ApplyPendingMigrationsAsync(connectionString, app.Logger);

await EnsureDefaultAdminAsync(connectionString);
await EnsureSampleProjectAsync(connectionString, app.Logger);

// Serwuje wwwroot/index.html pod adresem "/" oraz zbudowany frontend.
app.UseDefaultFiles();
app.UseStaticFiles();

// Każda ścieżka /api/* (poza logowaniem i mostem token->ciasteczko dla przeglądarki
// otwieranej przez makra CAD) wymaga poprawnej sesji — reszta endpointów nie musi tego
// sprawdzać samodzielnie. Zalogowany użytkownik trafia do HttpContext.Items pod kluczem
// "CurrentUser" (typu CurrentUser, zob. AuthEndpoints.cs), skąd mogą go odczytać
// poszczególne handlery (np. do sprawdzenia roli administratora).
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") &&
        !context.Request.Path.StartsWithSegments("/api/auth/login") &&
        !context.Request.Path.StartsWithSegments("/api/auth/browser-login"))
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

var createTicketStore = new CreateTicketStore();
var browserBridgeTicketStore = new BrowserBridgeTicketStore();

app.MapAuthEndpoints(connectionString, browserBridgeTicketStore);
app.MapUserEndpoints(connectionString);
app.MapProjectEndpoints(connectionString);
app.MapProjectAccessEndpoints(connectionString);
app.MapItemEndpoints(connectionString, storage, createTicketStore);
app.MapTagEndpoints(connectionString);
app.MapPropertyEndpoints(connectionString);
app.MapStructureEndpoints(connectionString);
app.MapBomEndpoints(connectionString);
app.MapDocumentationEndpoints(connectionString);
app.MapHistoryEndpoints(connectionString);
app.MapMaterialEndpoints(connectionString);
app.MapManufacturerEndpoints(connectionString);
app.MapClientEndpoints(connectionString, storage);
app.MapSavedFilterEndpoints(connectionString);
app.MapAttachmentEndpoints(connectionString, storage);
app.MapConfigEndpoints(storage);
app.MapSettingsEndpoints(connectionString, storage, appSettingsPath);
app.MapLogEndpoints(logRoot);
app.MapNotificationEndpoints(connectionString);

// Bez rejestracji w DI (patrz komentarz w ScheduledBackupService.cs) — uruchamiane ręcznie,
// z tokenem powiązanym z zamykaniem aplikacji, żeby pętla zatrzymała się razem z serwerem.
_ = new ScheduledBackupService(connectionString, storage, backupRoot, app.Logger)
    .RunAsync(app.Lifetime.ApplicationStopping);
_ = new DiskSpaceMonitorService(connectionString, storage, app.Logger)
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

// Jeśli tabela projects jest pusta (świeża instalacja — po EnsureDefaultAdminAsync wyżej
// admin na pewno już istnieje), zasiewa jeden przykładowy projekt (złożenie + dwie części
// w różnych statusach, tworzące prosty BOM) do zwiedzenia, plus powiadomienie do admina
// przypominające o wyczyszczeniu bazy przed prawdziwą pracą (Ustawienia -> Magazyn
// plików -> Danger zone). Wprost do bazy (bez przechodzenia przez HTTP endpointy), tym
// samym stylem co EnsureDefaultAdminAsync powyżej -- bez symulowania pełnej historii
// statusów, tylko końcowe stany kolumn (panel Historii i tak pokaże zdarzenie
// "utworzono" z created_at/created_by).
//
// "system_state.sample_project_seeded" to bramka, nie sam COUNT(*) FROM projects -- "Wyczyść
// bazę -> Projekty" (Danger zone) też zeruje tabelę projects, więc goły COUNT by ponownie
// zasiał przykładowy projekt po KAŻDYM restarcie procesu po takim czyszczeniu, mimo że admin
// świadomie chciał mieć pustą bazę. Flaga jest ustawiana raz i na zawsze (albo "zasiałem",
// albo "zastałem już prawdziwe dane") i Danger zone jej nie dotyka.
static async Task EnsureSampleProjectAsync(string connectionString, ILogger logger)
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    await using (var seededCmd = new NpgsqlCommand(
        "SELECT sample_project_seeded FROM system_state WHERE id = true;", conn))
    {
        if (await seededCmd.ExecuteScalarAsync() is true)
            return;
    }

    await using (var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM projects;", conn))
    {
        var count = (long)(await countCmd.ExecuteScalarAsync())!;
        if (count > 0)
        {
            // Instalacja aktualizowana z wersji sprzed tej funkcji (albo z innego powodu ma
            // już projekty) -- nie zasiewaj, ale zapamiętaj to rozstrzygnięcie, żeby kolejne
            // starty nie musiały już liczyć wierszy w projects za każdym razem.
            await SetSampleProjectSeededAsync(conn, null);
            return;
        }
    }

    Guid adminId;
    await using (var adminCmd = new NpgsqlCommand("SELECT id FROM users WHERE role = 'admin' ORDER BY username LIMIT 1;", conn))
    {
        var result = await adminCmd.ExecuteScalarAsync();
        if (result is null)
            return; // nie powinno się zdarzyć (EnsureDefaultAdminAsync już się wykonał), ale bez admina nie ma kogo zrobić właścicielem/odbiorcą
        adminId = (Guid)result;
    }

    await using var tx = await conn.BeginTransactionAsync();
    // Bramka "projects jest puste" wyżej czytana jest PRZED tą transakcją — dwa procesy
    // startujące niemal jednocześnie na tej samej, świeżo pustej bazie (np. chwilowe
    // nakładanie się dwóch instancji przy wdrożeniu z wieloma replikami) mogłyby oba
    // przejść tę bramkę i oba spróbować wstawić projekt o tej samej, unikalnej nazwie —
    // ten catch łapie taki wyścig zamiast wywalać cały start aplikacji nieobsłużonym
    // wyjątkiem (drugi proces po prostu rezygnuje, tak jakby zobaczył niepusty "projects").
    try
    {
        const string projectName = "Przykładowy projekt";
        Guid projectId;
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO projects (name, description, is_sample)
            VALUES (@name, @description, true)
            RETURNING id;
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("name", projectName);
            cmd.Parameters.AddWithValue("description",
                "Projekt demonstracyjny wygenerowany automatycznie przy pierwszym uruchomieniu. " +
                "Usuń go przed rozpoczęciem prawdziwej pracy (Ustawienia -> Magazyn plików -> Danger zone).");
            projectId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        const string insertItemSql = """
            INSERT INTO items (
                id, project_id, item_type, file_name, properties, item_number, status,
                revision_number, root_position, owner_id, owner_locked, created_by, show_in_tree
            ) VALUES (
                @id, @projectId, @itemType, @fileName, @props::jsonb,
                nextval('item_number_seq'), @status, @revisionNumber, @rootPosition,
                @ownerId, @ownerLocked, @createdBy, @showInTree
            );
            """;

        var assemblyId = Guid.NewGuid();
        await using (var cmd = new NpgsqlCommand(insertItemSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", assemblyId);
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("itemType", "assembly");
            cmd.Parameters.AddWithValue("fileName", "Złożenie przykładowe");
            cmd.Parameters.AddWithValue("props", "{}");
            cmd.Parameters.AddWithValue("status", "w_pracy");
            cmd.Parameters.AddWithValue("revisionNumber", 1);
            cmd.Parameters.AddWithValue("rootPosition", 1);
            cmd.Parameters.AddWithValue("ownerId", adminId);
            cmd.Parameters.AddWithValue("ownerLocked", true);
            cmd.Parameters.AddWithValue("createdBy", adminId);
            cmd.Parameters.AddWithValue("showInTree", true);
            await cmd.ExecuteNonQueryAsync();
        }

        var boltId = Guid.NewGuid();
        await using (var cmd = new NpgsqlCommand(insertItemSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", boltId);
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("itemType", "part");
            cmd.Parameters.AddWithValue("fileName", "Śruba M6x20");
            cmd.Parameters.AddWithValue("props", """{"rodzaj":"Normalia"}""");
            cmd.Parameters.AddWithValue("status", "sprawdzany");
            cmd.Parameters.AddWithValue("revisionNumber", 1);
            cmd.Parameters.AddWithValue("rootPosition", 1);
            cmd.Parameters.AddWithValue("ownerId", adminId);
            cmd.Parameters.AddWithValue("ownerLocked", true);
            cmd.Parameters.AddWithValue("createdBy", adminId);
            cmd.Parameters.AddWithValue("showInTree", false);
            await cmd.ExecuteNonQueryAsync();
        }

        var bodyId = Guid.NewGuid();
        await using (var cmd = new NpgsqlCommand(insertItemSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("id", bodyId);
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("itemType", "part");
            cmd.Parameters.AddWithValue("fileName", "Korpus");
            cmd.Parameters.AddWithValue("props", """{"rodzaj":"Wykonywana"}""");
            cmd.Parameters.AddWithValue("status", "wydany");
            cmd.Parameters.AddWithValue("revisionNumber", 2);
            cmd.Parameters.AddWithValue("rootPosition", 1);
            // "Wydany" element jest zawsze zwolniony -- zob. PATCH /api/items/{id}/status.
            cmd.Parameters.AddWithValue("ownerId", DBNull.Value);
            cmd.Parameters.AddWithValue("ownerLocked", false);
            cmd.Parameters.AddWithValue("createdBy", adminId);
            cmd.Parameters.AddWithValue("showInTree", false);
            await cmd.ExecuteNonQueryAsync();
        }

        const string insertRelationSql = """
            INSERT INTO item_relations (parent_id, child_id, quantity, position)
            VALUES (@parentId, @childId, @quantity, @position);
            """;
        await using (var cmd = new NpgsqlCommand(insertRelationSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("parentId", assemblyId);
            cmd.Parameters.AddWithValue("childId", boltId);
            cmd.Parameters.AddWithValue("quantity", 4);
            cmd.Parameters.AddWithValue("position", 1);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(insertRelationSql, conn, tx))
        {
            cmd.Parameters.AddWithValue("parentId", assemblyId);
            cmd.Parameters.AddWithValue("childId", bodyId);
            cmd.Parameters.AddWithValue("quantity", 1);
            cmd.Parameters.AddWithValue("position", 2);
            await cmd.ExecuteNonQueryAsync();
        }

        int tagId;
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO tags (name) VALUES ('demo') ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name RETURNING id;", conn, tx))
        {
            tagId = (int)(await cmd.ExecuteScalarAsync())!;
        }
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO item_tags (item_id, tag_id) VALUES (@itemId, @tagId);", conn, tx))
        {
            cmd.Parameters.AddWithValue("itemId", assemblyId);
            cmd.Parameters.AddWithValue("tagId", tagId);
            await cmd.ExecuteNonQueryAsync();
        }

        await Notifications.NotifyAsync(conn, logger, adminId, "sample_project", new { projectName }, projectId: projectId, tx: tx);
        await SetSampleProjectSeededAsync(conn, tx);

        await tx.CommitAsync();

        // logger (nie Console.WriteLine) -- pod Windows Service (packaging/windows/EasyPDM.iss)
        // proces nie ma podłączonej konsoli, więc Console.WriteLine nigdzie by nie trafiło;
        // logger jest podpięty do FileLoggerProvider (Ustawienia -> Logi) niezależnie od trybu
        // uruchomienia.
        logger.LogInformation("Utworzono przykładowy projekt startowy — usuń go przed rozpoczęciem prawdziwej pracy (Ustawienia -> Magazyn plików -> Danger zone).");
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        // Inny proces wygrał wyścig i już wstawił projekt o tej nazwie — "tx" (nieukończone)
        // zostanie automatycznie wycofane przy dispose. Ten drugi proces sam ustawi flagę we
        // WŁASNEJ transakcji, ale ustawiamy ją też tutaj (poza już martwym "tx", stąd bez
        // parametru transakcji) na wszelki wypadek — no-op, jeśli już jest ustawiona.
        await SetSampleProjectSeededAsync(conn, null);
    }
}

static async Task SetSampleProjectSeededAsync(NpgsqlConnection conn, NpgsqlTransaction? tx)
{
    const string sql = """
        INSERT INTO system_state (id, sample_project_seeded) VALUES (true, true)
        ON CONFLICT (id) DO UPDATE SET sample_project_seeded = true;
        """;
    await using var cmd = new NpgsqlCommand(sql, conn, tx);
    await cmd.ExecuteNonQueryAsync();
}

// Odsłania niejawną klasę Program (top-level statements) jako publiczną — potrzebne, żeby
// EasyPDM.Api.Tests mogło użyć WebApplicationFactory<Program> z osobnego assembly.
public partial class Program;
