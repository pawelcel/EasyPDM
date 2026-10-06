using Npgsql;

// Postęp wysyłki/pobierania: makro CAD zgłasza listę plików i odhacza kolejne, a aplikacja
// webowa pokazuje to jako listę z ptaszkami. Zob. TransferProgressStore po powód, dla
// którego stan siedzi w pamięci i jest kluczowany UŻYTKOWNIKIEM, a nie identyfikatorem sesji.
//
// Wszystkie cztery endpointy działają na "wołającym" — nie przyjmują żadnego identyfikatora
// użytkownika z zewnątrz. Makro jest zalogowane tym samym kontem co przeglądarka (most
// bilet->ciasteczko, zob. AuthEndpoints), więc jedno i drugie trafia w ten sam wpis bez
// przekazywania czegokolwiek w URL-u.
static class TransferProgressEndpoints
{
    // Ile pozycji trafia do raportu w powiadomieniu. Złożenie potrafi mieć ich kilkadziesiąt,
    // a powiadomienie ma zostać czytelnym podsumowaniem, nie kopią całej listy -- reszta jest
    // policzona ("i jeszcze N"). Sama lista plik po pliku jest i tak widoczna NA ŻYWO w panelu
    // postępu, po to on jest.
    private const int MaxReportedEntries = 40;

    public static void MapTransferProgressEndpoints(this WebApplication app, TransferProgressStore store, string connectionString)
    {
        // PUT /api/progress   body: { "kind": "upload"|"download", "entries": [{ "key": "...", "label": "..." }] }
        // Wołane RAZ, zanim makro zacznie przesyłać cokolwiek. Zastępuje poprzedni bieg tego
        // samego użytkownika -- nowe kliknięcie "Upload" unieważnia starą listę.
        app.MapPut("/api/progress", async (StartProgressRequest body, HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            if (body.Kind is not ("upload" or "download"))
                return Results.BadRequest("Pole 'kind' musi być 'upload' albo 'download'.");
            if (body.Entries is null || body.Entries.Count == 0)
                return Results.BadRequest("Pole 'entries' nie może być puste.");

            // Klucze muszą być unikalne, bo po nich idzie odhaczanie. Przy wysyłce złożenia
            // ten sam plik potrafi wystąpić w drzewie wielokrotnie (ta sama śruba w kilku
            // miejscach) -- makro ma wtedy przysłać go RAZ, zgodnie z tym, że i wysyła go raz.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<TransferProgressEntry>();
            foreach (var e in body.Entries)
            {
                if (string.IsNullOrWhiteSpace(e.Key) || string.IsNullOrWhiteSpace(e.Label))
                    return Results.BadRequest("Każda pozycja musi mieć niepuste 'key' i 'label'.");
                if (!seen.Add(e.Key))
                    return Results.BadRequest($"Zduplikowany klucz pozycji: '{e.Key}'.");
                entries.Add(new TransferProgressEntry { Key = e.Key, Label = e.Label });
            }

            // Drzewo do wyświetlenia. Relacje przysyła makro (wysyłka: klucze to ścieżki plików,
            // a nowych komponentów jeszcze nie ma w PDM, więc struktura istnieje tylko w CAD-zie).
            // Przy pobieraniu kluczami są identyfikatory elementów, a relacje między nimi są już
            // w bazie — serwer bierze je sam, więc istniejące makra pobierania nie muszą się zmieniać.
            var edges = body.Edges?
                .Where(e => !string.IsNullOrWhiteSpace(e.Parent) && !string.IsNullOrWhiteSpace(e.Child))
                .Select(e => (e.Parent, e.Child))
                .ToList() ?? [];
            if (edges.Count == 0 && body.Kind == "download")
                edges = await LoadDownloadEdgesAsync(connectionString, entries);

            entries = ProgressTree.Arrange(entries, edges);
            store.Start(user.Id, body.Kind, entries);
            return Results.Ok(new { count = entries.Count });
        });

        // PATCH /api/progress   body: { "key": "...", "status": "active"|"done"|"failed"|"skipped" }
        // Brak biegu albo nieznany klucz to NIE jest błąd dla makra: zwracamy 200 z
        // "matched": false. Postęp jest informacją poboczną i nic w makrze nie może się
        // wywrócić dlatego, że serwer zdążył się w międzyczasie zrestartować.
        app.MapPatch("/api/progress", (MarkProgressRequest body, HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;

            if (body.Status is not ("pending" or "active" or "done" or "failed" or "skipped"))
                return Results.BadRequest("Pole 'status' musi być 'pending', 'active', 'done', 'failed' albo 'skipped'.");
            if (string.IsNullOrWhiteSpace(body.Key))
                return Results.BadRequest("Pole 'key' nie może być puste.");

            return Results.Ok(new { matched = store.Mark(user.Id, body.Key, body.Status) });
        });

        // POST /api/progress/finish — makro skończyło. Lista NIE znika od razu: zostaje
        // oznaczona jako zakończona, żeby użytkownik zobaczył komplet ptaszków.
        app.MapPost("/api/progress/finish", async (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            var finished = store.Finish(user.Id);

            // Raport z biegu zostaje w powiadomieniach. Makro kończyło dotąd blokującym oknem
            // w CAD-zie -- a odkąd fokus po wysyłce wraca do przeglądarki, takie okno powstaje
            // ZA nią i wisi, czekając na kliknięcie, którego nikt nie widzi. Powiadomienie
            // trafia tam, gdzie człowiek i tak patrzy, i zostaje do odszukania później.
            //
            // Powstaje TUTAJ, a nie w makrze, z tego samego powodu co liczenie postępu po
            // stronie serwera: jedno miejsce na trzy CAD-y, więc raport wygląda tak samo
            // niezależnie od tego, z czego wysyłano.
            if (finished is not null)
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                await Notifications.NotifyAsync(
                    conn, app.Logger, user.Id, "cad_transfer_finished", BuildReport(finished));
            }

            return Results.Ok();
        });

        // POST /api/progress/cancel — użytkownik prosi o przerwanie biegu (przycisk „Anuluj"
        // w panelu postępu). Zob. TransferProgressStore.Cancel po to, czego serwer tu NIE robi.
        app.MapPost("/api/progress/cancel", (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            return Results.Ok(new { matched = store.Cancel(user.Id) });
        });

        // GET /api/progress/cancelled — makro pyta, czy ma się zatrzymać. Płaska liczba 1/0,
        // a nie wartość logiczna: parsery JSON w makrach VBA mają tylko JsonGetString i
        // JsonGetLong (ten sam powód co przy /api/cad-requests/taken).
        app.MapGet("/api/progress/cancelled", (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            return Results.Ok(new { cancelled = store.IsCancelled(user.Id) ? 1 : 0 });
        });

        // DELETE /api/progress — zamknięcie listy przez użytkownika w przeglądarce.
        app.MapDelete("/api/progress", (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            store.Clear(user.Id);
            return Results.Ok();
        });

        // GET /api/progress — odpytywane przez aplikację webową co ~1 s. Brak biegu to
        // zwykły stan, nie błąd: zwracamy 200 i "null", żeby front nie musiał odróżniać
        // 404 "nie ma biegu" od 404 "zły adres".
        app.MapGet("/api/progress", (HttpContext ctx) =>
        {
            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            var progress = store.Get(user.Id);
            if (progress is null)
                return Results.Ok(new { progress = (object?)null });

            return Results.Ok(new
            {
                progress = new
                {
                    kind = progress.Kind,
                    finished = progress.Finished,
                    cancelled = progress.Cancelled,
                    startedAt = progress.StartedAt,
                    // Liczby wyliczamy TUTAJ, a nie na froncie -- ta sama zasada co przy
                    // itemNumberLabel/recordName: jedno miejsce liczy, klienci tylko pokazują.
                    total = progress.Entries.Count,
                    done = progress.Entries.Count(e => e.Status is "done" or "skipped"),
                    entries = progress.Entries.Select(e => new { key = e.Key, label = e.Label, status = e.Status, depth = e.Depth }),
                }
            });
        });
    }

    record ProgressEntryRequest(string Key, string Label);
    record ProgressEdgeRequest(string Parent, string Child);
    record StartProgressRequest(string Kind, List<ProgressEntryRequest>? Entries, List<ProgressEdgeRequest>? Edges);

    // Relacje BOM między pozycjami listy pobierania (kluczami są tu identyfikatory elementów).
    // Klucz, który nie jest identyfikatorem, po prostu nie bierze udziału — lista zostaje płaska.
    // Błąd bazy też niczego nie przerywa: drzewo to wygoda wyświetlania, nie część pobierania.
    private static async Task<List<(string Parent, string Child)>> LoadDownloadEdgesAsync(
        string connectionString, List<TransferProgressEntry> entries)
    {
        // Relacje wracają z bazy jako Guid; odwzorowujemy je na DOKŁADNIE te napisy kluczy, które
        // przysłało makro — inaczej różnica w wielkości liter rozjechałaby dopasowanie.
        var keyById = new Dictionary<Guid, string>();
        foreach (var e in entries)
            if (Guid.TryParse(e.Key, out var id))
                keyById.TryAdd(id, e.Key);
        var ids = keyById.Keys.ToArray();
        var result = new List<(string Parent, string Child)>();
        if (ids.Length < 2)
            return result;
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            const string sql = """
                SELECT parent_id, child_id FROM item_relations
                WHERE parent_id = ANY(@ids) AND child_id = ANY(@ids)
                ORDER BY parent_id, position;
                """;
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("ids", ids);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                result.Add((keyById[reader.GetGuid(0)], keyById[reader.GetGuid(1)]));
        }
        catch
        {
            result.Clear();
        }
        return result;
    }
    record MarkProgressRequest(string Key, string Status);

    // Raport z zakończonego biegu: liczby plus lista pozycji, żeby w powiadomieniu dało się
    // zobaczyć NIE TYLKO ile, ale i co poszło. "pending" to pozycje, których makro nie zdążyło
    // oznaczyć (bieg przerwany) -- liczone osobno od "failed", bo nic się nie zepsuło, po
    // prostu do nich nie doszło.
    private static object BuildReport(TransferProgress progress)
    {
        var entries = progress.Entries;
        var done = entries.Count(e => e.Status == "done");
        var skipped = entries.Count(e => e.Status == "skipped");
        var failed = entries.Count(e => e.Status == "failed");

        return new
        {
            kind = progress.Kind,
            cancelled = progress.Cancelled,
            total = entries.Count,
            done,
            skipped,
            failed,
            pending = entries.Count - done - skipped - failed,
            entries = entries.Take(MaxReportedEntries).Select(e => new { label = e.Label, status = e.Status }).ToList(),
            omitted = Math.Max(0, entries.Count - MaxReportedEntries),
        };
    }
}
