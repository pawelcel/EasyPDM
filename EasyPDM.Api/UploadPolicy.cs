using System.Text.RegularExpressions;
using Npgsql;

// Jakie pliki serwer przyjmuje i jak duże: Ustawienia -> Magazyn plików -> Dozwolone typy plików
// (tabela upload_rules, migracja 079). Każdy endpoint, który zapisuje przesłany plik, pyta to
// jedno miejsce.
//
//   pusta lista            każdy typ i rozmiar, jak dotąd
//   lista z rozszerzeniami tylko te typy (415 dla innych i dla pliku bez rozszerzenia), każdy ze
//                          swoim limitem MB (413 ponad limit; brak limitu = dowolny rozmiar)
//
// Rozmiar sprawdzany jest dwa razy: dla każdego pliku po odczytaniu formularza i dla całego ciała
// żądania (Kestrel przestaje czytać ponad największy limit z listy), więc za duży plik nie jest
// najpierw w całości zapisywany do pliku tymczasowego. Gdy któryś typ nie ma limitu, limitu ciała
// nie ma też.
//
// Odtwarzanie kopii zapasowej (POST /api/settings/restore) jest wyłączone z limitów: tylko dla
// administratora, zawsze ZIP i zawiera cały magazyn plików, więc bywa dużo większe niż plik CAD.
//
// Reguły są w pamięci, odświeżane po zapisie w ustawieniach (Invalidate) i co najwyżej po
// CacheLifetime -- tyle trwa, zanim kopia zapasowa odtworzona z innymi regułami zacznie działać.
public sealed class UploadPolicy
{
    // Zapas na granice multipart i małe pola tekstowe wysyłane obok pliku.
    private const long RequestOverheadBytes = 16L * 1024 * 1024;
    private const long Megabyte = 1024L * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly Regex ExtensionPattern = new(@"^\.[a-z0-9][a-z0-9_-]{0,15}$", RegexOptions.Compiled);

    // Rozszerzenia wysyłane przez integracje CAD (pliki modeli i rysunków, STEP, PDF, obraz
    // podglądu) -- podpowiedź w ustawieniach, żeby lista dozwolonych typów nie odcięła żadnej z
    // nich. ".sc" to projekt SolidCraft (pomocnik wysyła go jako plik CAD elementu).
    public static readonly string[] CadMacroExtensions =
        [".fcstd", ".sldprt", ".sldasm", ".slddrw", ".ipt", ".iam", ".idw", ".dwg", ".sc", ".step", ".stp", ".pdf", ".png"];

    private readonly string _connectionString;
    private Snapshot? _snapshot;

    private sealed record Snapshot(IReadOnlyDictionary<string, int?> Rules, DateTime LoadedAt);

    public UploadPolicy(string connectionString) => _connectionString = connectionString;

    // Rozszerzenie (z kropką, małymi literami) -> limit w MB albo null (bez limitu). Pusta = bez reguł.
    public IReadOnlyDictionary<string, int?> Rules
    {
        get
        {
            var snapshot = _snapshot;
            if (snapshot is null || DateTime.UtcNow - snapshot.LoadedAt > CacheLifetime)
            {
                snapshot = new Snapshot(Load(), DateTime.UtcNow);
                _snapshot = snapshot;
            }
            return snapshot.Rules;
        }
    }

    public void Invalidate() => _snapshot = null;

    private Dictionary<string, int?> Load()
    {
        var rules = new Dictionary<string, int?>(StringComparer.Ordinal);
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        using var cmd = new NpgsqlCommand("SELECT extension, max_size_mb FROM upload_rules;", conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rules[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetInt32(1);
        return rules;
    }

    // " STEP " / "step" / ".Step" -> ".step"; null, gdy to nie wygląda na rozszerzenie pliku.
    public static string? Normalize(string? raw)
    {
        var ext = (raw ?? "").Trim().ToLowerInvariant();
        if (ext.Length > 0 && ext[0] != '.')
            ext = "." + ext;
        return ExtensionPattern.IsMatch(ext) ? ext : null;
    }

    // Limit całego ciała żądania; null = bez limitu (brak reguł albo typ bez limitu).
    public long? MaxRequestBodyBytes
    {
        get
        {
            var rules = Rules;
            if (rules.Count == 0 || rules.Values.Any(v => v is null))
                return null;
            return rules.Values.Max()!.Value * Megabyte + RequestOverheadBytes;
        }
    }

    // null = plik można zapisać; w przeciwnym razie odpowiedź do odesłania zamiast tego.
    public IResult? Check(IFormFile file)
    {
        var rules = Rules;
        if (rules.Count == 0)
            return null;

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !rules.TryGetValue(extension, out var maxMb))
        {
            return Results.Text(
                $"File type '{(string.IsNullOrEmpty(extension) ? "(none)" : extension)}' is not accepted. " +
                $"Accepted: {string.Join(", ", rules.Keys.Order(StringComparer.Ordinal))}.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        if (maxMb is int mb && file.Length > mb * Megabyte)
            return Results.Text($"A {extension} file can be at most {mb} MB.", statusCode: StatusCodes.Status413PayloadTooLarge);

        return null;
    }

    public IResult RequestTooLarge()
    {
        var largest = Rules.Values.Max();
        return Results.Text($"The upload is larger than the largest allowed file ({largest} MB).",
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    public static bool IsExempt(PathString path) => path.StartsWithSegments("/api/settings/restore");
}
