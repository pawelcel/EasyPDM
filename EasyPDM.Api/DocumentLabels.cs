using Npgsql;

// Podpisy w dokumentach generowanych przez program, w języku dokumentacji: nagłówki kolumn
// eksportu CSV i pola tabelki rysunkowej. Języki dokumentacji administrator
// zakłada pod dowolnym skrótem, więc wbudowane tłumaczenia są tylko dla PL, EN i DE; każdy podpis
// da się zmienić, a dla innych języków wpisać, w Ustawienia -> Język (tabela document_labels).
// Kolejność rozstrzygania: podpis zapisany dla języka -> wbudowany dla jego skrótu -> angielski.
static class DocumentLabels
{
    // Klucz -> wbudowane tłumaczenia (PL, EN, DE). Kolejność wyznacza kolejność w ustawieniach.
    public static readonly (string Key, string Pl, string En, string De)[] BuiltIn =
    [
        ("csv.no", "L.p.", "No.", "Pos."),
        ("csv.name", "Nazwa", "Name", "Name"),
        ("csv.quantity", "Ilość", "Quantity", "Menge"),
        ("csv.totalQuantity", "Ilość łącznie", "Total quantity", "Gesamtmenge"),
        ("csv.material", "Materiał", "Material", "Material"),
        ("csv.standard", "Norma", "Standard", "Norm"),
        ("csv.manufacturer", "Producent", "Manufacturer", "Hersteller"),
        ("csv.orderNumber1", "Numer zamówieniowy 1", "Order number 1", "Bestellnummer 1"),
        ("csv.orderNumber2", "Numer zamówieniowy 2", "Order number 2", "Bestellnummer 2"),
        ("csv.processes", "Obróbka", "Processes", "Bearbeitung"),
        // Drawing title block (GET /api/items/{id}/title-block, filled in by the CAD macros).
        ("titleblock.drawnBy", "Narysował", "Drawn by", "Gezeichnet"),
        ("titleblock.approvedBy", "Zatwierdził", "Approved by", "Freigegeben"),
        ("titleblock.date", "Data", "Date", "Datum"),
        ("titleblock.material", "Materiał", "Material", "Werkstoff"),
        ("titleblock.scale", "Skala", "Scale", "Maßstab"),
        ("titleblock.sheet", "Arkusz", "Sheet", "Blatt"),
        ("titleblock.mass", "Masa", "Mass", "Masse"),
        ("titleblock.company", "Firma", "Company", "Firma"),
        ("titleblock.name", "Nazwa", "Name", "Benennung"),
        ("titleblock.drawingNumber", "Numer rysunku", "Drawing number", "Zeichnungsnummer"),
        ("titleblock.revision", "Rewizja", "Revision", "Revision"),
        ("titleblock.format", "Format", "Format", "Format"),
        ("titleblock.processNo", "Lp.", "No.", "Pos."),
        ("titleblock.process", "Obróbka", "Process", "Bearbeitung"),
        ("titleblock.processDescription", "Opis", "Description", "Beschreibung"),
        ("titleblock.revisionLetter", "Rew.", "Rev.", "Rev."),
        ("titleblock.revisionDate", "Data", "Date", "Datum"),
        ("titleblock.revisionDescription", "Opis zmiany", "Change description", "Änderung"),
        ("titleblock.revisionBy", "Kto", "By", "Name"),
    ];

    public static bool IsKnownKey(string key) => BuiltIn.Any(b => b.Key == key);

    public static string Default(string key, string? languageCode)
    {
        var b = BuiltIn.First(x => x.Key == key);
        return languageCode?.ToUpperInvariant() switch
        {
            "PL" => b.Pl,
            "DE" => b.De,
            _ => b.En,
        };
    }

    // Wszystkie podpisy dla języka: (klucz, obowiązujący tekst, tekst wbudowany, czy zmieniony,
    // wbudowany angielski — podpis bazowy, który ustawienia pokazują obok tłumaczenia).
    public static async Task<List<(string Key, string Text, string DefaultText, bool Custom, string BaseText)>> ForLanguageAsync(
        NpgsqlConnection conn, int languageId)
    {
        string? code;
        await using (var codeCmd = new NpgsqlCommand("SELECT code FROM project_languages WHERE id = @id;", conn))
        {
            codeCmd.Parameters.AddWithValue("id", languageId);
            code = await codeCmd.ExecuteScalarAsync() as string;
        }

        var custom = new Dictionary<string, string>();
        await using (var cmd = new NpgsqlCommand("SELECT key, text FROM document_labels WHERE language_id = @id;", conn))
        {
            cmd.Parameters.AddWithValue("id", languageId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                custom[reader.GetString(0)] = reader.GetString(1);
        }

        return BuiltIn.Select(b =>
        {
            var def = Default(b.Key, code);
            return custom.TryGetValue(b.Key, out var text) ? (b.Key, text, def, true, b.En) : (b.Key, def, def, false, b.En);
        }).ToList();
    }

    public static async Task<Dictionary<string, string>> ResolveAsync(NpgsqlConnection conn, int? languageId)
    {
        if (languageId is null)
            return BuiltIn.ToDictionary(b => b.Key, b => b.En);
        return (await ForLanguageAsync(conn, languageId.Value)).ToDictionary(l => l.Key, l => l.Text);
    }
}
