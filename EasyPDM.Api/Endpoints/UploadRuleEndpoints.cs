using Npgsql;

// Ustawienia -> Magazyn plików -> Dozwolone typy plików (zob. UploadPolicy): lista rozszerzeń,
// każde z opcjonalnym limitem MB. Pusta lista = każdy typ, jak dotąd. Tylko administrator.
static class UploadRuleEndpoints
{
    private const int MaxLimitMb = 1_000_000;

    public static void MapUploadRuleEndpoints(this WebApplication app, string connectionString, UploadPolicy uploads)
    {
        // GET /api/settings/upload-rules -> { rules: [{ extension, maxSizeMb }], cadMacroExtensions: [...] }
        app.MapGet("/api/settings/upload-rules", (HttpContext ctx) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            uploads.Invalidate();
            return Results.Ok(new
            {
                rules = uploads.Rules.OrderBy(r => r.Key, StringComparer.Ordinal)
                    .Select(r => new { extension = r.Key, maxSizeMb = r.Value }),
                cadMacroExtensions = UploadPolicy.CadMacroExtensions,
            });
        });

        // PUT /api/settings/upload-rules   body: { rules: [{ extension: "step", maxSizeMb: 200 }] }
        // Zastępuje całą listę. Rozszerzenia są normalizowane (".step", małe litery).
        app.MapPut("/api/settings/upload-rules", async (HttpContext ctx, UploadRulesRequest body) =>
        {
            if (!AuthEndpoints.IsAdmin(ctx))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var rules = new Dictionary<string, int?>(StringComparer.Ordinal);
            foreach (var rule in body.Rules ?? [])
            {
                var ext = UploadPolicy.Normalize(rule.Extension);
                if (ext is null)
                    return Results.BadRequest($"'{rule.Extension}' is not a file extension (letters and digits, e.g. .step).");
                if (rule.MaxSizeMb is int mb && (mb < 1 || mb > MaxLimitMb))
                    return Results.BadRequest($"The limit for {ext} must be between 1 and {MaxLimitMb} MB, or empty for no limit.");
                if (!rules.TryAdd(ext, rule.MaxSizeMb))
                    return Results.BadRequest($"{ext} is on the list twice.");
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var del = new NpgsqlCommand("DELETE FROM upload_rules;", conn, tx))
                await del.ExecuteNonQueryAsync();
            foreach (var (ext, mb) in rules)
            {
                await using var ins = new NpgsqlCommand("INSERT INTO upload_rules (extension, max_size_mb) VALUES (@ext, @mb);", conn, tx);
                ins.Parameters.AddWithValue("ext", ext);
                ins.Parameters.AddWithValue("mb", (object?)mb ?? DBNull.Value);
                await ins.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            uploads.Invalidate();

            return Results.Ok(new
            {
                rules = rules.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => new { extension = r.Key, maxSizeMb = r.Value }),
            });
        });
    }
}

record UploadRuleItem(string? Extension, int? MaxSizeMb);
record UploadRulesRequest(List<UploadRuleItem>? Rules);
