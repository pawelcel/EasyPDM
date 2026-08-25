using System.Security.Cryptography;
using Npgsql;

// Katalog klientów (Nazwa/Nazwa 2/Lokalizacja + osoby kontaktowe, wzorem
// ManufacturerEndpoints.cs) razem z własną, prostą strukturą folderów/plików na
// dokumenty klienta (np. normy) -- CELOWO izolowaną od items/item_relations, które
// dźwigają część/złożenie/status/rewizję/właściciela/BOM, zupełnie nieistotne tutaj.
// Bez żadnych sprawdzeń uprawnień/dostępu do projektu -- tak samo jak Producenci
// (otwarty, globalny katalog dostępny każdemu zalogowanemu użytkownikowi).
static class ClientEndpoints
{
    public static void MapClientEndpoints(this WebApplication app, string connectionString, StorageSettings storage)
    {
        // GET /api/clients?search=... — lekka lista (bez osób kontaktowych, tylko ich
        // liczba) do wyszukiwarki po lewej stronie oraz do pickera w Projekcie.
        app.MapGet("/api/clients", async (string? search) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT c.id, c.name, c.name2, c.location, COUNT(cc.id) AS contact_count
                FROM clients c
                LEFT JOIN client_contacts cc ON cc.client_id = c.id
                WHERE @search::text IS NULL
                    OR c.name ILIKE '%' || @search || '%'
                    OR c.name2 ILIKE '%' || @search || '%'
                    OR c.location ILIKE '%' || @search || '%'
                GROUP BY c.id
                ORDER BY c.name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("search", (object?)search ?? DBNull.Value);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    id = reader.GetInt32(0),
                    name = reader.GetString(1),
                    name2 = reader.IsDBNull(2) ? null : reader.GetString(2),
                    location = reader.IsDBNull(3) ? null : reader.GetString(3),
                    contactCount = reader.GetInt64(4)
                });
            }

            return Results.Ok(result);
        });

        // GET /api/clients/{id} — klient razem z osobami kontaktowymi.
        app.MapGet("/api/clients/{id:int}", async (int id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string? name = null, name2 = null, location = null;
            await using (var clientCmd = new NpgsqlCommand(
                "SELECT name, name2, location FROM clients WHERE id = @id;", conn))
            {
                clientCmd.Parameters.AddWithValue("id", id);
                await using var reader = await clientCmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return Results.NotFound();
                name = reader.GetString(0);
                name2 = reader.IsDBNull(1) ? null : reader.GetString(1);
                location = reader.IsDBNull(2) ? null : reader.GetString(2);
            }

            const string contactsSql = """
                SELECT id, first_name, last_name, phone, position, email
                FROM client_contacts
                WHERE client_id = @id
                ORDER BY last_name, first_name;
                """;
            var contacts = new List<object>();
            await using (var contactsCmd = new NpgsqlCommand(contactsSql, conn))
            {
                contactsCmd.Parameters.AddWithValue("id", id);
                await using var reader = await contactsCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    contacts.Add(ReadContact(reader));
            }

            return Results.Ok(new { id, name, name2, location, contacts });
        });

        // POST /api/clients   body: { name, name2?, location? }
        app.MapPost("/api/clients", async (ClientRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa klienta nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "INSERT INTO clients (name, name2, location) VALUES (@name, @name2, @location) RETURNING id;", conn);
            AddClientParameters(cmd, body);

            try
            {
                var id = (int)(await cmd.ExecuteScalarAsync())!;
                return Results.Created($"/api/clients/{id}", new { id });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Klient o tej nazwie już istnieje.");
            }
        });

        // PATCH /api/clients/{id}   body: { name, name2?, location? }
        app.MapPatch("/api/clients/{id:int}", async (int id, ClientRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa klienta nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "UPDATE clients SET name = @name, name2 = @name2, location = @location WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("id", id);
            AddClientParameters(cmd, body);

            try
            {
                var affected = await cmd.ExecuteNonQueryAsync();
                return affected == 0 ? Results.NotFound() : Results.Ok();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Klient o tej nazwie już istnieje.");
            }
        });

        // DELETE /api/clients/{id} — kaskadowo usuwa też kontakty i całe drzewko plików.
        // PRZED usunięciem zbiera fizyczne ścieżki wszystkich plików klienta (kaskada w
        // bazie skasuje same wiersze client_nodes, ale nie fizyczne pliki na dysku) —
        // ta sama dyscyplina co DELETE /api/items/{id} / DELETE /api/projects/{id}.
        app.MapDelete("/api/clients/{id:int}", async (int id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var filePaths = new List<string>();
            await using (var selectCmd = new NpgsqlCommand(
                "SELECT file_path FROM client_nodes WHERE client_id = @id AND file_path IS NOT NULL;", conn))
            {
                selectCmd.Parameters.AddWithValue("id", id);
                await using var reader = await selectCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    filePaths.Add(reader.GetString(0));
            }

            await using var deleteCmd = new NpgsqlCommand("DELETE FROM clients WHERE id = @id;", conn);
            deleteCmd.Parameters.AddWithValue("id", id);
            var affected = await deleteCmd.ExecuteNonQueryAsync();
            if (affected == 0)
                return Results.NotFound();

            foreach (var path in filePaths)
            {
                try { File.Delete(path); } catch (IOException) { /* magazyn i tak jest sierocy — nie blokujemy usunięcia rekordu */ }
            }

            return Results.Ok();
        });

        // POST /api/clients/{id}/contacts
        app.MapPost("/api/clients/{id:int}/contacts", async (int id, ClientContactRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM clients WHERE id = @id;", conn))
            {
                checkCmd.Parameters.AddWithValue("id", id);
                if (await checkCmd.ExecuteScalarAsync() is null)
                    return Results.NotFound("Klient nie istnieje.");
            }

            const string sql = """
                INSERT INTO client_contacts (client_id, first_name, last_name, phone, position, email)
                VALUES (@clientId, @firstName, @lastName, @phone, @position, @email)
                RETURNING id;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            AddContactParameters(cmd, id, body);

            var contactId = (int)(await cmd.ExecuteScalarAsync())!;
            return Results.Created($"/api/clients/{id}/contacts/{contactId}", new { id = contactId });
        });

        // PATCH /api/clients/{id}/contacts/{contactId}
        app.MapPatch("/api/clients/{id:int}/contacts/{contactId:int}", async (int id, int contactId, ClientContactRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                UPDATE client_contacts SET
                    first_name = @firstName, last_name = @lastName, phone = @phone,
                    position = @position, email = @email
                WHERE id = @contactId AND client_id = @clientId;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("contactId", contactId);
            AddContactParameters(cmd, id, body);

            var affected = await cmd.ExecuteNonQueryAsync();
            return affected == 0 ? Results.NotFound() : Results.Ok();
        });

        // DELETE /api/clients/{id}/contacts/{contactId}
        app.MapDelete("/api/clients/{id:int}/contacts/{contactId:int}", async (int id, int contactId) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "DELETE FROM client_contacts WHERE id = @contactId AND client_id = @clientId;", conn);
            cmd.Parameters.AddWithValue("contactId", contactId);
            cmd.Parameters.AddWithValue("clientId", id);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // GET /api/clients/{id}/nodes — płaska lista wszystkich węzłów (folderów i plików)
        // klienta; front buduje drzewo po parentId, tak samo jak drzewo projektu buduje się
        // z płaskiej listy relacji (item_relations).
        app.MapGet("/api/clients/{id:int}/nodes", async (int id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT id, parent_id, node_type, name, file_size, created_at
                FROM client_nodes
                WHERE client_id = @id
                ORDER BY node_type, name;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add(ReadNode(reader));

            return Results.Ok(result);
        });

        // POST /api/clients/{id}/nodes/folder   body: { parentId?, name }
        app.MapPost("/api/clients/{id:int}/nodes/folder", async (int id, CreateFolderRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa folderu nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ClientAndParentExistAsync(conn, id, body.ParentId))
                return Results.NotFound();

            const string sql = """
                INSERT INTO client_nodes (client_id, parent_id, node_type, name)
                VALUES (@clientId, @parentId, 'folder', @name)
                RETURNING id, parent_id, node_type, name, file_size, created_at;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("clientId", id);
            cmd.Parameters.AddWithValue("parentId", (object?)body.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());

            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            return Results.Ok(ReadNode(reader));
        });

        // POST /api/clients/{id}/nodes/file   multipart/form-data: file (wymagany), parentId (opcjonalny)
        app.MapPost("/api/clients/{id:int}/nodes/file", async (int id, HttpRequest request) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Oczekiwano danych multipart/form-data.");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest("Brak pliku w polu 'file'.");

            Guid? parentId = null;
            var parentIdRaw = form["parentId"].ToString();
            if (!string.IsNullOrEmpty(parentIdRaw))
            {
                if (!Guid.TryParse(parentIdRaw, out var parsed))
                    return Results.BadRequest("Niepoprawny parentId.");
                parentId = parsed;
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ClientAndParentExistAsync(conn, id, parentId))
                return Results.NotFound();

            var nodeId = Guid.NewGuid();
            var clientDir = Path.Combine(storage.Path, "clients", id.ToString());
            Directory.CreateDirectory(clientDir);
            var extension = Path.GetExtension(file.FileName);
            var storedPath = Path.Combine(clientDir, $"{nodeId}{extension}");

            await using (var stream = File.Create(storedPath))
            {
                await file.CopyToAsync(stream);
            }

            string hash;
            using (var sha256 = SHA256.Create())
            await using (var readStream = File.OpenRead(storedPath))
            {
                hash = Convert.ToHexString(await sha256.ComputeHashAsync(readStream));
            }

            const string sql = """
                INSERT INTO client_nodes (id, client_id, parent_id, node_type, name, file_path, file_size, file_hash)
                VALUES (@id, @clientId, @parentId, 'file', @name, @filePath, @size, @hash)
                RETURNING id, parent_id, node_type, name, file_size, created_at;
                """;
            try
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("id", nodeId);
                cmd.Parameters.AddWithValue("clientId", id);
                cmd.Parameters.AddWithValue("parentId", (object?)parentId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("name", file.FileName);
                cmd.Parameters.AddWithValue("filePath", storedPath);
                cmd.Parameters.AddWithValue("size", file.Length);
                cmd.Parameters.AddWithValue("hash", hash);

                await using var reader = await cmd.ExecuteReaderAsync();
                await reader.ReadAsync();
                return Results.Ok(ReadNode(reader));
            }
            catch
            {
                File.Delete(storedPath);
                throw;
            }
        });

        // PATCH /api/clients/{id}/nodes/{nodeId}   body: { name } — zmiana nazwy; ścieżka
        // fizyczna oparta o UUID nigdy się nie zmienia.
        app.MapPatch("/api/clients/{id:int}/nodes/{nodeId:guid}", async (int id, Guid nodeId, RenameNodeRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "UPDATE client_nodes SET name = @name WHERE id = @nodeId AND client_id = @clientId;", conn);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            cmd.Parameters.AddWithValue("nodeId", nodeId);
            cmd.Parameters.AddWithValue("clientId", id);

            var affected = await cmd.ExecuteNonQueryAsync();
            return affected == 0 ? Results.NotFound() : Results.Ok();
        });

        // DELETE /api/clients/{id}/nodes/{nodeId} — rekurencyjny CTE zbiera file_path
        // całego poddrzewa PRZED DELETE (ta sama dyscyplina co DELETE /api/items/{id}),
        // dopiero potem kasuje wiersz (kaskada w bazie usuwa resztę poddrzewa).
        app.MapDelete("/api/clients/{id:int}/nodes/{nodeId:guid}", async (int id, Guid nodeId) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string selectSql = """
                WITH RECURSIVE subtree AS (
                    SELECT id, file_path FROM client_nodes WHERE id = @nodeId AND client_id = @clientId
                    UNION ALL
                    SELECT cn.id, cn.file_path FROM client_nodes cn
                    JOIN subtree s ON cn.parent_id = s.id
                )
                SELECT file_path FROM subtree WHERE file_path IS NOT NULL;
                """;
            var filePaths = new List<string>();
            await using (var selectCmd = new NpgsqlCommand(selectSql, conn))
            {
                selectCmd.Parameters.AddWithValue("nodeId", nodeId);
                selectCmd.Parameters.AddWithValue("clientId", id);
                await using var reader = await selectCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    filePaths.Add(reader.GetString(0));
            }

            await using var deleteCmd = new NpgsqlCommand(
                "DELETE FROM client_nodes WHERE id = @nodeId AND client_id = @clientId;", conn);
            deleteCmd.Parameters.AddWithValue("nodeId", nodeId);
            deleteCmd.Parameters.AddWithValue("clientId", id);
            var affected = await deleteCmd.ExecuteNonQueryAsync();
            if (affected == 0)
                return Results.NotFound();

            foreach (var path in filePaths)
            {
                try { File.Delete(path); } catch (IOException) { /* magazyn i tak jest sierocy — nie blokujemy usunięcia rekordu */ }
            }

            return Results.Ok();
        });

        // GET /api/clients/{id}/nodes/{nodeId}/file — pobranie pliku.
        app.MapGet("/api/clients/{id:int}/nodes/{nodeId:guid}/file", async (int id, Guid nodeId) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT file_path, name FROM client_nodes
                WHERE id = @nodeId AND client_id = @clientId AND node_type = 'file';
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("nodeId", nodeId);
            cmd.Parameters.AddWithValue("clientId", id);

            string path, fileName;
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return Results.NotFound();
                path = reader.GetString(0);
                fileName = reader.GetString(1);
            }

            if (!File.Exists(path))
                return Results.NotFound("Plik zniknął z magazynu na dysku serwera.");

            return Results.File(path, "application/octet-stream", fileName);
        });

        // GET /api/clients/{id}/nodes/search?query=... — szuka plików po nazwie w całym
        // drzewku klienta, zwraca pełną ścieżkę (breadcrumb) każdego trafienia.
        app.MapGet("/api/clients/{id:int}/nodes/search", async (int id, string query) =>
        {
            if (string.IsNullOrWhiteSpace(query))
                return Results.Ok(new List<object>());

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                WITH RECURSIVE tree AS (
                    SELECT id, node_type, name, name::text AS path
                    FROM client_nodes
                    WHERE client_id = @clientId AND parent_id IS NULL
                    UNION ALL
                    SELECT cn.id, cn.node_type, cn.name, t.path || '/' || cn.name
                    FROM client_nodes cn
                    JOIN tree t ON cn.parent_id = t.id
                )
                SELECT id, path FROM tree
                WHERE node_type = 'file' AND name ILIKE '%' || @query || '%'
                ORDER BY path;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("clientId", id);
            cmd.Parameters.AddWithValue("query", query);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    id = reader.GetGuid(0),
                    path = reader.GetString(1)
                });
            }

            return Results.Ok(result);
        });
    }

    private static async Task<bool> ClientAndParentExistAsync(NpgsqlConnection conn, int clientId, Guid? parentId)
    {
        await using var checkCmd = new NpgsqlCommand("SELECT 1 FROM clients WHERE id = @clientId;", conn);
        checkCmd.Parameters.AddWithValue("clientId", clientId);
        if (await checkCmd.ExecuteScalarAsync() is null)
            return false;

        if (parentId is null)
            return true;

        await using var parentCmd = new NpgsqlCommand(
            "SELECT 1 FROM client_nodes WHERE id = @parentId AND client_id = @clientId AND node_type = 'folder';", conn);
        parentCmd.Parameters.AddWithValue("parentId", parentId.Value);
        parentCmd.Parameters.AddWithValue("clientId", clientId);
        return await parentCmd.ExecuteScalarAsync() is not null;
    }

    private static void AddClientParameters(NpgsqlCommand cmd, ClientRequest body)
    {
        cmd.Parameters.AddWithValue("name", body.Name.Trim());
        cmd.Parameters.AddWithValue("name2", (object?)NullIfBlank(body.Name2) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("location", (object?)NullIfBlank(body.Location) ?? DBNull.Value);
    }

    private static void AddContactParameters(NpgsqlCommand cmd, int clientId, ClientContactRequest body)
    {
        cmd.Parameters.AddWithValue("clientId", clientId);
        cmd.Parameters.AddWithValue("firstName", (object?)NullIfBlank(body.FirstName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lastName", (object?)NullIfBlank(body.LastName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("phone", (object?)NullIfBlank(body.Phone) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("position", (object?)NullIfBlank(body.Position) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", (object?)NullIfBlank(body.Email) ?? DBNull.Value);
    }

    private static object ReadContact(NpgsqlDataReader reader) => new
    {
        id = reader.GetInt32(0),
        firstName = reader.IsDBNull(1) ? null : reader.GetString(1),
        lastName = reader.IsDBNull(2) ? null : reader.GetString(2),
        phone = reader.IsDBNull(3) ? null : reader.GetString(3),
        position = reader.IsDBNull(4) ? null : reader.GetString(4),
        email = reader.IsDBNull(5) ? null : reader.GetString(5)
    };

    private static object ReadNode(NpgsqlDataReader reader) => new
    {
        id = reader.GetGuid(0),
        parentId = reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1),
        nodeType = reader.GetString(2),
        name = reader.GetString(3),
        fileSize = reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4),
        createdAt = reader.GetDateTime(5)
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

record ClientRequest(string Name, string? Name2, string? Location);
record ClientContactRequest(string? FirstName, string? LastName, string? Phone, string? Position, string? Email);
record CreateFolderRequest(Guid? ParentId, string Name);
record RenameNodeRequest(string Name);
