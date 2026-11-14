using Npgsql;

// Historia Części/Złożenia — do wyświetlenia na dole panelu właściwości: kiedy i kto
// utworzył element, zmiany statusu (kiedy/kto/z-na), rewizje z komentarzem (kiedy/kto/opis),
// dodanie/usunięcie załącznika (kiedy/kto/nazwa pliku) oraz zablokowanie/zwolnienie
// właściciela (kiedy/kto) oraz zmiany nazwy, właściwości i tagów (kiedy/kto/przed/po). Sześć
// źródeł (items.created_by, item_status_history, item_revision_comments,
// item_attachment_history, item_owner_history, item_change_history) połączonych jednym
// zapytaniem w chronologiczną listę.
static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/items/{id}/history — odczyt, świadomie otwarty dla KAŻDEGO zalogowanego
        // użytkownika (zob. GET /api/items), bez sprawdzenia dostępu do projektu.
        app.MapGet("/api/items/{id:guid}/history", async (Guid id, HttpContext ctx) =>
        {
            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // First and last name like everywhere else on the item, see UserNames.
            var sql = $"""
                SELECT 'created' AS type, i.created_at AS at, {UserNames.FullNameSql("u")} AS user_display_name,
                       NULL::text AS from_status, NULL::text AS to_status,
                       NULL::int AS revision_number, NULL::text AS comment, NULL::text AS file_name,
                       NULL::text AS prop_key, NULL::text AS old_value, NULL::text AS new_value
                FROM items i
                LEFT JOIN users u ON u.id = i.created_by
                WHERE i.id = @id AND i.created_at IS NOT NULL

                UNION ALL

                SELECT 'status', sh.changed_at, {UserNames.FullNameSql("u")},
                       sh.from_status, sh.to_status, NULL, NULL, NULL, NULL, NULL, NULL
                FROM item_status_history sh
                LEFT JOIN users u ON u.id = sh.changed_by
                WHERE sh.item_id = @id

                UNION ALL

                SELECT 'revision', rc.created_at, {UserNames.FullNameSql("u")},
                       NULL, NULL, rc.revision_number, rc.comment, NULL, NULL, NULL, NULL
                FROM item_revision_comments rc
                LEFT JOIN users u ON u.id = rc.created_by
                WHERE rc.item_id = @id

                UNION ALL

                SELECT CASE WHEN ah.action = 'added' THEN 'attachment_added' ELSE 'attachment_removed' END,
                       ah.at, {UserNames.FullNameSql("u")}, NULL, NULL, NULL, NULL, ah.file_name, NULL, NULL, NULL
                FROM item_attachment_history ah
                LEFT JOIN users u ON u.id = ah.user_id
                WHERE ah.item_id = @id

                UNION ALL

                SELECT CASE WHEN oh.action = 'locked' THEN 'owner_locked' ELSE 'owner_released' END,
                       oh.at, {UserNames.FullNameSql("u")}, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
                FROM item_owner_history oh
                LEFT JOIN users u ON u.id = oh.user_id
                WHERE oh.item_id = @id

                UNION ALL

                SELECT CASE ch.change WHEN 'name' THEN 'name_changed' ELSE ch.change END,
                       ch.at, {UserNames.FullNameSql("u")}, NULL, NULL, NULL, NULL, NULL,
                       ch.prop_key, ch.old_value, ch.new_value
                FROM item_change_history ch
                LEFT JOIN users u ON u.id = ch.user_id
                WHERE ch.item_id = @id

                ORDER BY at DESC;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    type = reader.GetString(0),
                    at = reader.GetDateTime(1),
                    userDisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    fromStatus = reader.IsDBNull(3) ? null : reader.GetString(3),
                    toStatus = reader.IsDBNull(4) ? null : reader.GetString(4),
                    revisionNumber = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                    revisionLabel = reader.IsDBNull(5) ? null : RevisionLabeling.Label(reader.GetInt32(5)),
                    comment = reader.IsDBNull(6) ? null : reader.GetString(6),
                    fileName = reader.IsDBNull(7) ? null : reader.GetString(7),
                    key = reader.IsDBNull(8) ? null : reader.GetString(8),
                    oldValue = reader.IsDBNull(9) ? null : reader.GetString(9),
                    newValue = reader.IsDBNull(10) ? null : reader.GetString(10),
                });
            }

            return Results.Ok(result);
        });
    }
}
