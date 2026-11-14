using Npgsql;

// The recycle bin's list, restore and purge. Deleting an item puts it here (DELETE /api/items/{id},
// RecycleBin.cs); nothing in the bin shows anywhere else, because its rows are out of the live
// tables.
//
//   list     an admin sees every entry; anyone else sees the entries they deleted themselves,
//            since whoever may delete may see what they deleted
//   restore  an admin or the person who deleted it. The item goes back under the folders it sat
//            in when they still exist. When they are all gone (or it sat at the root of a
//            project that is gone) the restore asks for a place: 409 with needsFolder, and the
//            caller sends folderId or projectId
//   purge    admins only, one entry or the whole bin. The rows go and so do the stored files,
//            for good
static class BinEndpoints
{
    public record RestoreBody(Guid? FolderId, Guid? ProjectId);

    private const string NeedsFolderSql = """
        ((cardinality(b.parent_ids) > 0 AND NOT EXISTS (SELECT 1 FROM items x WHERE x.id = ANY(b.parent_ids)))
         OR (cardinality(b.parent_ids) = 0 AND b.project_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM projects x WHERE x.id = b.project_id)))
        """;

    public static void MapBinEndpoints(this WebApplication app, string connectionString, StorageSettings storage)
    {
        // GET /api/bin
        app.MapGet("/api/bin", async (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            var admin = user.Role == "admin";
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"""
                SELECT b.id, b.root_item_id, b.item_type, b.label, b.item_count, b.project_id,
                       coalesce(p.name, b.project_name), p.id IS NULL AND b.project_id IS NOT NULL,
                       b.location, b.deleted_by, b.deleted_by_name, b.deleted_at, {NeedsFolderSql}
                FROM recycle_bin b LEFT JOIN projects p ON p.id = b.project_id
                WHERE @admin OR b.deleted_by = @user
                ORDER BY b.deleted_at DESC;
                """, conn);
            cmd.Parameters.AddWithValue("admin", admin);
            cmd.Parameters.AddWithValue("user", user.Id);
            var entries = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new
                {
                    id = reader.GetGuid(0),
                    itemId = reader.GetGuid(1),
                    itemType = reader.GetString(2),
                    label = reader.GetString(3),
                    itemCount = reader.GetInt32(4),
                    projectId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5),
                    projectName = reader.IsDBNull(6) ? null : reader.GetString(6),
                    projectGone = reader.GetBoolean(7),
                    location = reader.IsDBNull(8) ? null : reader.GetString(8),
                    deletedById = reader.IsDBNull(9) ? (Guid?)null : reader.GetGuid(9),
                    deletedByName = reader.GetString(10),
                    deletedAt = reader.GetFieldValue<DateTimeOffset>(11),
                    needsFolder = reader.GetBoolean(12),
                    canPurge = admin,
                });
            }
            return Results.Ok(entries);
        });

        // POST /api/bin/{id}/restore  body: { folderId?, projectId? } (both optional)
        app.MapPost("/api/bin/{id:guid}/restore", async (Guid id, HttpContext ctx, RestoreBody? body) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            Guid rootId;
            Guid? deletedBy;
            Guid? originalProject;
            bool needsFolder;
            Guid[] ids;
            await using (var cmd = new NpgsqlCommand($"""
                SELECT b.root_item_id, b.deleted_by, b.project_id, {NeedsFolderSql},
                       array(SELECT (e->>'id')::uuid FROM jsonb_array_elements(b.payload->'items') e)
                FROM recycle_bin b WHERE b.id = @id FOR UPDATE;
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("id", id);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.NotFound();
                rootId = reader.GetGuid(0);
                deletedBy = reader.IsDBNull(1) ? null : reader.GetGuid(1);
                originalProject = reader.IsDBNull(2) ? null : reader.GetGuid(2);
                needsFolder = reader.GetBoolean(3);
                ids = reader.GetFieldValue<Guid[]>(4);
            }

            if (user.Role != "admin" && deletedBy != user.Id)
                return Results.Text("Only an admin or the person who deleted it can restore this.", statusCode: StatusCodes.Status403Forbidden);

            Guid? targetParent = null;
            Guid? targetProject = null;
            var moved = body?.FolderId is not null || body?.ProjectId is not null;
            if (body?.FolderId is Guid folderId)
            {
                await using var cmd = new NpgsqlCommand("SELECT item_type, project_id FROM items WHERE id = @id;", conn, tx);
                cmd.Parameters.AddWithValue("id", folderId);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.BadRequest("That folder does not exist.");
                if (reader.GetString(0) != "folder")
                    return Results.BadRequest("Restore into a folder, not into a part, assembly or file.");
                targetParent = folderId;
                targetProject = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            }
            else if (body?.ProjectId is Guid projectId)
            {
                await using var cmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @id;", conn, tx);
                cmd.Parameters.AddWithValue("id", projectId);
                if (await cmd.ExecuteScalarAsync() is null)
                    return Results.BadRequest("That project does not exist.");
                targetProject = projectId;
            }
            else if (needsFolder)
            {
                return Results.Json(
                    new { needsFolder = true, message = "The folder it was in is gone. Choose a folder or a project to restore it into." },
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (user.Role != "admin")
            {
                // Only admins delete today, so this is for a later wider delete right: putting
                // something back takes access to the project it lands in.
                var project = moved ? targetProject : originalProject;
                if (project is Guid p && !await ItemEndpoints.HasProjectAccessAsync(conn, ctx, p))
                    return ItemEndpoints.ProjectAccessForbidden();
            }

            await using (var cmd = new NpgsqlCommand("SELECT 1 FROM items WHERE id = ANY(@ids) LIMIT 1;", conn, tx))
            {
                cmd.Parameters.AddWithValue("ids", ids);
                if (await cmd.ExecuteScalarAsync() is not null)
                    return Results.Text("Part of this entry exists again in the database, so it cannot be restored.", statusCode: StatusCodes.Status409Conflict);
            }

            await RecycleBin.RestoreAsync(conn, tx, user, id, rootId, originalProject, ids,
                new RecycleBin.Placement(targetParent, targetProject, moved));

            await using (var cmd = new NpgsqlCommand("DELETE FROM recycle_bin WHERE id = @id;", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            return Results.Ok(new { itemId = rootId, restoredCount = ids.Length });
        });

        // DELETE /api/bin/{id} -- purge one entry for good.
        app.MapDelete("/api/bin/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Results.Text("Only an administrator can purge the recycle bin.", statusCode: StatusCodes.Status403Forbidden);
            var purged = await PurgeAsync(connectionString, storage, id);
            return purged == 0 ? Results.NotFound() : Results.Ok(new { purgedCount = purged });
        });

        // DELETE /api/bin -- empty the whole bin for good.
        app.MapDelete("/api/bin", async (HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Results.Text("Only an administrator can purge the recycle bin.", statusCode: StatusCodes.Status403Forbidden);
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var binIds = new List<Guid>();
            await using (var cmd = new NpgsqlCommand("SELECT id FROM recycle_bin;", conn))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    binIds.Add(reader.GetGuid(0));
            }
            var purged = 0;
            foreach (var binId in binIds)
                purged += await PurgeAsync(connectionString, storage, binId);
            return Results.Ok(new { purgedCount = purged });
        });
    }

    // Removes the entry and then its files. A file still named by a live row or by another entry
    // stays: a path is never shared today, but deleting a file something still points at would
    // break that thing for good, and skipping it costs one leftover file at worst.
    private static async Task<int> PurgeAsync(string connectionString, StorageSettings storage, Guid binId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        List<string> paths;
        Guid[] itemIds;
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await using (var cmd = new NpgsqlCommand(
                "SELECT array(SELECT (e->>'id')::uuid FROM jsonb_array_elements(payload->'items') e) FROM recycle_bin WHERE id = @id FOR UPDATE;", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", binId);
                if (await cmd.ExecuteScalarAsync() is not Guid[] found)
                    return 0;
                itemIds = found;
            }
            paths = await RecycleBin.FilePathsAsync(conn, tx, binId);
            await using (var cmd = new NpgsqlCommand("DELETE FROM recycle_bin WHERE id = @id;", conn, tx))
            {
                cmd.Parameters.AddWithValue("id", binId);
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = new NpgsqlCommand("""
                SELECT p FROM unnest(@paths::text[]) p
                WHERE EXISTS (SELECT 1 FROM items WHERE file_path = p)
                   OR EXISTS (SELECT 1 FROM item_attachments WHERE file_path = p)
                   OR EXISTS (SELECT 1 FROM item_client_verification_attachments WHERE file_path = p)
                   OR EXISTS (SELECT 1 FROM project_attachments WHERE file_path = p)
                   OR EXISTS (SELECT 1 FROM recycle_bin b, jsonb_path_query(b.payload, 'lax $.*[*].file_path') v WHERE v #>> '{}' = p);
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("paths", paths.ToArray());
                var kept = new HashSet<string>();
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        kept.Add(reader.GetString(0));
                }
                paths.RemoveAll(kept.Contains);
            }
            await tx.CommitAsync();
        }

        foreach (var path in paths)
        {
            try { File.Delete(path); } catch (IOException) { /* the rows are gone; a leftover file harms nothing */ }
        }
        return itemIds.Length;
    }
}
