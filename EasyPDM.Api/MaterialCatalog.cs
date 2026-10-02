using System.Text.Json;
using Npgsql;

// Materiał przysłany przez makro CAD bierze się z dokumentu, a nie z listy wyboru, więc
// katalog może go jeszcze nie znać. Zakładamy go wtedy sami -- inaczej element miałby
// materiał, którego nie da się ani wybrać przy następnej edycji, ani użyć jako filtr w
// "Całej bazie". Katalogi są wiązane z elementami PO NAZWIE (zob. TECHNICAL, "Data model"),
// więc sam wpis wystarczy; grupa/podgrupa zostają puste do uzupełnienia ręcznie.
//
// Jedno miejsce, bo robią to DWIE ścieżki i muszą robić to samo: PATCH /properties (makro po
// wysyłce) oraz POST /nodes (tworzenie elementu, gdzie materiał przychodzi z okna dodawania
// wypełnionego przez makro). Dopóki zakładał to tylko PATCH, element utworzony z materiałem z
// CAD-a nosił nazwę, której w katalogu jeszcze nie było.
static class MaterialCatalog
{
    // ON CONFLICT DO NOTHING zamiast sprawdzania "czy jest": dwa makra wysyłające równolegle
    // ten sam materiał nie mogą się wywrócić na wyścigu.
    public static async Task EnsureFromPropertiesAsync(
        JsonElement properties, NpgsqlConnection conn, NpgsqlTransaction? tx = null)
    {
        if (!properties.TryGetProperty("material", out var materialValue)
            || materialValue.ValueKind != JsonValueKind.String)
            return;

        var materialName = materialValue.GetString()?.Trim();
        if (string.IsNullOrEmpty(materialName))
            return;

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO materials (name) VALUES (@name) ON CONFLICT (name) DO NOTHING;", conn, tx);
        cmd.Parameters.AddWithValue("name", materialName);
        await cmd.ExecuteNonQueryAsync();
    }
}
