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
                SELECT ir.parent_id, ir.child_id, ir.quantity
                FROM item_relations ir
                JOIN items i ON i.id = ir.parent_id
                WHERE i.project_id = @projectId;
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
                    quantity = reader.GetDecimal(2)
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

            const string sql = """
                INSERT INTO item_relations (parent_id, child_id, quantity)
                VALUES (@parentId, @childId, @quantity)
                ON CONFLICT (parent_id, child_id) DO UPDATE SET quantity = EXCLUDED.quantity;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("parentId", parentId);
            cmd.Parameters.AddWithValue("childId", body.ChildId);
            cmd.Parameters.AddWithValue("quantity", body.Quantity ?? 1);
            await cmd.ExecuteNonQueryAsync();

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
    }
}

record ChildRelationRequest(Guid ChildId, decimal? Quantity);
