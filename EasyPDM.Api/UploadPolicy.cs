// Jakie pliki serwer przyjmuje i jak duże. Każdy endpoint, który zapisuje przesłany plik, pyta
// to jedno miejsce. Oba limity są WYŁĄCZONE, dopóki administrator ich nie ustawi — bez
// ustawień serwer przyjmuje każdy typ i rozmiar, jak dotąd:
//
//   Uploads:AllowedExtensions  lista rozszerzeń rozdzielona przecinkami, np. ".sldprt,.step,.pdf"
//                              (wielkość liter bez znaczenia, kropka opcjonalna); plik bez
//                              rozszerzenia jest wtedy odrzucany (415)
//   Uploads:MaxFileSizeMb      największy pojedynczy plik w MB (413)
//
// Rozmiar sprawdzany jest dwa razy: dla każdego pliku po odczytaniu formularza i dla całego ciała
// żądania (Kestrel przestaje czytać, gdy ciało przekroczy limit), więc za duży plik nie jest
// najpierw w całości zapisywany do pliku tymczasowego.
//
// Odtwarzanie kopii zapasowej (POST /api/settings/restore) jest wyłączone z limitów: tylko dla
// administratora, zawsze ZIP i zawiera cały magazyn plików, więc bywa dużo większe niż plik CAD.
public sealed class UploadPolicy
{
    // Zapas na granice multipart i małe pola tekstowe wysyłane obok pliku.
    private const long RequestOverheadBytes = 16L * 1024 * 1024;

    // null = każde rozszerzenie.
    public IReadOnlySet<string>? AllowedExtensions { get; }
    // null = bez limitu.
    public long? MaxFileSizeBytes { get; }
    public long? MaxRequestBodyBytes => MaxFileSizeBytes + RequestOverheadBytes;

    public UploadPolicy(IEnumerable<string>? allowedExtensions, long? maxFileSizeMb)
    {
        if (maxFileSizeMb is <= 0)
            throw new InvalidOperationException("Uploads:MaxFileSizeMb must be greater than 0.");

        if (allowedExtensions is not null)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in allowedExtensions)
            {
                var ext = raw.Trim();
                if (ext.Length == 0)
                    continue;
                set.Add(ext.StartsWith('.') ? ext : "." + ext);
            }
            if (set.Count == 0)
                throw new InvalidOperationException("Uploads:AllowedExtensions is set but empty: no upload could ever be accepted.");
            AllowedExtensions = set;
        }

        MaxFileSizeBytes = maxFileSizeMb * 1024 * 1024;
    }

    public static UploadPolicy FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("Uploads");
        var listText = section["AllowedExtensions"];
        var extensions = listText is null
            ? null
            : listText.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        return new UploadPolicy(extensions, section.GetValue<long?>("MaxFileSizeMb"));
    }

    // null = plik można zapisać; w przeciwnym razie odpowiedź do odesłania zamiast tego.
    public IResult? Check(IFormFile file)
    {
        if (AllowedExtensions is not null)
        {
            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                return Results.Text(
                    $"File type '{(string.IsNullOrEmpty(extension) ? "(none)" : extension)}' is not accepted. " +
                    $"Accepted: {string.Join(", ", AllowedExtensions.Order())}.",
                    statusCode: StatusCodes.Status415UnsupportedMediaType);
            }
        }

        if (file.Length > MaxFileSizeBytes)
            return TooLarge();

        return null;
    }

    public IResult TooLarge() => Results.Text(
        $"File is larger than the {MaxFileSizeBytes / (1024 * 1024)} MB limit.",
        statusCode: StatusCodes.Status413PayloadTooLarge);

    public static bool IsExempt(PathString path) => path.StartsWithSegments("/api/settings/restore");
}
