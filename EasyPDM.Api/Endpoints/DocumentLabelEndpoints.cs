using Npgsql;

// Podpisy w dokumentach (Ustawienia -> Język) — zob. DocumentLabels.cs. Odczyt dla każdego
// zalogowanego, zmiana tylko dla administratora, jak sama lista języków dokumentacji.
static class DocumentLabelEndpoints
{
    public static void MapDocumentLabelEndpoints(this WebApplication app, string connectionString)
    {
        // GET /api/document-labels/{languageId} -> [{ key, text, defaultText, custom }]
        app.MapGet("/api/document-labels/{languageId:int}", async (int languageId) =>
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using (var exists = new NpgsqlCommand("SELECT 1 FROM project_languages WHERE id = @id;", conn))
            {
                exists.Parameters.AddWithValue("id", languageId);
                if (await exists.ExecuteScalarAsync() is null)
                    return Results.NotFound();
            }
            var labels = await DocumentLabels.ForLanguageAsync(conn, languageId);
            return Results.Ok(labels.Select(l => new { key = l.Key, text = l.Text, defaultText = l.DefaultText, custom = l.Custom }));
        });

        // PUT /api/document-labels/{languageId}   body: { "labels": { "csv.name": "Benennung", ... } }
        // Pusty tekst albo równy wbudowanemu usuwa zmianę — podpis wraca do wbudowanego (i będzie
        // podążał za jego ewentualną poprawką w kolejnej wersji). Klucze spoza listy to błąd.
        app.MapPut("/api/document-labels/{languageId:int}", async (int languageId, DocumentLabelsRequest body, HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Results.Text("Administrator rights required.", statusCode: StatusCodes.Status403Forbidden);
            var labels = body.Labels ?? [];
            if (labels.Keys.FirstOrDefault(k => !DocumentLabels.IsKnownKey(k)) is { } unknown)
                return Results.BadRequest($"Unknown label: {unknown}.");

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            string? code;
            await using (var codeCmd = new NpgsqlCommand("SELECT code FROM project_languages WHERE id = @id;", conn))
            {
                codeCmd.Parameters.AddWithValue("id", languageId);
                code = await codeCmd.ExecuteScalarAsync() as string;
            }
            if (code is null)
                return Results.NotFound();

            await using var tx = await conn.BeginTransactionAsync();
            foreach (var (key, raw) in labels)
            {
                var text = raw?.Trim() ?? "";
                if (text.Length == 0 || text == DocumentLabels.Default(key, code))
                {
                    await using var del = new NpgsqlCommand(
                        "DELETE FROM document_labels WHERE language_id = @id AND key = @key;", conn, tx);
                    del.Parameters.AddWithValue("id", languageId);
                    del.Parameters.AddWithValue("key", key);
                    await del.ExecuteNonQueryAsync();
                }
                else
                {
                    await using var up = new NpgsqlCommand("""
                        INSERT INTO document_labels (language_id, key, text) VALUES (@id, @key, @text)
                        ON CONFLICT (language_id, key) DO UPDATE SET text = EXCLUDED.text;
                        """, conn, tx);
                    up.Parameters.AddWithValue("id", languageId);
                    up.Parameters.AddWithValue("key", key);
                    up.Parameters.AddWithValue("text", text);
                    await up.ExecuteNonQueryAsync();
                }
            }
            await tx.CommitAsync();
            return Results.Ok();
        });
    }
}

record DocumentLabelsRequest(Dictionary<string, string?>? Labels);
