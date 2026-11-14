using Npgsql;
using NpgsqlTypes;

// A released parts list stays as it was released.
//
// item_relations has no revision: it says which item is a child, not which revision of it. So
// sending a released part back to work (a new revision) changed every list that named it,
// released ones included, and a released assembly's BOM showed names, properties and revisions
// that were never released with it.
//
//   freeze   moving a part or assembly to "wydany" copies its name, number and properties into
//            item_releases, and its list into item_release_lines with each child's revision, in
//            the status change's transaction
//   one rule a revision that was released reads its copy; any other reads item_relations. It
//            holds at every level: a line names a revision of its child, and that revision's own
//            copy gives the child's details and, for a sub-assembly, its own lines
//   children an assembly is released only with its components released (ItemEndpoints, the BOM
//            rule), so every child has a copy to point at; one released before this change is
//            copied on the way through
//
// item_relations is left alone. It is the list of the current revision, and the tree, the macros
// and every status rule go on reading it.
static class Release
{
    public const string Stage = "wydany";

    // Under the status change's transaction, right after the item's row moved to "wydany". Locks
    // the children so none can leave "wydany" during the copy. Null, or the answer that stops it.
    public static async Task<IResult?> FreezeAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid itemId, Guid userId)
    {
        var children = await LockChildrenAsync(conn, tx, itemId);
        if (children.Any(c => c.Status != Stage))
            return Results.Conflict("A component changed status in the meantime. Nothing was changed: refresh and try again.");

        await WriteAsync(conn, tx, itemId, userId);
        await FreezeUnfrozenAsync(conn, tx, children, [itemId]);
        return null;
    }

    private sealed record Child(Guid Id, int? Revision, string? Status);

    private static async Task<List<Child>> LockChildrenAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid itemId)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT i.id, i.revision_number, i.status
            FROM item_relations ir JOIN items i ON i.id = ir.child_id
            WHERE ir.parent_id = @id AND i.item_type IN ('part', 'assembly')
            ORDER BY i.id
            FOR SHARE OF i;
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", itemId);
        var children = new List<Child>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            children.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return children;
    }

    // A released child with no copy of its own was released before this change and skipped by
    // migration 078 (something under it had moved on). It is released, so what it has now is
    // what was released: copy it too, as far down as that goes.
    private static async Task FreezeUnfrozenAsync(NpgsqlConnection conn, NpgsqlTransaction tx, List<Child> children, HashSet<Guid> seen)
    {
        foreach (var child in children.Where(c => c.Status == Stage && seen.Add(c.Id)))
        {
            if (await ExistsAsync(conn, tx, child.Id, child.Revision))
                continue;
            await WriteAsync(conn, tx, child.Id, null);
            await FreezeUnfrozenAsync(conn, tx, await LockChildrenAsync(conn, tx, child.Id), seen);
        }
    }

    // The item's current revision, as it stands. A revision is released once and revisions only
    // go forward, so a copy that is already there is the same release and is kept.
    private static async Task WriteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid itemId, Guid? userId)
    {
        await using var cmd = new NpgsqlCommand("""
            WITH release AS (
                INSERT INTO item_releases
                    (item_id, revision_number, released_by, file_name, item_number, item_number_prefix,
                     item_number_digits, item_number_with_name, properties)
                SELECT i.id, i.revision_number, @userId, i.file_name, i.item_number, i.item_number_prefix,
                       i.item_number_digits, i.item_number_with_name, i.properties
                FROM items i WHERE i.id = @itemId
                ON CONFLICT DO NOTHING
                RETURNING id
            )
            INSERT INTO item_release_lines (release_id, child_id, child_revision, quantity, position)
            SELECT r.id, ir.child_id, c.revision_number, ir.quantity, ir.position
            FROM release r
            JOIN item_relations ir ON ir.parent_id = @itemId
            JOIN items c ON c.id = ir.child_id;
            """, conn, tx);
        cmd.Parameters.AddWithValue("itemId", itemId);
        cmd.Parameters.Add(new NpgsqlParameter<Guid?>("userId", NpgsqlDbType.Uuid) { TypedValue = userId });
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid itemId, int? revision)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM item_releases WHERE item_id = @itemId AND revision_number IS NOT DISTINCT FROM @revision;", conn, tx);
        cmd.Parameters.AddWithValue("itemId", itemId);
        cmd.Parameters.Add(new NpgsqlParameter<int?>("revision", NpgsqlDbType.Integer) { TypedValue = revision });
        return await cmd.ExecuteScalarAsync() is not null;
    }

    // When the item's current revision was released, and its list is therefore the copy. Null
    // for an item whose current revision was never released (in work, or released before this
    // change and not copied).
    public static async Task<DateTime?> ReleasedAtAsync(NpgsqlConnection conn, Guid itemId)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT r.released_at FROM item_releases r JOIN items i ON i.id = r.item_id
            WHERE r.item_id = @itemId AND r.revision_number IS NOT DISTINCT FROM i.revision_number;
            """, conn);
        cmd.Parameters.AddWithValue("itemId", itemId);
        return await cmd.ExecuteScalarAsync() as DateTime?;
    }

    // An item a released list names cannot be deleted while that list's item stays: the line has
    // no foreign key, so a delete would quietly shorten a released list. The labels of the
    // releases that stop it, or none.
    public static async Task<List<string>> BlockingDeleteAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid[] ids)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT i.item_number, i.item_number_prefix, i.item_number_digits, i.item_number_with_name, i.file_name
            FROM item_release_lines l
            JOIN item_releases r ON r.id = l.release_id
            JOIN items i ON i.id = r.item_id
            WHERE l.child_id = ANY(@ids) AND NOT (r.item_id = ANY(@ids))
            LIMIT 10;
            """, conn, tx);
        cmd.Parameters.AddWithValue("ids", ids);
        var labels = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            labels.Add(ItemNumbering.RecordName(reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetBoolean(3), reader.GetString(4)));
        return labels;
    }

    // Every line under @id at every depth, by the one rule above, in path order. The details of a
    // line come from its revision's copy when there is one, otherwise from the item as it is now.
    // "visited" stops a cycle that got into item_relations some other way.
    public const string LinesSql = """
        WITH RECURSIVE bom AS (
            SELECT e.child_id, e.child_revision, e.quantity, e.quantity AS extended_quantity, 1 AS depth,
                   ARRAY[e.position] AS path, ARRAY[root.id] AS visited
            FROM items root,
            LATERAL (
                SELECT l.child_id, l.child_revision, l.quantity, l.position
                FROM item_releases r JOIN item_release_lines l ON l.release_id = r.id
                WHERE r.item_id = root.id AND r.revision_number IS NOT DISTINCT FROM root.revision_number
                UNION ALL
                SELECT ir.child_id, c.revision_number, ir.quantity, ir.position
                FROM item_relations ir JOIN items c ON c.id = ir.child_id
                WHERE ir.parent_id = root.id
                  AND NOT EXISTS (SELECT 1 FROM item_releases r
                                  WHERE r.item_id = root.id AND r.revision_number IS NOT DISTINCT FROM root.revision_number)
            ) e
            WHERE root.id = @id
            UNION ALL
            SELECT e.child_id, e.child_revision, e.quantity, b.extended_quantity * e.quantity, b.depth + 1,
                   b.path || e.position, b.visited || b.child_id
            FROM bom b,
            LATERAL (
                SELECT l.child_id, l.child_revision, l.quantity, l.position
                FROM item_releases r JOIN item_release_lines l ON l.release_id = r.id
                WHERE r.item_id = b.child_id AND r.revision_number IS NOT DISTINCT FROM b.child_revision
                UNION ALL
                SELECT ir.child_id, c.revision_number, ir.quantity, ir.position
                FROM item_relations ir JOIN items c ON c.id = ir.child_id
                WHERE ir.parent_id = b.child_id
                  AND NOT EXISTS (SELECT 1 FROM item_releases r
                                  WHERE r.item_id = b.child_id AND r.revision_number IS NOT DISTINCT FROM b.child_revision)
            ) e
            WHERE NOT (b.child_id = ANY(b.visited))
        )
        SELECT b.child_id, b.quantity, b.extended_quantity, b.depth, b.path,
               CASE WHEN cr.id IS NULL THEN i.item_number ELSE cr.item_number END,
               CASE WHEN cr.id IS NULL THEN i.item_number_prefix ELSE cr.item_number_prefix END,
               CASE WHEN cr.id IS NULL THEN i.file_name ELSE cr.file_name END,
               b.child_revision,
               CASE WHEN cr.id IS NULL THEN i.properties ELSE cr.properties END,
               CASE WHEN cr.id IS NULL THEN i.item_number_digits ELSE cr.item_number_digits END,
               CASE WHEN cr.id IS NULL THEN i.item_number_with_name ELSE cr.item_number_with_name END,
               cr.id IS NOT NULL
        FROM bom b
        JOIN items i ON i.id = b.child_id
        LEFT JOIN item_releases cr ON cr.item_id = b.child_id AND cr.revision_number IS NOT DISTINCT FROM b.child_revision
        ORDER BY b.path;
        """;
}
