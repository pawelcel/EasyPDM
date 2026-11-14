using Npgsql;
using NpgsqlTypes;

// Log in as: an administrator works as another user, to see and do what that user sees and does
// ("why can't I ...?") without asking for their password. The session row itself changes: user_id
// becomes the user and impersonator_id keeps the administrator (076_login_as.sql), so there is no
// second token, and Back or Log out ends it.
//
// The rules, and where each is held:
//   - Only an administrator starts one, from a browser session that is not already switched
//     (the start handler below). A macro token never switches.
//   - Only a user with the role "user" may be picked, never another administrator and never
//     oneself. While switched the session therefore has exactly that user's rights, and every
//     administrator check in the API (role == "admin") refuses on its own.
//   - What the user must do themselves stays refused while switched: changing the password
//     (AuthEndpoints) and starting another switch.
//   - Every request that may change something writes a change line here (Program.cs), start and
//     end write their own lines, and administrators read the log from the same dialog.
//   - A session whose administrator lost the role, or whose user became an administrator, no
//     longer signs anyone in (AuthEndpoints.ValidateToken) and ends with an "expired" line;
//     deleting either account deletes the session with it.
static class LoginAsEndpoints
{
    public const string EndBack = "back";
    public const string EndLoggedOut = "logged_out";
    public const string EndExpired = "expired";

    private const int MaxPeople = 2000;
    private const int MaxProjectsShown = 200;
    private const int DefaultLogLines = 200;
    private const int MaxLogLines = 1000;
    private const int MaxPathLength = 500;

    public static void MapLoginAsEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/auth/login-as/people — who an administrator may log in as: users with the role
        // "user".
        app.MapGet("/api/auth/login-as/people", async (HttpContext ctx) =>
        {
            if (AdminRefusal(ctx) is { } refused)
                return refused;
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"""
                SELECT u.id, u.username, {UserNames.FullNameSql("u")} AS name FROM users u
                WHERE u.role = 'user'
                ORDER BY lower({UserNames.FullNameSql("u")}), u.username
                LIMIT {MaxPeople};
                """, conn);
            var people = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                people.Add(new { id = reader.GetGuid(0), username = reader.GetString(1), displayName = reader.GetString(2) });
            return Results.Ok(people);
        });

        // GET /api/auth/login-as/people/{id} — the projects that user is a member of, so the
        // administrator sees what the switch will show before making it.
        app.MapGet("/api/auth/login-as/people/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            if (AdminRefusal(ctx) is { } refused)
                return refused;
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            if (await UserAsync(conn, null, id) is not { } name)
                return Results.NotFound("No user with the role \"user\" has this id.");

            var projects = new List<string>();
            var projectCount = 0;
            await using var cmd = new NpgsqlCommand("""
                SELECT p.name, count(*) OVER () FROM projects p
                JOIN project_users pu ON pu.project_id = p.id
                WHERE pu.user_id = @id
                ORDER BY lower(p.name)
                LIMIT @limit;
                """, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("limit", MaxProjectsShown);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                projects.Add(reader.GetString(0));
                projectCount = (int)reader.GetInt64(1);
            }
            return Results.Ok(new { id, displayName = name, projects, projectCount });
        });

        // POST /api/auth/login-as   body: { "userId": "..." } — this session becomes that user's.
        app.MapPost("/api/auth/login-as", async (HttpContext ctx, LoginAsRequest? body) =>
        {
            if (AdminRefusal(ctx) is { } refused)
                return refused;
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            var token = AuthEndpoints.CurrentSessionToken(ctx);
            if (token is null || token.StartsWith("pdmm_", StringComparison.Ordinal))
                return Results.Text("Log in as needs a browser session.", statusCode: StatusCodes.Status403Forbidden);
            if (body?.UserId is not { } targetId || targetId == Guid.Empty)
                return Results.BadRequest("Pick a user.");
            if (targetId == user.Id)
                return Results.BadRequest("You cannot log in as yourself.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            if (await UserAsync(conn, tx, targetId) is not { } targetName)
                return Results.BadRequest("Only a user with the role \"user\" can be picked, not an administrator.");

            // The session's own row decides: still this administrator's, still not switched.
            await using (var cmd = new NpgsqlCommand("""
                UPDATE sessions s SET user_id = @target, impersonator_id = s.user_id
                FROM users a
                WHERE s.token = @token AND s.user_id = @admin AND s.impersonator_id IS NULL
                  AND a.id = s.user_id AND a.role = 'admin';
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("target", targetId);
                cmd.Parameters.AddWithValue("token", token);
                cmd.Parameters.AddWithValue("admin", user.Id);
                if (await cmd.ExecuteNonQueryAsync() != 1)
                    return Results.Conflict("This session cannot switch now. Reload the page and try again.");
            }

            await WriteByIdsAsync(conn, tx, "start", user.Id, targetId, null, ClientAddress(ctx));
            await tx.CommitAsync();
            return Results.Ok(new { id = targetId, displayName = targetName });
        });

        // POST /api/auth/login-as/back — the session goes back to the administrator.
        app.MapPost("/api/auth/login-as/back", async (HttpContext ctx) =>
        {
            var token = AuthEndpoints.CurrentSessionToken(ctx);
            if (token is null)
                return NotSwitched();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            Guid adminId, targetId;
            await using (var cmd = new NpgsqlCommand("""
                WITH old AS (
                    SELECT token, user_id, impersonator_id FROM sessions
                    WHERE token = @token AND impersonator_id IS NOT NULL
                    FOR UPDATE
                )
                UPDATE sessions s SET user_id = old.impersonator_id, impersonator_id = NULL
                FROM old WHERE s.token = old.token
                RETURNING old.impersonator_id, old.user_id;
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("token", token);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return NotSwitched();
                adminId = reader.GetGuid(0);
                targetId = reader.GetGuid(1);
            }

            await WriteByIdsAsync(conn, tx, "end", adminId, targetId, EndBack, ClientAddress(ctx));
            await tx.CommitAsync();
            return Results.Ok();
        });

        // GET /api/auth/login-as/log?limit=200&before=<id> — the record, newest first.
        app.MapGet("/api/auth/login-as/log", async (HttpContext ctx, int? limit, long? before) =>
        {
            if (AdminRefusal(ctx) is { } refused)
                return refused;
            var take = Math.Clamp(limit ?? DefaultLogLines, 1, MaxLogLines);
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT id, at, kind, admin_id, admin_name, target_id, target_name, detail, client_address
                FROM login_as_log
                WHERE @before::bigint IS NULL OR id < @before
                ORDER BY id DESC
                LIMIT @take;
                """, conn);
            cmd.Parameters.Add(new NpgsqlParameter<long?>("before", NpgsqlDbType.Bigint) { TypedValue = before });
            cmd.Parameters.AddWithValue("take", take);
            var lines = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lines.Add(new
                {
                    id = reader.GetInt64(0),
                    at = reader.GetDateTime(1),
                    kind = reader.GetString(2),
                    adminId = reader.GetGuid(3),
                    adminName = reader.GetString(4),
                    targetId = reader.GetGuid(5),
                    targetName = reader.GetString(6),
                    detail = reader.IsDBNull(7) ? null : reader.GetString(7),
                    clientAddress = reader.IsDBNull(8) ? null : reader.GetString(8),
                });
            }
            return Results.Ok(lines);
        });
    }

    // The answer to a request that is not from an administrator's own, unswitched session. While
    // switched the session's user has the role "user", so AuthEndpoints.IsAdmin already refuses;
    // the switched check says why.
    private static IResult? AdminRefusal(HttpContext ctx)
    {
        if (ctx.Items["CurrentUser"] is CurrentUser { LoginAs: not null })
            return SwitchedRefused();
        return AuthEndpoints.IsAdmin(ctx) ? null
            : Results.Text("Only an administrator can do this.", statusCode: StatusCodes.Status403Forbidden);
    }

    public static IResult SwitchedRefused() =>
        Results.Text("Not available while you are logged in as another user. Go back to your own account first.",
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult NotSwitched() =>
        Results.Conflict("This session is not logged in as another user.");

    public static string? ClientAddress(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString();

    // Ends a switched session (log out, or one that no longer holds) and writes its end line.
    // Does nothing for a session that is not switched.
    public static async Task EndSessionAsync(NpgsqlConnection conn, string token, string detail, string? address)
    {
        await using var cmd = new NpgsqlCommand($"""
            WITH gone AS (
                DELETE FROM sessions WHERE token = @token AND impersonator_id IS NOT NULL
                RETURNING impersonator_id, user_id
            )
            INSERT INTO login_as_log (kind, admin_id, admin_name, target_id, target_name, detail, client_address)
            SELECT 'end', g.impersonator_id, COALESCE({UserNames.FullNameSql("a")}, ''),
                   g.user_id, COALESCE({UserNames.FullNameSql("t")}, ''), @detail, @address
            FROM gone g
            LEFT JOIN users a ON a.id = g.impersonator_id
            LEFT JOIN users t ON t.id = g.user_id;
            """, conn);
        cmd.Parameters.AddWithValue("token", token);
        cmd.Parameters.AddWithValue("detail", detail);
        cmd.Parameters.Add(new NpgsqlParameter("address", NpgsqlDbType.Text) { Value = (object?)address ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync();
    }

    // True for a request that writes its own end line, so no change line is needed for it.
    public static bool WritesOwnEnd(HttpContext ctx) =>
        HttpMethods.IsPost(ctx.Request.Method)
        && (ctx.Request.Path.Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase)
            || ctx.Request.Path.Equals("/api/auth/login-as/back", StringComparison.OrdinalIgnoreCase));

    // A request that may change something, made while switched: the method, path and answer go
    // into the log. Never throws: a request that already ran must not fail for its log line, so
    // a failure here goes to the server log instead.
    public static async Task RecordChangeAsync(string connectionString, HttpContext ctx, CurrentUser user, int status, ILogger logger)
    {
        if (user.LoginAs is not { } loginAs)
            return;
        var path = ctx.Request.Path.ToString();
        if (path.Length > MaxPathLength)
            path = path[..MaxPathLength];
        var detail = $"{ctx.Request.Method} {path} {status}";
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO login_as_log (kind, admin_id, admin_name, target_id, target_name, detail, client_address)
                VALUES ('change', @adminId, @adminName, @targetId, @targetName, @detail, @address);
                """, conn);
            cmd.Parameters.AddWithValue("adminId", loginAs.AdminId);
            cmd.Parameters.AddWithValue("adminName", loginAs.AdminName);
            cmd.Parameters.AddWithValue("targetId", user.Id);
            cmd.Parameters.AddWithValue("targetName", loginAs.TargetName);
            cmd.Parameters.AddWithValue("detail", detail);
            cmd.Parameters.Add(new NpgsqlParameter("address", NpgsqlDbType.Text) { Value = (object?)ClientAddress(ctx) ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Log in as: could not record {Detail} by {Admin} as {Target}.", detail, loginAs.AdminName, loginAs.TargetName);
        }
    }

    private static async Task WriteByIdsAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string kind,
        Guid adminId, Guid targetId, string? detail, string? address)
    {
        await using var cmd = new NpgsqlCommand($"""
            INSERT INTO login_as_log (kind, admin_id, admin_name, target_id, target_name, detail, client_address)
            VALUES (@kind, @adminId, COALESCE((SELECT {UserNames.FullNameSql("a")} FROM users a WHERE a.id = @adminId), ''),
                    @targetId, COALESCE((SELECT {UserNames.FullNameSql("t")} FROM users t WHERE t.id = @targetId), ''),
                    @detail, @address);
            """, conn, tx);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("adminId", adminId);
        cmd.Parameters.AddWithValue("targetId", targetId);
        cmd.Parameters.Add(new NpgsqlParameter("detail", NpgsqlDbType.Text) { Value = (object?)detail ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("address", NpgsqlDbType.Text) { Value = (object?)address ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync();
    }

    // The name of a user with the role "user", read under the caller's transaction when there is one.
    private static async Task<string?> UserAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid id)
    {
        await using var cmd = new NpgsqlCommand(
            $"SELECT {UserNames.FullNameSql("u")} FROM users u WHERE u.id = @id AND u.role = 'user'{(tx is null ? "" : " FOR SHARE")};",
            conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync() as string;
    }
}

record LoginAsRequest(Guid? UserId);
