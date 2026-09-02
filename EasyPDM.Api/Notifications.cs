using System.Text.Json;
using Npgsql;

// Wspólny helper do tworzenia powiadomień z dowolnego endpointu/usługi w tle. Sprawdza
// najpierw preferencje odbiorcy (notification_preferences) -- brak wiersza oznacza
// włączone (opt-out, nie opt-in). Celowo nigdy nie rzuca dalej -- błąd zapisania
// powiadomienia nie ma nigdy cofać właściwej akcji, która je wywołała.
static class Notifications
{
    public static async Task NotifyAsync(
        NpgsqlConnection conn, ILogger logger, Guid userId, string type, object data,
        Guid? itemId = null, Guid? projectId = null, NpgsqlTransaction? tx = null)
    {
        try
        {
            const string prefSql =
                "SELECT enabled FROM notification_preferences WHERE user_id = @userId AND type = @type;";
            await using (var prefCmd = new NpgsqlCommand(prefSql, conn, tx))
            {
                prefCmd.Parameters.AddWithValue("userId", userId);
                prefCmd.Parameters.AddWithValue("type", type);
                if (await prefCmd.ExecuteScalarAsync() is false)
                    return; // jawnie wyłączone przez odbiorcę
            }

            const string insertSql = """
                INSERT INTO notifications (user_id, type, data, item_id, project_id)
                VALUES (@userId, @type, @data::jsonb, @itemId, @projectId);
                """;
            await using var cmd = new NpgsqlCommand(insertSql, conn, tx);
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("type", type);
            cmd.Parameters.AddWithValue("data", JsonSerializer.Serialize(data));
            cmd.Parameters.AddWithValue("itemId", (object?)itemId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("projectId", (object?)projectId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Nie udało się zapisać powiadomienia typu '{Type}' dla użytkownika {UserId}.", type, userId);
        }
    }
}
