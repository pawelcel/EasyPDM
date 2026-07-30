using System.IO.Compression;
using System.Xml.Linq;

namespace PdmSystem.Indexer.Services.Adapters;

/// <summary>
/// Odczytuje właściwości dokumentu z pliku FreeCAD (.FCStd).
///
/// .FCStd to archiwum ZIP zawierające m.in. plik "Document.xml" z opisem dokumentu
/// w formacie XML — dużo prostszym niż binarny OLE Property Set używany przez SolidWorks.
/// Ten adapter czyta właściwości zdefiniowane na poziomie dokumentu (element &lt;Properties&gt;
/// bezpośrednio pod korzeniem &lt;Document&gt;), czyli standardowe pola typu Comment, Company,
/// LastModifiedBy, License itd. oraz wszelkie właściwości dodane przez użytkownika na tym poziomie.
///
/// Właściwości przypisane do pojedynczych obiektów wewnątrz dokumentu (np. do konkretnej bryły)
/// nie są tu jeszcze obsługiwane — to naturalne rozszerzenie na później, jeśli będzie potrzebne.
/// </summary>
public class FreeCadAdapter : ICadAdapter
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".fcstd" };

    public Dictionary<string, string> ReadProperties(string filePath)
    {
        var result = new Dictionary<string, string>();

        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var docEntry = archive.GetEntry("Document.xml");
            if (docEntry == null)
                return result;

            using var stream = docEntry.Open();
            var doc = XDocument.Load(stream);

            // Struktura: <Document> <Properties> <Property name="Comment" type="App::PropertyString">
            //                                        <String value="..." />
            //                                     </Property> ... </Properties> </Document>
            var propsNode = doc.Root?.Element("Properties");
            if (propsNode == null)
                return result;

            foreach (var propNode in propsNode.Elements("Property"))
            {
                string? name = propNode.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(name))
                    continue;

                string? value = ExtractValue(propNode);
                if (value != null)
                    result[name] = value;
            }
        }
        catch
        {
            // Plik nie jest poprawnym archiwum ZIP, jest uszkodzony albo ma nietypową
            // strukturę — pomijamy właściwości, reszta skanu (hash, rozmiar) się powiedzie.
        }

        return result;
    }

    /// <summary>
    /// Właściwość w Document.xml ma jeden element potomny, którego nazwa zależy od typu
    /// (np. &lt;String value="..."/&gt;, &lt;Float value="..."/&gt;, &lt;Bool value="..."/&gt;,
    /// &lt;Integer value="..."/&gt;). Bierzemy po prostu atrybut "value" pierwszego dziecka.
    /// </summary>
    private static string? ExtractValue(XElement propNode)
    {
        var valueNode = propNode.Elements().FirstOrDefault();
        return valueNode?.Attribute("value")?.Value;
    }
}
