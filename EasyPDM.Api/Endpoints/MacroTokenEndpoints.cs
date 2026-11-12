using Npgsql;

// Lista tokenów makr CAD i ich odwoływanie (Ustawienia -> Użytkownicy, zob. migracja 071).
// Administrator widzi i odwołuje wszystkie, zwykły użytkownik — tylko swoje.
static class MacroTokenEndpoints
{
    public static void MapMacroTokenEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/macro-tokens -> [{ id, userId, username, displayName, client, createdAt, lastUsedAt, expiresAt }]
        app.MapGet("/api/macro-tokens", async (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT m.id, u.id, u.username, u.display_name, m.client, m.created_at, m.last_used_at, m.expires_at
                FROM macro_tokens m JOIN users u ON u.id = m.user_id
                WHERE m.expires_at > now() AND (@isAdmin OR m.user_id = @userId)
                ORDER BY m.last_used_at DESC;
                """, conn);
            cmd.Parameters.AddWithValue("isAdmin", user.Role == "admin");
            cmd.Parameters.AddWithValue("userId", user.Id);
            var tokens = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tokens.Add(new
                {
                    id = reader.GetGuid(0),
                    userId = reader.GetGuid(1),
                    username = reader.GetString(2),
                    displayName = reader.GetString(3),
                    client = reader.GetString(4),
                    createdAt = reader.GetDateTime(5),
                    lastUsedAt = reader.GetDateTime(6),
                    expiresAt = reader.GetDateTime(7),
                });
            }
            return Results.Ok(tokens);
        });

        // DELETE /api/macro-tokens/{id} — makro z tym tokenem przy następnym zapytaniu dostaje 401
        // i prosi o ponowne zalogowanie.
        app.MapDelete("/api/macro-tokens/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM macro_tokens WHERE id = @id AND (@isAdmin OR user_id = @userId);", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("isAdmin", user.Role == "admin");
            cmd.Parameters.AddWithValue("userId", user.Id);
            return await cmd.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.Ok();
        });
    }
}
