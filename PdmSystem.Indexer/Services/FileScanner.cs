using System.Security.Cryptography;
using PdmSystem.Indexer.Models;
using PdmSystem.Indexer.Services.Adapters;

namespace PdmSystem.Indexer.Services;

public class FileScanner
{
    private readonly HashSet<string> _extensions;
    private readonly Dictionary<string, ICadAdapter> _adapterByExtension;

    /// <param name="extensions">Wszystkie rozszerzenia do zeskanowania (mogą wykraczać poza te obsługiwane przez adaptery — np. .pdf, .step, dla których na razie zbieramy tylko metadane pliku).</param>
    /// <param name="adapters">Zarejestrowane adaptery CAD. Dodanie nowego programu CAD = nowy wpis na tej liście w Program.cs.</param>
    public FileScanner(IEnumerable<string> extensions, IEnumerable<ICadAdapter> adapters)
    {
        _extensions = extensions.Select(e => e.ToLowerInvariant()).ToHashSet();

        _adapterByExtension = new Dictionary<string, ICadAdapter>();
        foreach (var adapter in adapters)
        {
            foreach (var ext in adapter.SupportedExtensions)
                _adapterByExtension[ext.ToLowerInvariant()] = adapter;
        }
    }

    /// <summary>
    /// Rekurencyjnie skanuje folder i zwraca metadane każdego pasującego pliku.
    /// Błędy pojedynczych plików nie przerywają skanu.
    /// </summary>
    public IEnumerable<ItemRecord> ScanFolder(string rootFolder)
    {
        if (!Directory.Exists(rootFolder))
        {
            Console.WriteLine($"  [pomiń] Folder nie istnieje: {rootFolder}");
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(rootFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => _extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [błąd] Nie można odczytać folderu {rootFolder}: {ex.Message}");
            yield break;
        }

        foreach (var filePath in files)
        {
            ItemRecord? record = null;
            try
            {
                record = ScanFile(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [błąd] {filePath}: {ex.Message}");
            }

            if (record != null)
                yield return record;
        }
    }

    private ItemRecord ScanFile(string filePath)
    {
        var info = new FileInfo(filePath);
        string hash = ComputeHash(filePath);
        string extension = info.Extension.ToLowerInvariant();
        string extensionNoDot = extension.TrimStart('.');

        // Wybór adaptera po rozszerzeniu — plik bez zarejestrowanego adaptera
        // nadal trafia do bazy z pustymi properties (same metadane: hash, rozmiar, daty).
        var properties = _adapterByExtension.TryGetValue(extension, out var adapter)
            ? adapter.ReadProperties(filePath)
            : new Dictionary<string, string>();

        return new ItemRecord
        {
            FilePath = info.FullName,
            FileName = info.Name,
            FileType = extensionNoDot,
            FileHash = hash,
            FileSize = info.Length,
            ModifiedAt = info.LastWriteTimeUtc,
            Properties = properties
        };
    }

    private static string ComputeHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes);
    }
}
