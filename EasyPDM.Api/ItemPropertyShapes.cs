using System.Text.Json;

// Kształt właściwości, z których korzysta sam program (a nie tylko je wyświetla): obróbki
// (properties.processes) i tłumaczenia nazwy (properties.nameTexts). Czyta je eksport CSV,
// podpowiedzi nazw i — w przyszłości — makro wypełniające tabelkę rysunkową, więc źle
// zbudowana wartość (np. napis zamiast listy, languageId jako tekst) psuła je po cichu. Pozostałe
// klucze to wolny tekst i zostają bez sprawdzania.
static class ItemPropertyShapes
{
    private const int MaxTextLength = 2000;

    // Zwraca opis błędu albo null, gdy kształt jest poprawny (albo kluczy nie ma w ogóle).
    public static string? Validate(JsonElement properties)
    {
        if (properties.ValueKind != JsonValueKind.Object)
            return null;

        if (properties.TryGetProperty("nameTexts", out var nameTexts) && nameTexts.ValueKind != JsonValueKind.Null)
        {
            if (nameTexts.ValueKind != JsonValueKind.Array)
                return "nameTexts must be a list of translations.";
            foreach (var t in nameTexts.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object || !IsInt(t, "languageId") || !IsText(t, "name", required: true))
                    return "Every name translation needs a languageId (number) and a non-empty name.";
            }
        }

        if (properties.TryGetProperty("processes", out var processes) && processes.ValueKind != JsonValueKind.Null)
        {
            if (processes.ValueKind != JsonValueKind.Array)
                return "processes must be a list of processes.";
            foreach (var p in processes.EnumerateArray())
            {
                // Zwykły napis to zapis sprzed wariantów obróbek — nadal czytany jako nazwa.
                if (p.ValueKind == JsonValueKind.String)
                    continue;
                if (p.ValueKind != JsonValueKind.Object
                    || !IsText(p, "name", required: true)
                    || !IsOptionalText(p, "description")
                    || (p.TryGetProperty("id", out var pid) && pid.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
                    return "Every process needs a name; description and id must be text and a number, or absent.";
                if (p.TryGetProperty("texts", out var texts) && texts.ValueKind != JsonValueKind.Null)
                {
                    if (texts.ValueKind != JsonValueKind.Array)
                        return "Process translations (texts) must be a list.";
                    foreach (var t in texts.EnumerateArray())
                    {
                        if (t.ValueKind != JsonValueKind.Object || !IsInt(t, "languageId")
                            || !IsText(t, "name", required: true) || !IsOptionalText(t, "description"))
                            return "Every process translation needs a languageId (number) and a non-empty name.";
                    }
                }
            }
        }

        return null;
    }

    private static bool IsInt(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out _);

    private static bool IsText(JsonElement obj, string key, bool required) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
        && (!required || !string.IsNullOrWhiteSpace(v.GetString())) && v.GetString()!.Length <= MaxTextLength;

    private static bool IsOptionalText(JsonElement obj, string key) =>
        !obj.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null
        || (v.ValueKind == JsonValueKind.String && v.GetString()!.Length <= MaxTextLength);
}
