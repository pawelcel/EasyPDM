using System.Collections.Concurrent;
using System.Globalization;

// Własny, prosty provider logowania do pliku (bez dodatkowego pakietu NuGet — spójnie z resztą
// backendu, np. własna implementacja PBKDF2 w PasswordHasher.cs zamiast biblioteki). Podpięty
// pod standardowy ILoggerFactory w Program.cs, więc automatycznie respektuje te same reguły
// poziomu logowania co konsola (sekcja "Logging" w appsettings.json) i przechwytuje WSZYSTKO —
// nasze własne komunikaty (np. ScheduledBackupService) oraz wewnętrzne logi ASP.NET Core.
// Jeden plik dziennie (logs/pdmsystem-yyyy-MM-dd.log), widoczne w Ustawienia -> Logi (tylko
// administrator). Zapis pod prostym lockiem — skala tej aplikacji nie uzasadnia kolejki w tle.
class FileLoggerProvider : ILoggerProvider
{
    private const int RetentionDays = 30;

    private readonly string logDirectory;
    private readonly object writeLock = new();
    private readonly ConcurrentDictionary<string, FileLogger> loggers = new();

    public FileLoggerProvider(string logDirectory)
    {
        this.logDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
        PruneOldLogs();
    }

    public ILogger CreateLogger(string categoryName) =>
        loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));

    internal void Write(string line)
    {
        var path = Path.Combine(logDirectory, $"pdmsystem-{DateTime.Now:yyyy-MM-dd}.log");
        lock (writeLock)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private void PruneOldLogs()
    {
        var cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
        foreach (var file in Directory.GetFiles(logDirectory, "pdmsystem-*.log"))
        {
            var datePart = Path.GetFileNameWithoutExtension(file).Replace("pdmsystem-", "");
            if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date < cutoff)
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    public void Dispose()
    {
    }
}

class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    // Filtrowanie wg poziomu/kategorii (sekcja "Logging" w appsettings.json) jest już
    // zastosowane przez ILoggerFactory przed dotarciem tutaj — nie duplikujemy go.
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var levelCode = logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{levelCode}] {categoryName}: {formatter(state, exception)}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        provider.Write(line);
    }
}
