using System.Security.Cryptography;
using Npgsql;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

// "Approved by" reaches the drawing PDF the moment an item is released.
//
// A drawing sent before release has an empty APPROVED BY field, and a released item cannot be
// uploaded again, so neither CAD program can put the approval on the PDF itself. Instead, when
// an item moves to "wydany", the server writes the releasing person's name and the date into
// the item's drawing PDF -- the attachment with role "pdf" -- at the fields' positions, and the
// stamped file REPLACES the original (agreed: no unstamped copy is kept).
//
// Only a PDF the macros marked as exported from an EasyPDM sheet (item_attachments.sheet_layout,
// e.g. "easypdm-A3") is touched: the layout says where the fields are. The EasyPDM sheets are the
// same in FreeCAD and SolidWorks (EasyPDM.FreeCad/templates/build_templates.py): the title block
// is 180 x 44 mm in the bottom-right corner, 10 mm from the trimmed edges, with APPROVED BY's
// value baseline 19.5 mm below the block's top edge, 1.5 mm in, and its date 47.5 mm in.
//
// Whatever the fields already show is painted over first (white, inside the cell lines): an
// item sent back to work and released again without a new upload, or a drawing whose title
// block was filled in by a later Download, would otherwise get the new approval on top of the
// old one -- confirmed by rendering a stamped FreeCAD export. The layout mark is cleared once
// the PDF is stamped, too, so the same file is stamped only once.
//
// Best effort: a PDF that cannot be stamped is logged and left as it is -- the release itself
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

    // Called after the status change to "wydany" has been committed.
    public static async Task StampApprovalAsync(NpgsqlConnection conn, ILogger logger, Guid itemId, Guid approverId)
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

            string approver;
            await using (var cmd = new NpgsqlCommand($"SELECT {UserNames.FullNameSql("u")} FROM users u WHERE u.id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("id", approverId);
                approver = await cmd.ExecuteScalarAsync() as string ?? "";
            }
            var date = DateTime.Now.ToString("yyyy-MM-dd");

            var stamped = Stamp(path, size.W, size.H, approver, date, out var pages);
            if (stamped == 0)
            {
                logger.LogWarning("Drawing stamp: no page of item {Item}'s PDF matches {Layout} ({Pages} page(s)); left as it is.", itemId, layout, pages);
                return;
            }

            await using (var cmd = new NpgsqlCommand(
                "UPDATE item_attachments SET file_size = @size, file_hash = @hash, sheet_layout = NULL WHERE id = @id;", conn))
            {
                cmd.Parameters.AddWithValue("id", attachmentId);
                cmd.Parameters.AddWithValue("size", new FileInfo(path).Length);
                await using (var fs = File.OpenRead(path))
                    cmd.Parameters.AddWithValue("hash", Convert.ToHexString(await SHA256.HashDataAsync(fs)));
                await cmd.ExecuteNonQueryAsync();
            }
            logger.LogInformation("Drawing stamp: item {Item} approved by {Approver} on {Date}, {Count} page(s) stamped.", itemId, approver, date, stamped);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Drawing stamp: could not stamp item {Item}'s drawing PDF.", itemId);
        }
    }

    // Writes the approval onto every page of the sheet's size (a drawing can have several
    // sheets, each with its title block) and replaces the file. Returns the pages stamped.
    internal static int Stamp(string path, double widthMm, double heightMm, string approver, string date, out int pageCount)
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
                // The value rows of the two cells (APPROVED BY: x 0..46, DATE: 46..70; the row
                // spans y 11..22 with its label above y 15), kept clear of the 0.35 mm lines.
                gfx.DrawRectangle(XBrushes.White, Pt(x0 + 0.6), Pt(y0 + 15), Pt(44.8), Pt(6.6));
                gfx.DrawRectangle(XBrushes.White, Pt(x0 + 46.6), Pt(y0 + 15), Pt(22.8), Pt(6.6));
                gfx.DrawString(approver, font, XBrushes.Black, new XPoint(Pt(x0 + 1.5), Pt(y0 + 19.5)), XStringFormats.BaseLineLeft);
                gfx.DrawString(date, font, XBrushes.Black, new XPoint(Pt(x0 + 47.5), Pt(y0 + 19.5)), XStringFormats.BaseLineLeft);
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
