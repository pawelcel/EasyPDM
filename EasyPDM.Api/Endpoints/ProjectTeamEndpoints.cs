using Npgsql;

// The company's side of a project and of its assemblies (migration 084):
//   - the project's lead among EasyPDM users (projects.lead_user_id) and the people taking part,
//     each with a role, one person possibly in several (project_team). Both are picked from the
//     users who can see the project (project_users) and the administrators. Everyone with access
//     sees them; only an administrator changes them, like the project's other fields.
//   - who works on an assembly (item_assignees): people of its project's team or its lead, any
//     number, one of them possibly leading. Changed only by the project's lead, who hands out
//     the work (an administrator sets the lead); the person assigned is notified.
// Losing access to the project, or leaving its team, takes a person off what depended on it
// (PruneAsync), so nobody stays assigned to work they can no longer see.
static class ProjectTeamEndpoints
{
    private const int MaxRoleLength = 200;

    // A user's name as people know it: first and last name when filled in, else the display name.
    private const string UserName =
        "COALESCE(NULLIF(btrim(concat_ws(' ', u.first_name, u.last_name)), ''), u.display_name)";

    public static void MapProjectTeamEndpoints(this WebApplication app, string connectionString)
    {
        const string onlyLead = "Only the project's lead assigns people to its assemblies.";
        // GET /api/projects/{projectId}/team -> { leadUserId, leadName, members, candidates }
        app.MapGet("/api/projects/{projectId:guid}/team", async (Guid projectId, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            if (!await ProjectExistsAsync(conn, projectId))
                return Results.NotFound();
            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return NoAccess();

            Guid? leadUserId = null;
            string? leadName = null;
            await using (var cmd = new NpgsqlCommand(
                $"SELECT u.id, {UserName} FROM projects p JOIN users u ON u.id = p.lead_user_id WHERE p.id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("id", projectId);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    leadUserId = reader.GetGuid(0);
                    leadName = reader.GetString(1);
                }
            }

            var members = new List<object>();
            await using (var cmd = new NpgsqlCommand($"""
                SELECT t.id, u.id, {UserName}, t.role
                FROM project_team t JOIN users u ON u.id = t.user_id
                WHERE t.project_id = @id
                ORDER BY t.added_at, t.id;
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", projectId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    members.Add(new { id = reader.GetInt64(0), userId = reader.GetGuid(1), name = reader.GetString(2), role = reader.GetString(3) });
            }

            return Results.Ok(new { leadUserId, leadName, members, candidates = await CandidatesAsync(conn, projectId) });
        });

        // PUT /api/projects/{projectId}/lead-user   body: { userId }  (null clears it)
        app.MapPut("/api/projects/{projectId:guid}/lead-user", async (Guid projectId, HttpContext ctx, TeamLeadRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return AdminOnly();
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            if (!await ProjectExistsAsync(conn, projectId))
                return Results.NotFound();
            if (body.UserId is not null && !await IsCandidateAsync(conn, projectId, body.UserId.Value))
                return Results.BadRequest("The lead must be a user who can see the project.");

            await using (var cmd = new NpgsqlCommand("UPDATE projects SET lead_user_id = @userId WHERE id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("id", projectId);
                cmd.Parameters.AddWithValue("userId", (object?)body.UserId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            await PruneAsync(conn, projectId);
            return Results.Ok();
        });

        // POST /api/projects/{projectId}/team   body: { userId, role }
        app.MapPost("/api/projects/{projectId:guid}/team", async (Guid projectId, HttpContext ctx, TeamMemberRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return AdminOnly();
            if (body.UserId is null)
                return Results.BadRequest("Pick a person.");
            var role = (body.Role ?? "").Trim();
            if (role.Length > MaxRoleLength)
                return Results.BadRequest($"The role can be at most {MaxRoleLength} characters long.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            if (!await ProjectExistsAsync(conn, projectId))
                return Results.NotFound();
            if (!await IsCandidateAsync(conn, projectId, body.UserId.Value))
                return Results.BadRequest("Only a user who can see the project can take part in it.");

            await using var cmd = new NpgsqlCommand("""
                INSERT INTO project_team (project_id, user_id, role) VALUES (@projectId, @userId, @role)
                ON CONFLICT DO NOTHING
                RETURNING id;
                """, conn);
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("userId", body.UserId.Value);
            cmd.Parameters.AddWithValue("role", role);
            if (await cmd.ExecuteScalarAsync() is not long id)
                return Results.Conflict("This person already has this role on the project.");
            return Results.Ok(new { id });
        });

        // PATCH /api/projects/{projectId}/team/{id}   body: { role }  (id: the row)
        app.MapPatch("/api/projects/{projectId:guid}/team/{id:long}", async (Guid projectId, long id, HttpContext ctx, TeamMemberRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return AdminOnly();
            var role = (body.Role ?? "").Trim();
            if (role.Length > MaxRoleLength)
                return Results.BadRequest($"The role can be at most {MaxRoleLength} characters long.");
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("UPDATE project_team SET role = @role WHERE project_id = @projectId AND id = @id;", conn);
            cmd.Parameters.AddWithValue("role", role);
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("id", id);
            try
            {
                return await cmd.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.Ok();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("This person already has this role on the project.");
            }
        });

        // DELETE /api/projects/{projectId}/team/{id}  (id: the row)
        app.MapDelete("/api/projects/{projectId:guid}/team/{id:long}", async (Guid projectId, long id, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return AdminOnly();
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using (var cmd = new NpgsqlCommand("DELETE FROM project_team WHERE project_id = @projectId AND id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("projectId", projectId);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }
            await PruneAsync(conn, projectId);
            return Results.Ok();
        });

        // GET /api/items/{itemId}/assignees -> { assignees, candidates, canEdit }  (assemblies)
        app.MapGet("/api/items/{itemId:guid}/assignees", async (Guid itemId, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var projectId = await AssemblyProjectAsync(conn, itemId);
            if (projectId is null)
                return Results.NotFound();
            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId.Value))
                return NoAccess();

            var assignees = new List<object>();
            await using (var cmd = new NpgsqlCommand($"""
                SELECT u.id, {UserName}, a.is_lead
                FROM item_assignees a JOIN users u ON u.id = a.user_id
                WHERE a.item_id = @itemId
                ORDER BY a.is_lead DESC, a.added_at;
                """, conn))
            {
                cmd.Parameters.AddWithValue("itemId", itemId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    assignees.Add(new { userId = reader.GetGuid(0), name = reader.GetString(1), isLead = reader.GetBoolean(2) });
            }

            var candidates = new List<object>();
            await using (var cmd = new NpgsqlCommand($"""
                SELECT u.id, {UserName}
                FROM users u
                WHERE u.id IN (SELECT user_id FROM project_team WHERE project_id = @projectId
                               UNION SELECT lead_user_id FROM projects WHERE id = @projectId AND lead_user_id IS NOT NULL)
                ORDER BY 2;
                """, conn))
            {
                cmd.Parameters.AddWithValue("projectId", projectId.Value);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    candidates.Add(new { userId = reader.GetGuid(0), name = reader.GetString(1) });
            }

            return Results.Ok(new { assignees, candidates, canEdit = await CanAssignAsync(conn, ctx, projectId.Value) });
        });

        // POST /api/items/{itemId}/assignees   body: { userId }
        app.MapPost("/api/items/{itemId:guid}/assignees", async (Guid itemId, HttpContext ctx, AssigneeRequest body) =>
        {
            if (body.UserId is null)
                return Results.BadRequest("Pick a person.");
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var projectId = await AssemblyProjectAsync(conn, itemId);
            if (projectId is null)
                return Results.NotFound();
            if (!await CanAssignAsync(conn, ctx, projectId.Value))
                return Results.Text(onlyLead, statusCode: StatusCodes.Status403Forbidden);
            if (!await IsOnTeamAsync(conn, projectId.Value, body.UserId.Value))
                return Results.BadRequest("Only a person of the project's team can be assigned.");

            await using var cmd = new NpgsqlCommand(
                "INSERT INTO item_assignees (item_id, user_id) VALUES (@itemId, @userId) ON CONFLICT DO NOTHING;", conn);
            cmd.Parameters.AddWithValue("itemId", itemId);
            cmd.Parameters.AddWithValue("userId", body.UserId.Value);
            if (await cmd.ExecuteNonQueryAsync() == 0)
                return Results.Conflict("This person is already assigned.");

            // Tell the person, unless the lead assigned themselves.
            var me = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (body.UserId.Value != me.Id)
            {
                var (itemLabel, projectName) = await ItemAndProjectNamesAsync(conn, itemId);
                await Notifications.NotifyAsync(conn, app.Logger, body.UserId.Value, "assembly_assigned",
                    new { itemLabel, projectName, assignedBy = me.DisplayName }, itemId: itemId, projectId: projectId.Value);
            }
            return Results.Ok();
        });

        // PUT /api/items/{itemId}/assignees/{userId}/lead   body: { isLead }
        app.MapPut("/api/items/{itemId:guid}/assignees/{userId:guid}/lead", async (Guid itemId, Guid userId, HttpContext ctx, AssigneeLeadRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var projectId = await AssemblyProjectAsync(conn, itemId);
            if (projectId is null)
                return Results.NotFound();
            if (!await CanAssignAsync(conn, ctx, projectId.Value))
                return Results.Text(onlyLead, statusCode: StatusCodes.Status403Forbidden);

            await using var tx = await conn.BeginTransactionAsync();
            if (body.IsLead)
            {
                await using var clear = new NpgsqlCommand("UPDATE item_assignees SET is_lead = false WHERE item_id = @itemId AND is_lead;", conn, tx);
                clear.Parameters.AddWithValue("itemId", itemId);
                await clear.ExecuteNonQueryAsync();
            }
            await using var set = new NpgsqlCommand(
                "UPDATE item_assignees SET is_lead = @isLead WHERE item_id = @itemId AND user_id = @userId;", conn, tx);
            set.Parameters.AddWithValue("isLead", body.IsLead);
            set.Parameters.AddWithValue("itemId", itemId);
            set.Parameters.AddWithValue("userId", userId);
            if (await set.ExecuteNonQueryAsync() == 0)
                return Results.NotFound();
            await tx.CommitAsync();
            return Results.Ok();
        });

        // DELETE /api/items/{itemId}/assignees/{userId}
        app.MapDelete("/api/items/{itemId:guid}/assignees/{userId:guid}", async (Guid itemId, Guid userId, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var projectId = await AssemblyProjectAsync(conn, itemId);
            if (projectId is null)
                return Results.NotFound();
            if (!await CanAssignAsync(conn, ctx, projectId.Value))
                return Results.Text(onlyLead, statusCode: StatusCodes.Status403Forbidden);
            await using var cmd = new NpgsqlCommand("DELETE FROM item_assignees WHERE item_id = @itemId AND user_id = @userId;", conn);
            cmd.Parameters.AddWithValue("itemId", itemId);
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok();
        });
    }

    // Keeps the company side of a project consistent after someone lost access to it, left its
    // team or stopped being its lead: the lead and team only among users who can see the project,
    // and assemblies assigned only to people of the team or its lead.
    public static async Task PruneAsync(NpgsqlConnection conn, Guid projectId)
    {
        const string sql = """
            UPDATE projects p SET lead_user_id = NULL
            WHERE p.id = @projectId AND p.lead_user_id IS NOT NULL
              AND NOT EXISTS (SELECT 1 FROM users u WHERE u.id = p.lead_user_id AND u.role = 'admin')
              AND NOT EXISTS (SELECT 1 FROM project_users pu WHERE pu.project_id = p.id AND pu.user_id = p.lead_user_id);

            DELETE FROM project_team t
            WHERE t.project_id = @projectId
              AND NOT EXISTS (SELECT 1 FROM users u WHERE u.id = t.user_id AND u.role = 'admin')
              AND NOT EXISTS (SELECT 1 FROM project_users pu WHERE pu.project_id = t.project_id AND pu.user_id = t.user_id);

            DELETE FROM item_assignees a
            USING items i
            WHERE i.id = a.item_id AND i.project_id = @projectId
              AND NOT EXISTS (SELECT 1 FROM project_team t WHERE t.project_id = @projectId AND t.user_id = a.user_id)
              AND NOT EXISTS (SELECT 1 FROM projects p WHERE p.id = @projectId AND p.lead_user_id = a.user_id);
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<object>> CandidatesAsync(NpgsqlConnection conn, Guid projectId)
    {
        var result = new List<object>();
        await using var cmd = new NpgsqlCommand($"""
            SELECT u.id, {UserName}
            FROM users u
            WHERE u.role = 'admin' OR EXISTS (SELECT 1 FROM project_users pu WHERE pu.project_id = @projectId AND pu.user_id = u.id)
            ORDER BY 2;
            """, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new { userId = reader.GetGuid(0), name = reader.GetString(1) });
        return result;
    }

    private static async Task<bool> IsCandidateAsync(NpgsqlConnection conn, Guid projectId, Guid userId)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT 1 FROM users u
            WHERE u.id = @userId
              AND (u.role = 'admin' OR EXISTS (SELECT 1 FROM project_users pu WHERE pu.project_id = @projectId AND pu.user_id = u.id));
            """, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("userId", userId);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> IsOnTeamAsync(NpgsqlConnection conn, Guid projectId, Guid userId)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT 1 WHERE EXISTS (SELECT 1 FROM project_team WHERE project_id = @projectId AND user_id = @userId)
                        OR EXISTS (SELECT 1 FROM projects WHERE id = @projectId AND lead_user_id = @userId);
            """, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("userId", userId);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    // Only the project's lead on the company side hands out the work on its assemblies.
    private static async Task<bool> CanAssignAsync(NpgsqlConnection conn, HttpContext ctx, Guid projectId)
    {
        var user = (CurrentUser)ctx.Items["CurrentUser"]!;
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @id AND lead_user_id = @userId;", conn);
        cmd.Parameters.AddWithValue("id", projectId);
        cmd.Parameters.AddWithValue("userId", user.Id);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<(string ItemLabel, string ProjectName)> ItemAndProjectNamesAsync(NpgsqlConnection conn, Guid itemId)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT i.item_number, i.item_number_prefix, i.item_number_digits, i.item_number_with_name, i.file_name, p.name
            FROM items i JOIN projects p ON p.id = i.project_id
            WHERE i.id = @id;
            """, conn);
        cmd.Parameters.AddWithValue("id", itemId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return ("", "");
        var label = ItemNumbering.RecordName(
            reader.IsDBNull(0) ? null : reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetBoolean(3),
            reader.GetString(4));
        return (label, reader.GetString(5));
    }

    // The project of an assembly with one; null for anything else (the people are an assembly's).
    private static async Task<Guid?> AssemblyProjectAsync(NpgsqlConnection conn, Guid itemId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT project_id FROM items WHERE id = @id AND item_type = 'assembly' AND project_id IS NOT NULL;", conn);
        cmd.Parameters.AddWithValue("id", itemId);
        return await cmd.ExecuteScalarAsync() is Guid id ? id : null;
    }

    private static async Task<bool> ProjectExistsAsync(NpgsqlConnection conn, Guid projectId)
    {
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", projectId);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static IResult AdminOnly() => Results.Text("Administrator rights required.", statusCode: StatusCodes.Status403Forbidden);
    private static IResult NoAccess() => Results.Text("You do not have access to this project.", statusCode: StatusCodes.Status403Forbidden);
}

record TeamLeadRequest(Guid? UserId);
record TeamMemberRequest(Guid? UserId, string? Role);
record AssigneeRequest(Guid? UserId);
record AssigneeLeadRequest(bool IsLead);
