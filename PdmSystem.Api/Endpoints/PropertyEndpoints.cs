using System.Text.Json;
using Npgsql;

static class PropertyEndpoints
{
    public static void MapPropertyEndpoints(this WebApplication app, string connectionString)
    {
        // ============================================================
        // PATCH /api/items/{id}/properties   body: { "material": "Stal S235", "supplier": "..." }
        // ============================================================
        app.MapPatch("/api/items/{id:guid}/properties", async (Guid id, JsonElement body) =>
        {
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
        app.MapDelete("/api/items/{id:guid}/properties/{key}", async (Guid id, string key) =>
        {
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
