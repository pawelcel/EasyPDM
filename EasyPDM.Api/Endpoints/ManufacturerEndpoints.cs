using Npgsql;

// Katalog producentów: osoby kontaktowe i typy produktów, zarządzane z osobnej pozycji
// w panelu bocznym. Powiązanie z elementami jest wyłącznie "po nazwie" — element trzyma
// w properties.manufacturer/properties.productType goły tekst, bez klucza obcego, więc
// usunięcie producenta albo typu nie zmienia niczego w już opisanych elementach.
static class ManufacturerEndpoints
{
    public static void MapManufacturerEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/manufacturers?search=... — lekka lista (bez osób kontaktowych, tylko ich
        // liczba) do wyszukiwarki po lewej stronie.
        app.MapGet("/api/manufacturers", async (string? search) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT m.id, m.name, COUNT(c.id) AS contact_count
                FROM manufacturers m
                LEFT JOIN manufacturer_contacts c ON c.manufacturer_id = m.id
                WHERE @search::text IS NULL OR m.name ILIKE '%' || @search || '%'
                GROUP BY m.id
                ORDER BY m.name;
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
                    contactCount = reader.GetInt64(2)
                });
            }

            return Results.Ok(result);
        });

        // GET /api/manufacturers/{id} — producent razem z osobami kontaktowymi.
        app.MapGet("/api/manufacturers/{id:int}", async (int id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string? name = null;
            await using (var nameCmd = new NpgsqlCommand("SELECT name FROM manufacturers WHERE id = @id;", conn))
            {
                nameCmd.Parameters.AddWithValue("id", id);
                var result = await nameCmd.ExecuteScalarAsync();
                if (result is null)
                    return Results.NotFound();
                name = (string)result;
            }

            const string contactsSql = """
                SELECT id, first_name, last_name, phone, position, email, address
                FROM manufacturer_contacts
                WHERE manufacturer_id = @id
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

            const string productTypesSql = """
                SELECT id, name FROM manufacturer_product_types
                WHERE manufacturer_id = @id
                ORDER BY name;
                """;
            var productTypes = new List<object>();
            await using (var typesCmd = new NpgsqlCommand(productTypesSql, conn))
            {
                typesCmd.Parameters.AddWithValue("id", id);
                await using var reader = await typesCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    productTypes.Add(new { id = reader.GetInt32(0), name = reader.GetString(1) });
            }

            return Results.Ok(new { id, name, contacts, productTypes });
        });

        // POST /api/manufacturers   body: { "name": "..." }
        app.MapPost("/api/manufacturers", async (ManufacturerRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa producenta nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "INSERT INTO manufacturers (name) VALUES (@name) RETURNING id;", conn);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());

            try
            {
                var id = (int)(await cmd.ExecuteScalarAsync())!;
                return Results.Created($"/api/manufacturers/{id}", new { id });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Producent o tej nazwie już istnieje.");
            }
        });

        // PATCH /api/manufacturers/{id}   body: { "name": "..." }
        app.MapPatch("/api/manufacturers/{id:int}", async (int id, ManufacturerRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa producenta nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand("UPDATE manufacturers SET name = @name WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());

            try
            {
                var affected = await cmd.ExecuteNonQueryAsync();
                return affected == 0 ? Results.NotFound() : Results.Ok();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Producent o tej nazwie już istnieje.");
            }
        });

        // DELETE /api/manufacturers/{id} — kaskadowo usuwa też jego osoby kontaktowe.
        app.MapDelete("/api/manufacturers/{id:int}", async (int id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand("DELETE FROM manufacturers WHERE id = @id;", conn);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // POST /api/manufacturers/{id}/contacts
        // body: { firstName, lastName, phone, position, email, address } — wszystkie pola opcjonalne.
        app.MapPost("/api/manufacturers/{id:int}/contacts", async (int id, ContactRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM manufacturers WHERE id = @id;", conn))
            {
                checkCmd.Parameters.AddWithValue("id", id);
                if (await checkCmd.ExecuteScalarAsync() is null)
                    return Results.NotFound("Producent nie istnieje.");
            }

            const string sql = """
                INSERT INTO manufacturer_contacts (manufacturer_id, first_name, last_name, phone, position, email, address)
                VALUES (@manufacturerId, @firstName, @lastName, @phone, @position, @email, @address)
                RETURNING id;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            AddContactParameters(cmd, id, body);

            var contactId = (int)(await cmd.ExecuteScalarAsync())!;
            return Results.Created($"/api/manufacturers/{id}/contacts/{contactId}", new { id = contactId });
        });

        // PATCH /api/manufacturers/{id}/contacts/{contactId}
        app.MapPatch("/api/manufacturers/{id:int}/contacts/{contactId:int}", async (int id, int contactId, ContactRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                UPDATE manufacturer_contacts SET
                    first_name = @firstName, last_name = @lastName, phone = @phone,
                    position = @position, email = @email, address = @address
                WHERE id = @contactId AND manufacturer_id = @manufacturerId;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("contactId", contactId);
            AddContactParameters(cmd, id, body);

            var affected = await cmd.ExecuteNonQueryAsync();
            return affected == 0 ? Results.NotFound() : Results.Ok();
        });

        // DELETE /api/manufacturers/{id}/contacts/{contactId}
        app.MapDelete("/api/manufacturers/{id:int}/contacts/{contactId:int}", async (int id, int contactId) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "DELETE FROM manufacturer_contacts WHERE id = @contactId AND manufacturer_id = @manufacturerId;", conn);
            cmd.Parameters.AddWithValue("contactId", contactId);
            cmd.Parameters.AddWithValue("manufacturerId", id);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });

        // POST /api/manufacturers/{id}/product-types   body: { "name": "..." }
        app.MapPost("/api/manufacturers/{id:int}/product-types", async (int id, ProductTypeRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa typu produktu nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM manufacturers WHERE id = @id;", conn))
            {
                checkCmd.Parameters.AddWithValue("id", id);
                if (await checkCmd.ExecuteScalarAsync() is null)
                    return Results.NotFound("Producent nie istnieje.");
            }

            await using var cmd = new NpgsqlCommand(
                "INSERT INTO manufacturer_product_types (manufacturer_id, name) VALUES (@manufacturerId, @name) RETURNING id;",
                conn);
            cmd.Parameters.AddWithValue("manufacturerId", id);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());

            try
            {
                var typeId = (int)(await cmd.ExecuteScalarAsync())!;
                return Results.Created($"/api/manufacturers/{id}/product-types/{typeId}", new { id = typeId });
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Ten producent ma już typ produktu o tej nazwie.");
            }
        });

        // PATCH /api/manufacturers/{id}/product-types/{typeId}   body: { "name": "..." }
        app.MapPatch("/api/manufacturers/{id:int}/product-types/{typeId:int}", async (int id, int typeId, ProductTypeRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa typu produktu nie może być pusta.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "UPDATE manufacturer_product_types SET name = @name WHERE id = @typeId AND manufacturer_id = @manufacturerId;",
                conn);
            cmd.Parameters.AddWithValue("typeId", typeId);
            cmd.Parameters.AddWithValue("manufacturerId", id);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());

            try
            {
                var affected = await cmd.ExecuteNonQueryAsync();
                return affected == 0 ? Results.NotFound() : Results.Ok();
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return Results.Conflict("Ten producent ma już typ produktu o tej nazwie.");
            }
        });

        // DELETE /api/manufacturers/{id}/product-types/{typeId} — elementy, które mają tę
        // nazwę zapisaną w properties.productType, zostają nietknięte (to wolny tekst,
        // nie klucz obcy) — dokładnie tak samo jak przy usunięciu samego producenta.
        app.MapDelete("/api/manufacturers/{id:int}/product-types/{typeId:int}", async (int id, int typeId) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "DELETE FROM manufacturer_product_types WHERE id = @typeId AND manufacturer_id = @manufacturerId;", conn);
            cmd.Parameters.AddWithValue("typeId", typeId);
            cmd.Parameters.AddWithValue("manufacturerId", id);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });
    }

    private static void AddContactParameters(NpgsqlCommand cmd, int manufacturerId, ContactRequest body)
    {
        cmd.Parameters.AddWithValue("manufacturerId", manufacturerId);
        cmd.Parameters.AddWithValue("firstName", (object?)NullIfBlank(body.FirstName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("lastName", (object?)NullIfBlank(body.LastName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("phone", (object?)NullIfBlank(body.Phone) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("position", (object?)NullIfBlank(body.Position) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", (object?)NullIfBlank(body.Email) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("address", (object?)NullIfBlank(body.Address) ?? DBNull.Value);
    }

    private static object ReadContact(NpgsqlDataReader reader) => new
    {
        id = reader.GetInt32(0),
        firstName = reader.IsDBNull(1) ? null : reader.GetString(1),
        lastName = reader.IsDBNull(2) ? null : reader.GetString(2),
        phone = reader.IsDBNull(3) ? null : reader.GetString(3),
        position = reader.IsDBNull(4) ? null : reader.GetString(4),
        email = reader.IsDBNull(5) ? null : reader.GetString(5),
        address = reader.IsDBNull(6) ? null : reader.GetString(6)
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

record ManufacturerRequest(string Name);
record ProductTypeRequest(string Name);
record ContactRequest(string? FirstName, string? LastName, string? Phone, string? Position, string? Email, string? Address);
