using System.IO.Compression;
using Npgsql;

// Pobieranie "dokumentacji" — załączników (item_attachments) zebranych z całego poddrzewa
// Folderu/Złożenia (rekurencyjnie przez item_relations) albo z całego projektu — jako
// jeden plik ZIP, z możliwością wybrania rozszerzeń plików do uwzględnienia (np. tylko .txt).
// Struktura tree'a (Część/Plik jako osobne elementy) NIE wchodzi w skład dokumentacji —
// tu chodzi wyłącznie o pliki "podpięte" (załączniki) do elementów, zgodnie z tym, jak
// AttachmentsPanel już rozróżnia to od struktury.
static class DocumentationEndpoints
{
    public static void MapDocumentationEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/items/{id}/documentation/extensions
        app.MapGet("/api/items/{id:guid}/documentation/extensions", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var projectId = await GetItemProjectIdAsync(conn, id);
            if (projectId is null)
                return Results.NotFound();
            if (!await HasProjectAccessAsync(conn, ctx, projectId.Value))
                return AccessForbidden();

            var itemIds = await GetSelfAndDescendantIdsAsync(conn, id);
            var extensions = await GetExtensionsAsync(conn, itemIds);
            return Results.Ok(extensions);
        });

        // GET /api/items/{id}/documentation?ext=txt&ext=pdf — bez "ext" pobiera wszystkie rozszerzenia.
        app.MapGet("/api/items/{id:guid}/documentation", async (Guid id, string[]? ext, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var projectId = await GetItemProjectIdAsync(conn, id);
            if (projectId is null)
                return Results.NotFound();
            if (!await HasProjectAccessAsync(conn, ctx, projectId.Value))
                return AccessForbidden();

            var itemIds = await GetSelfAndDescendantIdsAsync(conn, id);
            var zip = await BuildZipAsync(conn, itemIds, ext);
            if (zip is null)
                return Results.BadRequest("Brak załączników pasujących do wybranych rozszerzeń.");

            return Results.File(zip, "application/zip", "dokumentacja.zip");
        });

        // GET /api/projects/{projectId}/documentation/extensions
        app.MapGet("/api/projects/{projectId:guid}/documentation/extensions", async (Guid projectId, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ProjectExistsAsync(conn, projectId))
                return Results.NotFound();
            if (!await HasProjectAccessAsync(conn, ctx, projectId))
                return AccessForbidden();

            var itemIds = await GetProjectItemIdsAsync(conn, projectId);
            var extensions = await GetExtensionsAsync(conn, itemIds);
            return Results.Ok(extensions);
        });

        // GET /api/projects/{projectId}/documentation?ext=txt&ext=pdf
        app.MapGet("/api/projects/{projectId:guid}/documentation", async (Guid projectId, string[]? ext, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ProjectExistsAsync(conn, projectId))
                return Results.NotFound();
            if (!await HasProjectAccessAsync(conn, ctx, projectId))
                return AccessForbidden();

            var itemIds = await GetProjectItemIdsAsync(conn, projectId);
            var zip = await BuildZipAsync(conn, itemIds, ext);
            if (zip is null)
                return Results.BadRequest("Brak załączników pasujących do wybranych rozszerzeń.");

            return Results.File(zip, "application/zip", "dokumentacja.zip");
        });
    }

    private static IResult AccessForbidden() =>
        Results.Text("Brak dostępu do tego projektu.", statusCode: StatusCodes.Status403Forbidden);

    private static async Task<bool> HasProjectAccessAsync(NpgsqlConnection conn, HttpContext ctx, Guid projectId)
    {
        var user = (CurrentUser)ctx.Items["CurrentUser"]!;
        if (user.Role == "admin") return true;

        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM project_users WHERE project_id = @projectId AND user_id = @userId;", conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("userId", user.Id);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<bool> ProjectExistsAsync(NpgsqlConnection conn, Guid projectId)
    {
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", projectId);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private static async Task<Guid?> GetItemProjectIdAsync(NpgsqlConnection conn, Guid id)
    {
        await using var cmd = new NpgsqlCommand("SELECT project_id FROM items WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        var result = await cmd.ExecuteScalarAsync();
        return result is null ? null : (Guid)result;
    }

    // Element sam + wszyscy jego potomkowie w strukturze (item_relations) — dla Części
    // (liścia) to po prostu on sam; dla Folderu/Złożenia całe poddrzewo.
    private static async Task<List<Guid>> GetSelfAndDescendantIdsAsync(NpgsqlConnection conn, Guid id)
    {
        const string sql = """
            WITH RECURSIVE descendants AS (
                SELECT @id::uuid AS item_id
                UNION
                SELECT ir.child_id FROM item_relations ir
                JOIN descendants d ON ir.parent_id = d.item_id
            )
            SELECT item_id FROM descendants;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        var result = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetGuid(0));
        return result;
    }

    // Wszystkie elementy "mieszkające" w danym projekcie (project_id), niezależnie od tego,
    // czy są w strukturze drzewa, czy nie — to najszersze, najprostsze rozumienie "elementów
    // podpiętych pod projekt".
    private static async Task<List<Guid>> GetProjectItemIdsAsync(NpgsqlConnection conn, Guid projectId)
    {
        const string sql = "SELECT id FROM items WHERE project_id = @projectId;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);

        var result = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetGuid(0));
        return result;
    }

    private static async Task<List<string>> GetExtensionsAsync(NpgsqlConnection conn, List<Guid> itemIds)
    {
        if (itemIds.Count == 0)
            return new List<string>();

        const string sql = "SELECT file_name FROM item_attachments WHERE item_id = ANY(@ids);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", itemIds.ToArray());

        var extensions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var extension = Path.GetExtension(reader.GetString(0)).TrimStart('.').ToLowerInvariant();
            if (extension.Length > 0)
                extensions.Add(extension);
        }
        return extensions.ToList();
    }

    // Zwraca bajty gotowego ZIP-a albo null, jeśli nie znaleziono żadnego pasującego pliku
    // (pusty "ext" = wszystkie rozszerzenia). Wewnątrz ZIP-a każdy załącznik trafia do
    // folderu nazwanego numerem/nazwą elementu, do którego jest podpięty — dzięki temu
    // pliki o tej samej nazwie z różnych elementów się nie nadpisują.
    private static async Task<byte[]?> BuildZipAsync(NpgsqlConnection conn, List<Guid> itemIds, string[]? extensionFilter)
    {
        if (itemIds.Count == 0)
            return null;

        const string sql = """
            SELECT ia.file_name, ia.file_path, i.item_number, i.item_number_prefix, i.file_name AS item_name
            FROM item_attachments ia
            JOIN items i ON i.id = ia.item_id
            WHERE ia.item_id = ANY(@ids)
            ORDER BY i.item_number NULLS LAST, i.file_name, ia.file_name;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("ids", itemIds.ToArray());

        var wantedExtensions = extensionFilter is { Length: > 0 }
            ? extensionFilter.Select(e => e.TrimStart('.').ToLowerInvariant()).ToHashSet()
            : null;

        using var memoryStream = new MemoryStream();
        var wroteAny = false;
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedEntryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var attachmentFileName = reader.GetString(0);
                var attachmentPath = reader.GetString(1);
                var itemNumber = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                var itemNumberPrefix = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var itemName = reader.GetString(4);

                var extension = Path.GetExtension(attachmentFileName).TrimStart('.').ToLowerInvariant();
                if (wantedExtensions is not null && !wantedExtensions.Contains(extension))
                    continue;
                if (!File.Exists(attachmentPath))
                    continue;

                var folderName = SanitizeSegment(itemNumber is not null ? $"{itemNumberPrefix}{itemNumber} ({itemName})" : itemName);
                var entryName = MakeUniqueEntryName($"{folderName}/{SanitizeSegment(attachmentFileName)}", usedEntryNames);

                var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(attachmentPath);
                await fileStream.CopyToAsync(entryStream);
                wroteAny = true;
            }
        }

        if (!wroteAny)
            return null;

        return memoryStream.ToArray();
    }

    private static string SanitizeSegment(string segment) => segment.Replace('/', '-').Replace('\\', '-').Trim();

    private static string MakeUniqueEntryName(string entryName, HashSet<string> used)
    {
        if (used.Add(entryName))
            return entryName;

        var extension = Path.GetExtension(entryName);
        var withoutExtension = entryName[..^extension.Length];
        var counter = 2;
        string candidate;
        do
        {
            candidate = $"{withoutExtension} ({counter}){extension}";
            counter++;
        } while (!used.Add(candidate));
        return candidate;
    }
}
