using Npgsql;

// People on the client's side involved in a project, each with a role (project_contacts), next to
// the project lead. A person may be on it several times with different roles; a row is addressed
// by its own id. Picked from the same contacts as the lead -- the client's own and those of the
// project's Name 2 (ProjectEndpoints.ValidateLeadContactAsync checks both the same way). Everyone
// with access to the project sees them; only an administrator changes them, as the other project
// fields.
static class ProjectContactEndpoints
{
    private const int MaxRoleLength = 200;

    public static void MapProjectContactEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/projects/{projectId}/contacts
        app.MapGet("/api/projects/{projectId:guid}/contacts", async (Guid projectId, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            if (!await ProjectExistsAsync(conn, projectId))
                return Results.NotFound();
            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, projectId))
                return Results.Text("You do not have access to this project.", statusCode: StatusCodes.Status403Forbidden);

            const string sql = """
                SELECT c.id, c.first_name, c.last_name, c.phone, c.position, c.email, c.address, pc.role, pc.id
                FROM project_contacts pc
                JOIN client_contacts c ON c.id = pc.contact_id
                WHERE pc.project_id = @projectId
                ORDER BY pc.added_at, c.last_name, c.first_name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", projectId);
            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string? S(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
                result.Add(new
                {
                    contactId = reader.GetInt32(0),
                    firstName = S(1),
                    lastName = S(2),
                    phone = S(3),
                    position = S(4),
                    email = S(5),
                    address = S(6),
                    role = reader.GetString(7),
                    id = reader.GetInt64(8),
                });
            }
            return Results.Ok(result);
        });

        // POST /api/projects/{projectId}/contacts   body: { contactId, role }
        app.MapPost("/api/projects/{projectId:guid}/contacts", async (Guid projectId, HttpContext ctx, ProjectContactRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return AdminOnly();
            if (body.ContactId is null)
                return Results.BadRequest("Pick a contact.");
            var role = (body.Role ?? "").Trim();
            if (role.Length > MaxRoleLength)
                return Results.BadRequest($"The role can be at most {MaxRoleLength} characters long.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var project = await ProjectClientAsync(conn, projectId);
            if (project is null)
                return Results.NotFound();
            if (project.Value.ClientId is null)
                return Results.BadRequest("Pick the project's client first.");
            if (await ProjectEndpoints.ValidateLeadContactAsync(conn, project.Value.ClientId, project.Value.ClientName2Id, body.ContactId) is not null)
                return Results.BadRequest("This contact does not belong to the project's client or its Name 2.");

            await using var cmd = new NpgsqlCommand("""
                INSERT INTO project_contacts (project_id, contact_id, role) VALUES (@projectId, @contactId, @role)
                ON CONFLICT DO NOTHING
                RETURNING id;
                """, conn);
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("contactId", body.ContactId.Value);
            cmd.Parameters.AddWithValue("role", role);
            if (await cmd.ExecuteScalarAsync() is not long id)
                return Results.Conflict("This person already has this role on the project.");
            return Results.Ok(new { id });
        });

        // PATCH /api/projects/{projectId}/contacts/{id}   body: { role }  (id: the row, not the contact)
        app.MapPatch("/api/projects/{projectId:guid}/contacts/{id:long}", async (Guid projectId, long id, HttpContext ctx, ProjectContactRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return AdminOnly();
            var role = (body.Role ?? "").Trim();
            if (role.Length > MaxRoleLength)
                return Results.BadRequest($"The role can be at most {MaxRoleLength} characters long.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE project_contacts SET role = @role WHERE project_id = @projectId AND id = @id;", conn);
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

        // DELETE /api/projects/{projectId}/contacts/{id}  (id: the row, not the contact)
        app.MapDelete("/api/projects/{projectId:guid}/contacts/{id:long}", async (Guid projectId, long id, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return AdminOnly();
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM project_contacts WHERE project_id = @projectId AND id = @id;", conn);
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok();
        });
    }

    // After the project's client or Name 2 changes, drops the people who no longer belong to it --
    // the rule ValidateLeadContactAsync applies to a newly added one.
    public static async Task PruneAsync(NpgsqlConnection conn, Guid projectId)
    {
        await using var cmd = new NpgsqlCommand("""
            DELETE FROM project_contacts pc
            USING client_contacts c, projects p
            WHERE pc.project_id = @projectId AND p.id = pc.project_id AND c.id = pc.contact_id
              AND (p.client_id IS NULL OR c.client_id <> p.client_id
                   OR (c.name2_id IS NOT NULL AND c.name2_id IS DISTINCT FROM p.client_name2_id));
            """, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ProjectExistsAsync(NpgsqlConnection conn, Guid projectId)
    {
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", projectId);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<(int? ClientId, int? ClientName2Id)?> ProjectClientAsync(NpgsqlConnection conn, Guid projectId)
    {
        await using var cmd = new NpgsqlCommand("SELECT client_id, client_name2_id FROM projects WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", projectId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return (reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }

    private static IResult AdminOnly() => Results.Text("Administrator rights required.", statusCode: StatusCodes.Status403Forbidden);
}

record ProjectContactRequest(int? ContactId, string? Role);
