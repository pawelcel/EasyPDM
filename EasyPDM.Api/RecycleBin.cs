using System.Text;
using Npgsql;

// The recycle bin's two halves: taking a set of items out of the live tables into one
// recycle_bin row (Snapshot), and writing them back (Restore). See 075_recycle_bin.sql for why
// the rows leave the live tables instead of carrying a deleted flag.
//
// Which tables go along is read from the database's own foreign keys, not from a list here:
// every table that an ON DELETE CASCADE chain reaches from items. A deleted item's rows are
// exactly the rows that cascade would have destroyed, so this captures all of them, and a table
// a later migration hangs off items (or off one of these) is captured without anyone
// remembering to add it. A list kept by hand would lose that table's rows on every restore.
//
// Restore writes each table back in foreign-key order. Before writing it drops the rows whose
// other end has gone while the entry waited (a component deleted since, a tag removed), and
// clears a column whose foreign key is ON DELETE SET NULL (a person removed), which is what the
// live delete of that other end would have done to the row.
static class RecycleBin
{
    private sealed record ForeignKey(string Table, string Column, string RefTable, string RefColumn, char OnDelete)
    {
        public bool Cascade => OnDelete == 'c';
        public bool SetNull => OnDelete == 'n';
        public bool SelfRef => Table == RefTable;
    }

    private sealed record Graph(List<string> Order, Dictionary<string, string> Condition, List<ForeignKey> Keys);

    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    // Single-column foreign keys between the tables of the schema items lives in (the one the
    // connection's search path finds it in, not current_schema(), which a $user schema can shadow). The
    // ones this bin follows are all single-column; a composite key would be left out of the
    // capture conditions and its rows would still be captured through their item_id.
    private static async Task<List<ForeignKey>> ForeignKeysAsync(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT src.relname, a.attname, dst.relname, b.attname, c.confdeltype
            FROM pg_constraint c
            JOIN pg_class src ON src.oid = c.conrelid
            JOIN pg_class dst ON dst.oid = c.confrelid
            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
            JOIN pg_attribute b ON b.attrelid = c.confrelid AND b.attnum = c.confkey[1]
            WHERE c.contype = 'f' AND cardinality(c.conkey) = 1
              AND src.relnamespace = (SELECT relnamespace FROM pg_class WHERE oid = 'items'::regclass)
              AND dst.relnamespace = (SELECT relnamespace FROM pg_class WHERE oid = 'items'::regclass)
            ORDER BY src.relname, a.attname;
            """, conn, tx);
        var keys = new List<ForeignKey>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            keys.Add(new ForeignKey(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetChar(4)));
        return keys;
    }

    private static async Task<Graph> GraphAsync(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var keys = await ForeignKeysAsync(conn, tx);
        // Without these the bin would keep the item row alone and a restore would come back with
        // no files or history, silently. Refuse the delete instead.
        if (!keys.Any(k => k.Cascade && k.RefTable == "items"))
            throw new InvalidOperationException("Recycle bin: no table cascades from items in this schema, so a deleted item could not be kept whole.");

        var captured = new HashSet<string> { "items" };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var key in keys)
                if (key.Cascade && captured.Contains(key.RefTable) && captured.Add(key.Table))
                    grew = true;
        }

        // A row is captured when one of its cascading keys points at a captured row. Written as
        // nested IN (SELECT ...) so it reads the live tables before the delete; a self-reference
        // (an answer to a comment) adds nothing, its rows come in through their item_id.
        var condition = new Dictionary<string, string> { ["items"] = "id = ANY(@ids)" };
        string ConditionOf(string table, HashSet<string> seen)
        {
            if (condition.TryGetValue(table, out var known))
                return known;
            seen.Add(table);
            var parts = keys
                .Where(k => k.Table == table && k.Cascade && !k.SelfRef && captured.Contains(k.RefTable) && !seen.Contains(k.RefTable))
                .Select(k => $"{Q(k.Column)} IN (SELECT {Q(k.RefColumn)} FROM {Q(k.RefTable)} WHERE {ConditionOf(k.RefTable, seen)})")
                .ToList();
            seen.Remove(table);
            var text = parts.Count == 0 ? "false" : "(" + string.Join(" OR ", parts) + ")";
            condition[table] = text;
            return text;
        }
        foreach (var table in captured)
            ConditionOf(table, []);

        // Parents before children, by every foreign key between captured tables (not only the
        // cascading ones: a row pointing at another captured row needs that row written first).
        var order = new List<string>();
        var left = captured.OrderBy(t => t, StringComparer.Ordinal).ToList();
        while (left.Count > 0)
        {
            var ready = left.FirstOrDefault(t => !keys.Any(k => k.Table == t && !k.SelfRef && left.Contains(k.RefTable)))
                        ?? left[0];
            order.Add(ready);
            left.Remove(ready);
        }

        return new Graph(order, condition, keys.Where(k => captured.Contains(k.Table)).ToList());
    }

    // The JSON payload for these items: { "<table>": [rows...], ... }, built in the database
    // from the live rows. Call it inside the delete's transaction, before the DELETE.
    public static async Task<string> SnapshotSqlAsync(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        var graph = await GraphAsync(conn, tx);
        var sql = new StringBuilder("'{}'::jsonb");
        foreach (var table in graph.Order)
        {
            sql.Append($"\n || jsonb_build_object('{table.Replace("'", "''")}', ")
               .Append($"(SELECT coalesce(jsonb_agg(to_jsonb(t)), '[]'::jsonb) FROM {Q(table)} t WHERE {graph.Condition[table]}))");
        }
        return sql.ToString();
    }

    // Where the restored top item goes, decided by the caller.
    //   Moved         false: back where it was (its old parents that still exist, else a root of
    //                 its project). true: to the place below, dropping its old outside links.
    //   TargetParent  the folder it goes under when moved; null puts it at the project's root
    //   TargetProject the project it goes to when moved
    public sealed record Placement(Guid? TargetParent, Guid? TargetProject, bool Moved);

    // Writes the entry's rows back. The caller has checked that none of its items exists now.
    public static async Task RestoreAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, CurrentUser actor, Guid binId, Guid rootId, Guid? originalProject, Guid[] ids, Placement placement)
    {
        var graph = await GraphAsync(conn, tx);

        await using (var keysCmd = new NpgsqlCommand("SELECT array(SELECT jsonb_object_keys(payload)) FROM recycle_bin WHERE id = @bin;", conn, tx))
        {
            keysCmd.Parameters.AddWithValue("bin", binId);
            var present = (string[])(await keysCmd.ExecuteScalarAsync())!;
            graph.Order.RemoveAll(t => !present.Contains(t));
        }

        foreach (var table in graph.Order)
        {
            await ExecAsync(conn, tx, $"CREATE TEMP TABLE bin_rows ON COMMIT DROP AS SELECT * FROM {Q(table)} WITH NO DATA;");
            await ExecAsync(conn, tx, $"""
                INSERT INTO bin_rows
                SELECT * FROM jsonb_populate_recordset(NULL::{Q(table)}, (SELECT payload->'{table.Replace("'", "''")}' FROM recycle_bin WHERE id = @bin));
                """, ("bin", binId));

            if (table == "items")
            {
                // Moved: everything that sat in the old project goes to the new one with it, the
                // way a move to another project takes the folder's content along.
                if (placement.Moved)
                    await ExecAsync(conn, tx,
                        "UPDATE bin_rows SET project_id = @target WHERE project_id IS NOT DISTINCT FROM @orig;",
                        ("target", (object?)placement.TargetProject ?? DBNull.Value), ("orig", (object?)originalProject ?? DBNull.Value));
                // A project removed while the entry waited: its items come back out of any
                // project, as removing a project leaves its items (ProjectEndpoints).
                await ExecAsync(conn, tx,
                    "UPDATE bin_rows SET project_id = NULL WHERE project_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM projects p WHERE p.id = bin_rows.project_id);");
            }

            if (table == "item_relations")
            {
                if (placement.Moved)
                    await ExecAsync(conn, tx, "DELETE FROM bin_rows WHERE child_id = @root AND NOT parent_id = ANY(@ids);",
                        ("root", rootId), ("ids", ids));
                if (placement.TargetParent is Guid parent)
                    await ExecAsync(conn, tx, "INSERT INTO bin_rows (parent_id, child_id, quantity, position) VALUES (@parent, @root, 1, 0);",
                        ("parent", parent), ("root", rootId));
            }

            foreach (var key in graph.Keys.Where(k => k.Table == table))
            {
                var missing = $"bin_rows.{Q(key.Column)} IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {Q(key.RefTable)} x WHERE x.{Q(key.RefColumn)} = bin_rows.{Q(key.Column)})";
                if (key.SelfRef)
                    missing += $" AND NOT EXISTS (SELECT 1 FROM bin_rows y WHERE y.{Q(key.RefColumn)} = bin_rows.{Q(key.Column)})";
                if (key.SetNull)
                {
                    await ExecAsync(conn, tx, $"UPDATE bin_rows SET {Q(key.Column)} = NULL WHERE {missing};");
                }
                else
                {
                    // A dropped answer takes its own answers along, as the cascade would.
                    while (await ExecAsync(conn, tx, $"DELETE FROM bin_rows WHERE {missing};") > 0 && key.SelfRef) { }
                }
            }

            if (table == "item_relations")
            {
                // A parent that lives on outside this entry may have used the old position since.
                await ExecAsync(conn, tx, """
                    UPDATE bin_rows SET position = coalesce((SELECT max(x.position) FROM item_relations x WHERE x.parent_id = bin_rows.parent_id), 0) + 1
                    WHERE NOT parent_id = ANY(@ids);
                    """, ("ids", ids));
            }

            await ExecAsync(conn, tx, $"INSERT INTO {Q(table)} SELECT * FROM bin_rows; DROP TABLE bin_rows;");
        }

        // show_in_tree matters only for an item with no parent: one that ends without any shows
        // as a root of its project, and one moved into a folder is that folder's content, as an
        // item made there would be. Back in its old folder it keeps what it had.
        await ExecAsync(conn, tx, """
            UPDATE items SET show_in_tree = NOT EXISTS (SELECT 1 FROM item_relations r WHERE r.child_id = items.id)
            WHERE id = @root AND (@moved OR NOT EXISTS (SELECT 1 FROM item_relations r WHERE r.child_id = items.id));
            """, ("root", rootId), ("moved", placement.Moved));
    }

    // Every stored file named anywhere in the entry, for purge.
    public static async Task<List<string>> FilePathsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid binId)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT v #>> '{}'
            FROM recycle_bin b, jsonb_path_query(b.payload, 'lax $.*[*].file_path') v
            WHERE b.id = @bin AND jsonb_typeof(v) = 'string';
            """, conn, tx);
        cmd.Parameters.AddWithValue("bin", binId);
        var paths = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            paths.Add(reader.GetString(0));
        return paths;
    }

    private static async Task<int> ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteNonQueryAsync();
    }
}
