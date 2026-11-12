using System.Text.Json;
using Npgsql;

// Podpowiedzi nazw przy tworzeniu Części/Złożenia: nazwy istniejących elementów w danym języku
// dokumentacji razem z ich tłumaczeniami na inne języki — wybór podpowiedzi przenosi te
// tłumaczenia do nowego elementu (properties.nameTexts), żeby "Kostka" nie była tłumaczona
// od nowa przy każdym kolejnym elemencie.
//
// Nazwa elementu (file_name) jest w głównym języku jego projektu (bez projektu — w domyślnym),
// a properties.nameTexts trzyma tłumaczenia na pozostałe. Odczyt otwarty dla każdego
// zalogowanego, tak jak "Cała baza" (GET /api/items).
static class ItemNameEndpoints
{
    private const int MaxSuggestions = 50;

    public static void MapItemNameEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/item-names?languageId=2&itemType=part&search=kost
        // -> [{ name, texts: [{ languageId, name }] }], alfabetycznie, bez powtórzeń nazwy,
        // najwyżej MaxSuggestions pozycji. search zawęża już w bazie do elementów, które mają
        // ten tekst w nazwie albo w tłumaczeniach -- okno pyta w trakcie pisania, więc nie ma
        // powodu wozić nazw wszystkich elementów przy każdym jego otwarciu.
        app.MapGet("/api/item-names", async (int languageId, string itemType, string? search) =>
        {
            var needle = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            if (itemType is not ("part" or "assembly"))
                return Results.BadRequest("itemType to part albo assembly.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT i.file_name, i.properties -> 'nameTexts',
                       COALESCE(p.main_language_id, default_project_language_id())
                FROM items i LEFT JOIN projects p ON p.id = i.project_id
                WHERE i.item_type = @itemType
                  AND (@search::text IS NULL
                       OR position(lower(@search) in lower(i.file_name)) > 0
                       OR position(lower(@search) in lower(COALESCE(i.properties ->> 'nameTexts', ''))) > 0);
                """, conn);
            cmd.Parameters.AddWithValue("itemType", itemType);
            cmd.Parameters.Add(new NpgsqlParameter("search", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)needle ?? DBNull.Value });

            // Ta sama nazwa bywa w wielu elementach — zostaje wpis z największą liczbą tłumaczeń.
            var byName = new Dictionary<string, (string Name, Dictionary<int, string> Texts)>(StringComparer.CurrentCultureIgnoreCase);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var all = new Dictionary<int, string>();
                if (!reader.IsDBNull(2))
                    all[reader.GetInt32(2)] = reader.GetString(0);
                if (!reader.IsDBNull(1))
                {
                    using var doc = JsonDocument.Parse(reader.GetString(1));
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in doc.RootElement.EnumerateArray())
                        {
                            if (t.ValueKind == JsonValueKind.Object
                                && t.TryGetProperty("languageId", out var lang) && lang.ValueKind == JsonValueKind.Number
                                && t.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                                && !string.IsNullOrWhiteSpace(n.GetString()))
                                all.TryAdd(lang.GetInt32(), n.GetString()!.Trim());
                        }
                    }
                }

                if (!all.TryGetValue(languageId, out var name) || string.IsNullOrWhiteSpace(name))
                    continue;
                // Baza dopasowała element po którejkolwiek z jego nazw; podpowiada się tylko ta
                // w żądanym języku, więc i ona musi zawierać szukany tekst.
                if (needle is not null && !name.Contains(needle, StringComparison.CurrentCultureIgnoreCase))
                    continue;
                var texts = all.Where(kv => kv.Key != languageId).ToDictionary(kv => kv.Key, kv => kv.Value);
                if (!byName.TryGetValue(name, out var existing) || texts.Count > existing.Texts.Count)
                    byName[name] = (name, texts);
            }

            var result = byName.Values
                .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxSuggestions)
                .Select(v => new
                {
                    name = v.Name,
                    texts = v.Texts.Select(kv => new { languageId = kv.Key, name = kv.Value }),
                });
            return Results.Ok(result);
        });
    }
}
