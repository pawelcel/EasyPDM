using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Npgsql;

// Is a CAD client's local copy of a component the item's current CAD file?
//
// An assembly is built against particular files of its components, and someone else may upload
// a newer one -- a new revision, or the same revision overwritten -- while the assembly is being
// worked on. The server cannot see which component files the client has open, so the client
// says what it has, per component, and the server compares it with what it holds:
//
//   contentVersion  a stamp the client wrote into the file before uploading it (the FreeCAD
//                   add-on keeps it in the document's Meta). It stays the same when the file is
//                   saved again without being uploaded, which a hash does not: CAD programs
//                   re-save components when the assembly is saved.
//   fileHash        SHA-256 of the local file, for files without a stamp (older uploads, CAD
//                   clients that do not stamp yet).
//
// item_cad_versions keeps every CAD file an item has had, so a copy can be told apart as
// "current", "older" (with who uploaded the newer one and when) or "unknown" -- changed
// locally, or uploaded where the server never saw it. Read-only and open to every signed-in
// user, as GET /api/items and the attachments are.
static partial class CadVersionEndpoints
{
    private const int MaxComponents = 2000;

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex ContentVersionPattern();

    // The stamp kept for an upload: on a "cad" file and in a plain form only.
    public static string? CleanContentVersion(string? role, string? value) =>
        role == "cad" && value is not null && ContentVersionPattern().IsMatch(value) ? value : null;

    public static async Task RecordAsync(NpgsqlConnection conn, Guid itemId, Guid attachmentId, string fileName,
        string? contentVersion, string hash, int? revisionNumber, Guid userId)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO item_cad_versions (item_id, attachment_id, file_name, content_version, file_hash, revision_number, uploaded_by)
            VALUES (@itemId, @attachmentId, @fileName, @contentVersion, @hash, @revisionNumber, @userId);
            """, conn);
        cmd.Parameters.AddWithValue("itemId", itemId);
        cmd.Parameters.AddWithValue("attachmentId", attachmentId);
        cmd.Parameters.AddWithValue("fileName", fileName);
        cmd.Parameters.AddWithValue("contentVersion", (object?)contentVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("revisionNumber", (object?)revisionNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public sealed record LocalCopy(Guid ItemId, string? ContentVersion, string? FileHash);
    public sealed record CheckRequest(List<LocalCopy>? Components);

    private sealed record Version(string FileName, string? ContentVersion, string? FileHash, int? RevisionNumber,
        DateTime UploadedAt, string? UploadedBy);

    public static void MapCadVersionEndpoints(this WebApplication app, string connectionString)
    {
        // POST /api/items/cad-versions   { "components": [ { itemId, contentVersion?, fileHash? } ] }
        // -> one entry per component: the item's state and current CAD file, and "local":
        //    "current" | "older" | "unknown" | "none" (the item has no CAD file).
        app.MapPost("/api/items/cad-versions", async (CheckRequest body) =>
        {
            var components = body.Components ?? [];
            if (components.Count > MaxComponents)
                return Results.BadRequest($"At most {MaxComponents} components per request.");
            var ids = components.Select(c => c.ItemId).Distinct().ToArray();

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            var items = new Dictionary<Guid, (string Status, int? Revision, bool Locked, string? Owner)>();
            await using (var cmd = new NpgsqlCommand($"""
                SELECT i.id, i.status, i.revision_number, i.owner_locked, {UserNames.FullNameSql("u")}
                FROM items i LEFT JOIN users u ON u.id = i.owner_id
                WHERE i.id = ANY(@ids);
                """, conn))
            {
                cmd.Parameters.AddWithValue("ids", ids);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    items[reader.GetGuid(0)] = (
                        reader.IsDBNull(1) ? "" : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetInt32(2),
                        reader.GetBoolean(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4));
            }

            // Every known version, newest first; the first per item is the current file.
            var versions = new Dictionary<Guid, List<Version>>();
            await using (var cmd = new NpgsqlCommand($"""
                SELECT v.item_id, v.file_name, v.content_version, v.file_hash, v.revision_number, v.uploaded_at,
                       {UserNames.FullNameSql("u")}
                FROM item_cad_versions v LEFT JOIN users u ON u.id = v.uploaded_by
                WHERE v.item_id = ANY(@ids)
                ORDER BY v.uploaded_at DESC, v.id;
                """, conn))
            {
                cmd.Parameters.AddWithValue("ids", ids);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var itemId = reader.GetGuid(0);
                    if (!versions.TryGetValue(itemId, out var list))
                        versions[itemId] = list = [];
                    list.Add(new Version(
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        reader.GetDateTime(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6)));
                }
            }

            var result = components.Select(c =>
            {
                if (!items.TryGetValue(c.ItemId, out var item))
                    return (object)new { itemId = c.ItemId, found = false };
                var known = versions.GetValueOrDefault(c.ItemId) ?? [];
                var current = known.FirstOrDefault();
                var (local, matched) = Classify(c, known);
                return new
                {
                    itemId = c.ItemId,
                    found = true,
                    status = item.Status,
                    revisionNumber = item.Revision,
                    revisionLabel = RevisionLabeling.Label(item.Revision),
                    ownerLocked = item.Locked,
                    ownerDisplayName = item.Owner,
                    current = current is null ? null : Describe(current),
                    local,
                    localVersion = matched is null ? null : Describe(matched),
                };
            }).ToList();
            return Results.Ok(result);
        });
    }

    // "current" when the copy matches the newest file, "older" when it matches an earlier one,
    // "unknown" when it matches none, "none" when the item has no CAD file at all. The stamp
    // decides when the client sent one; the hash only for copies without a stamp, so a stamped
    // file re-saved by the CAD program is not taken for a changed one.
    private static (string Local, Version? Matched) Classify(LocalCopy copy, List<Version> known)
    {
        if (known.Count == 0)
            return ("none", null);
        Func<Version, bool>? same = null;
        if (!string.IsNullOrEmpty(copy.ContentVersion))
            same = v => v.ContentVersion == copy.ContentVersion;
        else if (!string.IsNullOrEmpty(copy.FileHash))
            same = v => string.Equals(v.FileHash, copy.FileHash, StringComparison.OrdinalIgnoreCase);
        if (same is null)
            return ("unknown", null);
        var index = known.FindIndex(v => same(v));
        return index switch
        {
            < 0 => ("unknown", null),
            0 => ("current", known[0]),
            _ => ("older", known[index]),
        };
    }

    private static object Describe(Version v) => new
    {
        fileName = v.FileName,
        revisionNumber = v.RevisionNumber,
        revisionLabel = RevisionLabeling.Label(v.RevisionNumber),
        uploadedAt = v.UploadedAt,
        uploadedBy = v.UploadedBy,
    };
}
