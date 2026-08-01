using Npgsql;

// RELACJE MIĘDZY ELEMENTAMI (BOM/złożenia — struktura projektu)
static class StructureEndpoints
{
    public static void MapStructureEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/projects/{projectId}/relations — wszystkie relacje rodzic-dziecko
        // dla elementów należących do danego projektu (do zbudowania drzewka po stronie klienta).
        app.MapGet("/api/projects/{projectId:guid}/relations", async (Guid projectId) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT ir.parent_id, ir.child_id, ir.quantity, ir.position
                FROM item_relations ir
                JOIN items i ON i.id = ir.parent_id
                WHERE i.project_id = @projectId
                ORDER BY ir.parent_id, ir.position;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", projectId);

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
        app.MapPost("/api/items/{parentId:guid}/children", async (Guid parentId, ChildRelationRequest body) =>
        {
            if (parentId == body.ChildId)
                return Results.BadRequest("Element nie może być podelementem samego siebie.");

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
        app.MapPatch("/api/items/{parentId:guid}/children/{childId:guid}/position", async (Guid parentId, Guid childId, PositionRequest body) =>
        {
            if (body.Position <= 0)
                return Results.BadRequest("L.p. musi być liczbą całkowitą większą od zera.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

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
        app.MapPatch("/api/items/{parentId:guid}/children/reorder", async (Guid parentId, ReorderRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
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
        app.MapDelete("/api/items/{parentId:guid}/children/{childId:guid}", async (Guid parentId, Guid childId) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

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
        app.MapPatch("/api/projects/{projectId:guid}/roots/reorder", async (Guid projectId, RootReorderRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var existingIds = new List<Guid>();
            const string selectSql = """
                SELECT i.id FROM items i
                WHERE i.project_id = @projectId AND i.show_in_tree = true
                  AND NOT EXISTS (SELECT 1 FROM item_relations ir WHERE ir.child_id = i.id);
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
