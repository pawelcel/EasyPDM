using Npgsql;

// Dane firmy (Ustawienia -> Firma): nazwa i logo, które trafią do tabelki rysunkowej. Odczyt
// dla każdego zalogowanego (makro CAD wypełnia tabelkę na koncie zwykłego użytkownika), zapis
// tylko dla administratora. Zob. migracja 067.
static class CompanyEndpoints
{
    private const long MaxLogoBytes = 2 * 1024 * 1024;

    public static void MapCompanyEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/settings/company -> { name, hasLogo, logoUpdatedAt }
        app.MapGet("/api/settings/company", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT name, logo IS NOT NULL, logo_updated_at FROM company_profile WHERE id = true;", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return Results.Ok(new { name = (string?)null, hasLogo = false, logoUpdatedAt = (DateTime?)null });
            return Results.Ok(new
            {
                name = reader.IsDBNull(0) ? null : reader.GetString(0),
                hasLogo = reader.GetBoolean(1),
                logoUpdatedAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2),
            });
        });

        // PATCH /api/settings/company   body: { name } — pusta nazwa czyści pole.
        app.MapPatch("/api/settings/company", async (CompanyRequest body, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO company_profile (id, name) VALUES (true, @name)
                ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;
                """, conn);
            cmd.Parameters.AddWithValue("name", string.IsNullOrWhiteSpace(body.Name) ? DBNull.Value : body.Name.Trim());
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok();
        });

        // GET /api/settings/company/logo — sam obraz; 404, gdy logo nie wgrano.
        app.MapGet("/api/settings/company/logo", async () =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT logo, logo_content_type FROM company_profile WHERE id = true AND logo IS NOT NULL;", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return Results.NotFound();
            return Results.File(reader.GetFieldValue<byte[]>(0), reader.GetString(1));
        });

        // PUT /api/settings/company/logo   multipart: file — PNG albo JPEG, do 2 MB.
        app.MapPut("/api/settings/company/logo", async (HttpRequest request, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();
            if (!request.HasFormContentType)
                return Results.BadRequest("Oczekiwano danych multipart/form-data.");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest("Brak pliku w polu 'file'.");
            if (file.Length > MaxLogoBytes)
                return Results.BadRequest("Logo może mieć najwyżej 2 MB.");

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);
            var bytes = buffer.ToArray();
            // Rodzaj rozpoznawany po zawartości, nie po rozszerzeniu ani nagłówku z przeglądarki.
            var contentType = DetectImageType(bytes);
            if (contentType is null)
                return Results.BadRequest("Logo musi być plikiem PNG albo JPEG.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO company_profile (id, logo, logo_content_type, logo_updated_at) VALUES (true, @logo, @type, now())
                ON CONFLICT (id) DO UPDATE SET logo = EXCLUDED.logo, logo_content_type = EXCLUDED.logo_content_type,
                                               logo_updated_at = EXCLUDED.logo_updated_at;
                """, conn);
            cmd.Parameters.AddWithValue("logo", bytes);
            cmd.Parameters.AddWithValue("type", contentType);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok();
        });

        // DELETE /api/settings/company/logo
        app.MapDelete("/api/settings/company/logo", async (HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Forbidden();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE company_profile SET logo = NULL, logo_content_type = NULL, logo_updated_at = NULL WHERE id = true;", conn);
            await cmd.ExecuteNonQueryAsync();
            return Results.Ok();
        });
    }

    private static string? DetectImageType(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        return null;
    }

    private static IResult Forbidden() => Results.Text("Wymagane uprawnienia administratora.", statusCode: StatusCodes.Status403Forbidden);
}

record CompanyRequest(string? Name);
