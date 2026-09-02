using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;

// BOM zagłębiony wielopoziomowo (nie tylko bezpośrednie dzieci) + eksport CSV.
// Struktura sama w sobie (dodawanie/usuwanie/kolejność krawędzi item_relations) jest
// w StructureEndpoints.cs — tu wyłącznie odczyt/eksport rozwiniętego drzewa BOM-u.
static class BomEndpoints
{
    public static void MapBomEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/items/{id}/bom — zagłębione (depth >= 2) wpisy BOM-u tego elementu,
        // do dołączenia na ekranie pod bezpośrednimi dziećmi (te dziś obsługuje
        // tree.childrenOf po stronie frontendu — nie duplikujemy ich tutaj).
        app.MapGet("/api/items/{id:guid}/bom", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemExistsAsync(conn, id))
                return Results.NotFound();

            var rows = await FetchBomRowsAsync(conn, id);
            var result = rows
                .Where(r => r.Depth >= 2)
                .Select(r => new
                {
                    itemId = r.ChildId,
                    quantity = r.Quantity,
                    depth = r.Depth,
                    path = r.Path,
                    itemNumber = r.ItemNumber,
                    itemNumberPrefix = r.ItemNumberPrefix,
                    fileName = r.FileName,
                    revisionNumber = r.RevisionNumber,
                    properties = JsonDocument.Parse(r.PropertiesJson).RootElement
                });

            return Results.Ok(result);
        });

        // GET /api/items/{id}/bom/csv — pełny (depth >= 1), zagłębiony BOM jako CSV
        // do pobrania (średnik jako separator + BOM UTF-8, żeby polski Excel otwierał
        // to poprawnie).
        app.MapGet("/api/items/{id:guid}/bom/csv", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemExistsAsync(conn, id))
                return Results.NotFound();

            var rows = await FetchBomRowsAsync(conn, id);

            var csv = new StringBuilder();
            csv.Append("L.p.;Nazwa;Ilość;Materiał;Producent;Numer zamówieniowy 1;Numer zamówieniowy 2\r\n");
            foreach (var row in rows)
            {
                var props = JsonDocument.Parse(row.PropertiesJson).RootElement;
                var name = row.ItemNumber is not null ? $"{row.ItemNumberPrefix}{row.ItemNumber} ({row.FileName})" : row.FileName;
                csv.Append(string.Join(';', new[]
                {
                    CsvField(string.Join('.', row.Path)),
                    CsvField(name),
                    CsvField(row.Quantity.ToString(CultureInfo.InvariantCulture)),
                    CsvField(PropertyOrEmpty(props, "material")),
                    CsvField(PropertyOrEmpty(props, "manufacturer")),
                    CsvField(PropertyOrEmpty(props, "orderNumber")),
                    CsvField(PropertyOrEmpty(props, "orderNumber2")),
                }));
                csv.Append("\r\n");
            }

            var itemLabel = await GetItemLabel(conn, id);
            var fileName = $"BOM_{itemLabel}.csv".Replace("\"", "");
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return Results.File(bytes, "text/csv; charset=utf-8", fileName);
        });

        // GET /api/items/{id}/used-in — "gdzie używane": WSZYSTKIE złożenia (na dowolnej
        // głębokości, nie tylko bezpośredni rodzic), do których ten element pośrednio albo
        // bezpośrednio należy — odwrotność /bom (tam schodzimy w dół drzewa, tu wchodzimy w
        // górę). Element współdzielony w wielu złożeniach/projektach (zob. komentarz przy
        // "candidates" w add-node-dialog.tsx) może mieć wielu "rodziców" na różnych
        // poziomach i w różnych projektach naraz — stąd DISTINCT po id złożenia, nawet gdyby
        // dawało się do niego dojść kilkoma różnymi ścieżkami.
        app.MapGet("/api/items/{id:guid}/used-in", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemExistsAsync(conn, id))
                return Results.NotFound();

            const string sql = """
                WITH RECURSIVE ancestors AS (
                    SELECT ir.parent_id, ARRAY[ir.child_id] AS visited
                    FROM item_relations ir
                    WHERE ir.child_id = @id
                    UNION ALL
                    SELECT ir.parent_id, a.visited || ir.child_id
                    FROM item_relations ir
                    JOIN ancestors a ON ir.child_id = a.parent_id
                    WHERE NOT (ir.child_id = ANY(a.visited))
                )
                SELECT DISTINCT i.id, i.item_number, i.item_number_prefix, i.file_name,
                       i.item_type, i.project_id, i.revision_number, p.name AS project_name
                FROM ancestors a
                JOIN items i ON i.id = a.parent_id
                LEFT JOIN projects p ON p.id = i.project_id
                ORDER BY i.file_name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    id = reader.GetGuid(0),
                    itemNumber = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                    itemNumberPrefix = reader.IsDBNull(2) ? null : reader.GetString(2),
                    fileName = reader.GetString(3),
                    itemType = reader.GetString(4),
                    projectId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5),
                    revisionNumber = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
                    projectName = reader.IsDBNull(7) ? null : reader.GetString(7),
                });
            }

            return Results.Ok(result);
        });

        // GET /api/items/{id}/bom/aggregated-csv — ten sam zagłębiony BOM co /bom/csv, ale
        // zsumowany po elemencie: ten sam komponent użyty w kilku miejscach struktury (różne
        // złożenia, różne poziomy) daje JEDEN wiersz z łączną ilością. Ilość jest "rozwinięta"
        // przez cały łańcuch (extended_quantity = iloczyn quantity na ścieżce od @id) — np.
        // złożenie użyte 2x, a w nim część 3x, liczy się jako 6 sztuk tej części łącznie.
        app.MapGet("/api/items/{id:guid}/bom/aggregated-csv", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemExistsAsync(conn, id))
                return Results.NotFound();

            var rows = await FetchBomRowsAsync(conn, id);
            var aggregated = rows
                .GroupBy(r => r.ChildId)
                .Select(g => new
                {
                    g.First().ItemNumber,
                    g.First().ItemNumberPrefix,
                    g.First().FileName,
                    g.First().PropertiesJson,
                    TotalQuantity = g.Sum(r => r.ExtendedQuantity)
                })
                .OrderBy(a => a.ItemNumber ?? int.MaxValue);

            var csv = new StringBuilder();
            csv.Append("Nazwa;Ilość łącznie;Materiał;Producent;Numer zamówieniowy 1;Numer zamówieniowy 2\r\n");
            foreach (var row in aggregated)
            {
                var props = JsonDocument.Parse(row.PropertiesJson).RootElement;
                var name = row.ItemNumber is not null ? $"{row.ItemNumberPrefix}{row.ItemNumber} ({row.FileName})" : row.FileName;
                csv.Append(string.Join(';', new[]
                {
                    CsvField(name),
                    CsvField(row.TotalQuantity.ToString(CultureInfo.InvariantCulture)),
                    CsvField(PropertyOrEmpty(props, "material")),
                    CsvField(PropertyOrEmpty(props, "manufacturer")),
                    CsvField(PropertyOrEmpty(props, "orderNumber")),
                    CsvField(PropertyOrEmpty(props, "orderNumber2")),
                }));
                csv.Append("\r\n");
            }

            var itemLabel = await GetItemLabel(conn, id);
            var fileName = $"BOM_zsumowany_{itemLabel}.csv".Replace("\"", "");
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            return Results.File(bytes, "text/csv; charset=utf-8", fileName);
        });
    }

    // Odczyt BOM-u (te trzy endpointy) jest świadomie otwarty dla KAŻDEGO zalogowanego
    // użytkownika (zob. GET /api/items) — bez sprawdzenia dostępu do projektu, stąd
    // tylko zwykłe sprawdzenie istnienia elementu, bez pobierania jego project_id.
    private static async Task<bool> ItemExistsAsync(NpgsqlConnection conn, Guid id)
    {
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM items WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<string> GetItemLabel(NpgsqlConnection conn, Guid id)
    {
        await using var cmd = new NpgsqlCommand("SELECT item_number, item_number_prefix, file_name FROM items WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return "BOM";
        var fileName = reader.GetString(2);
        if (reader.IsDBNull(0))
            return fileName;
        var prefix = reader.IsDBNull(1) ? "" : reader.GetString(1);
        return $"{prefix}{reader.GetInt32(0)} ({fileName})";
    }

    private static string PropertyOrEmpty(JsonElement properties, string key) =>
        properties.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string CsvField(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    // Rekurencyjnie schodzi w głąb item_relations od @id, zwracając WSZYSTKIE zagłębione
    // wpisy (depth 1 = bezpośrednie dzieci, depth 2 = wnuki, ...) posortowane po ścieżce
    // L.p. (path) — czyli w kolejności depth-first zgodnej z numeracją "2.1". "visited"
    // to zabezpieczenie przed nieskończoną rekurencją, gdyby mimo cycle-checku przy
    // POST /children powstał kiedyś cykl inną drogą (np. bezpośrednio w bazie).
    private static async Task<List<BomRow>> FetchBomRowsAsync(NpgsqlConnection conn, Guid id)
    {
        const string sql = """
            WITH RECURSIVE bom AS (
                SELECT ir.child_id, ir.quantity, ir.quantity AS extended_quantity, 1 AS depth,
                       ARRAY[ir.position] AS path, ARRAY[ir.parent_id] AS visited
                FROM item_relations ir
                WHERE ir.parent_id = @id
                UNION ALL
                SELECT ir.child_id, ir.quantity, b.extended_quantity * ir.quantity, b.depth + 1,
                       b.path || ir.position, b.visited || ir.parent_id
                FROM item_relations ir
                JOIN bom b ON ir.parent_id = b.child_id
                WHERE NOT (ir.parent_id = ANY(b.visited))
            )
            SELECT b.child_id, b.quantity, b.extended_quantity, b.depth, b.path,
                   i.item_number, i.item_number_prefix, i.file_name, i.revision_number, i.properties
            FROM bom b
            JOIN items i ON i.id = b.child_id
            ORDER BY b.path;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        var rows = new List<BomRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new BomRow(
                ChildId: reader.GetGuid(0),
                Quantity: reader.GetDecimal(1),
                ExtendedQuantity: reader.GetDecimal(2),
                Depth: reader.GetInt32(3),
                Path: reader.GetFieldValue<int[]>(4),
                ItemNumber: reader.IsDBNull(5) ? null : reader.GetInt32(5),
                ItemNumberPrefix: reader.IsDBNull(6) ? null : reader.GetString(6),
                FileName: reader.GetString(7),
                RevisionNumber: reader.IsDBNull(8) ? null : reader.GetInt32(8),
                PropertiesJson: reader.GetFieldValue<string>(9)));
        }
        return rows;
    }

    private record BomRow(
        Guid ChildId, decimal Quantity, decimal ExtendedQuantity, int Depth, int[] Path,
        int? ItemNumber, string? ItemNumberPrefix, string FileName, int? RevisionNumber, string PropertiesJson);
}
