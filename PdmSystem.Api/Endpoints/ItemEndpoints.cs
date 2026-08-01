using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

static class ItemEndpoints
{
    public static void MapItemEndpoints(this WebApplication app, string connectionString, string storageRoot)
    {
        // POST /api/projects/{projectId}/items   multipart/form-data:
        //   file        — wymagany, sam plik
        //   properties  — opcjonalny, JSON np. {"material":"Stal S235"}
        //   parentId    — opcjonalny, guid — jeśli podany, plik od razu trafia jako podelement w drzewku
        app.MapPost("/api/projects/{projectId:guid}/items", async (Guid projectId, HttpRequest request) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Oczekiwano danych multipart/form-data.");

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.BadRequest("Brak pliku w polu 'file'.");

            Guid? parentId = null;
            if (form.TryGetValue("parentId", out var parentIdValue) && !string.IsNullOrWhiteSpace(parentIdValue))
            {
                if (!Guid.TryParse(parentIdValue, out var parsedParentId))
                    return Results.BadRequest("Pole 'parentId' zawiera niepoprawny identyfikator.");
                parentId = parsedParentId;
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @id;", conn))
            {
                checkCmd.Parameters.AddWithValue("id", projectId);
                if (await checkCmd.ExecuteScalarAsync() is null)
                    return Results.NotFound("Projekt nie istnieje.");
            }

            if (parentId is not null)
            {
                var parentInfo = await GetItemTypeAndStatus(connectionString, parentId.Value);
                if (parentInfo is null)
                    return Results.NotFound("Element nadrzędny nie istnieje.");
                if (!IsChildTypeAllowed(parentInfo.Value.ItemType, "file"))
                    return Results.BadRequest("Do tego elementu nie można nic dodać w strukturze.");
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
                INSERT INTO items (id, project_id, item_type, file_path, file_name, file_type, file_hash, file_size, modified_at, properties, root_position)
                VALUES (
                    @id, @projectId, 'file', @filePath, @fileName, @fileType, @hash, @size, now(), @props::jsonb,
                    COALESCE((SELECT MAX(root_position) FROM items WHERE project_id = @projectId), 0) + 1
                );
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

                if (parentId is not null)
                {
                    await using var relCmd = new NpgsqlCommand(
                        """
                        INSERT INTO item_relations (parent_id, child_id, quantity, position)
                        VALUES (
                            @parentId, @childId, 1,
                            COALESCE((SELECT MAX(position) FROM item_relations WHERE parent_id = @parentId), 0) + 1
                        );
                        """, conn);
                    relCmd.Parameters.AddWithValue("parentId", parentId.Value);
                    relCmd.Parameters.AddWithValue("childId", itemId);
                    await relCmd.ExecuteNonQueryAsync();
                }
            }
            catch
            {
                File.Delete(storedPath); // nie zostawiamy sierocego pliku, jeśli zapis do bazy się nie uda
                throw;
            }

            return Results.Created($"/api/items/{itemId}", new { id = itemId });
        });

        // POST /api/projects/{projectId}/nodes   body: { "name": "...", "itemType": "folder"|"part"|"file"|"assembly", "properties": {...}, "parentId": "..." }
        // Tworzy Folder, Część, Złożenie albo Plik bez zawartości (bez uploadu) — Folder/Część/Złożenie
        // to kontenery bez własnego pliku; "file" utworzony tędy to plik "na razie bez zawartości"
        // (można ją dograć później przez POST /api/projects/{projectId}/items z parentId).
        // Część i Złożenie dostają automatycznie kolejny item_number (to pozycje BOM).
        app.MapPost("/api/projects/{projectId:guid}/nodes", async (Guid projectId, CreateNodeRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa jest wymagana.");
            if (body.ItemType != "folder" && body.ItemType != "part" && body.ItemType != "file" && body.ItemType != "assembly")
                return Results.BadRequest("Nieprawidłowy typ elementu — dozwolone: 'folder', 'part', 'file', 'assembly'.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @id;", conn))
            {
                checkCmd.Parameters.AddWithValue("id", projectId);
                if (await checkCmd.ExecuteScalarAsync() is null)
                    return Results.NotFound("Projekt nie istnieje.");
            }

            if (body.ParentId is not null)
            {
                var parentInfo = await GetItemTypeAndStatus(connectionString, body.ParentId.Value);
                if (parentInfo is null)
                    return Results.NotFound("Element nadrzędny nie istnieje.");
                if (!IsChildTypeAllowed(parentInfo.Value.ItemType, body.ItemType))
                    return Results.BadRequest("Do tego elementu nie można nic dodać w strukturze.");
            }

            var itemId = Guid.NewGuid();
            var propertiesJson = body.Properties.HasValue ? body.Properties.Value.GetRawText() : "{}";

            const string insertSql = """
                INSERT INTO items (id, project_id, item_type, file_name, properties, item_number, status, revision_number, modified_at, root_position)
                VALUES (
                    @id, @projectId, @itemType, @name, @props::jsonb,
                    CASE WHEN @itemType IN ('part', 'assembly') THEN nextval('item_number_seq') ELSE NULL END,
                    CASE WHEN @itemType IN ('part', 'assembly') THEN 'w_pracy' ELSE NULL END,
                    CASE WHEN @itemType IN ('part', 'assembly') THEN 1 ELSE NULL END,
                    now(),
                    COALESCE((SELECT MAX(root_position) FROM items WHERE project_id = @projectId), 0) + 1
                )
                RETURNING item_number;
                """;
            await using var insertCmd = new NpgsqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("id", itemId);
            insertCmd.Parameters.AddWithValue("projectId", projectId);
            insertCmd.Parameters.AddWithValue("itemType", body.ItemType);
            insertCmd.Parameters.AddWithValue("name", body.Name.Trim());
            insertCmd.Parameters.AddWithValue("props", propertiesJson);

            await using var reader = await insertCmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            int? itemNumber = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            await reader.DisposeAsync();

            if (body.ParentId is not null)
            {
                await using var relCmd = new NpgsqlCommand(
                    """
                    INSERT INTO item_relations (parent_id, child_id, quantity, position)
                    VALUES (
                        @parentId, @childId, 1,
                        COALESCE((SELECT MAX(position) FROM item_relations WHERE parent_id = @parentId), 0) + 1
                    );
                    """, conn);
                relCmd.Parameters.AddWithValue("parentId", body.ParentId.Value);
                relCmd.Parameters.AddWithValue("childId", itemId);
                await relCmd.ExecuteNonQueryAsync();
            }

            return Results.Created($"/api/items/{itemId}", new { id = itemId, itemNumber });
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

            if (reader.IsDBNull(0))
                return Results.BadRequest("Ten element nie ma przypisanego pliku.");

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
                SELECT i.id, i.project_id, i.file_name, i.file_type, i.file_path, i.properties, i.modified_at,
                       i.item_type, i.item_number, i.show_in_tree, i.status, i.revision_number, i.root_position
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
                        ["fileType"] = reader.IsDBNull(3) ? null : reader.GetString(3),
                        ["filePath"] = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ["properties"] = JsonDocument.Parse(reader.GetFieldValue<string>(5)).RootElement,
                        ["modifiedAt"] = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                        ["itemType"] = reader.GetString(7),
                        ["itemNumber"] = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        ["showInTree"] = reader.GetBoolean(9),
                        ["status"] = reader.IsDBNull(10) ? null : reader.GetString(10),
                        ["revisionNumber"] = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                        ["rootPosition"] = reader.GetInt32(12),
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
                SELECT id, project_id, file_name, file_type, file_path, properties, modified_at,
                       item_type, item_number, show_in_tree, status, revision_number, root_position
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
                ["fileType"] = reader.IsDBNull(3) ? null : reader.GetString(3),
                ["filePath"] = reader.IsDBNull(4) ? null : reader.GetString(4),
                ["properties"] = JsonDocument.Parse(reader.GetFieldValue<string>(5)).RootElement,
                ["modifiedAt"] = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                ["itemType"] = reader.GetString(7),
                ["itemNumber"] = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                ["showInTree"] = reader.GetBoolean(9),
                ["status"] = reader.IsDBNull(10) ? null : reader.GetString(10),
                ["revisionNumber"] = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                ["rootPosition"] = reader.GetInt32(12),
                ["tags"] = tagsByItem.TryGetValue(id, out var t) ? t : new List<string>()
            };

            return Results.Ok(result);
        });

        // ============================================================
        // PATCH /api/items/{id}/name   body: { "name": "Nowa nazwa" }
        // ============================================================
        app.MapPatch("/api/items/{id:guid}/name", async (Guid id, RenameRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa nie może być pusta.");

            var info = await GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();
            if (IsLocked(info.Value.ItemType, info.Value.Status))
                return Results.BadRequest("Nazwę można zmieniać tylko w statusie 'W pracy'.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "UPDATE items SET file_name = @name WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("name", body.Name.Trim());
            var affected = await cmd.ExecuteNonQueryAsync();

            return affected == 0 ? Results.NotFound() : Results.Ok();
        });

        // ============================================================
        // PATCH /api/items/{id}/status   body: { "status": "...", "comment": "..." (opcjonalnie) }
        // Maszyna stanów dla Części/Złożeń:
        //   w_pracy    -> sprawdzany
        //   sprawdzany -> w_pracy | wydany
        //   wydany     -> w_pracy  (podnosi revision_number o 1 — frontend prosi o potwierdzenie)
        // "comment" ma sens tylko przy podnoszeniu rewizji (wydany -> w_pracy) — opisuje, co
        // się zmieniło w nowej rewizji; tworzony zarówno z aplikacji webowej, jak i z makra
        // FreeCAD. Jest opcjonalny — puste/brak pola nic nie zapisuje.
        // ============================================================
        app.MapPatch("/api/items/{id:guid}/status", async (Guid id, StatusRequest body) =>
        {
            if (body.Status != "w_pracy" && body.Status != "sprawdzany" && body.Status != "wydany")
                return Results.BadRequest("Nieprawidłowy status — dozwolone: 'w_pracy', 'sprawdzany', 'wydany'.");

            var info = await GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();
            var (itemType, currentStatus) = info.Value;

            if (itemType != "part" && itemType != "assembly")
                return Results.BadRequest("Status dotyczy tylko Części i Złożeń.");

            bool isValidTransition = body.Status == currentStatus || (currentStatus, body.Status) switch
            {
                ("w_pracy", "sprawdzany") => true,
                ("sprawdzany", "w_pracy") => true,
                ("sprawdzany", "wydany") => true,
                ("wydany", "w_pracy") => true,
                (null, "w_pracy") => true,
                _ => false
            };
            if (!isValidTransition)
                return Results.BadRequest($"Nie można zmienić statusu z '{currentStatus}' na '{body.Status}'.");

            bool bumpRevision = currentStatus == "wydany" && body.Status == "w_pracy";

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            string sql = bumpRevision
                ? "UPDATE items SET status = @status, revision_number = COALESCE(revision_number, 0) + 1 WHERE id = @id RETURNING revision_number;"
                : "UPDATE items SET status = @status WHERE id = @id RETURNING revision_number;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("status", body.Status);
            var revisionNumber = (int?)await cmd.ExecuteScalarAsync();

            if (bumpRevision && revisionNumber is not null && !string.IsNullOrWhiteSpace(body.Comment))
            {
                const string commentSql = """
                    INSERT INTO item_revision_comments (item_id, revision_number, comment)
                    VALUES (@itemId, @revisionNumber, @comment)
                    ON CONFLICT (item_id, revision_number) DO UPDATE SET comment = EXCLUDED.comment, created_at = now();
                    """;
                await using var commentCmd = new NpgsqlCommand(commentSql, conn);
                commentCmd.Parameters.AddWithValue("itemId", id);
                commentCmd.Parameters.AddWithValue("revisionNumber", revisionNumber.Value);
                commentCmd.Parameters.AddWithValue("comment", body.Comment!.Trim());
                await commentCmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new { status = body.Status, revisionNumber });
        });

        // GET /api/items/{id}/revisions — historia komentarzy rewizji (tylko te, które mają
        // komentarz — rewizje bez komentarza nie mają tu wpisu).
        app.MapGet("/api/items/{id:guid}/revisions", async (Guid id) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT revision_number, comment, created_at
                FROM item_revision_comments
                WHERE item_id = @id
                ORDER BY revision_number;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            var result = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new
                {
                    revisionNumber = reader.GetInt32(0),
                    comment = reader.GetString(1),
                    createdAt = reader.GetDateTime(2)
                });
            }

            return Results.Ok(result);
        });

        // PATCH /api/items/{id}/visibility   body: { "showInTree": false }
        // Element bez rodzica nie ma relacji do odpięcia — "usuń ze struktury" oznacza wtedy:
        // zostań w projekcie, ale przestań się pokazywać jako korzeń w drzewku.
        app.MapPatch("/api/items/{id:guid}/visibility", async (Guid id, VisibilityRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "UPDATE items SET show_in_tree = @value WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("value", body.ShowInTree);
            var affected = await cmd.ExecuteNonQueryAsync();

            return affected == 0 ? Results.NotFound() : Results.Ok();
        });

        // PATCH /api/items/{id}/project   body: { "projectId": "..." }
        // Przenosi Część/Złożenie do innego projektu jako widoczny korzeń jego drzewka — używane,
        // gdy dodajemy "Istniejący element" z całej bazy na poziomie głównym (bez rodzica) projektu,
        // do którego element jeszcze nie należy. Zagnieżdżone odwołania w item_relations (element
        // jako komponent BOM w innych złożeniach) zostają nienaruszone niezależnie od project_id.
        app.MapPatch("/api/items/{id:guid}/project", async (Guid id, MoveToProjectRequest body) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @projectId;", conn))
            {
                checkCmd.Parameters.AddWithValue("projectId", body.ProjectId);
                if (await checkCmd.ExecuteScalarAsync() is null)
                    return Results.NotFound("Projekt docelowy nie istnieje.");
            }

            const string sql = "UPDATE items SET project_id = @projectId, show_in_tree = true WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("projectId", body.ProjectId);
            var affected = await cmd.ExecuteNonQueryAsync();

            return affected == 0 ? Results.NotFound() : Results.Ok();
        });

        // DELETE /api/items/{id} — usuwa element CAŁKOWICIE, razem z podelementami w drzewku.
        // Element potomny jest usuwany razem z rodzicem tylko wtedy, gdy nie ma innego rodzica
        // spoza usuwanego poddrzewa (współdzielona część w innym złożeniu zostaje nietknięta —
        // zostaje jedynie odpięta od tej gałęzi, którą kasujemy).
        // Tylko administrator — zwykły użytkownik może odpinać ze struktury (visibility/children),
        // ale nie usuwać rekordów całkowicie z bazy.
        app.MapDelete("/api/items/{id:guid}", async (Guid id, HttpContext httpContext) =>
        {
            if ((httpContext.Items["CurrentUser"] as CurrentUser)?.Role != "admin")
                return Results.Text("Tylko administrator może usuwać elementy całkowicie z bazy.", statusCode: StatusCodes.Status403Forbidden);

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM items WHERE id = @id;", conn))
            {
                checkCmd.Parameters.AddWithValue("id", id);
                if (await checkCmd.ExecuteScalarAsync() is null)
                    return Results.NotFound();
            }

            const string selectSql = """
                WITH RECURSIVE descendants AS (
                    SELECT @id::uuid AS item_id
                    UNION
                    SELECT ir.child_id FROM item_relations ir
                    JOIN descendants d ON ir.parent_id = d.item_id
                ),
                to_delete AS (
                    SELECT d.item_id FROM descendants d
                    WHERE d.item_id = @id
                       OR NOT EXISTS (
                            SELECT 1 FROM item_relations ir2
                            WHERE ir2.child_id = d.item_id
                              AND ir2.parent_id NOT IN (SELECT item_id FROM descendants)
                          )
                )
                SELECT i.id, i.file_path FROM items i
                JOIN to_delete td ON td.item_id = i.id;
                """;

            var idsToDelete = new List<Guid>();
            var filePaths = new List<string>();
            await using (var selectCmd = new NpgsqlCommand(selectSql, conn))
            {
                selectCmd.Parameters.AddWithValue("id", id);
                await using var reader = await selectCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    idsToDelete.Add(reader.GetGuid(0));
                    if (!reader.IsDBNull(1))
                        filePaths.Add(reader.GetString(1));
                }
            }

            await using (var deleteCmd = new NpgsqlCommand("DELETE FROM items WHERE id = ANY(@ids);", conn))
            {
                deleteCmd.Parameters.AddWithValue("ids", idsToDelete.ToArray());
                await deleteCmd.ExecuteNonQueryAsync();
            }

            foreach (var path in filePaths)
            {
                try { File.Delete(path); } catch (IOException) { /* magazyn i tak jest sierocy — nie blokujemy usunięcia rekordu */ }
            }

            return Results.Ok(new { deletedCount = idsToDelete.Count });
        });
    }

    // Część/Złożenie poza statusem 'w_pracy' ma zablokowaną edycję właściwości/nazwy
    // (wyjątek: Cena/waluta/brutto-netto — patrz PropertyEndpoints). Folder/plik nie mają
    // statusu (null), więc nigdy nie są traktowane jako zablokowane.
    internal static bool IsLocked(string itemType, string? status) =>
        (itemType == "part" || itemType == "assembly") && status != "w_pracy";

    // Co wolno podpiąć jako dziecko pod czym w strukturze:
    //   folder    -> wszystko (folder/część/złożenie/plik)
    //   assembly  -> tylko część i złożenie (BOM)
    //   part      -> nic (Część jest liściem)
    //   file      -> nic (Plik jest liściem)
    internal static bool IsChildTypeAllowed(string parentType, string childType) => parentType switch
    {
        "folder" => true,
        "assembly" => childType is "part" or "assembly",
        _ => false
    };

    internal static async Task<(string ItemType, string? Status)?> GetItemTypeAndStatus(string connectionString, Guid id)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = "SELECT item_type, status FROM items WHERE id = @id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    internal static async Task<Dictionary<Guid, List<string>>> LoadTagsForItems(string connectionString, List<Guid> ids)
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
}

record CreateNodeRequest(string Name, string ItemType, JsonElement? Properties, Guid? ParentId);
record VisibilityRequest(bool ShowInTree);
record RenameRequest(string Name);
record StatusRequest(string Status, string? Comment = null);
record MoveToProjectRequest(Guid ProjectId);
