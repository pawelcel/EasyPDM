using System.Text.Json;
using Npgsql;

// RELACJE MIĘDZY ELEMENTAMI (BOM/złożenia — struktura projektu)
static class StructureEndpoints
{
    public static void MapStructureEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/items/{id}/children — bezpośrednie dzieci (item_relations, depth=1) tego
        // elementu jako PEŁNE obiekty Item + quantity/position — dla widoków, które (w
        // odróżnieniu od widoku jednego projektu) nie mają załadowanego całego drzewka
        // relacji z góry, np. "Cała baza" (item-list.tsx), gdzie zaznaczony element może
        // być z dowolnego projektu. Odczyt elementu — świadomie otwarty dla KAŻDEGO
        // zalogowanego użytkownika (zob. GET /api/items), bez sprawdzenia dostępu do projektu.
        app.MapGet("/api/items/{id:guid}/children", async (Guid id, HttpContext ctx) =>
        {
            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT i.id, i.project_id, i.file_name, i.file_type, i.file_path, i.properties, i.modified_at,
                       i.item_type, i.item_number, i.item_number_prefix, i.show_in_tree, i.status, i.revision_number,
                       i.root_position, i.owner_id, i.owner_locked, u.display_name, ir.quantity, ir.position
                FROM item_relations ir
                JOIN items i ON i.id = ir.child_id
                LEFT JOIN users u ON u.id = i.owner_id
                WHERE ir.parent_id = @id
                ORDER BY ir.position;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            var rows = new List<(Dictionary<string, object?> Item, decimal Quantity, int Position)>();
            var childIds = new List<Guid>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var childId = reader.GetGuid(0);
                    childIds.Add(childId);
                    var itemDict = new Dictionary<string, object?>
                    {
                        ["id"] = childId,
                        ["projectId"] = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                        ["fileName"] = reader.GetString(2),
                        ["fileType"] = reader.IsDBNull(3) ? null : reader.GetString(3),
                        ["filePath"] = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ["properties"] = JsonDocument.Parse(reader.GetFieldValue<string>(5)).RootElement,
                        ["modifiedAt"] = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                        ["itemType"] = reader.GetString(7),
                        ["itemNumber"] = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        ["itemNumberPrefix"] = reader.IsDBNull(9) ? null : reader.GetString(9),
                        ["showInTree"] = reader.GetBoolean(10),
                        ["status"] = reader.IsDBNull(11) ? null : reader.GetString(11),
                        ["revisionNumber"] = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                        ["rootPosition"] = reader.GetInt32(13),
                        ["ownerId"] = reader.IsDBNull(14) ? null : reader.GetGuid(14),
                        ["ownerLocked"] = reader.GetBoolean(15),
                        ["ownerDisplayName"] = reader.IsDBNull(16) ? null : reader.GetString(16),
                        ["tags"] = new List<string>()
                    };
                    rows.Add((itemDict, reader.GetDecimal(17), reader.GetInt32(18)));
                }
            }

            if (childIds.Count > 0)
            {
                var tagsByItem = await ItemEndpoints.LoadTagsForItems(connectionString, childIds);
                foreach (var (itemDict, _, _) in rows)
                {
                    var childId = (Guid)itemDict["id"]!;
                    itemDict["tags"] = tagsByItem.TryGetValue(childId, out var t) ? t : new List<string>();
                }
            }

            var response = rows.Select(r => new { item = r.Item, quantity = r.Quantity, position = r.Position });
            return Results.Ok(response);
        });

        // GET /api/projects/{projectId}/relations — wszystkie relacje rodzic-dziecko
        // dla elementów należących do danego projektu (do zbudowania drzewka po stronie klienta).
        // Nieprzypisany zwykły użytkownik dostaje pustą listę (nie błąd) — z jego punktu widzenia
        // projekt po prostu nie ma żadnej struktury do pokazania, spójnie z tym, że w ogóle nie
        // widzi go na liście projektów (GET /api/projects).
        app.MapGet("/api/projects/{projectId:guid}/relations", async (Guid projectId, HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT ir.parent_id, ir.child_id, ir.quantity, ir.position
                FROM item_relations ir
                JOIN items i ON i.id = ir.parent_id
                WHERE i.project_id = @projectId
                  AND (@isAdmin OR EXISTS (
                        SELECT 1 FROM project_users pu WHERE pu.project_id = @projectId AND pu.user_id = @userId
                  ))
                ORDER BY ir.parent_id, ir.position;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("isAdmin", user.Role == "admin");
            cmd.Parameters.AddWithValue("userId", user.Id);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    parentId = reader.GetGuid(0),
                    childId = reader.GetGuid(1),
                    quantity = reader.GetDecimal(2),
                    position = reader.GetInt32(3)
                });
            }

            return Results.Ok(result);
        });

        // POST /api/items/{parentId}/children   body: { "childId": "...", "quantity": 1 }
        // Dodaje element jako podelement innego elementu (krawędź w strukturze złożenia).
        app.MapPost("/api/items/{parentId:guid}/children", async (Guid parentId, ChildRelationRequest body, HttpContext ctx) =>
        {
            if (parentId == body.ChildId)
                return Results.BadRequest("Element nie może być podelementem samego siebie.");

            if (body.Quantity is <= 0)
                return Results.BadRequest("Ilość musi być większa od zera.");

            var parentInfo = await ItemEndpoints.GetItemTypeAndStatus(connectionString, parentId);
            if (parentInfo is null)
                return Results.NotFound("Element nadrzędny nie istnieje.");
            var childInfo = await ItemEndpoints.GetItemTypeAndStatus(connectionString, body.ChildId);
            if (childInfo is null)
                return Results.NotFound("Element podrzędny nie istnieje.");
            if (!ItemEndpoints.IsChildTypeAllowed(parentInfo.Value.ItemType, childInfo.Value.ItemType))
                return Results.BadRequest("Do tego elementu nie można nic dodać w strukturze.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, parentInfo.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            // Dostęp sprawdzany też do projektu DZIECKA, nie tylko rodzica — bez tego
            // użytkownik z dostępem tylko do projektu A mógłby dopisać do BOM-u element z
            // zupełnie innego, prywatnego projektu B, do którego nie ma żadnych uprawnień.
            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, childInfo.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, parentInfo.Value.OwnerId, parentInfo.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            // Zapobiega cyklowi: odrzuć, jeśli parentId jest już potomkiem childId
            // (czyli dodanie tej krawędzi zamknęłoby pętlę w strukturze).
            const string cycleCheckSql = """
                WITH RECURSIVE descendants AS (
                    SELECT child_id FROM item_relations WHERE parent_id = @childId
                    UNION
                    SELECT ir.child_id FROM item_relations ir
                    JOIN descendants d ON ir.parent_id = d.child_id
                )
                SELECT 1 FROM descendants WHERE child_id = @parentId;
                """;
            await using (var cycleCmd = new NpgsqlCommand(cycleCheckSql, conn))
            {
                cycleCmd.Parameters.AddWithValue("childId", body.ChildId);
                cycleCmd.Parameters.AddWithValue("parentId", parentId);
                if (await cycleCmd.ExecuteScalarAsync() is not null)
                    return Results.BadRequest("Nie można dodać — spowodowałoby to cykl w strukturze.");
            }

            // Nowa relacja trafia na koniec BOM-u rodzica (position = max + 1); przy aktualizacji
            // samej ilości (ON CONFLICT) pozycja zostaje bez zmian — nie miesza kolejności BOM-u.
            const string sql = """
                INSERT INTO item_relations (parent_id, child_id, quantity, position)
                VALUES (
                    @parentId, @childId, @quantity,
                    COALESCE((SELECT MAX(position) FROM item_relations WHERE parent_id = @parentId), 0) + 1
                )
                ON CONFLICT (parent_id, child_id) DO UPDATE SET quantity = EXCLUDED.quantity;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("parentId", parentId);
            cmd.Parameters.AddWithValue("childId", body.ChildId);
            cmd.Parameters.AddWithValue("quantity", body.Quantity ?? 1);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // PATCH /api/items/{parentId}/children/{childId}/position   body: { "position": 3 }
        // Ręczna zmiana L.p. jednej pozycji BOM — wpisanie liczby wprost. Numer musi być
        // dodatnią liczbą całkowitą i nie może powtarzać się wśród innych podelementów tego
        // samego rodzica (bez automatycznego przesuwania pozostałych — użytkownik musi wybrać
        // wolny numer, albo skorzystać z przeciągnięcia, które przenumerowuje całość).
        app.MapPatch("/api/items/{parentId:guid}/children/{childId:guid}/position", async (Guid parentId, Guid childId, PositionRequest body, HttpContext ctx) =>
        {
            if (body.Position <= 0)
                return Results.BadRequest("L.p. musi być liczbą całkowitą większą od zera.");

            var parentInfo = await ItemEndpoints.GetItemTypeAndStatus(connectionString, parentId);
            if (parentInfo is null)
                return Results.NotFound("Element nadrzędny nie istnieje.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, parentInfo.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, parentInfo.Value.OwnerId, parentInfo.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            await using (var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM item_relations WHERE parent_id = @parentId AND child_id != @childId AND position = @position;", conn))
            {
                checkCmd.Parameters.AddWithValue("parentId", parentId);
                checkCmd.Parameters.AddWithValue("childId", childId);
                checkCmd.Parameters.AddWithValue("position", body.Position);
                if (await checkCmd.ExecuteScalarAsync() is not null)
                    return Results.BadRequest("Ten numer L.p. jest już zajęty w tym BOM-ie.");
            }

            const string sql = "UPDATE item_relations SET position = @position WHERE parent_id = @parentId AND child_id = @childId;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("parentId", parentId);
            cmd.Parameters.AddWithValue("childId", childId);
            cmd.Parameters.AddWithValue("position", body.Position);
            var affected = await cmd.ExecuteNonQueryAsync();

            return affected == 0 ? Results.NotFound() : Results.Ok();
        });

        // PATCH /api/items/{parentId}/children/reorder   body: { "childIds": ["...", "...", ...] }
        // Przeciągnięcie podelementu w inne miejsce w UI — klient wysyła CAŁĄ nową kolejność
        // dzieci tego rodzica, a serwer przenumerowuje L.p. na 1..N w jednej transakcji.
        app.MapPatch("/api/items/{parentId:guid}/children/reorder", async (Guid parentId, ReorderRequest body, HttpContext ctx) =>
        {
            var parentInfo = await ItemEndpoints.GetItemTypeAndStatus(connectionString, parentId);
            if (parentInfo is null)
                return Results.NotFound("Element nadrzędny nie istnieje.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, parentInfo.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, parentInfo.Value.OwnerId, parentInfo.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            await using var tx = await conn.BeginTransactionAsync();

            var existingIds = new List<Guid>();
            await using (var selectCmd = new NpgsqlCommand(
                "SELECT child_id FROM item_relations WHERE parent_id = @parentId;", conn, tx))
            {
                selectCmd.Parameters.AddWithValue("parentId", parentId);
                await using var reader = await selectCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    existingIds.Add(reader.GetGuid(0));
            }

            if (existingIds.Count != body.ChildIds.Count || existingIds.ToHashSet().SetEquals(body.ChildIds) is false)
            {
                await tx.RollbackAsync();
                return Results.BadRequest("Nowa kolejność musi zawierać dokładnie te same podelementy, co obecny BOM.");
            }

            for (var i = 0; i < body.ChildIds.Count; i++)
            {
                await using var updateCmd = new NpgsqlCommand(
                    "UPDATE item_relations SET position = @position WHERE parent_id = @parentId AND child_id = @childId;",
                    conn, tx);
                updateCmd.Parameters.AddWithValue("parentId", parentId);
                updateCmd.Parameters.AddWithValue("childId", body.ChildIds[i]);
                updateCmd.Parameters.AddWithValue("position", i + 1);
                await updateCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return Results.Ok();
        });

        // DELETE /api/items/{parentId}/children/{childId} — usuwa powiązanie (nie usuwa elementu).
        app.MapDelete("/api/items/{parentId:guid}/children/{childId:guid}", async (Guid parentId, Guid childId, HttpContext ctx) =>
        {
            var parentInfo = await ItemEndpoints.GetItemTypeAndStatus(connectionString, parentId);
            if (parentInfo is null)
                return Results.NotFound("Element nadrzędny nie istnieje.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, parentInfo.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, parentInfo.Value.OwnerId, parentInfo.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            const string sql = "DELETE FROM item_relations WHERE parent_id = @parentId AND child_id = @childId;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("parentId", parentId);
            cmd.Parameters.AddWithValue("childId", childId);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // PATCH /api/projects/{projectId}/roots/reorder   body: { "itemIds": ["...", "...", ...] }
        // To samo co /children/reorder, ale dla elementów bez rodzica (korzeni drzewka
        // w danym projekcie) — te nie mają wpisu w item_relations, więc kolejność trzyma
        // osobna kolumna items.root_position, przenumerowywana tu na 1..N.
        app.MapPatch("/api/projects/{projectId:guid}/roots/reorder", async (Guid projectId, RootReorderRequest body, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return ItemEndpoints.ProjectAccessForbidden();

            await using var tx = await conn.BeginTransactionAsync();

            // Korzeń = show_in_tree=true, NIEZALEŻNIE od tego, czy element ma też rodzica gdzie
            // indziej -- element może być jednocześnie widoczny jako korzeń projektu I zagnieżdżony
            // pod złożeniem (np. część dodana do projektu, a potem dołączona jako podelement już
            // istniejącego złożenia -- oba miejsca mają pozostać widoczne, patrz komentarz przy
            // show_in_tree w item-creation insertach).
            var existingIds = new List<Guid>();
            const string selectSql = """
                SELECT i.id FROM items i
                WHERE i.project_id = @projectId AND i.show_in_tree = true;
                """;
            await using (var selectCmd = new NpgsqlCommand(selectSql, conn, tx))
            {
                selectCmd.Parameters.AddWithValue("projectId", projectId);
                await using var reader = await selectCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    existingIds.Add(reader.GetGuid(0));
            }

            if (existingIds.Count != body.ItemIds.Count || existingIds.ToHashSet().SetEquals(body.ItemIds) is false)
            {
                await tx.RollbackAsync();
                return Results.BadRequest("Nowa kolejność musi zawierać dokładnie te same korzenie, co obecne drzewko.");
            }

            for (var i = 0; i < body.ItemIds.Count; i++)
            {
                await using var updateCmd = new NpgsqlCommand(
                    "UPDATE items SET root_position = @position WHERE id = @id;", conn, tx);
                updateCmd.Parameters.AddWithValue("id", body.ItemIds[i]);
                updateCmd.Parameters.AddWithValue("position", i + 1);
                await updateCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return Results.Ok();
        });
    }
}

record ChildRelationRequest(Guid ChildId, decimal? Quantity);
record PositionRequest(int Position);
record RootReorderRequest(List<Guid> ItemIds);
record ReorderRequest(List<Guid> ChildIds);
