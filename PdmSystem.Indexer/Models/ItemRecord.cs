namespace PdmSystem.Indexer.Models;

/// <summary>
/// Reprezentuje jeden zeskanowany plik z dysku, gotowy do zapisu w bazie.
/// </summary>
public class ItemRecord
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string FileType { get; init; }
    public required string FileHash { get; init; }
    public required long FileSize { get; init; }
    public required DateTime ModifiedAt { get; init; }

    /// <summary>
    /// Właściwości niestandardowe odczytane z pliku (np. "Materiał" -> "Stal S235").
    /// Trafiają bezpośrednio do kolumny JSONB w bazie.
    /// </summary>
    public Dictionary<string, string> Properties { get; init; } = new();
}
