using System.Security.Cryptography;
using Npgsql;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

// The people on a drawing reach its PDF the moment the status changes.
//
// "Drawn by" is whoever sends the current revision for review and "approved by" whoever
// releases it (TitleBlockEndpoints.SignaturesAsync) -- the person who finished a drawing and
// sent it on answers for it, not whoever opened it first. Neither is known when the drawing is
// exported, and a released item cannot be uploaded again, so no CAD macro can put them on the
// PDF reliably. Instead, when an item moves to "sprawdzany" or "wydany", the server writes the
// title block's people and dates, as SignaturesAsync gives them, into the item's drawing PDF
// -- the attachment with role "pdf" -- and the stamped file REPLACES the original (agreed: no
// unstamped copy is kept). In review only "drawn by" is known; on release both are written.
//
// Only a PDF the macros marked as exported from an EasyPDM sheet (item_attachments.sheet_layout,
// e.g. "easypdm-A3") is touched: the layout says where the fields are. The EasyPDM sheets are the
// same in FreeCAD and SolidWorks (EasyPDM.FreeCad/templates/build_templates.py): the title block
// is 180 x 44 mm in the bottom-right corner, 10 mm from the trimmed edges; DRAWN BY's value
// baseline is 8.5 mm below its top edge and APPROVED BY's 19.5 mm, each 1.5 mm in, with the
// dates 47.5 mm in.
//
// Whatever the fields already show is painted over first (white, inside the cell lines): the
// macro may have filled them at upload, an item may go back to work and be sent for review
// again, and without it the new text landed on top of the old -- confirmed by rendering a
// stamped FreeCAD export. The layout mark is cleared once the PDF is stamped on release, so a
// released file is never stamped again (a new revision brings a new upload).
//
// Best effort: a PDF that cannot be stamped is logged and left as it is -- the status change
// has already been committed and must not fail because of a drawing file.
static class DrawingStamp
{
    private static readonly Dictionary<string, (double W, double H)> Layouts = new()
    {
        ["easypdm-A3"] = (420, 297),
        ["easypdm-A2"] = (594, 420),
        ["easypdm-A1"] = (841, 594),
    };

    public static bool IsKnownLayout(string? layout) => layout is not null && Layouts.ContainsKey(layout);

    private const double ValueSizeMm = 3.5;
    private static readonly object FontLock = new();
    private static bool _fontReady;

    // PDFsharp finds fonts through a resolver; the server has no system fonts to rely on (the
    // Docker image has none), so the sheet's own lettering is embedded in the assembly.
    private sealed class OsifontResolver : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) => new("osifont");

        public byte[]? GetFont(string faceName)
        {
            using var stream = typeof(DrawingStamp).Assembly.GetManifestResourceStream("EasyPDM.Api.Fonts.osifont.ttf")
                ?? throw new InvalidOperationException("The embedded osifont is missing.");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    private static void EnsureFont()
    {
        lock (FontLock)
        {
            if (_fontReady)
                return;
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = new OsifontResolver();
            _fontReady = true;
        }
    }

    private static double Pt(double mm) => mm * 72.0 / 25.4;

    internal readonly record struct Field(double BaselineMm, string Name, string Date);

    // Called after a change to "sprawdzany" or "wydany" has been committed.
    public static async Task StampTitleBlockAsync(NpgsqlConnection conn, ILogger logger, Guid itemId, bool released)
    {
        try
        {
            Guid attachmentId;
            string path, layout;
            await using (var cmd = new NpgsqlCommand("""
                SELECT id, file_path, sheet_layout FROM item_attachments
                WHERE item_id = @id AND preview_role = 'pdf' AND sheet_layout IS NOT NULL
                ORDER BY uploaded_at DESC LIMIT 1;
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", itemId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (!await r.ReadAsync())
                    return;
                attachmentId = r.GetGuid(0);
                path = r.GetString(1);
                layout = r.GetString(2);
            }
            if (!Layouts.TryGetValue(layout, out var size) || !File.Exists(path))
            {
                logger.LogWarning("Drawing stamp: item {Item} has a {Layout} PDF that cannot be stamped (unknown layout or missing file).", itemId, layout);
                return;
            }

            var (drawn, approved) = await TitleBlockEndpoints.SignaturesAsync(conn, itemId);
            var fields = new List<Field>();
            if (drawn is { } d)
                fields.Add(new Field(8.5, d.Name ?? "", d.At.ToLocalTime().ToString("yyyy-MM-dd")));
            if (approved is { } a)
                fields.Add(new Field(19.5, a.Name ?? "", a.At.ToLocalTime().ToString("yyyy-MM-dd")));
            if (fields.Count == 0)
                return;

            var stamped = Stamp(path, size.W, size.H, fields, out var pages);
            if (stamped == 0)
            {
                logger.LogWarning("Drawing stamp: no page of item {Item}'s PDF matches {Layout} ({Pages} page(s)); left as it is.", itemId, layout, pages);
                return;
            }

            await using (var cmd = new NpgsqlCommand(
                "UPDATE item_attachments SET file_size = @size, file_hash = @hash, sheet_layout = CASE WHEN @released THEN NULL ELSE sheet_layout END WHERE id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("id", attachmentId);
                cmd.Parameters.AddWithValue("released", released);
                cmd.Parameters.AddWithValue("size", new FileInfo(path).Length);
                await using (var fs = File.OpenRead(path))
                    cmd.Parameters.AddWithValue("hash", Convert.ToHexString(await SHA256.HashDataAsync(fs)));
                await cmd.ExecuteNonQueryAsync();
            }
            logger.LogInformation("Drawing stamp: item {Item} drawn by {Drawn}, approved by {Approved}; {Count} page(s) stamped.",
                itemId, drawn?.Name ?? "-", approved?.Name ?? "-", stamped);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Drawing stamp: could not stamp item {Item}'s drawing PDF.", itemId);
        }
    }

    // Writes the fields onto every page of the sheet's size (a drawing can have several
    // sheets, each with its title block) and replaces the file. Returns the pages stamped.
    internal static int Stamp(string path, double widthMm, double heightMm, IReadOnlyList<Field> fields, out int pageCount)
    {
        EnsureFont();
        var temp = path + ".stamping";
        int stamped = 0;
        using (var doc = PdfReader.Open(path, PdfDocumentOpenMode.Modify))
        {
            pageCount = doc.PageCount;
            var font = new XFont("osifont", Pt(ValueSizeMm));
            foreach (var page in doc.Pages)
            {
                // A landscape sheet stored as a rotated portrait page would put the text in the
                // wrong place; such a page is left alone rather than stamped badly.
                if (page.Rotate % 360 != 0)
                    continue;
                var w = page.Width.Millimeter;
                var h = page.Height.Millimeter;
                if (Math.Abs(w - widthMm) > 3 || Math.Abs(h - heightMm) > 3)
                    continue;

                // Title block's top-left corner, from the page's own size (y down from the top).
                var x0 = w - 10 - 180;
                var y0 = h - 10 - 44;
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                foreach (var f in fields)
                {
                    // The value row of the name cell (x 0..46) and the date cell (46..70): each
                    // row is 11 mm high with its label in the top 4 mm, kept clear of the
                    // 0.35 mm lines.
                    var top = y0 + f.BaselineMm - 4.5;
                    gfx.DrawRectangle(XBrushes.White, Pt(x0 + 0.6), Pt(top), Pt(44.8), Pt(6.6));
                    gfx.DrawRectangle(XBrushes.White, Pt(x0 + 46.6), Pt(top), Pt(22.8), Pt(6.6));
                    gfx.DrawString(f.Name, font, XBrushes.Black, new XPoint(Pt(x0 + 1.5), Pt(y0 + f.BaselineMm)), XStringFormats.BaseLineLeft);
                    gfx.DrawString(f.Date, font, XBrushes.Black, new XPoint(Pt(x0 + 47.5), Pt(y0 + f.BaselineMm)), XStringFormats.BaseLineLeft);
                }
                stamped++;
            }
            if (stamped > 0)
                doc.Save(temp);
        }
        if (stamped > 0)
            File.Move(temp, path, overwrite: true);
        return stamped;
    }
}
