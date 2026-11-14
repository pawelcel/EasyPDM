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
        // GET /api/settings/tags — tagi z liczbą elementów, które je noszą (Ustawienia -> Tagi,
        // tylko administrator). Liczy wszystkie elementy, także z projektów, do których
        // administrator formalnie nie jest przypisany — usuwanie tagu i tak działa globalnie.
        // ============================================================
        app.MapGet("/api/settings/tags", async (HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT t.name, COUNT(it.item_id)::int
                FROM tags t
                LEFT JOIN item_tags it ON it.tag_id = t.id
                GROUP BY t.id, t.name
                ORDER BY lower(t.name), t.name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            var tags = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tags.Add(new { name = reader.GetString(0), itemCount = reader.GetInt32(1) });

            return Results.Ok(tags);
        });

        // ============================================================
        // DELETE /api/settings/tags/{tagName} — usuwa tag z całej bazy (tylko administrator).
        // Wiązania w item_tags znikają kaskadą, także na elementach wydanych: tag to etykieta
        // porządkowa, nie część specyfikacji, więc blokada statusu, która chroni zmianę tagów
        // pojedynczego elementu, nie ma tu zastosowania. Bez tego raz wpisany tag (literówka,
        // zakończona akcja) zostawał w podpowiedziach i filtrze na zawsze, nawet gdy nikt go
        // już nie nosił.
        // ============================================================
        app.MapDelete("/api/settings/tags/{tagName}", async (string tagName, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // Każdy element, z którego tag znika kaskadą, dostaje wpis "tag_removed" w historii,
            // w tym samym zapytaniu (CTE czyta item_tags sprzed usunięcia).
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            await using var cmd = new NpgsqlCommand("""
                WITH gone AS (DELETE FROM tags WHERE name = @name RETURNING id),
                logged AS (
                    INSERT INTO item_change_history (item_id, change, old_value, user_id)
                    SELECT it.item_id, 'tag_removed', @name, @userId
                    FROM item_tags it JOIN gone g ON g.id = it.tag_id
                )
                SELECT count(*) FROM gone;
                """, conn);
            cmd.Parameters.AddWithValue("name", DecodeTagName(tagName));
            cmd.Parameters.AddWithValue("userId", user.Id);
            return (long)(await cmd.ExecuteScalarAsync())! == 0 ? Results.NotFound() : Results.Ok();
        });

        // ============================================================
        // POST /api/items/{id}/tags   body: { "name": "do-przegladu" }
        // ============================================================
        app.MapPost("/api/items/{id:guid}/tags", async (Guid id, TagRequest body, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("The tag name cannot be empty.");

            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            var name = body.Name.Trim();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status))
                return Results.BadRequest("Tags can only be changed while the item is In progress.");
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            const string sql = """
                INSERT INTO tags (name) VALUES (@name)
                ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name
                RETURNING id;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("name", name);
            var tagId = (int)(await cmd.ExecuteScalarAsync())!;

            // Tag, który element już ma, nie dodaje wpisu w historii (ON CONFLICT nic nie zwraca).
            const string linkSql = """
                WITH linked AS (
                    INSERT INTO item_tags (item_id, tag_id) VALUES (@itemId, @tagId)
                    ON CONFLICT DO NOTHING
                    RETURNING item_id
                )
                INSERT INTO item_change_history (item_id, change, new_value, user_id)
                SELECT item_id, 'tag_added', @name, @userId FROM linked;
                """;
            await using var linkCmd = new NpgsqlCommand(linkSql, conn);
            linkCmd.Parameters.AddWithValue("itemId", id);
            linkCmd.Parameters.AddWithValue("tagId", tagId);
            linkCmd.Parameters.AddWithValue("name", name);
            linkCmd.Parameters.AddWithValue("userId", user.Id);
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

            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status))
                return Results.BadRequest("Tags can only be changed while the item is In progress.");
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            const string sql = """
                WITH removed AS (
                    DELETE FROM item_tags
                    WHERE item_id = @itemId
                      AND tag_id = (SELECT id FROM tags WHERE name = @tagName)
                    RETURNING item_id
                )
                INSERT INTO item_change_history (item_id, change, old_value, user_id)
                SELECT item_id, 'tag_removed', @tagName, @userId FROM removed;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("itemId", id);
            cmd.Parameters.AddWithValue("tagName", DecodeTagName(tagName));
            cmd.Parameters.AddWithValue("userId", user.Id);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });
    }

    // Routing zostawia ukośnik w wartości ścieżki zakodowany ("a%2Fb"), więc tag z "/" w nazwie
    // nie trafiał w żaden wiersz: usunięcie z całej bazy kończyło się 404, a zdjęcie z elementu
    // po cichu nic nie robiło. Pozostałe znaki są już zdekodowane; nazwa tagu z dosłownym "%2F"
    // jest na tyle nieprawdopodobna, że nie warto dla niej zmieniać kształtu adresu.
    private static string DecodeTagName(string tagName) =>
        tagName.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);

    private static IResult Forbidden() => Results.Text("Administrator rights required.", statusCode: StatusCodes.Status403Forbidden);
}

record TagRequest(string Name);
