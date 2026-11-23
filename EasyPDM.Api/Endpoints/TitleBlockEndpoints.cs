using System.Text.Json;
using Npgsql;

// Everything a drawing title block needs for one Part/Assembly, in one call, so the CAD macros
// (FreeCAD first, see pawelcel/EasyPDM-FreeCAD) fill the sheet the same way and none of them
// re-derives "who approved it" from the raw history on its own.
//
// Language: the main language of the item's home project (items.project_id), or the default
// documentation language for an item without a project -- the same rule as the BOM CSV.
// Translations of the name and the processes are returned only when that project is
// multilingual, and only for its other languages.
//
// People and dates belong to the CURRENT revision: a revision starts when the item comes back
// to In progress from Released or Cancelled (that is what bumps the revision). Within it,
// "drawn by" is whoever last moved the item to In review and "approved by" whoever moved it to
// Released -- empty until that happens, which is intended: a drawing printed before release
// carries no approval.
//
// Revisions list every revision comment (B, C, ...); A has none, being the first release.
// Open to every logged-in user, like GET /api/items/{id}.
static class TitleBlockEndpoints
{
    public readonly record struct Signature(string? Name, DateTime At);

    // "Drawn by" and "approved by" of the current revision (see the top of the file). Shared
    // with DrawingStamp, so the stamped PDF and the macros' title block can never disagree.
    public static async Task<(Signature? Drawn, Signature? Approved)> SignaturesAsync(NpgsqlConnection conn, Guid itemId)
    {
        Signature? drawn = null, approved = null;
        await using var cmd = new NpgsqlCommand($"""
            WITH rev_start AS (
                SELECT COALESCE(MAX(changed_at), '-infinity'::timestamptz) AS at
                FROM item_status_history
                WHERE item_id = @id AND to_status = 'w_pracy' AND from_status IN ('wydany', 'anulowana')
            )
            SELECT DISTINCT ON (sh.to_status) sh.to_status, sh.changed_at, {UserNames.FullNameSql("u")}
            FROM item_status_history sh
            LEFT JOIN users u ON u.id = sh.changed_by
            WHERE sh.item_id = @id AND sh.to_status IN ('sprawdzany', 'wydany')
              AND sh.changed_at >= (SELECT at FROM rev_start)
            ORDER BY sh.to_status, sh.changed_at DESC;
            """, conn);
        cmd.Parameters.AddWithValue("id", itemId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var who = new Signature(r.IsDBNull(2) ? null : r.GetString(2), r.GetDateTime(1));
            if (r.GetString(0) == "sprawdzany") drawn = who; else approved = who;
        }
        return (drawn, approved);
    }

    public static void MapTitleBlockEndpoints(this WebApplication app, string connectionString)
    {
        app.MapGet("/api/items/{id:guid}/title-block", async (Guid id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // --- the item, its project and documentation language ---
            string itemType, fileName;
            string? status;
            int? revisionNumber, languageId = null;
            string? languageCode = null;
            bool multilingual = false;
            Guid? projectId;
            JsonElement properties;
            string recordName;
            await using (var cmd = new NpgsqlCommand("""
                SELECT i.item_type, i.file_name, i.status, i.revision_number, i.project_id, i.properties,
                       i.item_number, i.item_number_prefix, i.item_number_digits, i.item_number_with_name,
                       l.id, l.code, COALESCE(p.multilingual, false)
                FROM items i
                LEFT JOIN projects p ON p.id = i.project_id
                LEFT JOIN project_languages l ON l.id = COALESCE(p.main_language_id, default_project_language_id())
                WHERE i.id = @id;
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    return Results.NotFound();
                itemType = r.GetString(0);
                fileName = r.GetString(1);
                status = r.IsDBNull(2) ? null : r.GetString(2);
                revisionNumber = r.IsDBNull(3) ? null : r.GetInt32(3);
                projectId = r.IsDBNull(4) ? null : r.GetGuid(4);
                properties = JsonDocument.Parse(r.GetFieldValue<string>(5)).RootElement.Clone();
                recordName = r.IsDBNull(6)
                    ? fileName
                    : ItemNumbering.RecordName(r.GetInt32(6), r.IsDBNull(7) ? null : r.GetString(7),
                        r.IsDBNull(8) ? null : r.GetInt32(8), r.IsDBNull(9) ? null : r.GetBoolean(9), fileName);
                if (!r.IsDBNull(10)) { languageId = r.GetInt32(10); languageCode = r.GetString(11); }
                multilingual = projectId is not null && r.GetBoolean(12);
            }

            if (itemType != "part" && itemType != "assembly")
                return Results.BadRequest("Only parts and assemblies have a title block.");

            // Other languages of a multilingual project, in the order they were added (English and
            // Polish come built in, others follow), so translations read "EN: Cube · DE: Würfel".
            var otherLanguages = new List<(int Id, string Code)>();
            if (multilingual)
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT l.id, l.code FROM project_language_links pl
                    JOIN project_languages l ON l.id = pl.language_id
                    WHERE pl.project_id = @project AND l.id <> @main
                    ORDER BY l.id;
                    """, conn);
                cmd.Parameters.AddWithValue("project", projectId!.Value);
                cmd.Parameters.AddWithValue("main", (object?)languageId ?? DBNull.Value);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    otherLanguages.Add((r.GetInt32(0), r.GetString(1)));
            }

            // --- name: fileName is already in the main language; translations from nameTexts ---
            var nameTexts = new Dictionary<int, string>();
            if (properties.TryGetProperty("nameTexts", out var nt) && nt.ValueKind == JsonValueKind.Array)
                foreach (var t in nt.EnumerateArray())
                    if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("languageId", out var lid) && lid.ValueKind == JsonValueKind.Number
                        && t.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()))
                        nameTexts[lid.GetInt32()] = n.GetString()!;
            var nameTranslations = otherLanguages
                .Where(l => nameTexts.ContainsKey(l.Id))
                .Select(l => new { languageCode = l.Code, name = nameTexts[l.Id] })
                .ToList();

            // --- processes, in the order picked on the item ---
            var processes = new List<object>();
            if (properties.TryGetProperty("processes", out var procs) && procs.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in procs.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        processes.Add(new { name = entry.GetString() ?? "", description = (string?)null, translations = Array.Empty<object>() });
                        continue;
                    }
                    if (entry.ValueKind != JsonValueKind.Object)
                        continue;
                    var texts = new Dictionary<int, JsonElement>();
                    if (entry.TryGetProperty("texts", out var tx) && tx.ValueKind == JsonValueKind.Array)
                        foreach (var t in tx.EnumerateArray())
                            if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("languageId", out var lid) && lid.ValueKind == JsonValueKind.Number)
                                texts[lid.GetInt32()] = t;
                    var main = languageId is not null && texts.TryGetValue(languageId.Value, out var m) ? m : entry;
                    processes.Add(new
                    {
                        name = Text(main, "name") ?? Text(entry, "name") ?? "",
                        description = Text(main, "description"),
                        translations = otherLanguages
                            .Where(l => texts.ContainsKey(l.Id))
                            .Select(l => new { languageCode = l.Code, name = Text(texts[l.Id], "name") ?? "", description = Text(texts[l.Id], "description") })
                            .ToList(),
                    });
                }
            }

            // --- parts list of an assembly: its direct components, numbered as in EasyPDM ---
            // The number is the BOM position (item_relations.position, the L.p. people edit and
            // drag in the structure), not a count of the rows, so the drawing's balloons and the
            // parts list say the same as the structure in EasyPDM. Names follow the same language
            // rule as the item's own name: the main name, and translations into the project's
            // other languages from each component's nameTexts.
            var bom = new List<object>();
            if (itemType == "assembly")
            {
                var rows = new List<(int Position, decimal Quantity, Guid Id, string Name, string RecordName, JsonElement Props)>();
                await using (var cmd = new NpgsqlCommand("""
                    SELECT r.position, COALESCE(r.quantity, 1), c.id, c.file_name, c.properties,
                           c.item_number, c.item_number_prefix, c.item_number_digits, c.item_number_with_name
                    FROM item_relations r
                    JOIN items c ON c.id = r.child_id
                    WHERE r.parent_id = @id
                    ORDER BY r.position, c.file_name;
                    """, conn))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    await using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        var childName = r.GetString(3);
                        var childRecord = r.IsDBNull(5)
                            ? childName
                            : ItemNumbering.RecordName(r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6),
                                r.IsDBNull(7) ? null : r.GetInt32(7), r.IsDBNull(8) ? null : r.GetBoolean(8), childName);
                        rows.Add((r.GetInt32(0), r.GetDecimal(1), r.GetGuid(2), childName, childRecord,
                            JsonDocument.Parse(r.GetFieldValue<string>(4)).RootElement.Clone()));
                    }
                }
                foreach (var row in rows)
                {
                    var texts = new Dictionary<int, string>();
                    if (row.Props.TryGetProperty("nameTexts", out var cnt) && cnt.ValueKind == JsonValueKind.Array)
                        foreach (var t in cnt.EnumerateArray())
                            if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("languageId", out var lid) && lid.ValueKind == JsonValueKind.Number
                                && t.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()))
                                texts[lid.GetInt32()] = n.GetString()!;
                    bom.Add(new
                    {
                        position = row.Position,
                        itemId = row.Id,
                        name = row.Name,
                        nameTranslations = otherLanguages
                            .Where(l => texts.ContainsKey(l.Id))
                            .Select(l => new { languageCode = l.Code, name = texts[l.Id] })
                            .ToList(),
                        drawingNumber = row.RecordName,
                        quantity = row.Quantity,
                        // The same item properties as the BOM CSV's columns.
                        manufacturer = Text(row.Props, "manufacturer"),
                        standard = Text(row.Props, "norm"),
                        orderNumber = Text(row.Props, "orderNumber"),
                        orderNumber2 = Text(row.Props, "orderNumber2"),
                    });
                }
            }

            // --- who drew / approved the current revision ---
            var (drawn, approved) = await SignaturesAsync(conn, id);
            object? drawnBy = drawn is { } d ? new { name = d.Name, at = d.At } : null;
            object? approvedBy = approved is { } a ? new { name = a.Name, at = a.At } : null;

            // --- revision table (B, C, ...) ---
            var revisions = new List<object>();
            await using (var cmd = new NpgsqlCommand($"""
                SELECT rc.revision_number, rc.comment, rc.created_at, {UserNames.FullNameSql("u")}
                FROM item_revision_comments rc
                LEFT JOIN users u ON u.id = rc.created_by
                WHERE rc.item_id = @id AND rc.revision_number > 1
                ORDER BY rc.revision_number;
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    revisions.Add(new
                    {
                        revisionLabel = RevisionLabeling.Label(r.GetInt32(0)),
                        description = r.GetString(1),
                        at = r.GetDateTime(2),
                        by = r.IsDBNull(3) ? null : r.GetString(3),
                    });
            }

            // --- company ---
            string? companyName = null;
            var hasLogo = false;
            await using (var cmd = new NpgsqlCommand("SELECT name, logo IS NOT NULL FROM company_profile WHERE id = true;", conn))
            await using (var r = await cmd.ExecuteReaderAsync())
                if (await r.ReadAsync()) { companyName = r.IsDBNull(0) ? null : r.GetString(0); hasLogo = r.GetBoolean(1); }

            var allLabels = await DocumentLabels.ResolveAsync(conn, languageId);
            var labels = allLabels
                .Where(kv => kv.Key.StartsWith("titleblock.", StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key["titleblock.".Length..], kv => kv.Value);

            return Results.Ok(new
            {
                itemId = id,
                languageCode,
                multilingual,
                labels,
                name = fileName,
                nameTranslations,
                drawingNumber = recordName,
                revisionLabel = RevisionLabeling.Label(revisionNumber),
                status,
                material = Text(properties, "material"),
                // Mass in kg as the upload macros store it (properties.mass, plain number text).
                mass = Text(properties, "mass"),
                drawnBy,
                approvedBy,
                processes,
                revisions,
                bom,
                company = new { name = companyName, hasLogo, logoUrl = hasLogo ? "/api/settings/company/logo" : null },
            });
        });
    }

    private static string? Text(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;
}
