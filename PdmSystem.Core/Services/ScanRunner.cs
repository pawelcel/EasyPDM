using PdmSystem.Core.Services.Adapters;

namespace PdmSystem.Core.Services;

public class ScanResult
{
    public int Processed { get; set; }
    public int Errors { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}

/// <summary>
/// Pojedyncze miejsce z logiką "zeskanuj skonfigurowane foldery i zapisz do bazy".
/// Używane zarówno przez PdmSystem.Indexer (ręczne, jednorazowe uruchomienie z konsoli),
/// jak i przez PdmSystem.Api (automatyczne skanowanie w tle + endpoint na żądanie) —
/// żeby te dwa miejsca nie rozjeżdżały się z czasem w dwie osobne implementacje tej samej logiki.
/// </summary>
public class ScanRunner
{
    private readonly FileScanner _scanner;
    private readonly DatabaseWriter _writer;
    private readonly Action<string> _log;

    public ScanRunner(
        IEnumerable<string> extensions,
        IEnumerable<ICadAdapter> adapters,
        string connectionString,
        Action<string>? log = null)
    {
        _scanner = new FileScanner(extensions, adapters);
        _writer = new DatabaseWriter(connectionString);
        _log = log ?? (_ => { });
    }

    public async Task<ScanResult> RunAsync(IEnumerable<string> folders)
    {
        int processed = 0;
        int errors = 0;

        foreach (var folder in folders)
        {
            _log($"Skanuję: {folder}");

            foreach (var record in _scanner.ScanFolder(folder))
            {
                try
                {
                    await _writer.UpsertItemAsync(record);
                    processed++;
                    _log($"  [ok] {record.FileName}  ({record.Properties.Count} właściwości)");
                }
                catch (Exception ex)
                {
                    errors++;
                    _log($"  [błąd zapisu] {record.FileName}: {ex.Message}");
                }
            }
        }

        return new ScanResult
        {
            Processed = processed,
            Errors = errors,
            CompletedAtUtc = DateTime.UtcNow
        };
    }
}
