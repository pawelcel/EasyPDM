using System.Text.RegularExpressions;

// Nazwy plików Windows nie mogą zawierać kilku znaków specjalnych -- ta sama lista i to samo
// zastąpienie podkreśleniem, dawniej reimplementowane niezależnie w każdym makrze CAD
// (SanitizeFilename w EasyPDM.SolidWorks/EasyPDM.Inventor, sanitize_filename w
// EasyPDM.FreeCad -- każde z osobną, rozjeżdżającą się kopią: np. inny tekst zastępczy dla
// pustej nazwy w FreeCAD niż w SolidWorks/Inventor). Jedno miejsce tutaj -- odpowiedzi API
// dorzucają gotowe "sanitizedFileName" obok "fileName".
//
// Fallback dla PUSTEJ nazwy celowo NIE jest tutaj -- to jedyny krok, gdzie ma znaczenie
// język interfejsu konkretnego makra (komunikat "brak nazwy" po polsku/angielsku/niemiecku),
// więc zostaje jako pojedyncza linijka po stronie klienta, używająca jego własnego systemu
// tłumaczeń. Ta funkcja może więc legalnie zwrócić pusty string.
static class FilenameSanitizing
{
    private static readonly Regex BadChars = new(@"[\\/:*?""<>|]", RegexOptions.Compiled);

    public static string Sanitize(string name) => BadChars.Replace(name, "_").Trim();
}
