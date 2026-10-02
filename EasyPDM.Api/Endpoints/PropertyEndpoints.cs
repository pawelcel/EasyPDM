using System.Text.Json;
using Npgsql;

static class PropertyEndpoints
{
    // Właściwości Ceny (cena/waluta/brutto-netto/data wprowadzenia ceny) można zmieniać
    // w dowolnym statusie — wszystko inne tylko w statusie 'w_pracy'.
    private static readonly HashSet<string> AlwaysEditableKeys = new()
    {
        "price", "currency", "priceType", "priceDate"
    };

    public static void MapPropertyEndpoints(this WebApplication app, string connectionString)
    {
        // ============================================================
        // PATCH /api/items/{id}/properties   body: { "material": "Stal S235", "supplier": "..." }
        // ============================================================
        app.MapPatch("/api/items/{id:guid}/properties", async (Guid id, JsonElement body, HttpContext ctx) =>
        {
            // Bez tego sprawdzenia np. body `5` albo `[1,2]` (poprawny JSON, ale nie obiekt)
            // rzucałoby niezłapany wyjątek niżej (EnumerateObject()/JSONB `||` na nie-obiekcie) —
            // 500 zamiast czytelnego 400.
            if (body.ValueKind != JsonValueKind.Object)
                return Results.BadRequest("Body musi być obiektem JSON.");

            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status))
            {
                foreach (var prop in body.EnumerateObject())
                {
                    if (!AlwaysEditableKeys.Contains(prop.Name))
                        return Results.BadRequest(
                            "Właściwości można zmieniać tylko w statusie 'W pracy' (wyjątek: cena, waluta, brutto/netto).");
                }
            }

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            // Rodzaj wyznacza prefiks numeru, a prefiks wchodzi w nazwę pliku: to makro CAD
            // buduje ją z numeru elementu i pod taką nazwą plik ląduje na dysku oraz w bazie.
            // Dlatego gdy element ma już plik w którymkolwiek z czterech wyróżnionych pól
            // (preview_role: cad, drawing, pdf, step), zmiana rodzaju jest ODRZUCANA -- nie
            // "przyjmowana ze starym prefiksem", bo rodzaj rozjechany z numerem jest gorszy
            // niż brak możliwości poprawki.
            //
            // Zwykłe załączniki (preview_role IS NULL -- atest, karta materiałowa, zdjęcie)
            // nie blokują niczego: zachowują własną nazwę nadaną przez wgrywającego, więc
            // numer elementu nigdzie w nich nie występuje.
            //
            // Dopóki wyróżnione pola są puste, pomyłka w rodzaju ("wykonywana" zamiast
            // "zakupowej") da się poprawić, a prefiks przelicza się razem z nią.
            string? newRodzaj = null;
            if ((info.Value.ItemType == "part" || info.Value.ItemType == "assembly")
                && body.TryGetProperty("rodzaj", out var rodzajValue)
                && rodzajValue.ValueKind == JsonValueKind.String)
            {
                newRodzaj = rodzajValue.GetString();
            }

            // Tylko RZECZYWISTA zmiana jest blokowana -- ponowne przysłanie tej samej wartości
            // (np. zapis całego formularza) nie ma powodu kończyć się błędem.
            if (newRodzaj is not null && newRodzaj != await GetCurrentRodzajAsync(conn, id)
                && await HasNamedFileAsync(conn, id))
            {
                return Results.BadRequest(
                    "Nie można zmienić rodzaju — element ma już plik w jednym z wyróżnionych pól " +
                    "(CAD, rysunek, PDF, model 3D), a nazwy tych plików zawierają jego numer, " +
                    "który od rodzaju zależy.");
            }

            await using var tx = await conn.BeginTransactionAsync();

            const string sql = """
                UPDATE items SET properties = properties || @props::jsonb
                WHERE id = @id;
                """;
            await using (var cmd = new NpgsqlCommand(sql, conn, tx))
            {
                cmd.Parameters.AddWithValue("id", id);
                cmd.Parameters.AddWithValue("props", body.GetRawText());
                await cmd.ExecuteNonQueryAsync();
            }

            // Nieznany materiał z makra CAD zakłada się sam -- zob. MaterialCatalog, gdzie
            // leży powód i dlaczego to samo robi ścieżka tworzenia elementu.
            await MaterialCatalog.EnsureFromPropertiesAsync(body, conn, tx);

            var prefixRecalculated = false;
            if (newRodzaj is not null)
            {
                // Rodzaj Złożenia ma własne mapowanie na klucz numeracji (zakupowe/klienta dzielą
                // prefiks z odpowiednim rodzajem Części) -- zob. ItemEndpoints.AssemblyPrefixKind.
                var prefixKind = info.Value.ItemType == "assembly"
                    ? ItemEndpoints.AssemblyPrefixKind(newRodzaj)
                    : newRodzaj;

                // Warunek NOT EXISTS zostaje mimo sprawdzenia wyżej: tamto chroni przed
                // świadomą zmianą, ten przed wyścigiem (plik wgrany między jednym a drugim).
                const string recalcSql = """
                    UPDATE items
                    SET item_number_prefix = (SELECT prefix FROM item_number_prefixes WHERE rodzaj = @rodzaj)
                    WHERE id = @id
                      AND NOT EXISTS (
                          SELECT 1 FROM item_attachments
                          WHERE item_id = @id AND preview_role IS NOT NULL
                      );
                    """;
                await using var recalcCmd = new NpgsqlCommand(recalcSql, conn, tx);
                recalcCmd.Parameters.AddWithValue("id", id);
                recalcCmd.Parameters.AddWithValue("rodzaj", (object?)prefixKind ?? DBNull.Value);
                prefixRecalculated = await recalcCmd.ExecuteNonQueryAsync() > 0;
            }

            await tx.CommitAsync();

            return Results.Ok(new { prefixRecalculated });
        });

        // ============================================================
        // DELETE /api/items/{id}/properties/{key}
        // ============================================================
        app.MapDelete("/api/items/{id:guid}/properties/{key}", async (Guid id, string key, HttpContext ctx) =>
        {
            var info = await ItemEndpoints.GetItemTypeAndStatus(connectionString, id);
            if (info is null)
                return Results.NotFound();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            if (!await ItemEndpoints.HasProjectAccessAsync(conn, ctx, info.Value.ProjectId))
                return ItemEndpoints.ProjectAccessForbidden();

            if (ItemEndpoints.IsLocked(info.Value.ItemType, info.Value.Status) && !AlwaysEditableKeys.Contains(key))
                return Results.BadRequest(
                    "Właściwości można zmieniać tylko w statusie 'W pracy' (wyjątek: cena, waluta, brutto/netto).");

            var user = (CurrentUser)ctx.Items["CurrentUser"]!;
            if (!ItemEndpoints.CanEditOwnerLocked(user.Id, info.Value.OwnerId, info.Value.OwnerLocked))
                return ItemEndpoints.OwnerLockedForbidden();

            const string sql = "UPDATE items SET properties = properties - @key WHERE id = @id;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("key", key);
            await cmd.ExecuteNonQueryAsync();

            return Results.Ok();
        });
    }

    // Rodzaj zapisany dziś na elemencie -- do porównania, czy PATCH faktycznie go zmienia.
    private static async Task<string?> GetCurrentRodzajAsync(NpgsqlConnection conn, Guid id)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT properties->>'rodzaj' FROM items WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync() as string;
    }

    // Czy element ma plik w którymś z czterech wyróżnionych pól -- czyli taki, którego nazwę
    // makro zbudowało z numeru elementu.
    private static async Task<bool> HasNamedFileAsync(NpgsqlConnection conn, Guid id)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM item_attachments WHERE item_id = @id AND preview_role IS NOT NULL);",
            conn);
        cmd.Parameters.AddWithValue("id", id);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
}
