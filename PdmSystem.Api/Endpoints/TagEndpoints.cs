using Npgsql;

static class TagEndpoints
{
    public static void MapTagEndpoints(this WebApplication app, string connectionString)
    {
        // ============================================================
        // GET /api/tags — lista wszystkich istniejących tagów (do podpowiedzi/autouzupełniania).
        // ============================================================
        app.MapGet("/api/tags", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand("SELECT name FROM tags ORDER BY name;", conn);
            var names = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));

            return Results.Ok(names);
        });

        // ============================================================
        // POST /api/items/{id}/tags   body: { "name": "do-przegladu" }
        // ============================================================
        app.MapPost("/api/items/{id:guid}/tags", async (Guid id, TagRequest body, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa tagu nie może być pusta.");

            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            var name = body.Name.Trim();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            const string sql = """
                INSERT INTO tags (name) VALUES (@name)
                ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name
                RETURNING id;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("name", name);
            var tagId = (int)(await cmd.ExecuteScalarAsync())!;

            const string linkSql = """
                INSERT INTO item_tags (item_id, tag_id) VALUES (@itemId, @tagId)
                ON CONFLICT DO NOTHING;
                """;
            await using var linkCmd = new NpgsqlCommand(linkSql, conn);
            linkCmd.Parameters.AddWithValue("itemId", id);
            linkCmd.Parameters.AddWithValue("tagId", tagId);
            await linkCmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // ============================================================
        // DELETE /api/items/{id}/tags/{tagName}
        // ============================================================
        app.MapDelete("/api/items/{id:guid}/tags/{tagName}", async (Guid id, string tagName, HttpContext ctx) =>
        {
            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            const string sql = """
                DELETE FROM item_tags
                WHERE item_id = @itemId
                  AND tag_id = (SELECT id FROM tags WHERE name = @tagName);
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("itemId", id);
            cmd.Parameters.AddWithValue("tagName", tagName);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });
    }
}

record TagRequest(string Name);
