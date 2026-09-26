using System.Linq;
using Npgsql;

// Tworzenie, edycja i usuwanie projektów — wyłącznie dla administratora (ten sam wzorzec
// sprawdzania roli co w UserEndpoints/ItemEndpoints). Sam odczyt (GET) jest dostępny dla
// każdego zalogowanego użytkownika.
static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/projects — lista projektów z liczbą elementów w każdym. Administrator widzi
        // wszystkie; zwykły użytkownik tylko te, do których został przypisany (project_users) —
        // nieprzypisany projekt jest dla niego tak, jakby nie istniał (nie pojawia się na liście,
        // więc nie da się go wybrać ani przejrzeć jego struktury przez UI).
        app.MapGet("/api/projects", async (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT p.id, p.name, p.description, p.client, p.client_id, c.name,
                       p.client_name2_id, n2.name2, p.closed,
                       p.start_date, p.end_date, p.created_at, COUNT(i.id) AS item_count,
                       p.lead_contact_id, lc.first_name, lc.last_name
                FROM projects p
                LEFT JOIN items i ON i.project_id = p.id
                LEFT JOIN clients c ON c.id = p.client_id
                LEFT JOIN client_name2 n2 ON n2.id = p.client_name2_id
                LEFT JOIN client_contacts lc ON lc.id = p.lead_contact_id
                WHERE @isAdmin OR EXISTS (
                    SELECT 1 FROM project_users pu WHERE pu.project_id = p.id AND pu.user_id = @userId
                )
                GROUP BY p.id, c.id, n2.id, lc.id
                ORDER BY c.name, n2.name2, p.name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("isAdmin", user.Role == "admin");
            cmd.Parameters.AddWithValue("userId", user.Id);
            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(ReadProject(reader));
            return Results.Ok(result);
        });

        // POST /api/projects   body: { name, description?, client?, clientName2Id?, closed, startDate?, endDate? }
        app.MapPost("/api/projects", async (HttpContext ctx, ProjectRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa projektu nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var name2Error = await ValidateClientName2Async(conn, body.ClientId, body.ClientName2Id);
            if (name2Error is not null)
                return Results.BadRequest(name2Error);

            var leadContactError = await ValidateLeadContactAsync(conn, body.ClientId, body.ClientName2Id, body.LeadContactId);
            if (leadContactError is not null)
                return Results.BadRequest(leadContactError);

            const string sql = """
                INSERT INTO projects (name, description, client_id, client_name2_id, closed, start_date, end_date, lead_contact_id)
                VALUES (@name, @description, @clientId, @clientName2Id, @closed, @startDate, @endDate, @leadContactId)
                RETURNING id, name, description, client, client_id,
                    (SELECT name FROM clients WHERE clients.id = client_id) AS client_name,
                    client_name2_id,
                    (SELECT name2 FROM client_name2 WHERE client_name2.id = client_name2_id) AS client_name2_name,
                    closed, start_date, end_date, created_at,
                    lead_contact_id,
                    (SELECT first_name FROM client_contacts WHERE client_contacts.id = lead_contact_id) AS lead_contact_first_name,
                    (SELECT last_name FROM client_contacts WHERE client_contacts.id = lead_contact_id) AS lead_contact_last_name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("description", (object?)body.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("clientId", (object?)body.ClientId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("clientName2Id", (object?)body.ClientName2Id ?? DBNull.Value);
            cmd.Parameters.AddWithValue("closed", body.Closed);
            cmd.Parameters.AddWithValue("startDate", (object?)body.StartDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("endDate", (object?)body.EndDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("leadContactId", (object?)body.LeadContactId ?? DBNull.Value);

            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                await reader.ReadAsync();
                return Results.Ok(ReadProject(reader, itemCount: 0));
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Projekt o tej nazwie już istnieje.");
            }
        });

        // PATCH /api/projects/{id}   body: { name, description?, client?, clientName2Id?, closed, startDate?, endDate? }
        app.MapPatch("/api/projects/{id:guid}", async (Guid id, HttpContext ctx, ProjectRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa projektu nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var name2Error = await ValidateClientName2Async(conn, body.ClientId, body.ClientName2Id);
            if (name2Error is not null)
                return Results.BadRequest(name2Error);

            var leadContactError = await ValidateLeadContactAsync(conn, body.ClientId, body.ClientName2Id, body.LeadContactId);
            if (leadContactError is not null)
                return Results.BadRequest(leadContactError);

            const string sql = """
                UPDATE projects SET
                    name = @name,
                    description = @description,
                    client_id = @clientId,
                    client_name2_id = @clientName2Id,
                    closed = @closed,
                    start_date = @startDate,
                    end_date = @endDate,
                    lead_contact_id = @leadContactId
                WHERE id = @id
                RETURNING id, name, description, client, client_id,
                    (SELECT name FROM clients WHERE clients.id = client_id) AS client_name,
                    client_name2_id,
                    (SELECT name2 FROM client_name2 WHERE client_name2.id = client_name2_id) AS client_name2_name,
                    closed, start_date, end_date, created_at,
                    (SELECT COUNT(*) FROM items WHERE items.project_id = projects.id),
                    lead_contact_id,
                    (SELECT first_name FROM client_contacts WHERE client_contacts.id = lead_contact_id) AS lead_contact_first_name,
                    (SELECT last_name FROM client_contacts WHERE client_contacts.id = lead_contact_id) AS lead_contact_last_name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("description", (object?)body.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("clientId", (object?)body.ClientId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("clientName2Id", (object?)body.ClientName2Id ?? DBNull.Value);
            cmd.Parameters.AddWithValue("closed", body.Closed);
            cmd.Parameters.AddWithValue("startDate", (object?)body.StartDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("endDate", (object?)body.EndDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("leadContactId", (object?)body.LeadContactId ?? DBNull.Value);

            try
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.NotFound();
                return Results.Ok(ReadProject(reader));
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Projekt o tej nazwie już istnieje.");
            }
        });

        // DELETE /api/projects/{id} — usuwa TYLKO sam projekt. Części/Złożenia, które do
        // niego należały, NIE są kasowane — zostają odpięte (project_id = NULL), czyli
        // stają się elementami "bez projektu", dokładnie w tym samym stanie co po ręcznym
        // "Usuń ze struktury" (zob. nullable_item_project): nadal w pełni istnieją, razem ze
        // swoimi plikami, załącznikami, tagami, historią i relacjami BOM, widoczne wyłącznie
        // przez globalne wyszukiwanie "Cała baza". Świadoma decyzja — usunięcie projektu to
        // usunięcie samego "kontenera", nie masowe kasowanie danych.
        //
        // items.project_id ma ON DELETE CASCADE (potrzebne osobno dla "Wyczyść bazę" w
        // Ustawieniach, które MA kasować elementy kaskadowo) — stąd jawne odpięcie PRZED
        // DELETE FROM projects, w jednej transakcji: bez tego kaskada zabrałaby ze sobą
        // wszystkie elementy projektu, zanim zdążylibyśmy je odpiąć.
        app.MapDelete("/api/projects/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // Nazwa projektu + kto był przypisany — zebrane PRZED DELETE (project_users jest
            // kaskadowo kasowane razem z projektem), żeby móc powiadomić przypisanych już PO
            // udanym usunięciu.
            string? projectName = null;
            await using (var nameCmd = new NpgsqlCommand("SELECT name FROM projects WHERE id = @id;", conn))
            {
                nameCmd.Parameters.AddWithValue("id", id);
                projectName = (string?)await nameCmd.ExecuteScalarAsync();
            }
            if (projectName is null)
                return Results.NotFound();

            var assignedUserIds = new List<Guid>();
            await using (var usersCmd = new NpgsqlCommand("SELECT user_id FROM project_users WHERE project_id = @id;", conn))
            {
                usersCmd.Parameters.AddWithValue("id", id);
                await using var reader = await usersCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    assignedUserIds.Add(reader.GetGuid(0));
            }

            await using (var tx = await conn.BeginTransactionAsync())
            {
                await using (var detachCmd = new NpgsqlCommand("UPDATE items SET project_id = NULL WHERE project_id = @id;", conn, tx))
                {
                    detachCmd.Parameters.AddWithValue("id", id);
                    await detachCmd.ExecuteNonQueryAsync();
                }

                await using (var deleteCmd = new NpgsqlCommand("DELETE FROM projects WHERE id = @id;", conn, tx))
                {
                    deleteCmd.Parameters.AddWithValue("id", id);
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
            }

            foreach (var userId in assignedUserIds)
                await Notifications.NotifyAsync(conn, app.Logger, userId, "project_deleted", new { projectName });

            return Results.Ok();
        });
    }

    // Kolumny 0-11 (id..createdAt) i "lead contact" mają zawsze tę samą pozycję względem
    // SIEBIE, ale item_count (kolumna 12) jest obecna tylko w zapytaniach GET/PATCH -- POST
    // go w ogóle nie SELECTuje (patrz wywołanie z itemCount: 0 niżej) -- stąd przesunięcie
    // o jedną kolumnę w zależności od tego, czy itemCount przyszedł z zewnątrz czy z bazy.
    private static object ReadProject(NpgsqlDataReader reader, long? itemCount = null)
    {
        var leadBase = itemCount is null ? 13 : 12;
        return new
        {
            id = reader.GetGuid(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            client = reader.IsDBNull(3) ? null : reader.GetString(3),
            clientId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
            clientName = reader.IsDBNull(5) ? null : reader.GetString(5),
            clientName2Id = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
            clientName2Name = reader.IsDBNull(7) ? null : reader.GetString(7),
            closed = reader.GetBoolean(8),
            startDate = reader.IsDBNull(9) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(9),
            endDate = reader.IsDBNull(10) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(10),
            createdAt = reader.GetDateTime(11),
            itemCount = itemCount ?? reader.GetInt64(12),
            leadContactId = reader.IsDBNull(leadBase) ? (int?)null : reader.GetInt32(leadBase),
            leadContactName = BuildContactName(reader, leadBase + 1, leadBase + 2)
        };
    }

    private static string? BuildContactName(NpgsqlDataReader reader, int firstNameIndex, int lastNameIndex)
    {
        var firstName = reader.IsDBNull(firstNameIndex) ? null : reader.GetString(firstNameIndex);
        var lastName = reader.IsDBNull(lastNameIndex) ? null : reader.GetString(lastNameIndex);
        var name = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrEmpty(name) ? null : name;
    }

    // Projekt ma prawdziwy klucz obcy do JEDNEJ Nazwy 2 (nie dopasowanie po nazwie jak
    // properties.clientName2 na elementach) -- musi więc rzeczywiście należeć do
    // client_id z tego samego żądania, inaczej zapisalibyśmy niespójną parę. Zwraca komunikat
    // błędu (do BadRequest) albo null, gdy wszystko się zgadza (albo Name2 w ogóle nie podano).
    private static async Task<string?> ValidateClientName2Async(NpgsqlConnection conn, int? clientId, int? clientName2Id)
    {
        if (clientName2Id is null)
            return null;

        await using var cmd = new NpgsqlCommand("SELECT client_id FROM client_name2 WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", clientName2Id.Value);
        var actualClientId = (int?)await cmd.ExecuteScalarAsync();
        if (actualClientId is null)
            return "Wskazana Nazwa 2 nie istnieje.";
        if (actualClientId != clientId)
            return "Wskazana Nazwa 2 nie należy do wybranego klienta.";
        return null;
    }

    // Prowadzący projekt musi być kontaktem TEGO klienta -- albo kontaktem samego klienta
    // (name2_id IS NULL, widoczny niezależnie od wybranej Nazwy 2), albo kontaktem
    // przypisanym dokładnie do tej Nazwy 2, którą ma projekt (client_name2_id) -- ten sam
    // zakres "główna + podrzędna dla wybranej Nazwy 2", z którego front buduje listę do
    // wyboru (GET .../clients/{id} + GET .../clients/{id}/name2/{name2Id}).
    private static async Task<string?> ValidateLeadContactAsync(NpgsqlConnection conn, int? clientId, int? clientName2Id, int? leadContactId)
    {
        if (leadContactId is null)
            return null;
        if (clientId is null)
            return "Nie można wskazać prowadzącego projekt bez wybranego klienta.";

        await using var cmd = new NpgsqlCommand("SELECT client_id, name2_id FROM client_contacts WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", leadContactId.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return "Wskazany kontakt prowadzącego nie istnieje.";
        var contactClientId = reader.GetInt32(0);
        int? contactName2Id = reader.IsDBNull(1) ? null : reader.GetInt32(1);
        if (contactClientId != clientId)
            return "Wskazany kontakt nie należy do wybranego klienta.";
        if (contactName2Id is not null && contactName2Id != clientName2Id)
            return "Wskazany kontakt należy do innej Nazwy 2 tego klienta.";
        return null;
    }

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);
}

record ProjectRequest(string Name, string? Description, int? ClientId, int? ClientName2Id, bool Closed, DateOnly? StartDate, DateOnly? EndDate, int? LeadContactId);
