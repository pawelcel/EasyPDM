namespace PdmSystem.Indexer.Services.Adapters;

/// <summary>
/// Wspólny kontrakt dla czytnika właściwości danego formatu CAD.
/// Dodanie obsługi kolejnego programu CAD = nowa klasa implementująca ten interfejs,
/// zarejestrowana w Program.cs. Reszta systemu (skaner, baza danych) nie wymaga zmian.
/// </summary>
public interface ICadAdapter
{
    /// <summary>Rozszerzenia plików obsługiwane przez ten adapter, np. [".fcstd"].</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <summary>
    /// Odczytuje właściwości/metadane z pliku. W razie problemu (plik uszkodzony,
    /// zablokowany, nietypowa struktura) powinien zwrócić pusty słownik, a nie rzucać wyjątkiem —
    /// błąd pojedynczego pliku nie może przerywać całego skanu.
    /// </summary>
    Dictionary<string, string> ReadProperties(string filePath);
}
