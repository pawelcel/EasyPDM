// Rewizje w bazie (items.revision_number, item_revision_comments.revision_number) są zawsze
// liczbą całkowitą — litera (A, B, ..., Z, AA, ...) to czysto kosmetyczna konwersja do
// wyświetlenia/nazwy pliku, dawniej reimplementowana niezależnie w każdym kliencie osobno
// (frontend web, makra FreeCAD/SolidWorks/Inventor). Jedno miejsce tutaj — odpowiedzi API
// dorzucają gotowe "revisionLabel" obok "revisionNumber", żeby klienci nie musieli same
// tego przeliczać.
static class RevisionLabeling
{
    public static string? Label(int? n)
    {
        if (n is null) return null;
        return Label(n.Value);
    }

    public static string Label(int n)
    {
        var value = n;
        var label = "";
        while (value > 0)
        {
            var remainder = (value - 1) % 26;
            label = (char)('A' + remainder) + label;
            value = (value - 1) / 26;
        }
        return label.Length > 0 ? label : "A";
    }
}
