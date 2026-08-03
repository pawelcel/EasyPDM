using System.Text.Json;
using Npgsql;

static class PropertyEndpoints
{
    // Właściwości Ceny (cena/waluta/brutto-netto/data wprowadzenia ceny) można zmieniać
    // w dowolnym statusie — wszystko inne tylko w statusie 'w_pracy'.
    private static readonly HashSet<string> AlwaysEditableKeys = new()
    {
        "price", "currency", "priceType", "priceDate"
    };

    public static void MapPropertyEndpoints(this WebApplication app, string connectionString)
    {
        // ============================================================
        // PATCH /api/items/{id}/properties   body: { "material": "Stal S235", "supplier": "..." }
        // ============================================================
        app.MapPatch("/api/items/{id:guid}/properties", async (Guid id, JsonElement body, HttpContext ctx) =>
        {
            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status))
            {
                foreach (var prop in body.EnumerateObject())
                {
                    if (!AlwaysEditableKeys.Contains(prop.Name))
                        return Results.BadRequest(
                            "Właściwości można zmieniać tylko w statusie 'W pracy' (wyjątek: cena, waluta, brutto/netto).");
                }
            }

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                UPDATE items SET properties = properties || @props::jsonb
                WHERE id = @id;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("props", body.GetRawText());
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // ============================================================
        // DELETE /api/items/{id}/properties/{key}
        // ============================================================
        app.MapDelete("/api/items/{id:guid}/properties/{key}", async (Guid id, string key, HttpContext ctx) =>
        {
            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status) && !AlwaysEditableKeys.Contains(key))
                return Results.BadRequest(
                    "Właściwości można zmieniać tylko w statusie 'W pracy' (wyjątek: cena, waluta, brutto/netto).");

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "UPDATE items SET properties = properties - @key WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("key", key);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });
    }
}
