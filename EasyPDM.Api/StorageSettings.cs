// Trzyma AKTUALNĄ ścieżkę magazynu plików (StorageRoot) — mutowalną, żeby dało się ją
// zmienić w trakcie działania serwera (Ustawienia -> Magazyn plików) bez restartu API.
// Endpointy operujące na plikach dostają tę instancję zamiast sztywnego stringa i czytają
// .Path przy każdym użyciu, więc zmiana lokalizacji obowiązuje natychmiast wszędzie.
class StorageSettings
{
    private readonly object _lock = new();
    private string _path;

    public StorageSettings(string initialPath)
    {
        _path = initialPath;
    }

    public string Path
    {
        get { lock (_lock) return _path; }
        set { lock (_lock) _path = value; }
    }
}
