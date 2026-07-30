using System.Text;
using OpenMcdf;

namespace PdmSystem.Core.Services.Adapters;

/// <summary>
/// Odczytuje właściwości niestandardowe z plików SolidWorks (.sldprt/.sldasm/.slddrw).
///
/// STATUS: zaparkowane pod przyszłą Fazę integracji z SolidWorks — kod przeniesiony
/// z pierwszej wersji projektu, niezmieniony logicznie, tylko dopasowany do interfejsu
/// ICadAdapter. Wymaga pakietu NuGet OpenMcdf (patrz PdmSystem.Core.csproj).
///
/// Parsuje format OLE Property Set (MS-OLEPS). Obsługuje podstawowe typy: VT_LPSTR,
/// VT_LPWSTR, VT_I2, VT_I4, VT_R8, VT_BOOL, VT_FILETIME. Nie obsługuje VT_VECTOR/VT_BLOB.
/// </summary>
public class SolidWorksAdapter : ICadAdapter
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } =
        new[] { ".sldprt", ".sldasm", ".slddrw" };

    private const string DocSummaryInfoStream = "\u0005DocumentSummaryInformation";

    public Dictionary<string, string> ReadProperties(string filePath)
    {
        var result = new Dictionary<string, string>();

        try
        {
            using var cf = new CompoundFile(filePath);

            if (cf.RootStorage.TryGetStream(DocSummaryInfoStream, out var docSummary))
            {
                ReadPropertySets(docSummary.GetData(), result);
            }
        }
        catch
        {
            // Plik nie jest w formacie OLE Compound, jest uszkodzony albo zablokowany
            // (np. otwarty w SolidWorks) — pomijamy właściwości.
        }

        return result;
    }

    private static void ReadPropertySets(byte[] data, Dictionary<string, string> result)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        br.ReadUInt16();
        br.ReadUInt16();
        br.ReadUInt32();
        br.ReadBytes(16);
        uint numPropertySets = br.ReadUInt32();

        var offsets = new List<long>();
        for (int i = 0; i < numPropertySets; i++)
        {
            br.ReadBytes(16);
            uint offset = br.ReadUInt32();
            offsets.Add(offset);
        }

        foreach (var setOffset in offsets)
            ReadOnePropertySet(data, setOffset, result);
    }

    private static void ReadOnePropertySet(byte[] data, long setOffset, Dictionary<string, string> result)
    {
        using var br = MakeReaderAt(data, setOffset);

        br.ReadUInt32();
        uint numProperties = br.ReadUInt32();

        var idOffsets = new List<(uint id, uint offset)>();
        for (int i = 0; i < numProperties; i++)
        {
            uint id = br.ReadUInt32();
            uint offset = br.ReadUInt32();
            idOffsets.Add((id, offset));
        }

        var names = new Dictionary<uint, string>();
        foreach (var (id, offset) in idOffsets)
        {
            if (id == 0)
                names = ReadDictionary(data, setOffset + offset);
        }

        foreach (var (id, offset) in idOffsets)
        {
            if (id == 0) continue;

            var value = ReadPropertyValue(data, setOffset + offset);
            if (value == null) continue;

            string? name = names.TryGetValue(id, out var n) ? n : KnownName(id);
            if (name != null)
                result[name] = value;
        }
    }

    private static Dictionary<uint, string> ReadDictionary(byte[] data, long offset)
    {
        var names = new Dictionary<uint, string>();
        using var br = MakeReaderAt(data, offset);

        uint count = br.ReadUInt32();
        for (int i = 0; i < count; i++)
        {
            uint id = br.ReadUInt32();
            uint length = br.ReadUInt32();
            var bytes = br.ReadBytes((int)length);
            names[id] = Encoding.ASCII.GetString(bytes).TrimEnd('\0');

            int padding = (int)((4 - (length % 4)) % 4);
            if (padding > 0) br.ReadBytes(padding);
        }
        return names;
    }

    private static string? ReadPropertyValue(byte[] data, long offset)
    {
        using var br = MakeReaderAt(data, offset);
        uint type = br.ReadUInt32();

        return type switch
        {
            2 => br.ReadInt16().ToString(),
            3 => br.ReadInt32().ToString(),
            5 => br.ReadDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            11 => br.ReadInt16() != 0 ? "true" : "false",
            30 => ReadLpstr(br),
            31 => ReadLpwstr(br),
            64 => ReadFileTime(br),
            _ => null
        };
    }

    private static string ReadLpstr(BinaryReader br)
    {
        uint len = br.ReadUInt32();
        var bytes = br.ReadBytes((int)len);
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    private static string ReadLpwstr(BinaryReader br)
    {
        uint len = br.ReadUInt32();
        var bytes = br.ReadBytes((int)len * 2);
        return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
    }

    private static string? ReadFileTime(BinaryReader br)
    {
        long fileTime = br.ReadInt64();
        try { return DateTime.FromFileTimeUtc(fileTime).ToString("O"); }
        catch { return null; }
    }

    private static BinaryReader MakeReaderAt(byte[] data, long offset)
    {
        var ms = new MemoryStream(data);
        ms.Seek(offset, SeekOrigin.Begin);
        return new BinaryReader(ms);
    }

    private static string? KnownName(uint propertyId) => propertyId switch
    {
        2 => "Title",
        3 => "Subject",
        4 => "Author",
        5 => "Keywords",
        6 => "Comments",
        _ => null
    };
}
