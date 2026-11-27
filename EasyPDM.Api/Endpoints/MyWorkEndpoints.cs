using Npgsql;

// "My work" (main menu): the projects someone takes part in -- as the company-side lead, a team
// member or assigned to an assembly -- with the assemblies they are assigned to, and progress
// reports on each (item_progress, migration 086): a note and a percentage, one after another.
// The project's lead sees every assembly of the project that has people on it, with everyone's
// reports, and accepts a report or sends it back with remarks. The author can delete their latest
// report for system_state.progress_delete_minutes after writing it, and only until reviewed.
static class MyWorkEndpoints
{
    private const int MaxNoteLength = 2000;

    private const string UserName =
        "COALESCE(NULLIF(btrim(concat_ws(' ', u.first_name, u.last_name)), ''), u.display_name)";

    public static void MapMyWorkEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/my-work
        app.MapGet("/api/my-work", async (HttpContext ctx) =>
        {
            var me = (CurrentUser)ctx.Items["CurrentUser"]!;
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var deleteMinutes = await DeleteMinutesAsync(conn);

            // Projects I take part in, and whether I lead them.
            var projects = new List<(Guid Id, string Name, bool IsLead)>();
            await using (var cmd = new NpgsqlCommand("""
                SELECT p.id, p.name, p.lead_user_id = @me
                FROM projects p
                WHERE p.lead_user_id = @me
                   OR EXISTS (SELECT 1 FROM project_team t WHERE t.project_id = p.id AND t.user_id = @me)
                   OR EXISTS (SELECT 1 FROM item_assignees a JOIN items i ON i.id = a.item_id
                              WHERE i.project_id = p.id AND a.user_id = @me)
                ORDER BY p.closed, p.name;
                """, conn))
            {
                cmd.Parameters.AddWithValue("me", me.Id);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    projects.Add((reader.GetGuid(0), reader.GetString(1), !reader.IsDBNull(2) && reader.GetBoolean(2)));
            }
            var projectIds = projects.Select(p => p.Id).ToArray();
            var ledIds = projects.Where(p => p.IsLead).Select(p => p.Id).ToArray();

            // Assemblies to show: all with people on them in a project I lead, mine elsewhere.
            var assemblies = new List<(Guid Id, Guid ProjectId, string Label)>();
            await using (var cmd = new NpgsqlCommand("""
                SELECT i.id, i.project_id, i.item_number, i.item_number_prefix, i.item_number_digits, i.item_number_with_name, i.file_name
                FROM items i
                WHERE i.item_type = 'assembly' AND i.project_id = ANY(@projects)
                  AND (EXISTS (SELECT 1 FROM item_assignees a WHERE a.item_id = i.id AND a.user_id = @me)
                       OR (i.project_id = ANY(@led) AND EXISTS (SELECT 1 FROM item_assignees a WHERE a.item_id = i.id)))
                ORDER BY i.item_number NULLS LAST, i.file_name;
                """, conn))
            {
                cmd.Parameters.AddWithValue("projects", projectIds);
                cmd.Parameters.AddWithValue("led", ledIds);
                cmd.Parameters.AddWithValue("me", me.Id);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    assemblies.Add((reader.GetGuid(0), reader.GetGuid(1), ItemNumbering.RecordName(
                        reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        reader.IsDBNull(5) ? null : reader.GetBoolean(5),
                        reader.GetString(6))));
            }
            var itemIds = assemblies.Select(a => a.Id).ToArray();

            var assignees = new Dictionary<Guid, List<object>>();
            var mine = new HashSet<Guid>();
            await using (var cmd = new NpgsqlCommand($"""
                SELECT a.item_id, u.id, {UserName}, a.is_lead
                FROM item_assignees a JOIN users u ON u.id = a.user_id
                WHERE a.item_id = ANY(@items)
                ORDER BY a.is_lead DESC, a.added_at;
                """, conn))
            {
                cmd.Parameters.AddWithValue("items", itemIds);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var itemId = reader.GetGuid(0);
                    if (reader.GetGuid(1) == me.Id) mine.Add(itemId);
                    if (!assignees.TryGetValue(itemId, out var list)) assignees[itemId] = list = [];
                    list.Add(new { userId = reader.GetGuid(1), name = reader.GetString(2), isLead = reader.GetBoolean(3) });
                }
            }

            var entries = new Dictionary<Guid, List<Entry>>();
            await using (var cmd = new NpgsqlCommand($"""
                SELECT e.id, e.item_id, e.user_id, COALESCE({UserName}, ''), e.note, e.percent, e.created_at,
                       e.review_status, e.review_comment,
                       (SELECT COALESCE(NULLIF(btrim(concat_ws(' ', r.first_name, r.last_name)), ''), r.display_name)
                        FROM users r WHERE r.id = e.reviewed_by),
                       e.reviewed_at
                FROM item_progress e LEFT JOIN users u ON u.id = e.user_id
                WHERE e.item_id = ANY(@items)
                ORDER BY e.created_at, e.id;
                """, conn))
            {
                cmd.Parameters.AddWithValue("items", itemIds);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var itemId = reader.GetGuid(1);
                    if (!entries.TryGetValue(itemId, out var list)) entries[itemId] = list = [];
                    list.Add(new Entry(reader.GetInt64(0), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3),
                        reader.GetString(4), reader.GetInt32(5), reader.GetFieldValue<DateTimeOffset>(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.IsDBNull(9) ? null : reader.GetString(9),
                        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10)));
                }
            }

            var now = DateTimeOffset.UtcNow;
            object EntryJson(Entry e, Guid itemId) => new
            {
                id = e.Id,
                authorId = e.AuthorId,
                authorName = e.AuthorName,
                note = e.Note,
                percent = e.Percent,
                createdAt = e.CreatedAt,
                reviewStatus = e.ReviewStatus,
                reviewComment = e.ReviewComment,
                reviewerName = e.ReviewerName,
                reviewedAt = e.ReviewedAt,
                canDelete = e.AuthorId == me.Id && e.ReviewStatus is null
                    && now - e.CreatedAt <= TimeSpan.FromMinutes(deleteMinutes)
                    && entries[itemId].Where(x => x.AuthorId == me.Id).Last().Id == e.Id,
            };

            return Results.Ok(new
            {
                deleteMinutes,
                projects = projects.Select(p => new
                {
                    projectId = p.Id,
                    projectName = p.Name,
                    isLead = p.IsLead,
                    assemblies = assemblies.Where(a => a.ProjectId == p.Id).Select(a => new
                    {
                        itemId = a.Id,
                        label = a.Label,
                        canReport = mine.Contains(a.Id),
                        assignees = assignees.GetValueOrDefault(a.Id) ?? [],
                        entries = (entries.GetValueOrDefault(a.Id) ?? []).Select(e => EntryJson(e, a.Id)),
                    }),
                }),
            });
        });

        // POST /api/items/{itemId}/progress   body: { note, percent } -- by someone assigned to it
        app.MapPost("/api/items/{itemId:guid}/progress", async (Guid itemId, HttpContext ctx, ProgressRequest body) =>
        {
            var me = (CurrentUser)ctx.Items["CurrentUser"]!;
            var note = (body.Note ?? "").Trim();
            if (note.Length == 0)
                return Results.BadRequest("Write what was done.");
            if (note.Length > MaxNoteLength)
                return Results.BadRequest($"The entry can be at most {MaxNoteLength} characters long.");
            if (body.Percent is null or < 0 or > 100)
                return Results.BadRequest("The progress must be between 0 and 100 %.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using (var check = new NpgsqlCommand("SELECT 1 FROM item_assignees WHERE item_id = @itemId AND user_id = @me;", conn))
            {
                check.Parameters.AddWithValue("itemId", itemId);
                check.Parameters.AddWithValue("me", me.Id);
                if (await check.ExecuteScalarAsync() is null)
                    return Results.Text("Only someone assigned to this assembly reports on it.", statusCode: StatusCodes.Status403Forbidden);
            }
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO item_progress (item_id, user_id, note, percent) VALUES (@itemId, @me, @note, @percent) RETURNING id;", conn);
            cmd.Parameters.AddWithValue("itemId", itemId);
            cmd.Parameters.AddWithValue("me", me.Id);
            cmd.Parameters.AddWithValue("note", note);
            cmd.Parameters.AddWithValue("percent", body.Percent.Value);
            return Results.Ok(new { id = (long)(await cmd.ExecuteScalarAsync())! });
        });

        // DELETE /api/progress/{id} -- the author's latest entry, within the time allowed, unreviewed
        app.MapDelete("/api/progress/{id:long}", async (long id, HttpContext ctx) =>
        {
            var me = (CurrentUser)ctx.Items["CurrentUser"]!;
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var minutes = await DeleteMinutesAsync(conn);
            await using var cmd = new NpgsqlCommand("""
                DELETE FROM item_progress e
                WHERE e.id = @id AND e.user_id = @me AND e.review_status IS NULL
                  AND e.created_at >= now() - make_interval(mins => @minutes)
                  AND NOT EXISTS (SELECT 1 FROM item_progress later
                                  WHERE later.item_id = e.item_id AND later.user_id = e.user_id
                                    AND (later.created_at, later.id) > (e.created_at, e.id));
                """, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("me", me.Id);
            cmd.Parameters.AddWithValue("minutes", minutes);
            if (await cmd.ExecuteNonQueryAsync() == 0)
                return Results.Conflict($"Only your latest entry can be deleted, within {minutes} minutes of writing it and before it is reviewed.");
            return Results.Ok();
        });

        // PUT /api/progress/{id}/review   body: { status: "accepted" | "remarks", comment }
        app.MapPut("/api/progress/{id:long}/review", async (long id, HttpContext ctx, ReviewRequest body) =>
        {
            var me = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (body.Status is not ("accepted" or "remarks"))
                return Results.BadRequest("Accept the entry or add remarks.");
            var comment = (body.Comment ?? "").Trim();
            if (body.Status == "remarks" && comment.Length == 0)
                return Results.BadRequest("Remarks need a comment.");
            if (comment.Length > MaxNoteLength)
                return Results.BadRequest($"The comment can be at most {MaxNoteLength} characters long.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            Guid itemId;
            Guid? authorId;
            string projectName;
            await using (var find = new NpgsqlCommand("""
                SELECT e.item_id, e.user_id, p.name, p.lead_user_id = @me
                FROM item_progress e JOIN items i ON i.id = e.item_id JOIN projects p ON p.id = i.project_id
                WHERE e.id = @id;
                """, conn))
            {
                find.Parameters.AddWithValue("id", id);
                find.Parameters.AddWithValue("me", me.Id);
                await using var reader = await find.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.NotFound();
                if (reader.IsDBNull(3) || !reader.GetBoolean(3))
                    return Results.Text("Only the project's lead reviews progress.", statusCode: StatusCodes.Status403Forbidden);
                itemId = reader.GetGuid(0);
                authorId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
                projectName = reader.GetString(2);
            }

            await using (var cmd = new NpgsqlCommand("""
                UPDATE item_progress SET review_status = @status, review_comment = @comment, reviewed_by = @me, reviewed_at = now()
                WHERE id = @id;
                """, conn))
            {
                cmd.Parameters.AddWithValue("status", body.Status);
                cmd.Parameters.AddWithValue("comment", comment.Length == 0 ? DBNull.Value : comment);
                cmd.Parameters.AddWithValue("me", me.Id);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            if (authorId is not null && authorId != me.Id)
            {
                var itemLabel = await ItemLabelAsync(conn, itemId);
                await Notifications.NotifyAsync(conn, app.Logger, authorId.Value, "progress_reviewed",
                    new { itemLabel, projectName, status = body.Status, reviewedBy = me.DisplayName }, itemId: itemId);
            }
            return Results.Ok();
        });

        // GET/PATCH /api/settings/progress-policy -> { deleteMinutes }  (PATCH: administrator)
        app.MapGet("/api/settings/progress-policy", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            return Results.Ok(new { deleteMinutes = await DeleteMinutesAsync(conn) });
        });
        app.MapPatch("/api/settings/progress-policy", async (HttpContext ctx, ProgressPolicyRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Results.Text("Administrator rights required.", statusCode: StatusCodes.Status403Forbidden);
            if (body.DeleteMinutes is null or < 0 or > 10080)
                return Results.BadRequest("Give a number of minutes from 0 to 10080 (a week).");
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO system_state (id, progress_delete_minutes) VALUES (true, @minutes)
                ON CONFLICT (id) DO UPDATE SET progress_delete_minutes = @minutes;
                """, conn);
            cmd.Parameters.AddWithValue("minutes", body.DeleteMinutes.Value);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok(new { deleteMinutes = await DeleteMinutesAsync(conn) });
        });
    }

    // No system_state row yet = the default of 15 minutes.
    private static async Task<int> DeleteMinutesAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand("SELECT progress_delete_minutes FROM system_state WHERE id = true;", conn);
        return await cmd.ExecuteScalarAsync() is int minutes ? minutes : 15;
    }

    private static async Task<string> ItemLabelAsync(NpgsqlConnection conn, Guid itemId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT item_number, item_number_prefix, item_number_digits, item_number_with_name, file_name FROM items WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", itemId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return "";
        return ItemNumbering.RecordName(
            reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetBoolean(3), reader.GetString(4));
    }

    private record Entry(long Id, Guid? AuthorId, string AuthorName, string Note, int Percent, DateTimeOffset CreatedAt,
        string? ReviewStatus, string? ReviewComment, string? ReviewerName, DateTimeOffset? ReviewedAt);
}

record ProgressRequest(string? Note, int? Percent);
record ReviewRequest(string? Status, string? Comment);
record ProgressPolicyRequest(int? DeleteMinutes);
