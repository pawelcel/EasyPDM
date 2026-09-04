using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;

static class ItemEndpoints
{
    public static void MapItemEndpoints(this WebApplication app, string connectionString, StorageSettings storage, CreateTicketStore createTicketStore)
    {
        // GET /api/create-tickets/{ticket} — odpytywane przez makro CAD po otwarciu
        // przeglądarki (zob. Ticket w POST /nodes poniżej): "pending" dopóki przeglądarka nie
        // dokończy tworzenia elementu, potem dane nowego elementu. Nieznany bilet (jeszcze nie
        // istnieje w store, bo POST /nodes go jeszcze nie wypełnił) traktowany tak samo jak
        // "pending" — z punktu widzenia odpytującego nie ma różnicy, upraszcza pętlę w makrze
        // do dwóch stanów zamiast trzech.
        app.MapGet("/api/create-tickets/{ticket:guid}", (Guid ticket) =>
        {
            if (!createTicketStore.TryGet(ticket, out var state))
                return Results.Accepted(value: new { status = "pending" });

            return Results.Ok(new
            {
                itemId = state.ItemId,
                itemNumber = state.ItemNumber,
                itemNumberPrefix = state.ItemNumberPrefix,
                name = state.Name,
                exportStep = state.ExportStep,
                exportPdf = state.ExportPdf,
                existing = state.Existing,
            });
        });

        // POST /api/create-tickets/{ticket}/attach-existing   body: { itemId, exportStep? }
        // Druga (obok POST /nodes z Ticket) droga dopełnienia biletu — makro CAD otworzyło
        // przeglądarkę bez wiedzy, czy użytkownik zdecyduje się na nowy element, czy dogranie
        // do już istniejącego (zob. deep-link ?ticket=&name= w App.tsx); to jest ta druga
        // opcja, wybierana w banerze "oczekujące żądanie z makra" zamiast przez zwykłe
        // AddNodeDialog. Nie tworzy niczego — tylko każe makru dograć plik do WSKAZANEGO,
        // już istniejącego elementu (existing=true w odpowiedzi GET /create-tickets/{ticket}
        // każe makru odpalić lokalną ścieżkę push_to_existing_item zamiast tworzenia).
        app.MapPost("/api/create-tickets/{ticket:guid}/attach-existing", async (Guid ticket, AttachExistingTicketRequest body, HttpContext ctx) =>
        {
            var info = await GetItemTypeAndStatus(connectionString, body.ItemId);
            if (info is null)
                return Results.NotFound("Element nie istnieje.");
            if (info.Value.ItemType != "part" && info.Value.ItemType != "assembly")
                return Results.BadRequest("Można dograć plik tylko do Części albo Złożenia.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ProjectAccessForbidden();

            const string sql = "SELECT item_number, item_number_prefix, file_name FROM items WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", body.ItemId);
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            var itemNumber = reader.IsDBNull(0) ? (int?)null : reader.GetInt32(0);
            var itemNumberPrefix = reader.IsDBNull(1) ? null : reader.GetString(1);
            var fileName = reader.GetString(2);

            createTicketStore.Complete(ticket, body.ItemId, itemNumber, itemNumberPrefix, fileName, body.ExportStep, body.ExportPdf, existing: true);
            return Results.Ok();
        });

        // POST /api/projects/{projectId}/items   multipart/form-data:
        //   file        — wymagany, sam plik
        //   properties  — opcjonalny, JSON np. {"material":"Stal S235"}
        //   parentId    — opcjonalny, guid — jeśli podany, plik od razu trafia jako podelement w drzewku
        app.MapPost("/api/projects/{projectId:guid}/items", async (Guid projectId, HttpRequest request, HttpContext ctx) =>
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

            if (!await HasProjectAccessAsync(conn, ctx, projectId))
                return ProjectAccessForbidden();

            if (parentId is not null)
            {
                var parentInfo = await GetItemTypeAndStatus(connectionString, parentId.Value);
                if (parentInfo is null)
                    return Results.NotFound("Element nadrzędny nie istnieje.");
                if (!IsChildTypeAllowed(parentInfo.Value.ItemType, "file"))
                    return Results.BadRequest("Do tego elementu nie można nic dodać w strukturze.");
                // Rodzic zablokowany przez innego właściciela nie może dostać nowego dziecka —
                // ta sama reguła co dla dopięcia JUŻ ISTNIEJĄCEGO elementu (POST .../children).
                // W praktyce rodzicem "file" może tu być tylko folder (IsChildTypeAllowed), a
                // foldery nigdy nie mają owner_locked=true — sprawdzenie i tak dodane dla
                // spójności z resztą API, na wypadek gdyby ta reguła kiedyś się rozszerzyła.
                var uploadUser = (CurrentUser)ctx.Items["CurrentUser"]!;
                if (!CanEditOwnerLocked(uploadUser.Id, parentInfo.Value.OwnerId, parentInfo.Value.OwnerLocked))
                    return OwnerLockedForbidden();
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
            var projectStorageDir = Path.Combine(storage.Path, projectId.ToString());
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

            // show_in_tree=false gdy element od razu powstaje jako podelement (parentId podany) —
            // inaczej pokazywałby się PODWÓJNIE: jako korzeń projektu ORAZ zagnieżdżony pod
            // rodzicem. Element bez rodzica dostaje domyślne true (widoczny jako korzeń).
            const string insertSql = """
                INSERT INTO items (id, project_id, item_type, file_path, file_name, file_type, file_hash, file_size, modified_at, properties, root_position, show_in_tree)
                VALUES (
                    @id, @projectId, 'file', @filePath, @fileName, @fileType, @hash, @size, now(), @props::jsonb,
                    COALESCE((SELECT MAX(root_position) FROM items WHERE project_id = @projectId), 0) + 1,
                    @showInTree
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
                insertCmd.Parameters.AddWithValue("showInTree", parentId is null);
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
        app.MapPost("/api/projects/{projectId:guid}/nodes", async (Guid projectId, CreateNodeRequest body, HttpContext ctx) =>
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

            if (!await HasProjectAccessAsync(conn, ctx, projectId))
                return ProjectAccessForbidden();

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            if (body.ParentId is not null)
            {
                var parentInfo = await GetItemTypeAndStatus(connectionString, body.ParentId.Value);
                if (parentInfo is null)
                    return Results.NotFound("Element nadrzędny nie istnieje.");
                if (!IsChildTypeAllowed(parentInfo.Value.ItemType, body.ItemType))
                    return Results.BadRequest("Do tego elementu nie można nic dodać w strukturze.");
                // Rodzic zablokowany przez innego właściciela nie może dostać nowego dziecka —
                // ta sama reguła co dla dopięcia JUŻ ISTNIEJĄCEGO elementu (POST .../children,
                // StructureEndpoints.cs). Bez tego dowolny użytkownik z dostępem do projektu
                // mógł dopisać nową pozycję BOM-u pod zablokowanym złożeniem, mimo że każda
                // INNA zmiana tego złożenia (nazwa, właściwości, dopięcie istniejącego dziecka)
                // była poprawnie blokowana.
                if (!CanEditOwnerLocked(user.Id, parentInfo.Value.OwnerId, parentInfo.Value.OwnerLocked))
                    return OwnerLockedForbidden();
            }

            var itemId = Guid.NewGuid();
            var propertiesJson = body.Properties.HasValue ? body.Properties.Value.GetRawText() : "{}";
            // "rodzaj" decyduje o prefiksie numeru — zob. item_number_prefixes (Ustawienia ->
            // Nazewnictwo). Zamrożony TERAZ, w momencie tworzenia; późniejsza zmiana rodzaju
            // elementu albo mapowania w Ustawieniach nic tu już nie zmienia.
            string? propertyRodzaj = body.Properties.HasValue
                && body.Properties.Value.TryGetProperty("rodzaj", out var rodzajEl)
                && rodzajEl.ValueKind == JsonValueKind.String
                ? rodzajEl.GetString()
                : null;
            string? rodzaj = body.ItemType == "assembly"
                ? AssemblyPrefixKind(propertyRodzaj)
                : propertyRodzaj;

            // show_in_tree=false gdy element od razu powstaje jako podelement (parentId podany) —
            // inaczej pokazywałby się PODWÓJNIE: jako korzeń projektu ORAZ zagnieżdżony pod
            // rodzicem. Element bez rodzica dostaje domyślne true (widoczny jako korzeń).
            const string insertSql = """
                INSERT INTO items (id, project_id, item_type, file_name, properties, item_number, item_number_prefix, status, revision_number, modified_at, root_position, owner_id, owner_locked, created_by, show_in_tree)
                VALUES (
                    @id, @projectId, @itemType, @name, @props::jsonb,
                    CASE WHEN @itemType IN ('part', 'assembly') THEN nextval('item_number_seq') ELSE NULL END,
                    CASE WHEN @itemType IN ('part', 'assembly')
                         THEN (SELECT prefix FROM item_number_prefixes WHERE rodzaj = @rodzaj)
                         ELSE NULL END,
                    CASE WHEN @itemType IN ('part', 'assembly') THEN 'w_pracy' ELSE NULL END,
                    CASE WHEN @itemType IN ('part', 'assembly') THEN 1 ELSE NULL END,
                    now(),
                    COALESCE((SELECT MAX(root_position) FROM items WHERE project_id = @projectId), 0) + 1,
                    CASE WHEN @itemType IN ('part', 'assembly') THEN @ownerId ELSE NULL END,
                    @itemType IN ('part', 'assembly'),
                    @ownerId,
                    @showInTree
                )
                RETURNING item_number, item_number_prefix;
                """;
            await using var insertCmd = new NpgsqlCommand(insertSql, conn);
            insertCmd.Parameters.AddWithValue("id", itemId);
            insertCmd.Parameters.AddWithValue("projectId", projectId);
            insertCmd.Parameters.AddWithValue("itemType", body.ItemType);
            insertCmd.Parameters.AddWithValue("name", body.Name.Trim());
            insertCmd.Parameters.AddWithValue("props", propertiesJson);
            insertCmd.Parameters.AddWithValue("ownerId", user.Id);
            insertCmd.Parameters.AddWithValue("rodzaj", (object?)rodzaj ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("showInTree", body.ParentId is null);

            await using var reader = await insertCmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            int? itemNumber = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            string? itemNumberPrefix = reader.IsDBNull(1) ? null : reader.GetString(1);
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

            // Zamyka pętlę dla makra CAD, które otworzyło przeglądarkę i teraz odpytuje
            // GET /create-tickets/{ticket} — zob. CreateTicketStore.cs. Czysto addytywne,
            // zero zmiany zachowania dla wywołań bez ticketu (czyli normalnego webowego UI).
            if (body.Ticket is not null)
                createTicketStore.Complete(body.Ticket.Value, itemId, itemNumber, itemNumberPrefix, body.Name.Trim(), body.ExportStep, body.ExportPdf, existing: false);

            return Results.Created($"/api/items/{itemId}", new { id = itemId, itemNumber, itemNumberPrefix });
        });

        // ============================================================
        // POST /api/items/{id}/duplicate   body: { "parentId": "..."|null, "insertAfterOriginal": bool }
        // Tworzy kopię Części/Złożenia: ten sam typ i właściwości, nowy numer (kolejny z
        // item_number_seq), świeży status "w_pracy" i rewizja 1 (bez tagów i załączników
        // oryginału). Bez tagów i załączników oryginału.
        //
        // "insertAfterOriginal" włącza umieszczenie kopii DOKŁADNIE pod oryginałem, na tym samym
        // poziomie struktury (używane z widoku drzewka konkretnego projektu, gdzie znany jest
        // kontekst — "parentId" to rodzic, pod którym użytkownik aktualnie ogląda oryginał):
        //   - jeśli "parentId" wskazuje relację, która faktycznie istnieje (oryginał jest
        //     dzieckiem tego rodzica) — kopia trafia jako nowe dziecko TEGO SAMEGO rodzica,
        //     zaraz po oryginale (z tą samą ilością), a pozycje dalszych elementów przesuwają
        //     się o 1;
        //   - w przeciwnym razie (parentId=null, czyli oryginał jest korzeniem projektu) —
        //     kopia trafia jako nowy korzeń zaraz po oryginale, a pozycje dalszych korzeni
        //     przesuwają się o 1.
        // Bez "insertAfterOriginal" (np. duplikowanie z widoku całej bazy, gdzie nie ma
        // załadowanej struktury/relacji) kopia trafia po prostu na koniec listy korzeni projektu
        // — nie ma jak wywnioskować "właściwego" miejsca bez tego kontekstu.
        // ============================================================
        app.MapPost("/api/items/{id:guid}/duplicate", async (Guid id, DuplicateItemRequest? body, HttpContext ctx) =>
        {
            var info = await GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();
            if (info.Value.ItemType != "part" && info.Value.ItemType != "assembly")
                return Results.BadRequest("Duplikować można tylko Części i Złożenia.");
            var currentUser = (CurrentUser)ctx.Items["CurrentUser"]!;

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ProjectAccessForbidden();

            await using var tx = await conn.BeginTransactionAsync();

            var newId = Guid.NewGuid();

            if (body?.InsertAfterOriginal is true && body.ParentId is not null)
            {
                (int Position, decimal Quantity)? relation = null;
                await using (var relCmd = new NpgsqlCommand(
                    "SELECT position, quantity FROM item_relations WHERE parent_id = @parentId AND child_id = @childId;", conn, tx))
                {
                    relCmd.Parameters.AddWithValue("parentId", body.ParentId.Value);
                    relCmd.Parameters.AddWithValue("childId", id);
                    await using var reader = await relCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                        relation = (reader.GetInt32(0), reader.GetDecimal(1));
                }

                if (relation is not null)
                {
                    await using (var shiftCmd = new NpgsqlCommand(
                        "UPDATE item_relations SET position = position + 1 WHERE parent_id = @parentId AND position > @position;", conn, tx))
                    {
                        shiftCmd.Parameters.AddWithValue("parentId", body.ParentId.Value);
                        shiftCmd.Parameters.AddWithValue("position", relation.Value.Position);
                        await shiftCmd.ExecuteNonQueryAsync();
                    }

                    var (itemNumber, itemNumberPrefix) = await InsertDuplicateRowAsync(conn, tx, newId, id, currentUser.Id, rootPosition: null, showInTree: false);

                    await using (var insertRelCmd = new NpgsqlCommand(
                        """
                        INSERT INTO item_relations (parent_id, child_id, quantity, position)
                        VALUES (@parentId, @childId, @quantity, @position);
                        """, conn, tx))
                    {
                        insertRelCmd.Parameters.AddWithValue("parentId", body.ParentId.Value);
                        insertRelCmd.Parameters.AddWithValue("childId", newId);
                        insertRelCmd.Parameters.AddWithValue("quantity", relation.Value.Quantity);
                        insertRelCmd.Parameters.AddWithValue("position", relation.Value.Position + 1);
                        await insertRelCmd.ExecuteNonQueryAsync();
                    }

                    await tx.CommitAsync();
                    return Results.Created($"/api/items/{newId}", new { id = newId, itemNumber, itemNumberPrefix });
                }
                // Relacja nie istnieje (nieoczekiwane, ale defensywnie) — kopiujemy jak korzeń poniżej.
            }

            if (body?.InsertAfterOriginal is true)
            {
                Guid projectId;
                int sourceRootPosition;
                await using (var infoCmd = new NpgsqlCommand(
                    "SELECT project_id, root_position FROM items WHERE id = @id;", conn, tx))
                {
                    infoCmd.Parameters.AddWithValue("id", id);
                    await using var reader = await infoCmd.ExecuteReaderAsync();
                    await reader.ReadAsync();
                    projectId = reader.GetGuid(0);
                    sourceRootPosition = reader.GetInt32(1);
                }

                await using (var shiftCmd = new NpgsqlCommand(
                    "UPDATE items SET root_position = root_position + 1 WHERE project_id = @projectId AND root_position > @rootPosition;", conn, tx))
                {
                    shiftCmd.Parameters.AddWithValue("projectId", projectId);
                    shiftCmd.Parameters.AddWithValue("rootPosition", sourceRootPosition);
                    await shiftCmd.ExecuteNonQueryAsync();
                }

                var (itemNumber, itemNumberPrefix) = await InsertDuplicateRowAsync(conn, tx, newId, id, currentUser.Id, rootPosition: sourceRootPosition + 1, showInTree: true);
                await tx.CommitAsync();
                return Results.Created($"/api/items/{newId}", new { id = newId, itemNumber, itemNumberPrefix });
            }

            var (appendedItemNumber, appendedItemNumberPrefix) = await InsertDuplicateRowAsync(conn, tx, newId, id, currentUser.Id, rootPosition: null, showInTree: true);
            await tx.CommitAsync();
            return Results.Created($"/api/items/{newId}", new { id = newId, itemNumber = appendedItemNumber, itemNumberPrefix = appendedItemNumberPrefix });
        });

        // GET /api/items/{id}/file — pobranie/podgląd samego pliku.
        app.MapGet("/api/items/{id:guid}/file", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = "SELECT file_path, file_name FROM items WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            string? path;
            string fileName;
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return Results.NotFound();

                if (reader.IsDBNull(0))
                    return Results.BadRequest("Ten element nie ma przypisanego pliku.");

                path = reader.GetString(0);
                fileName = reader.GetString(1);
            }

            // Odczyt pliku elementu — świadomie otwarty dla KAŻDEGO zalogowanego użytkownika,
            // zob. GET /api/items niżej.
            if (!File.Exists(path))
                return Results.NotFound("Plik zniknął z magazynu na dysku serwera.");

            return Results.File(path, "application/octet-stream", fileName);
        });

        // ============================================================
        // GET /api/items?search=&tag=&projectId=
        // Lista elementów z opcjonalnym filtrem po nazwie/właściwościach, tagu i projekcie.
        // Świadomie otwarta dla KAŻDEGO zalogowanego użytkownika, niezależnie od przypisania
        // do projektu (project_users) — ograniczenia project_users dotyczą pracy w konkretnym
        // projekcie (jego drzewa/struktury) i zapisów, nie samego globalnego odczytu/
        // wyszukiwania elementów ("Cała baza" ma być czytelna dla wszystkich).
        // ============================================================
        app.MapGet("/api/items", async (string? search, string? tag, Guid? projectId, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // "Cała baza" — świadomie otwarta dla KAŻDEGO zalogowanego użytkownika,
            // niezależnie od przypisania do projektu (project_users) danego elementu, także
            // dla elementów bez projektu (i.project_id IS NULL). Ograniczenia project_users
            // dotyczą wyłącznie PRACY w konkretnym projekcie (jego drzewa/struktury, zob.
            // GET /api/projects/{id}/relations) i wszystkich zapisów — nie samego odczytu.
            const string sql = """
                SELECT i.id, i.project_id, i.file_name, i.file_type, i.file_path, i.properties, i.modified_at,
                       i.item_type, i.item_number, i.item_number_prefix, i.show_in_tree, i.status, i.revision_number,
                       i.root_position, i.owner_id, i.owner_locked, u.display_name
                FROM items i
                LEFT JOIN users u ON u.id = i.owner_id
                WHERE (@search::text IS NULL OR i.file_name ILIKE '%' || @search || '%'
                                               OR i.properties::text ILIKE '%' || @search || '%'
                                               OR (COALESCE(i.item_number_prefix, '') || i.item_number::text) ILIKE '%' || @search || '%')
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
                        ["projectId"] = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                        ["fileName"] = reader.GetString(2),
                        ["fileType"] = reader.IsDBNull(3) ? null : reader.GetString(3),
                        ["filePath"] = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ["properties"] = JsonDocument.Parse(reader.GetFieldValue<string>(5)).RootElement,
                        ["modifiedAt"] = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                        ["itemType"] = reader.GetString(7),
                        ["itemNumber"] = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        ["itemNumberPrefix"] = reader.IsDBNull(9) ? null : reader.GetString(9),
                        ["showInTree"] = reader.GetBoolean(10),
                        ["status"] = reader.IsDBNull(11) ? null : reader.GetString(11),
                        ["revisionNumber"] = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                        ["rootPosition"] = reader.GetInt32(13),
                        ["ownerId"] = reader.IsDBNull(14) ? null : reader.GetGuid(14),
                        ["ownerLocked"] = reader.GetBoolean(15),
                        ["ownerDisplayName"] = reader.IsDBNull(16) ? null : reader.GetString(16),
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
        app.MapGet("/api/items/{id:guid}", async (Guid id, HttpContext ctx) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            const string sql = """
                SELECT i.id, i.project_id, i.file_name, i.file_type, i.file_path, i.properties, i.modified_at,
                       i.item_type, i.item_number, i.item_number_prefix, i.show_in_tree, i.status, i.revision_number,
                       i.root_position, i.owner_id, i.owner_locked, u.display_name
                FROM items i
                LEFT JOIN users u ON u.id = i.owner_id
                WHERE i.id = @id;
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);

            // Odczyt szczegółów elementu — świadomie otwarty dla KAŻDEGO zalogowanego
            // użytkownika (zob. GET /api/items powyżej) — bez sprawdzenia HasProjectAccessAsync.
            Dictionary<string, object?> result;
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return Results.NotFound();

                result = new Dictionary<string, object?>
                {
                    ["id"] = reader.GetGuid(0),
                    ["projectId"] = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                    ["fileName"] = reader.GetString(2),
                    ["fileType"] = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ["filePath"] = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ["properties"] = JsonDocument.Parse(reader.GetFieldValue<string>(5)).RootElement,
                    ["modifiedAt"] = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    ["itemType"] = reader.GetString(7),
                    ["itemNumber"] = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    ["itemNumberPrefix"] = reader.IsDBNull(9) ? null : reader.GetString(9),
                    ["showInTree"] = reader.GetBoolean(10),
                    ["status"] = reader.IsDBNull(11) ? null : reader.GetString(11),
                    ["revisionNumber"] = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    ["rootPosition"] = reader.GetInt32(13),
                    ["ownerId"] = reader.IsDBNull(14) ? null : reader.GetGuid(14),
                    ["ownerLocked"] = reader.GetBoolean(15),
                    ["ownerDisplayName"] = reader.IsDBNull(16) ? null : reader.GetString(16),
                };
            }

            var tagsByItem = await LoadTagsForItems(connectionString, new List<Guid> { id });
            result["tags"] = tagsByItem.TryGetValue(id, out var t) ? t : new List<string>();

            return Results.Ok(result);
        });

        // ============================================================
        // PATCH /api/items/{id}/name   body: { "name": "Nowa nazwa" }
        // ============================================================
        app.MapPatch("/api/items/{id:guid}/name", async (Guid id, RenameRequest body, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return Results.BadRequest("Nazwa nie może być pusta.");

            var info = await GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ProjectAccessForbidden();

            if (IsLocked(info.Value.ItemType, info.Value.Status))
                return Results.BadRequest("Nazwę można zmieniać tylko w statusie 'W pracy'.");
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return OwnerLockedForbidden();

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
        //   wydany     -> w_pracy | anulowana
        //   anulowana  -> w_pracy
        // Wejście "w_pracy" Z "wydany" ALBO "anulowana" podnosi revision_number o 1 —
        // frontend prosi o potwierdzenie (i komentarz, patrz niżej). "Anulowana" wybieralna
        // WYŁĄCZNIE z "wydany" (element musiał zostać wydany, zanim okazał się zbędny);
        // złożenie z anulowanym elementem gdziekolwiek w BOM-ie (na dowolnej głębokości) nie
        // może samo przejść na "wydany" — patrz FindCancelledDescendantLabelsAsync niżej.
        // "comment" ma sens tylko przy podnoszeniu rewizji — opisuje, co się zmieniło w
        // nowej rewizji; tworzony zarówno z aplikacji webowej, jak i z makra FreeCAD. Jest
        // opcjonalny — puste/brak pola nic nie zapisuje.
        // ============================================================
        app.MapPatch("/api/items/{id:guid}/status", async (Guid id, StatusRequest body, HttpContext ctx) =>
        {
            if (body.Status != "w_pracy" && body.Status != "sprawdzany" && body.Status != "wydany" && body.Status != "anulowana")
                return Results.BadRequest("Nieprawidłowy status — dozwolone: 'w_pracy', 'sprawdzany', 'wydany', 'anulowana'.");

            var info = await GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ProjectAccessForbidden();

            var itemType = info.Value.ItemType;
            var currentStatus = info.Value.Status;

            if (itemType != "part" && itemType != "assembly")
                return Results.BadRequest("Status dotyczy tylko Części i Złożeń.");

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            // Jedyne miejsce w API, gdzie admin OMIJA blokadę właściciela (w odróżnieniu od
            // /lock, /release i edycji właściwości) — świadoma decyzja, żeby powiadomienia o
            // statusie (status_review/status_released/status_regressed/new_revision) miały w
            // ogóle szansę się uruchomić: bez tego wyjątku tylko sam właściciel mógłby zmienić
            // status zablokowanego elementu, więc "ktoś inny zmienił status" nigdy by nie
            // zaszło, dopóki element ma właściciela.
            if (!CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked) && user.Role != "admin")
                return OwnerLockedForbidden();

            bool isValidTransition = body.Status == currentStatus || (currentStatus, body.Status) switch
            {
                ("w_pracy", "sprawdzany") => true,
                ("sprawdzany", "w_pracy") => true,
                ("sprawdzany", "wydany") => true,
                ("wydany", "w_pracy") => true,
                ("wydany", "anulowana") => true,
                ("anulowana", "w_pracy") => true,
                (null, "w_pracy") => true,
                _ => false
            };
            if (!isValidTransition)
                return Results.BadRequest($"Nie można zmienić statusu z '{currentStatus}' na '{body.Status}'.");

            // Złożenie z anulowanym elementem w BOM-ie (na dowolnej głębokości, nie tylko
            // bezpośrednie dzieci) nie może zostać wydane — komunikat wskazuje który
            // element trafia bezpośrednio do okna potwierdzenia zmiany statusu na froncie
            // (StatusControl), bez potrzeby osobnego dialogu.
            if (itemType == "assembly" && body.Status == "wydany")
            {
                var cancelledDescendants = await FindCancelledDescendantLabelsAsync(conn, id);
                if (cancelledDescendants.Count > 0)
                    return Results.BadRequest(
                        $"Nie można ustawić statusu \"Wydany\" — złożenie zawiera anulowane elementy: {string.Join(", ", cancelledDescendants)}.");
            }

            bool bumpRevision = (currentStatus == "wydany" || currentStatus == "anulowana") && body.Status == "w_pracy";

            // Wydany element nie może mieć właściciela ani być zablokowany — zawsze jest zwolniony
            // (patrz też /lock i /release, które odrzucają próby zmiany blokady w tym statusie).
            //
            // WHERE status IS NOT DISTINCT FROM @currentStatus zamyka wyścig: currentStatus
            // przeczytany jest OSOBNYM zapytaniem wyżej (GetItemTypeAndStatus), więc dwa niemal
            // jednoczesne żądania (np. przypadkowy podwójny klik) mogłyby oba przejść walidację
            // przejścia i oba wykonać UPDATE, podwójnie podbijając revision_number. Warunek w
            // WHERE sprawia, że tylko PIERWSZE z nich faktycznie zmieni wiersz — drugie trafia
            // w 0 dopasowanych wierszy i dostaje 409 zamiast cichej podwójnej zmiany.
            string sql = bumpRevision
                ? """
                  UPDATE items SET status = @status, revision_number = COALESCE(revision_number, 0) + 1
                  WHERE id = @id AND status IS NOT DISTINCT FROM @currentStatus
                  RETURNING revision_number;
                  """
                : """
                  UPDATE items SET status = @status,
                         owner_id = CASE WHEN @status = 'wydany' THEN NULL ELSE owner_id END,
                         owner_locked = CASE WHEN @status = 'wydany' THEN false ELSE owner_locked END
                  WHERE id = @id AND status IS NOT DISTINCT FROM @currentStatus
                  RETURNING revision_number;
                  """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("status", body.Status);
            cmd.Parameters.AddWithValue("currentStatus", (object?)currentStatus ?? DBNull.Value);

            int? revisionNumber;
            bool rowFound;
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                rowFound = await reader.ReadAsync();
                revisionNumber = rowFound && !reader.IsDBNull(0) ? reader.GetInt32(0) : null;
            }

            if (!rowFound)
                return Results.Conflict("Status elementu zmienił się w międzyczasie — odśwież i spróbuj ponownie.");

            // Przejście na "wydany" niejawnie zwalnia właściciela (CASE WHEN w UPDATE-cie
            // wyżej) — w odróżnieniu od jawnego POST /release to zdarzenie nigdzie się dotąd
            // nie logowało, więc panel Historii milczał o tym zwolnieniu mimo że faktycznie
            // nastąpiło. info.Value.OwnerId to stan SPRZED tego UPDATE-u (patrz komentarz przy
            // "recipientId" niżej) — poprawny warunek "był właściciel do zwolnienia".
            if (body.Status == "wydany" && info.Value.OwnerId is not null)
                await LogOwnerHistoryAsync(conn, id, "released", user.Id);

            if (bumpRevision && revisionNumber is not null && !string.IsNullOrWhiteSpace(body.Comment))
            {
                const string commentSql = """
                    INSERT INTO item_revision_comments (item_id, revision_number, comment, created_by)
                    VALUES (@itemId, @revisionNumber, @comment, @userId)
                    ON CONFLICT (item_id, revision_number) DO UPDATE SET comment = EXCLUDED.comment, created_at = now(), created_by = EXCLUDED.created_by;
                    """;
                await using var commentCmd = new NpgsqlCommand(commentSql, conn);
                commentCmd.Parameters.AddWithValue("itemId", id);
                commentCmd.Parameters.AddWithValue("revisionNumber", revisionNumber.Value);
                commentCmd.Parameters.AddWithValue("comment", body.Comment!.Trim());
                commentCmd.Parameters.AddWithValue("userId", user.Id);
                await commentCmd.ExecuteNonQueryAsync();
            }

            // Historia (do panelu "Historia") — tylko rzeczywista zmiana, nie zapisujemy
            // wywołań, które nadają ten sam status, co już jest ustawiony.
            if (body.Status != currentStatus)
            {
                const string historySql = """
                    INSERT INTO item_status_history (item_id, from_status, to_status, changed_by)
                    VALUES (@itemId, @fromStatus, @toStatus, @userId);
                    """;
                await using var historyCmd = new NpgsqlCommand(historySql, conn);
                historyCmd.Parameters.AddWithValue("itemId", id);
                historyCmd.Parameters.AddWithValue("fromStatus", (object?)currentStatus ?? DBNull.Value);
                historyCmd.Parameters.AddWithValue("toStatus", body.Status);
                historyCmd.Parameters.AddWithValue("userId", user.Id);
                await historyCmd.ExecuteNonQueryAsync();
            }

            // Powiadomienia — nigdy o własnej akcji, i tylko gdy jest kogo powiadomić.
            // info.Value.OwnerId to stan SPRZED tego UPDATE-u (dla przejścia na "wydany" SQL
            // wyżej dopiero za chwilę zeruje owner_id, więc to wciąż poprawny odbiorca TEGO
            // przejścia) — ale element w statusie "wydany" ZAWSZE ma owner_id=NULL (nadane przy
            // poprzednim przejściu NA "wydany"), więc przejście "wydany" -> "w_pracy"
            // (jedyne, przy którym bumpRevision w ogóle się zdarza) zastałoby OwnerId puste.
            // Stąd zapasowy odbiorca: created_by (jedyne NIGDY niekasowane pole wskazujące
            // "czyj to element", w odróżnieniu od owner_id) — bez tego "new_revision" nigdy
            // nie miałoby komu się pokazać.
            var recipientId = info.Value.OwnerId ?? info.Value.CreatedBy;
            if (recipientId is not null && recipientId != user.Id)
            {
                var itemLabel = ItemLabel(info.Value.FileName, info.Value.ItemNumber, info.Value.ItemNumberPrefix);
                var notifyData = new { itemLabel };
                // body.Status != currentStatus — tak samo jak zapis do historii wyżej — bez
                // tego zduplikowany/powtórzony request (np. retry po zgubionej odpowiedzi z
                // makra CAD) z tym samym, JUŻ ustawionym statusem ponownie wysyłałby to samo
                // powiadomienie, mimo że faktycznie nic się nie zmieniło.
                if (body.Status != currentStatus && body.Status == "sprawdzany")
                    await Notifications.NotifyAsync(conn, app.Logger, recipientId.Value, "status_review", notifyData, itemId: id);
                else if (body.Status != currentStatus && body.Status == "wydany")
                    await Notifications.NotifyAsync(conn, app.Logger, recipientId.Value, "status_released", notifyData, itemId: id);
                else if (body.Status == "w_pracy" && (currentStatus == "sprawdzany" || currentStatus == "wydany" || currentStatus == "anulowana"))
                    await Notifications.NotifyAsync(conn, app.Logger, recipientId.Value, "status_regressed", notifyData, itemId: id);

                if (bumpRevision && revisionNumber is not null)
                    await Notifications.NotifyAsync(conn, app.Logger, recipientId.Value, "new_revision", notifyData, itemId: id);
            }

            return Results.Ok(new { status = body.Status, revisionNumber });
        });

        // ============================================================
        // POST /api/items/{id}/lock — "Zablokuj". Dostępne dla KAŻDEGO, ale tylko gdy element
        // jest aktualnie zwolniony — zablokowanie ustawia wywołującego jako nowego właściciela.
        // Jeśli element jest już zablokowany przez kogoś innego, odrzucamy — Z WYJĄTKIEM
        // admina, który może "przejąć" cudzą blokadę (np. nieobecny pracownik) i staje się
        // nowym właścicielem. To jedyne dwa miejsca w całym API, gdzie admin omija blokadę
        // właściciela (obok /release poniżej i zmiany statusu) — edycja właściwości nadal
        // wymaga bycia właścicielem nawet dla admina.
        // ============================================================
        app.MapPost("/api/items/{id:guid}/lock", async (Guid id, HttpContext ctx) =>
        {
            var info = await GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ProjectAccessForbidden();

            if (info.Value.ItemType != "part" && info.Value.ItemType != "assembly")
                return Results.BadRequest("Właściciela/blokadę mają tylko Części i Złożenia.");
            if (info.Value.Status == "wydany" || info.Value.Status == "anulowana")
                return Results.BadRequest("Wydanych ani anulowanych elementów nie można blokować — są zawsze zwolnione.");

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            var isAdmin = user.Role == "admin";
            if (IsOwnerLocked(info.Value.OwnerId, info.Value.OwnerLocked) && info.Value.OwnerId != user.Id && !isAdmin)
                return OwnerLockedForbidden();

            // WHERE (owner_locked = false OR owner_id = @ownerId OR @isAdmin) odtwarza dokładnie
            // ten sam warunek co sprawdzenie wyżej, ale ATOMOWO w samym UPDATE — bez tego dwóch
            // użytkowników klikających "Zablokuj" na tym samym, właśnie odblokowanym elemencie
            // niemal jednocześnie mogłoby OBAJ przejść powyższe sprawdzenie (czytane z osobnego
            // zapytania chwilę wcześniej) i oboje dostać 200 OK, choć w bazie wygrywa tylko ten,
            // czyj UPDATE się wykona jako drugi.
            const string sql = """
                UPDATE items SET owner_id = @ownerId, owner_locked = true
                WHERE id = @id AND (owner_locked = false OR owner_id = @ownerId OR @isAdmin);
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("ownerId", user.Id);
            cmd.Parameters.AddWithValue("isAdmin", isAdmin);
            var affected = await cmd.ExecuteNonQueryAsync();

            if (affected == 0)
                return OwnerLockedForbidden();

            await LogOwnerHistoryAsync(conn, id, "locked", user.Id);

            return Results.Ok(new { ownerId = user.Id, ownerLocked = true });
        });

        // ============================================================
        // POST /api/items/{id}/release — "Zwolnij". Zwykle tylko AKTUALNY właściciel może
        // zwolnić zablokowany element — WYJĄTEK: admin może zwolnić blokadę należącą do
        // kogokolwiek (np. nieobecny pracownik), tak samo jak w /lock powyżej.
        // ============================================================
        app.MapPost("/api/items/{id:guid}/release", async (Guid id, HttpContext ctx) =>
        {
            var info = await GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ProjectAccessForbidden();

            if (info.Value.ItemType != "part" && info.Value.ItemType != "assembly")
                return Results.BadRequest("Właściciela/blokadę mają tylko Części i Złożenia.");
            if (info.Value.Status == "wydany" || info.Value.Status == "anulowana")
                return Results.BadRequest("Wydane i anulowane elementy są zawsze zwolnione.");
            if (!IsOwnerLocked(info.Value.OwnerId, info.Value.OwnerLocked))
                return Results.BadRequest("Element nie jest zablokowany.");

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            var isAdmin = user.Role == "admin";
            if (info.Value.OwnerId != user.Id && !isAdmin)
                return Results.Text(
                    "Tylko właściciel może zwolnić ten element.", statusCode: StatusCodes.Status403Forbidden);

            // Zwolniony element nie ma właściciela — i tak może go edytować każdy, więc nie ma
            // sensu pamiętać, kto ostatnio go blokował. Nowego właściciela nadaje dopiero
            // kolejne "Zablokuj" (patrz endpoint /lock powyżej).
            //
            // WHERE (owner_id = @userId OR @isAdmin) zamyka wyścig analogiczny do tego w /lock:
            // "info" i sprawdzenie wyżej czytane są z osobnego zapytania chwilę wcześniej — bez
            // tego warunku w UPDATE, spóźnione /release (np. stary, właśnie odrzucony request
            // pierwotnego właściciela) mogłoby wyzerować blokadę, którą w międzyczasie
            // legalnie przejął ktoś inny (np. admin przez /lock), cicho kasując cudzą, świeżo
            // nabytą własność.
            const string sql = "UPDATE items SET owner_id = NULL, owner_locked = false WHERE id = @id AND (owner_id = @userId OR @isAdmin);";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("userId", user.Id);
            cmd.Parameters.AddWithValue("isAdmin", isAdmin);
            var affected = await cmd.ExecuteNonQueryAsync();

            if (affected == 0)
                return Results.Conflict("Właściciel elementu zmienił się w międzyczasie — odśwież i spróbuj ponownie.");

            await LogOwnerHistoryAsync(conn, id, "released", user.Id);

            return Results.Ok(new { ownerId = (Guid?)null, ownerLocked = false });
        });

        // GET /api/items/{id}/revisions — historia komentarzy rewizji (tylko te, które mają
        // komentarz — rewizje bez komentarza nie mają tu wpisu).
        app.MapGet("/api/items/{id:guid}/revisions", async (Guid id, HttpContext ctx) =>
        {
            var info = await GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            // Odczyt historii rewizji — świadomie otwarty dla KAŻDEGO zalogowanego
            // użytkownika, zob. GET /api/items.
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
        app.MapPatch("/api/items/{id:guid}/visibility", async (Guid id, VisibilityRequest body, HttpContext ctx) =>
        {
            var info = await GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ProjectAccessForbidden();

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return OwnerLockedForbidden();

            const string sql = "UPDATE items SET show_in_tree = @value WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("value", body.ShowInTree);
            var affected = await cmd.ExecuteNonQueryAsync();

            return affected == 0 ? Results.NotFound() : Results.Ok();
        });

        // PATCH /api/items/{id}/project   body: { "projectId": "..." | null }
        // Przenosi Część/Złożenie do innego projektu jako widoczny korzeń jego drzewka — używane,
        // gdy dodajemy "Istniejący element" z całej bazy na poziomie głównym (bez rodzica) projektu,
        // do którego element jeszcze nie należy. Zagnieżdżone odwołania w item_relations (element
        // jako komponent BOM w innych złożeniach) zostają nienaruszone niezależnie od project_id.
        // projectId=null ODPINA element od jakiegokolwiek projektu (zob. "Usuń ze struktury" w
        // aplikacji webowej i synchronizację BOM w makrach CAD) — element pozostaje w pełni
        // widoczny przez "Cała baza", tylko nie należy już do żadnego konkretnego projektu.
        app.MapPatch("/api/items/{id:guid}/project", async (Guid id, MoveToProjectRequest body, HttpContext ctx) =>
        {
            var info = await GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ProjectAccessForbidden();

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return OwnerLockedForbidden();

            if (body.ProjectId is not null)
            {
                await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM projects WHERE id = @projectId;", conn))
                {
                    checkCmd.Parameters.AddWithValue("projectId", body.ProjectId.Value);
                    if (await checkCmd.ExecuteScalarAsync() is null)
                        return Results.NotFound("Projekt docelowy nie istnieje.");
                }

                if (!await HasProjectAccessAsync(conn, ctx, body.ProjectId))
                    return ProjectAccessForbidden();
            }

            const string sql = "UPDATE items SET project_id = @projectId, show_in_tree = true WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("projectId", (object?)body.ProjectId ?? DBNull.Value);
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

            // Część/Złożenie trzymają swój plik CAD (i podgląd STEP/PDF) w item_attachments,
            // NIE w items.file_path (to pole jest tylko dla Plików) -- bez tego te pliki
            // zostawałyby sierotami na dysku na zawsze: ON DELETE CASCADE kasuje wiersze
            // item_attachments razem z items, ale samych plików fizycznie nie rusza.
            // Zapytanie MUSI polecieć przed DELETE FROM items (kaskada skasuje wiersze).
            await using (var attachmentsCmd = new NpgsqlCommand(
                "SELECT file_path FROM item_attachments WHERE item_id = ANY(@ids);", conn))
            {
                attachmentsCmd.Parameters.AddWithValue("ids", idsToDelete.ToArray());
                await using var reader = await attachmentsCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    filePaths.Add(reader.GetString(0));
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

    internal static async Task<(string ItemType, string? Status, Guid? OwnerId, bool OwnerLocked, Guid? ProjectId, string FileName, int? ItemNumber, string? ItemNumberPrefix, Guid? CreatedBy)?> GetItemTypeAndStatus(string connectionString, Guid id)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = "SELECT item_type, status, owner_id, owner_locked, project_id, file_name, item_number, item_number_prefix, created_by FROM items WHERE id = @id;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return (
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetGuid(8)
        );
    }

    // Etykieta elementu do zapisania w powiadomieniu (data JSONB) -- ten sam format co
    // itemDisplayLabel() po stronie frontu, zamrożony w momencie zdarzenia (przetrwa
    // późniejszą zmianę nazwy/numeru elementu).
    internal static string ItemLabel(string fileName, int? itemNumber, string? itemNumberPrefix) =>
        itemNumber is not null ? $"{itemNumberPrefix}{itemNumber} ({fileName})" : fileName;

    // Etykiety WSZYSTKICH elementów o statusie "anulowana" w całym poddrzewie BOM-u
    // złożenia @id (na dowolnej głębokości, nie tylko bezpośrednie dzieci) -- ten sam
    // rekurencyjny wzorzec co BomEndpoints.FetchBomRowsAsync (tablica "visited" jako
    // zabezpieczenie przed cyklem), tylko bez ilości/ścieżki, bo tu liczy się wyłącznie
    // istnienie. Wołane z PATCH /status przed dopuszczeniem przejścia na "wydany".
    private static async Task<List<string>> FindCancelledDescendantLabelsAsync(NpgsqlConnection conn, Guid id)
    {
        const string sql = """
            WITH RECURSIVE bom AS (
                SELECT ir.child_id, ARRAY[ir.parent_id] AS visited
                FROM item_relations ir
                WHERE ir.parent_id = @id
                UNION ALL
                SELECT ir.child_id, b.visited || ir.parent_id
                FROM item_relations ir
                JOIN bom b ON ir.parent_id = b.child_id
                WHERE NOT (ir.parent_id = ANY(b.visited))
            )
            SELECT i.file_name, i.item_number, i.item_number_prefix
            FROM bom b
            JOIN items i ON i.id = b.child_id
            WHERE i.status = 'anulowana';
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        var labels = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            labels.Add(ItemLabel(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        return labels;
    }

    // Rodzaj Złożenia -> klucz w item_number_prefixes. Złożenie zakupowe/klienta dzieli
    // prefiks z odpowiednim rodzajem Części (to ten sam towar, tylko złożony), a złożenie
    // wykonywane ma własny, historyczny klucz "Zlozenie". Brak rodzaju (np. złożenie
    // wgrane makrem CAD, które o rodzaj nie pyta) też trafia na "Zlozenie" -- dokładnie
    // to, co ten kod robił dla KAŻDEGO złożenia, zanim złożenia dostały rodzaj.
    // Odpowiednik tego mapowania w SQL jest w InsertDuplicateRowAsync niżej.
    internal static string AssemblyPrefixKind(string? rodzaj) => rodzaj switch
    {
        "Zakupowe" => "Zakupowa",
        "Klienta" => "Klienta",
        _ => "Zlozenie",
    };

    // Wspólne dla wszystkich endpointów operujących na elemencie/projekcie po ID — zwykły
    // użytkownik musi być przypisany do projektu (project_users), administrator zawsze ma
    // dostęp. Używane zarówno dla odczytu, jak i mutacji — nieprzypisany użytkownik nie
    // powinien móc ani przeglądać, ani zmieniać niczego w projekcie, którego nie widzi.
    // Element BEZ projektu (projectId == null) nie należy do żadnego projektu do którego
    // dostęp trzeba by sprawdzać — otwarty dla każdego zalogowanego, także przy zapisie
    // (widoczny wyłącznie przez globalne "Cała baza", które jest jawnie otwarte dla
    // wszystkich, zob. GET /api/items).
    internal static async Task<bool> HasProjectAccessAsync(NpgsqlConnection conn, HttpContext ctx, Guid? projectId)
    {
        if (projectId is null) return true;

        var user = (CurrentUser)ctx.Items["CurrentUser"]!;
        if (user.Role == "admin") return true;

        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM project_users WHERE project_id = @projectId AND user_id = @userId;", conn);
        cmd.Parameters.AddWithValue("projectId", projectId.Value);
        cmd.Parameters.AddWithValue("userId", user.Id);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    internal static IResult ProjectAccessForbidden() =>
        Results.Text("Brak dostępu do tego projektu.", statusCode: StatusCodes.Status403Forbidden);

    // Właściciel (Właściciel/owner_id + owner_locked) — NIEZALEŻNE od statusu 'w_pracy'/IsLocked
    // powyżej. Dopóki element jest zablokowany, tylko owner_id może go edytować — nawet
    // administrator nie omija tej blokady (w odróżnieniu od każdej innej reguły w tym API).
    internal static bool IsOwnerLocked(Guid? ownerId, bool ownerLocked) => ownerLocked && ownerId is not null;

    internal static bool CanEditOwnerLocked(Guid currentUserId, Guid? ownerId, bool ownerLocked) =>
        !IsOwnerLocked(ownerId, ownerLocked) || ownerId == currentUserId;

    internal static IResult OwnerLockedForbidden() => Results.Text(
        "Ten element jest zablokowany przez innego użytkownika — tylko właściciel może go edytować.",
        statusCode: StatusCodes.Status403Forbidden);

    // Wpis do panelu "Historia" — kto i kiedy zablokował (przejął na własność) albo zwolnił
    // element (patrz /lock i /release powyżej).
    private static async Task LogOwnerHistoryAsync(NpgsqlConnection conn, Guid itemId, string action, Guid userId)
    {
        const string sql = """
            INSERT INTO item_owner_history (item_id, action, user_id)
            VALUES (@itemId, @action, @userId);
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("itemId", itemId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    // Wspólny insert dla duplikowania — używany zarówno przy wstawianiu kopii "zaraz pod
    // oryginałem" (rootPosition podany explicite) jak i przy zwykłym dopisaniu na koniec listy
    // korzeni projektu (rootPosition = null, wyliczane tu jako MAX+1).
    internal static async Task<(int? ItemNumber, string? ItemNumberPrefix)> InsertDuplicateRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid newId, Guid sourceId, Guid ownerId, int? rootPosition, bool showInTree)
    {
        // Kopia to NOWY rekord — dostaje własnego właściciela (osobę duplikującą), zablokowanego
        // od razu, tak samo jak przy ręcznym tworzeniu Części/Złożenia; nie dziedziczy właściciela
        // oryginału. item_number_prefix liczony na podstawie WŁASNEGO (skopiowanego) rodzaju
        // kopii i BIEŻĄCEGO mapowania w Ustawieniach w momencie duplikacji — nie kopiowany
        // z oryginału (oryginał mógł powstać dawno temu, przy innym mapowaniu). show_in_tree
        // jest false, gdy kopia od razu ląduje jako podelement (żeby nie pokazać się PODWÓJNIE:
        // jako korzeń projektu ORAZ zagnieżdżona pod rodzicem) — patrz wywołania.
        const string sql = """
            INSERT INTO items (id, project_id, item_type, file_name, properties, item_number, item_number_prefix, status, revision_number, modified_at, root_position, owner_id, owner_locked, created_by, show_in_tree)
            SELECT @newId, src.project_id, src.item_type, src.file_name || ' (kopia)', src.properties,
                   nextval('item_number_seq'),
                   p.prefix,
                   'w_pracy', 1, now(),
                   COALESCE(@rootPosition, (SELECT COALESCE(MAX(root_position), 0) + 1 FROM items WHERE project_id = src.project_id)),
                   @ownerId, true, @ownerId, @showInTree
            FROM items src
            LEFT JOIN item_number_prefixes p
                ON p.rodzaj = (CASE
                    WHEN src.item_type = 'assembly' THEN (CASE src.properties->>'rodzaj'
                        WHEN 'Zakupowe' THEN 'Zakupowa'
                        WHEN 'Klienta' THEN 'Klienta'
                        ELSE 'Zlozenie' END)
                    ELSE src.properties->>'rodzaj' END)
            WHERE src.id = @sourceId
            RETURNING item_number, item_number_prefix;
            """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("newId", newId);
        cmd.Parameters.AddWithValue("sourceId", sourceId);
        cmd.Parameters.AddWithValue("ownerId", ownerId);
        cmd.Parameters.AddWithValue("rootPosition", (object?)rootPosition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("showInTree", showInTree);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1));
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

record CreateNodeRequest(string Name, string ItemType, JsonElement? Properties, Guid? ParentId, Guid? Ticket, bool? ExportStep, bool? ExportPdf);
record AttachExistingTicketRequest(Guid ItemId, bool? ExportStep, bool? ExportPdf);
record VisibilityRequest(bool ShowInTree);
record RenameRequest(string Name);
record StatusRequest(string Status, string? Comment = null);
record MoveToProjectRequest(Guid? ProjectId);
record DuplicateItemRequest(Guid? ParentId = null, bool InsertAfterOriginal = false);
