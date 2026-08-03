using Npgsql;

// Przypisania użytkowników do projektów (project_users) — kto (poza administratorem, który
// zawsze widzi wszystko) może w ogóle zobaczyć dany projekt i przeglądać jego strukturę.
// Zarządzanie przypisaniami jest dostępne wyłącznie dla administratora.
static class ProjectAccessEndpoints
{
    public static void MapProjectAccessEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/project-users — cała macierz przypisań naraz (mało danych: liczba
        // projektów razy liczba użytkowników), żeby UI zarządzania (projekty po lewej,
        // użytkownicy po prawej) mogło przełączać zaznaczony projekt bez dodatkowych zapytań.
        app.MapGet("/api/project-users", async (HttpContext ctx) =>
        {
            if (!IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "SELECT project_id, user_id FROM project_users;";
            await using var cmd = new NpgsqlCommand(sql, conn);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new { projectId = reader.GetGuid(0), userId = reader.GetGuid(1) });
            }

            return Results.Ok(result);
        });

        // POST /api/projects/{projectId}/users/{userId} — nadaje dostęp (idempotentne).
        app.MapPost("/api/projects/{projectId:guid}/users/{userId:guid}", async (Guid projectId, Guid userId, HttpContext ctx) =>
        {
            if (!IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                INSERT INTO project_users (project_id, user_id)
                VALUES (@projectId, @userId)
                ON CONFLICT (project_id, user_id) DO NOTHING;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // DELETE /api/projects/{projectId}/users/{userId} — odbiera dostęp.
        app.MapDelete("/api/projects/{projectId:guid}/users/{userId:guid}", async (Guid projectId, Guid userId, HttpContext ctx) =>
        {
            if (!IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "DELETE FROM project_users WHERE project_id = @projectId AND user_id = @userId;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });
    }

    private static bool IsAdmin(HttpContext ctx) => (ctx.Items["CurrentUser"] as CurrentUser)?.Role == "admin";

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);
}
