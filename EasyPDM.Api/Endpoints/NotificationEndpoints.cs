using System.Text.Json;
using Npgsql;

// Powiadomienia — lista dla zalogowanego użytkownika, oznaczanie jako przeczytane, oraz
// jego własne preferencje (które typy chce dostawać). Treść powiadomienia (pole "data")
// jest renderowana PO STRONIE FRONTU (i18n) — backend zwraca tylko surowe dane zapisane
// w momencie zdarzenia, zob. Notifications.cs.
static class NotificationEndpoints
{
    // Stała lista wszystkich typów — używana przez GET /api/notification-preferences,
    // żeby front zawsze dostał kompletną listę checkboxów, nawet dla typów, których
    // użytkownik jeszcze nigdy nie dostał/nie zmienił.
    private static readonly string[] AllTypes =
    [
        "status_review", "status_released", "status_regressed", "new_revision",
        "project_assigned", "project_unassigned", "project_deleted",
        "password_changed", "low_disk_space", "sample_project"
    ];

    public static void MapNotificationEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/notifications?limit=50 — najnowsze powiadomienia wołającego, plus łączna
        // liczba nieprzeczytanych (osobne zapytanie, bez limitu — plakietka ma być poprawna
        // nawet gdy nieprzeczytanych jest więcej niż zwrócona strona).
        app.MapGet("/api/notifications", async (HttpContext ctx, int? limit) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string countSql = "SELECT count(*) FROM notifications WHERE user_id = @userId AND read_at IS NULL;";
            await using var countCmd = new NpgsqlCommand(countSql, conn);
            countCmd.Parameters.AddWithValue("userId", user.Id);
            var unreadCount = (long)(await countCmd.ExecuteScalarAsync())!;

            const string listSql = """
                SELECT id, type, data, item_id, project_id, read_at, created_at
                FROM notifications
                WHERE user_id = @userId
                ORDER BY created_at DESC
                LIMIT @limit;
                """;
            await using var cmd = new NpgsqlCommand(listSql, conn);
            cmd.Parameters.AddWithValue("userId", user.Id);
            cmd.Parameters.AddWithValue("limit", Math.Clamp(limit ?? 50, 1, 200));

            var items = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                items.Add(new
                {
                    id = reader.GetGuid(0),
                    type = reader.GetString(1),
                    data = JsonDocument.Parse(reader.GetFieldValue<string>(2)).RootElement,
                    itemId = reader.IsDBNull(3) ? (Guid?)null : reader.GetGuid(3),
                    projectId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                    readAt = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5),
                    createdAt = reader.GetDateTime(6),
                });
            }

            return Results.Ok(new { unreadCount, items });
        });

        // POST /api/notifications/{id}/read — oznacza jedno powiadomienie jako przeczytane.
        // WHERE user_id = @userId zamiast osobnego sprawdzenia własności — próba oznaczenia
        // cudzego powiadomienia po prostu nic nie zmienia (0 wierszy), zwracamy 200 i tak,
        // żeby front nie musiał tego specjalnie rozróżniać.
        app.MapPost("/api/notifications/{id:guid}/read", async (Guid id, HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "UPDATE notifications SET read_at = now() WHERE id = @id AND user_id = @userId AND read_at IS NULL;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("userId", user.Id);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // DELETE /api/notifications/{id} — usuwa jedno powiadomienie. WHERE user_id = @userId
        // zamiast osobnego sprawdzenia własności — ten sam styl co POST .../read powyżej.
        app.MapDelete("/api/notifications/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "DELETE FROM notifications WHERE id = @id AND user_id = @userId;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("userId", user.Id);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // POST /api/notifications/read-all — oznacza WSZYSTKIE powiadomienia wołającego.
        app.MapPost("/api/notifications/read-all", async (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "UPDATE notifications SET read_at = now() WHERE user_id = @userId AND read_at IS NULL;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("userId", user.Id);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // GET /api/notification-preferences — WSZYSTKIE typy (stała lista, nie tylko te, dla
        // których wołający ma już jawny wiersz), z rozwiązanym "enabled" (domyślnie true, gdy
        // brak wiersza — opt-out, nie opt-in). Front dostaje gotową listę checkboxów bez
        // własnej wiedzy o domyślnych wartościach.
        app.MapGet("/api/notification-preferences", async (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var overrides = new Dictionary<string, bool>();
            await using (var cmd = new NpgsqlCommand(
                "SELECT type, enabled FROM notification_preferences WHERE user_id = @userId;", conn))
            {
                cmd.Parameters.AddWithValue("userId", user.Id);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    overrides[reader.GetString(0)] = reader.GetBoolean(1);
            }

            var result = AllTypes.Select(type => new
            {
                type,
                enabled = !overrides.TryGetValue(type, out var enabled) || enabled,
            });

            return Results.Ok(result);
        });

        // PATCH /api/notification-preferences   body: { "type": "...", "enabled": bool }
        app.MapPatch("/api/notification-preferences", async (HttpContext ctx, NotificationPreferenceRequest body) =>
        {
            if (!AllTypes.Contains(body.Type))
                return Results.BadRequest("Nieprawidłowy typ powiadomienia.");

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                INSERT INTO notification_preferences (user_id, type, enabled)
                VALUES (@userId, @type, @enabled)
                ON CONFLICT (user_id, type) DO UPDATE SET enabled = EXCLUDED.enabled;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("userId", user.Id);
            cmd.Parameters.AddWithValue("type", body.Type);
            cmd.Parameters.AddWithValue("enabled", body.Enabled);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });
    }

    private record NotificationPreferenceRequest(string Type, bool Enabled);
}
