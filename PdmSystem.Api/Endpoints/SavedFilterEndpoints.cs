using System.Text.Json;
using Npgsql;

// Zapisane filtry widoku "Cała baza" — każdy użytkownik ma własny, prywatny zestaw (user_id
// z sesji, nigdy z ciała żądania), więc nie ma tu ryzyka podejrzenia/skasowania cudzych presetów.
static class SavedFilterEndpoints
{
    public static void MapSavedFilterEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/saved-filters — lista zapisanych filtrów BIEŻĄCEGO użytkownika.
        app.MapGet("/api/saved-filters", async (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT id, name, filters, created_at
                FROM saved_filters
                WHERE user_id = @userId
                ORDER BY name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("userId", user.Id);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    id = reader.GetGuid(0),
                    name = reader.GetString(1),
                    filters = JsonDocument.Parse(reader.GetFieldValue<string>(2)).RootElement,
                    createdAt = reader.GetFieldValue<DateTime>(3),
                });
            }

            return Results.Ok(result);
        });

        // POST /api/saved-filters   body: { "name": "...", "filters": {...} }
        // Upsert po (user_id, name) — zapis pod istniejącą nazwą nadpisuje poprzedni preset,
        // więc "Zapisz jako X" zawsze znaczy "zaktualizuj definicję X", bez osobnego dialogu
        // "nazwa zajęta" jak przy katalogach materiałów/producentów.
        app.MapPost("/api/saved-filters", async (HttpContext ctx, SavedFilterRequest body) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa jest wymagana.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                INSERT INTO saved_filters (user_id, name, filters)
                VALUES (@userId, @name, @filters::jsonb)
                ON CONFLICT (user_id, name) DO UPDATE SET filters = EXCLUDED.filters
                RETURNING id, created_at;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("userId", user.Id);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("filters", body.Filters.GetRawText());

            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var id = reader.GetGuid(0);
            var createdAt = reader.GetFieldValue<DateTime>(1);

            return Results.Ok(new { id, name = body.Name.Trim(), filters = body.Filters, createdAt });
        });

        // DELETE /api/saved-filters/{id} — usuwa TYLKO jeśli preset należy do bieżącego
        // użytkownika (user_id w WHERE, nie tylko w id) — nie da się skasować cudzego przez
        // odgadnięcie/podejrzenie identyfikatora.
        app.MapDelete("/api/saved-filters/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "DELETE FROM saved_filters WHERE id = @id AND user_id = @userId;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("userId", user.Id);
            var affected = await cmd.ExecuteNonQueryAsync();

            return affected == 0 ? Results.NotFound() : Results.Ok();
        });
    }
}

record SavedFilterRequest(string Name, JsonElement Filters);
