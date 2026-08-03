using System.Globalization;

// Podgląd logów programu w Ustawieniach (tylko administrator) — czyta pliki zapisywane przez
// FileLoggerProvider (logs/pdmsystem-yyyy-MM-dd.log). Wyłącznie do odczytu.
static class LogEndpoints
{
    public static void MapLogEndpoints(this WebApplication app, string logRoot)
    {
        // GET /api/settings/logs — lista dostępnych dni (data + rozmiar pliku), najnowsze pierwsze.
        app.MapGet("/api/settings/logs", (HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            if (!Directory.Exists(logRoot))
                return Results.Ok(Array.Empty<object>());

            var files = Directory.GetFiles(logRoot, "pdmsystem-*.log")
                .Select(f => new
                {
                    date = Path.GetFileNameWithoutExtension(f).Replace("pdmsystem-", ""),
                    sizeBytes = new FileInfo(f).Length
                })
                .OrderByDescending(f => f.date, StringComparer.Ordinal)
                .ToList();

            return Results.Ok(files);
        });

        // GET /api/settings/logs/{date}?lines=1000 — ostatnie N wierszy dziennika z danego dnia
        // (domyślnie 1000, maks. 5000 — żeby duży plik nie trafiał w całości do przeglądarki).
        app.MapGet("/api/settings/logs/{date}", (string date, int? lines, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            if (!IsValidDate(date))
                return Results.BadRequest("Nieprawidłowy format daty — oczekiwano yyyy-MM-dd.");

            var path = Path.Combine(logRoot, $"pdmsystem-{date}.log");
            if (!File.Exists(path))
                return Results.NotFound();

            var take = Math.Clamp(lines ?? 1000, 1, 5000);
            var all = File.ReadAllLines(path);
            var tail = all.Length > take ? all[^take..] : all;

            return Results.Ok(new { lines = tail, totalLines = all.Length, truncated = all.Length > take });
        });

        // GET /api/settings/logs/{date}/download — pełny plik dziennika do pobrania.
        app.MapGet("/api/settings/logs/{date}/download", (string date, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            if (!IsValidDate(date))
                return Results.BadRequest("Nieprawidłowy format daty — oczekiwano yyyy-MM-dd.");

            var path = Path.Combine(logRoot, $"pdmsystem-{date}.log");
            if (!File.Exists(path))
                return Results.NotFound();

            return Results.File(path, "text/plain; charset=utf-8", $"pdmsystem-{date}.log");
        });
    }

    // "date" trafia bezpośrednio do ścieżki pliku — ścisła walidacja formatu (tylko cyfry i
    // myślniki w dokładnie tym układzie) wyklucza przy okazji próby przejścia po katalogach.
    private static bool IsValidDate(string date) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);
}
