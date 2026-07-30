using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string connectionString = app.Configuration["ConnectionString"]
    ?? throw new InvalidOperationException("Brak ConnectionString w appsettings.json");

// Katalog, w którym API fizycznie trzyma wgrane pliki (ręczne dodawanie elementów).
// Ścieżka względna liczona jest od katalogu aplikacji, żeby działało niezależnie od tego,
// z jakiego katalogu roboczego uruchomisz `dotnet run`.
string storageRoot = app.Configuration["StorageRoot"] ?? "storage";
if (!Path.IsPathRooted(storageRoot))
    storageRoot = Path.Combine(AppContext.BaseDirectory, storageRoot);
Directory.CreateDirectory(storageRoot);

// Serwuje wwwroot/index.html pod adresem "/" oraz app.js, style.css.
app.UseDefaultFiles();
app.UseStaticFiles();

// ============================================================
// PROJEKTY
// ============================================================

// GET /api/projects — lista projektów z liczbą elementów w każdym.
app.MapGet("/api/projects", async () =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    const string sql = """
        SELECT p.id, p.name, p.description, p.created_at, COUNT(i.id) AS item_count
        FROM projects p
        LEFT JOIN items i ON i.project_id = p.id
        GROUP BY p.id
        ORDER BY p.name;
        """;
    await using var cmd = new NpgsqlCommand(sql, conn);
    var result = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new
        {
            id = reader.GetGuid(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            createdAt = reader.GetDateTime(3),
            itemCount = reader.GetInt64(4)
        });
    }
    return Results.Ok(result);
});

// POST /api/projects   body: { "name": "...", "description": "..." }
app.MapPost("/api/projects", async (ProjectRequest body) =>
{
    if (string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest("Nazwa projektu nie może być pusta.");

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    const string sql = """
        INSERT INTO projects (name, description) VALUES (@name, @description)
        RETURNING id, name, description, created_at;
        """;
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("name", body.Name.Trim());
    cmd.Parameters.AddWithValue("description", (object?)body.Description ?? DBNull.Value);

    try
    {
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return Results.Ok(new
        {
            id = reader.GetGuid(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            createdAt = reader.GetDateTime(3),
            itemCount = 0
        });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        return Results.Conflict("Projekt o tej nazwie już istnieje.");
    }
});

// ============================================================
// ELEMENTY — ręczne tworzenie z uploadem pliku
// ============================================================

// POST /api/projects/{projectId}/items   multipart/form-data:
//   file        — wymagany, sam plik
//   properties  — opcjonalny, JSON np. {"material":"Stal S235"}
app.MapPost("/api/projects/{projectId:guid}/items", async (Guid projectId, HttpRequest request) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Oczekiwano danych multipart/form-data.");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.BadRequest("Brak pliku w polu 'file'.");

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @id;", conn))
    {
        checkCmd.Parameters.AddWithValue("id", projectId);
        if (await checkCmd.ExecuteScalarAsync() is null)
            return Results.NotFound("Projekt nie istnieje.");
    }

    string propertiesJson = "{}";
    if (form.TryGetValue("properties", out var propsValue) && !string.IsNullOrWhiteSpace(propsValue))
    {
        try
        {
            using var doc = JsonDocument.Parse(propsValue!);
            propertiesJson = doc.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return Results.BadRequest("Pole 'properties' zawiera niepoprawny JSON.");
        }
    }

    var itemId = Guid.NewGuid();
    var extension = Path.GetExtension(file.FileName);
    var projectStorageDir = Path.Combine(storageRoot, projectId.ToString());
    Directory.CreateDirectory(projectStorageDir);
    var storedPath = Path.Combine(projectStorageDir, $"{itemId}{extension}");

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

    const string insertSql = """
        INSERT INTO items (id, project_id, file_path, file_name, file_type, file_hash, file_size, modified_at, properties)
        VALUES (@id, @projectId, @filePath, @fileName, @fileType, @hash, @size, now(), @props::jsonb);
        """;
    try
    {
        await using var insertCmd = new NpgsqlCommand(insertSql, conn);
        insertCmd.Parameters.AddWithValue("id", itemId);
        insertCmd.Parameters.AddWithValue("projectId", projectId);
        insertCmd.Parameters.AddWithValue("filePath", storedPath);
        insertCmd.Parameters.AddWithValue("fileName", file.FileName);
        insertCmd.Parameters.AddWithValue("fileType", extension.TrimStart('.').ToLowerInvariant());
        insertCmd.Parameters.AddWithValue("hash", hash);
        insertCmd.Parameters.AddWithValue("size", file.Length);
        insertCmd.Parameters.AddWithValue("props", propertiesJson);
        await insertCmd.ExecuteNonQueryAsync();
    }
    catch
    {
        File.Delete(storedPath); // nie zostawiamy sierocego pliku, jeśli zapis do bazy się nie uda
        throw;
    }

    return Results.Created($"/api/items/{itemId}", new { id = itemId });
});

// GET /api/items/{id}/file — pobranie/podgląd samego pliku.
app.MapGet("/api/items/{id:guid}/file", async (Guid id) =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    const string sql = "SELECT file_path, file_name FROM items WHERE id = @id;";
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.NotFound();

    var path = reader.GetString(0);
    var fileName = reader.GetString(1);

    if (!File.Exists(path))
        return Results.NotFound("Plik zniknął z magazynu na dysku serwera.");

    return Results.File(path, "application/octet-stream", fileName);
});

// ============================================================
// GET /api/items?search=&tag=&projectId=
// Lista elementów z opcjonalnym filtrem po nazwie/właściwościach, tagu i projekcie.
// ============================================================
app.MapGet("/api/items", async (string? search, string? tag, Guid? projectId) =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    const string sql = """
        SELECT i.id, i.project_id, i.file_name, i.file_type, i.file_path, i.properties, i.modified_at
        FROM items i
        WHERE (@search::text IS NULL OR i.file_name ILIKE '%' || @search || '%'
                                       OR i.properties::text ILIKE '%' || @search || '%')
          AND (@tag::text IS NULL OR EXISTS (
                SELECT 1 FROM item_tags it
                JOIN tags t ON t.id = it.tag_id
                WHERE it.item_id = i.id AND t.name = @tag))
          AND (@projectId::uuid IS NULL OR i.project_id = @projectId)
        ORDER BY i.file_name;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("search", (object?)search ?? DBNull.Value);
    cmd.Parameters.AddWithValue("tag", (object?)tag ?? DBNull.Value);
    cmd.Parameters.AddWithValue("projectId", (object?)projectId ?? DBNull.Value);

    var items = new List<Dictionary<string, object?>>();
    var ids = new List<Guid>();

    await using (var reader = await cmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(0);
            ids.Add(id);
            items.Add(new Dictionary<string, object?>
            {
                ["id"] = id,
                ["projectId"] = reader.GetGuid(1),
                ["fileName"] = reader.GetString(2),
                ["fileType"] = reader.GetString(3),
                ["filePath"] = reader.GetString(4),
                ["properties"] = JsonDocument.Parse(reader.GetFieldValue<string>(5)).RootElement,
                ["modifiedAt"] = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                ["tags"] = new List<string>()
            });
        }
    }

    if (ids.Count > 0)
    {
        var tagsByItem = await LoadTagsForItems(connectionString, ids);
        foreach (var item in items)
        {
            var id = (Guid)item["id"]!;
            item["tags"] = tagsByItem.TryGetValue(id, out var t) ? t : new List<string>();
        }
    }

    return Results.Ok(items);
});

// ============================================================
// GET /api/items/{id}
// Szczegóły pojedynczego elementu.
// ============================================================
app.MapGet("/api/items/{id:guid}", async (Guid id) =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    const string sql = """
        SELECT id, project_id, file_name, file_type, file_path, properties, modified_at
        FROM items WHERE id = @id;
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.NotFound();

    var tagsByItem = await LoadTagsForItems(connectionString, new List<Guid> { id });

    var result = new Dictionary<string, object?>
    {
        ["id"] = reader.GetGuid(0),
        ["projectId"] = reader.GetGuid(1),
        ["fileName"] = reader.GetString(2),
        ["fileType"] = reader.GetString(3),
        ["filePath"] = reader.GetString(4),
        ["properties"] = JsonDocument.Parse(reader.GetFieldValue<string>(5)).RootElement,
        ["modifiedAt"] = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
        ["tags"] = tagsByItem.TryGetValue(id, out var t) ? t : new List<string>()
    };

    return Results.Ok(result);
});

// ============================================================
// GET /api/tags — lista wszystkich istniejących tagów (do podpowiedzi/autouzupełniania).
// ============================================================
app.MapGet("/api/tags", async () =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    await using var cmd = new NpgsqlCommand("SELECT name FROM tags ORDER BY name;", conn);
    var names = new List<string>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        names.Add(reader.GetString(0));

    return Results.Ok(names);
});

// ============================================================
// POST /api/items/{id}/tags   body: { "name": "do-przegladu" }
// ============================================================
app.MapPost("/api/items/{id:guid}/tags", async (Guid id, TagRequest body) =>
{
    if (string.IsNullOrWhiteSpace(body.Name))
        return Results.BadRequest("Nazwa tagu nie może być pusta.");

    var name = body.Name.Trim();

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    const string sql = """
        INSERT INTO tags (name) VALUES (@name)
        ON CONFLICT (name) DO UPDATE SET name = EXCLUDED.name
        RETURNING id;
        """;
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("name", name);
    var tagId = (int)(await cmd.ExecuteScalarAsync())!;

    const string linkSql = """
        INSERT INTO item_tags (item_id, tag_id) VALUES (@itemId, @tagId)
        ON CONFLICT DO NOTHING;
        """;
    await using var linkCmd = new NpgsqlCommand(linkSql, conn);
    linkCmd.Parameters.AddWithValue("itemId", id);
    linkCmd.Parameters.AddWithValue("tagId", tagId);
    await linkCmd.ExecuteNonQueryAsync();

    return Results.Ok();
});

// ============================================================
// DELETE /api/items/{id}/tags/{tagName}
// ============================================================
app.MapDelete("/api/items/{id:guid}/tags/{tagName}", async (Guid id, string tagName) =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    const string sql = """
        DELETE FROM item_tags
        WHERE item_id = @itemId
          AND tag_id = (SELECT id FROM tags WHERE name = @tagName);
        """;
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("itemId", id);
    cmd.Parameters.AddWithValue("tagName", tagName);
    await cmd.ExecuteNonQueryAsync();

    return Results.Ok();
});

// ============================================================
// PATCH /api/items/{id}/properties   body: { "material": "Stal S235", "supplier": "..." }
// ============================================================
app.MapPatch("/api/items/{id:guid}/properties", async (Guid id, JsonElement body) =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    const string sql = """
        UPDATE items SET properties = properties || @props::jsonb
        WHERE id = @id;
        """;
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("props", body.GetRawText());
    await cmd.ExecuteNonQueryAsync();

    return Results.Ok();
});

// ============================================================
// DELETE /api/items/{id}/properties/{key}
// ============================================================
app.MapDelete("/api/items/{id:guid}/properties/{key}", async (Guid id, string key) =>
{
    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    const string sql = "UPDATE items SET properties = properties - @key WHERE id = @id;";
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("key", key);
    await cmd.ExecuteNonQueryAsync();

    return Results.Ok();
});

app.Run();

// ============================================================
// Pomocnicze
// ============================================================
static async Task<Dictionary<Guid, List<string>>> LoadTagsForItems(string connectionString, List<Guid> ids)
{
    var result = ids.ToDictionary(id => id, _ => new List<string>());

    await using var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();

    const string sql = """
        SELECT it.item_id, t.name
        FROM item_tags it
        JOIN tags t ON t.id = it.tag_id
        WHERE it.item_id = ANY(@ids);
        """;
    await using var cmd = new NpgsqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("ids", ids.ToArray());

    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var itemId = reader.GetGuid(0);
        result[itemId].Add(reader.GetString(1));
    }

    return result;
}

record TagRequest(string Name);
record ProjectRequest(string Name, string? Description);
