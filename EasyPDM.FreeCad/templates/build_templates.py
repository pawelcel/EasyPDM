#!/usr/bin/env python3
"""Builds the EasyPDM TechDraw drawing sheet templates (SVG) for FreeCAD.

    python3 build_templates.py

writes EasyPDM_A3_Landscape.svg next to this file AND copies the shared block below (between
the BEGIN/END SHARED markers) into ../EasyPDMUpload.FCMacro and ../EasyPDMDownload.FCMacro,
so the committed template and the sheet the macros build per drawing come from the same code.
Edit this file, not the SVG and not the copies in the macros, then rerun it.

The Upload macro refreshes the sheet before it saves and sends a document (so the CAD file and
the PDF in EasyPDM carry current data) and offers it once for an ordinary TechDraw page; the
Download macro refreshes it after opening a file (e.g. to show who approved it).

Layout (agreed mockup, ISO 5457 / ISO 7200 style):
  - drawing frame 20 mm from the left edge (filing margin), 10 mm from the others;
  - grid reference strip 5 mm wide outside the frame: 50 mm fields counted from the sheet
    centre lines, numbers left->right on top and bottom, letters top->bottom on the sides
    (I and O are skipped so they cannot be read as 1 and 0), plus centring marks;
  - a 180 mm title block in the bottom-right corner.

Every value the PDM fills in is a FreeCAD editable text whose name starts with "pdm_"
(freecad:editable="pdm_drawn_by" ...). Field labels are editable too (pdm_label_*), so the
macro can write them in the project's main language; the template ships them in English.
Scale and sheet number use FreeCAD's own autofill in the bare template; the macro overwrites
them, because FreeCAD fills autofill fields only once, when the template is inserted.

Three groups are left empty and filled per drawing, because their size depends on the data:
pdm_processes (table above the title block, header at the bottom, rows growing upward),
pdm_revisions (table in the top-right corner, header at the top, rows growing downward) and
pdm_logo (square at the right end of the NAME row). Their position is in data-* attributes.

Custom templates keep working with EasyPDM as long as they use the same pdm_* names (and,
for the tables and logo, the same groups); a template without them gets nothing filled in.
"""

import os
import re
import sys

# ===== BEGIN SHARED (copied into EasyPDMUpload.FCMacro and EasyPDMDownload.FCMacro by templates/build_templates.py; edit it there) =====
import base64 as _sh_base64
import glob as _sh_glob
import time as _sh_time
from datetime import datetime as _sh_datetime
from xml.sax.saxutils import escape as _xml_escape

FREECAD_NS = "https://www.freecad.org/wiki/index.php?title=Svg_Namespace"
SHEET_TITLE_PREFIX = "EasyPDM drawing sheet"
SHEET_FONT = "osifont"  # ISO 3098 lettering shipped with FreeCAD (Mod/TechDraw/Resources/fonts)
ZONE_LETTERS = "ABCDEFGHJKLMNPQRSTUVWXYZ"
SHEET_FORMATS = {"A3": (420, 297)}

_THIN = "fill:none;stroke:#000000;stroke-width:0.35;stroke-linecap:round;stroke-linejoin:round"
_THICK = "fill:none;stroke:#000000;stroke-width:0.7;stroke-linecap:round;stroke-linejoin:round"

# Field labels in English; the macro overwrites them with the project's main language from
# the server's document labels (Settings -> Language -> Labels in documents, keys
# titleblock.*). Template field name -> server label key.
SHEET_LABELS = {
    "drawn_by": ("drawnBy", "DRAWN BY"),
    "drawn_date": ("date", "DATE"),
    "approved_by": ("approvedBy", "APPROVED BY"),
    "approved_date": ("date", "DATE"),
    "material": ("material", "MATERIAL"),
    "scale": ("scale", "SCALE"),
    "sheet": ("sheet", "SHEET"),
    "mass": ("mass", "MASS"),
    "company": ("company", "COMPANY"),
    "name": ("name", "NAME"),
    "drawing_number": ("drawingNumber", "DRAWING NUMBER"),
    "revision": ("revision", "REVISION"),
    "format": ("format", "FORMAT"),
}


def _sh_f(v):
    return ("%.2f" % v).rstrip("0").rstrip(".")


def _sh_esc(text):
    return _xml_escape(text or "", {'"': "&quot;"})


def approx_text_width(text, size):
    """Width in mm of `text` set in osifont at `size` mm -- a fallback for when Qt cannot
    measure (no GUI). osifont is narrow; 0.58 em per character errs on the wide side, so a
    line wraps a little early rather than running past its cell."""
    return len(text or "") * size * 0.58


def wrap_text(text, width, size, measure):
    """Splits `text` into lines no wider than `width` mm, at spaces; a single word longer
    than the line is left whole rather than cut mid-word."""
    lines = []
    for para in (text or "").splitlines() or [""]:
        line = ""
        for word in para.split(" "):
            candidate = word if not line else line + " " + word
            if line and measure(candidate, size) > width:
                lines.append(line)
                line = word
            else:
                line = candidate
        lines.append(line)
    while lines and lines[-1] == "":
        lines.pop()
    return lines


def wrap_joined(parts, width, size, measure, sep=" · "):
    """Translations as "EN: Cube · DE: Würfel": on one line while they fit, otherwise one
    language per line (each wrapped further if it is still too long)."""
    parts = [p for p in parts if p]
    if not parts:
        return []
    joined = sep.join(parts)
    if measure(joined, size) <= width:
        return [joined]
    lines = []
    for p in parts:
        lines.extend(wrap_text(p, width, size, measure))
    return lines


def _sh_text(x, y, content, size, extra=""):
    return '<text x="%s" y="%s" style="font-size:%spx;fill:#000000%s">%s</text>' % (
        _sh_f(x), _sh_f(y), _sh_f(size), extra, _sh_esc(content))


def _sh_line(x1, y1, x2, y2):
    return '<path d="M %s,%s L %s,%s"/>' % (_sh_f(x1), _sh_f(y1), _sh_f(x2), _sh_f(y2))


def build_base_sheet(fmt_name):
    """The bare template for one format: frame, grid reference, title block with empty
    pdm_* fields and the three empty groups."""
    W, H = SHEET_FORMATS[fmt_name]
    o = []
    add = o.append
    add('<g id="sheet" style="font-family:%s">' % SHEET_FONT)

    FL, FT, FR, FB = 20, 10, W - 10, H - 10      # drawing frame
    OL, OT, OR, OB = 15, 5, W - 5, H - 5         # outer line of the grid strip
    cx, cy = W / 2.0, H / 2.0
    add('<rect id="drawing_frame" x="%s" y="%s" width="%s" height="%s" style="%s"/>'
        % (FL, FT, _sh_f(FR - FL), _sh_f(FB - FT), _THICK))
    xs = [cx + 50 * k for k in range(-20, 21) if FL < cx + 50 * k < FR]
    ys = [cy + 50 * k for k in range(-20, 21) if FT < cy + 50 * k < FB]
    add('<g id="grid_reference" style="%s">' % _THIN)
    add('<rect x="%s" y="%s" width="%s" height="%s"/>' % (OL, OT, _sh_f(OR - OL), _sh_f(OB - OT)))
    for x in xs:
        add(_sh_line(x, OT, x, FT))
        add(_sh_line(x, FB, x, OB))
    for y in ys:
        add(_sh_line(OL, y, FL, y))
        add(_sh_line(FR, y, OR, y))
    add("</g>")
    cols, rows = [FL] + xs + [FR], [FT] + ys + [FB]
    add('<g id="grid_reference_markers" style="font-size:3.5px;text-anchor:middle;fill:#000000;stroke:none">')
    for i in range(len(cols) - 1):
        m = (cols[i] + cols[i + 1]) / 2
        for y in (OT + 3.75, FB + 3.75):
            add('<text x="%s" y="%s">%d</text>' % (_sh_f(m), _sh_f(y), i + 1))
    for i in range(len(rows) - 1):
        m = (rows[i] + rows[i + 1]) / 2 + 1.25
        for x in ((OL + FL) / 2.0, (FR + OR) / 2.0):
            add('<text x="%s" y="%s">%s</text>' % (_sh_f(x), _sh_f(m), ZONE_LETTERS[i]))
    add("</g>")
    add('<g id="centring_marks" style="%s">' % _THICK)
    add(_sh_line(cx, 0, cx, FT + 5))
    add(_sh_line(cx, H, cx, FB - 5))
    add(_sh_line(0, cy, FL + 5, cy))
    add(_sh_line(W, cy, FR - 5, cy))
    add("</g>")

    # Title block 180 x 44 mm in the bottom-right corner.
    x0, y0 = FR - 180, FB - 44
    X = lambda dx: x0 + dx
    Y = lambda dy: y0 + dy
    add('<g id="title_block_lines" style="%s">' % _THIN)
    add(_sh_line(X(70), Y(0), X(70), Y(44)))
    for dy in (11, 22, 33):
        add(_sh_line(X(0), Y(dy), X(70), Y(dy)))
    add(_sh_line(X(46), Y(0), X(46), Y(22)))
    add(_sh_line(X(22), Y(33), X(22), Y(44)))
    add(_sh_line(X(40), Y(33), X(40), Y(44)))
    add(_sh_line(X(70), Y(11), X(180), Y(11)))
    add(_sh_line(X(70), Y(28), X(180), Y(28)))
    add(_sh_line(X(163), Y(11), X(163), Y(28)))
    add(_sh_line(X(140), Y(28), X(140), Y(44)))
    add(_sh_line(X(162), Y(28), X(162), Y(44)))
    add("</g>")
    add('<rect id="title_block_frame" x="%s" y="%s" width="180" height="44" style="%s"/>'
        % (_sh_f(x0), _sh_f(y0), _THICK))

    def label(key, x, y):
        add('<text freecad:editable="pdm_label_%s" x="%s" y="%s" style="font-size:2px;fill:#000000">'
            "<tspan>%s</tspan></text>" % (key, _sh_f(x), _sh_f(y), _sh_esc(SHEET_LABELS[key][1])))

    def value(key, x, y, size=3.5, text="", bold=False, anchor="start", autofill=None):
        style = "font-size:%spx;fill:#000000" % _sh_f(size)
        if bold:
            style += ";font-weight:bold"
        if anchor != "start":
            style += ";text-anchor:%s" % anchor
        extra = ' freecad:autofill="%s"' % autofill if autofill else ""
        add('<text freecad:editable="pdm_%s"%s x="%s" y="%s" style="%s"><tspan>%s</tspan></text>'
            % (key, extra, _sh_f(x), _sh_f(y), style, _sh_esc(text)))

    add('<g id="title_block_fields" style="fill:#000000;stroke:none">')
    label("drawn_by", X(1.5), Y(3));        value("drawn_by", X(1.5), Y(8.5))
    label("drawn_date", X(47.5), Y(3));     value("drawn_date", X(47.5), Y(8.5))
    label("approved_by", X(1.5), Y(14));    value("approved_by", X(1.5), Y(19.5))
    label("approved_date", X(47.5), Y(14)); value("approved_date", X(47.5), Y(19.5))
    label("material", X(1.5), Y(25));       value("material", X(1.5), Y(30.5))
    label("scale", X(1.5), Y(36));          value("scale", X(1.5), Y(41.5), text="1:1", autofill="scale")
    label("sheet", X(23.5), Y(36));         value("sheet", X(23.5), Y(41.5), text="1 / 1", autofill="sheet")
    label("mass", X(41.5), Y(36));          value("mass", X(41.5), Y(41.5))
    label("company", X(71.5), Y(3));        value("company", X(71.5), Y(8.8), bold=True)
    label("name", X(71.5), Y(14));          value("name", X(71.5), Y(21.5), size=6, bold=True)
    value("name_translations", X(71.5), Y(26.3), size=2.3)
    label("drawing_number", X(71.5), Y(31)); value("drawing_number", X(71.5), Y(40), size=5, bold=True)
    label("revision", X(141.5), Y(31));     value("revision", X(151), Y(41.5), size=7, bold=True, anchor="middle")
    label("format", X(163.5), Y(31));       value("format", X(171), Y(40.5), text=fmt_name, anchor="middle")
    add("</g>")

    add('<g id="pdm_logo" data-x="%s" data-y="%s" data-size="17"/>' % (_sh_f(X(163)), _sh_f(Y(11))))
    add('<g id="pdm_processes" data-x="%s" data-bottom="%s" data-width="180"/>' % (_sh_f(x0), _sh_f(y0)))
    add('<g id="pdm_revisions" data-right="%s" data-top="%s" data-width="160"/>' % (_sh_f(FR), _sh_f(FT)))
    add("</g>")

    return (
        '<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n'
        '<!-- Generated by EasyPDM.FreeCad/templates/build_templates.py; do not edit by hand. -->\n'
        '<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"'
        ' xmlns:freecad="%s" version="1.1"\n'
        '     width="%smm" height="%smm" viewBox="0 0 %s %s">\n'
        '  <title>%s %s landscape</title>\n'
        % (FREECAD_NS, _sh_f(W), _sh_f(H), _sh_f(W), _sh_f(H), SHEET_TITLE_PREFIX, fmt_name)
        + "\n".join("  " + p for p in o) + "\n</svg>\n"
    )


def _processes_table(x0, bottom, width, rows, headers, measure):
    """rows: [{"name", "description", "nameTr": [..], "descTr": [..]}] in the item's order;
    headers: (no, process, description). Header at the bottom, row 1 right above it."""
    w_no, w_name = 10.0, 60.0
    w_desc = width - w_no - w_name
    MAIN, SMALL, LH_MAIN, LH_SMALL, HDR = 3.4, 2.3, 3.6, 2.7, 6.0
    pad = 1.5
    built = []
    for i, r in enumerate(rows):
        name_l = wrap_text(r.get("name", ""), w_name - 2 * pad, MAIN, measure) or [""]
        desc_l = wrap_text(r.get("description", ""), w_desc - 2 * pad, MAIN, measure)
        ntr_l = wrap_joined(r.get("nameTr", []), w_name - 2 * pad, SMALL, measure)
        dtr_l = wrap_joined(r.get("descTr", []), w_desc - 2 * pad, SMALL, measure)
        h = 1.6 + LH_MAIN * max(len(name_l), len(desc_l), 1) + LH_SMALL * max(len(ntr_l), len(dtr_l)) + 0.8
        built.append((i + 1, name_l, desc_l, ntr_l, dtr_l, max(h, 6.0)))
    hdr_top = bottom - HDR
    top = hdr_top - sum(b[5] for b in built)
    o = ['<g style="%s">' % _THIN]
    o.append(_sh_line(x0 + w_no, top, x0 + w_no, bottom))
    o.append(_sh_line(x0 + w_no + w_name, top, x0 + w_no + w_name, bottom))
    o.append(_sh_line(x0, hdr_top, x0 + width, hdr_top))
    yb = hdr_top
    for b in built:
        yt = yb - b[5]
        o.append(_sh_line(x0, yt, x0 + width, yt))
        yb = yt
    o.append('</g><g style="%s">' % _THICK)
    o.append('<rect x="%s" y="%s" width="%s" height="%s"/>' % (_sh_f(x0), _sh_f(top), _sh_f(width), _sh_f(bottom - top)))
    o.append("</g>")
    o.append(_sh_text(x0 + pad, hdr_top + 4.2, headers[0], 2.4, ";font-weight:bold"))
    o.append(_sh_text(x0 + w_no + pad, hdr_top + 4.2, headers[1], 2.4, ";font-weight:bold"))
    o.append(_sh_text(x0 + w_no + w_name + pad, hdr_top + 4.2, headers[2], 2.4, ";font-weight:bold"))
    yb = hdr_top
    for n, name_l, desc_l, ntr_l, dtr_l, h in built:
        yt = yb - h
        o.append(_sh_text(x0 + w_no / 2.0, yt + 5.0, str(n), MAIN, ";text-anchor:middle"))
        for col_x, main_l, small_l in ((x0 + w_no + pad, name_l, ntr_l), (x0 + w_no + w_name + pad, desc_l, dtr_l)):
            y = yt + 4.4
            for line in main_l:
                o.append(_sh_text(col_x, y, line, MAIN))
                y += LH_MAIN
            if not main_l:
                y += LH_MAIN
            y -= 0.6
            for line in small_l:
                o.append(_sh_text(col_x, y, line, SMALL))
                y += LH_SMALL
        yb = yt
    return o


def _revisions_table(right, top, width, rows, headers, measure):
    """rows: [{"label", "date", "description", "by"}] oldest first; headers: (rev, date,
    description, by). Header at the top, rows growing downward."""
    x0 = right - width
    w_rev, w_date, w_by = 12.0, 22.0, 26.0
    w_desc = width - w_rev - w_date - w_by
    SIZE, LH, HDR, pad = 2.6, 3.2, 6.0, 1.5
    o_lines, o_text = [], []
    y = top + HDR
    o_lines.append(_sh_line(x0, y, right, y))
    for r in rows:
        desc_l = wrap_text(r.get("description", ""), w_desc - 2 * pad, SIZE, measure) or [""]
        by_l = wrap_text(r.get("by", ""), w_by - 2 * pad, SIZE, measure) or [""]
        h = max(8.0, 3.0 + LH * max(len(desc_l), len(by_l)))
        o_text.append(_sh_text(x0 + w_rev / 2.0, y + 5.3, r.get("label", ""), 3.4, ";font-weight:bold;text-anchor:middle"))
        o_text.append(_sh_text(x0 + w_rev + pad, y + 5.3, r.get("date", ""), SIZE))
        for i, line in enumerate(desc_l):
            o_text.append(_sh_text(x0 + w_rev + w_date + pad, y + 5.3 + i * LH, line, SIZE))
        for i, line in enumerate(by_l):
            o_text.append(_sh_text(right - w_by + pad, y + 5.3 + i * LH, line, SIZE))
        y += h
        o_lines.append(_sh_line(x0, y, right, y))
    bottom = y
    for cx in (x0 + w_rev, x0 + w_rev + w_date, right - w_by):
        o_lines.append(_sh_line(cx, top, cx, bottom))
    o = ['<g style="%s">' % _THIN] + o_lines[:-1] + ["</g>"]
    o.append('<rect x="%s" y="%s" width="%s" height="%s" style="%s"/>' % (_sh_f(x0), _sh_f(top), _sh_f(width), _sh_f(bottom - top), _THICK))
    hx = (x0 + pad, x0 + w_rev + pad, x0 + w_rev + w_date + pad, right - w_by + pad)
    for x, h in zip(hx, headers):
        o.append(_sh_text(x, top + 4.2, h, 2.4, ";font-weight:bold"))
    return o + o_text


def _group_attrs(svg, gid):
    m = re.search(r'<g\b([^>]*\bid="%s"[^>]*?)\s*/?>' % gid, svg)
    if not m:
        return None
    return dict(re.findall(r'data-([a-z]+)="([^"]*)"', m.group(1)))


def _replace_group(svg, gid, inner):
    """Puts `inner` into the empty or previously filled group `gid`. A filled group ends with
    a </g><!--/gid--> marker, so the old content is found again on the next refresh even
    though it contains nested groups."""
    m = re.search(r'<g\b([^>]*\bid="%s"[^>]*?)\s*/>' % gid, svg)
    if not m:
        m = re.search(r'<g\b([^>]*\bid="%s"[^>]*)>.*?</g><!--/%s-->' % (gid, gid), svg, re.S)
    if not m:
        return svg
    attrs = m.group(1).rstrip()
    new = ('<g%s/>' % attrs) if not inner else ('<g%s>%s</g><!--/%s-->' % (attrs, "".join(inner), gid))
    return svg[:m.start()] + new + svg[m.end():]


def fill_sheet(svg, values, processes=None, revisions=None, process_headers=None,
               revision_headers=None, logo_href=None, measure=None):
    """Writes `values` ({"pdm_name": "Kostka", "pdm_label_name": "NAZWA", ...}) into the
    matching freecad:editable texts and builds the tables and the logo into their groups.
    Works on the EasyPDM template and on any custom template that uses the same names; what
    a template does not have is simply skipped."""
    measure = measure or approx_text_width

    def fill(m):
        name = m.group(2)
        if name not in values:
            return m.group(0)
        return m.group(1) + "<tspan>%s</tspan>" % _sh_esc(values[name]) + m.group(4)

    svg = re.sub(r'(<text\b[^>]*\bfreecad:editable="([^"]+)"[^>]*>)(.*?)(</text>)', fill, svg, flags=re.S)

    a = _group_attrs(svg, "pdm_processes")
    if a is not None:
        inner = []
        if processes:
            inner = _processes_table(float(a.get("x", 0)), float(a.get("bottom", 0)), float(a.get("width", 180)),
                                     processes, process_headers or ("No.", "PROCESS", "DESCRIPTION"), measure)
        svg = _replace_group(svg, "pdm_processes", inner)
    a = _group_attrs(svg, "pdm_revisions")
    if a is not None:
        inner = []
        if revisions:
            inner = _revisions_table(float(a.get("right", 0)), float(a.get("top", 0)), float(a.get("width", 160)),
                                     revisions, revision_headers or ("REV.", "DATE", "DESCRIPTION", "BY"), measure)
        svg = _replace_group(svg, "pdm_revisions", inner)
    a = _group_attrs(svg, "pdm_logo")
    if a is not None:
        inner = []
        if logo_href:
            size, pad = float(a.get("size", 17)), 1.5
            inner = ['<image x="%s" y="%s" width="%s" height="%s" preserveAspectRatio="xMidYMid meet" xlink:href="%s"/>'
                     % (_sh_f(float(a.get("x", 0)) + pad), _sh_f(float(a.get("y", 0)) + pad), _sh_f(size - 2 * pad), _sh_f(size - 2 * pad), logo_href)]
        svg = _replace_group(svg, "pdm_logo", inner)
    return svg

# --- FreeCAD side: refreshing the EasyPDM sheet on a document's TechDraw pages. Uses the
# host macro's api_get / api_get_binary and FreeCAD's App; nothing here runs on import.

def _sh_qt_measure():
    """Text width from the real osifont metrics when Qt is available (inside FreeCAD's GUI),
    so wrapping matches what TechDraw draws; approx_text_width otherwise."""
    try:
        try:
            from PySide6 import QtGui as _QtGui
        except ImportError:
            try:
                from PySide2 import QtGui as _QtGui
            except ImportError:
                from PySide import QtGui as _QtGui
        font_path = os.path.join(App.getResourceDir(), "Mod", "TechDraw", "Resources", "fonts", "osifont-lgpl3fe.ttf")
        families = []
        if os.path.exists(font_path):
            fid = _QtGui.QFontDatabase.addApplicationFont(font_path)
            families = _QtGui.QFontDatabase.applicationFontFamilies(fid) if fid >= 0 else []
        if not families:
            return approx_text_width
        font = _QtGui.QFont(families[0])
        font.setPixelSize(100)
        metrics = _QtGui.QFontMetricsF(font)
        advance = getattr(metrics, "horizontalAdvance", None) or metrics.width
        return lambda text, size: advance(text or "") * size / 100.0
    except Exception:
        return approx_text_width


def _sh_local_date(iso):
    """"2026-11-12T09:14:03.5Z" -> "2026-11-12" in the computer's own time zone."""
    if not iso:
        return ""
    try:
        s = re.sub(r"\.(\d{6})\d+", r".\1", iso.replace("Z", "+00:00"))
        dt = _sh_datetime.fromisoformat(s)
        if dt.tzinfo is not None:
            dt = dt.astimezone()
        return dt.strftime("%Y-%m-%d")
    except Exception:
        return iso[:10]


def _sh_scale_text(scale):
    try:
        s = float(scale)
    except Exception:
        return "1:1"
    if s <= 0:
        return "1:1"
    if s < 1:
        return "1:%s" % ("%g" % round(1.0 / s, 3))
    return "%s:1" % ("%g" % round(s, 3))


def _sh_logo_href(tb):
    company = tb.get("company") or {}
    if not company.get("hasLogo"):
        return None
    try:
        data = api_get_binary("/settings/company/logo")
    except Exception:
        return None
    mime = "image/png" if data[:4] == b"\x89PNG" else "image/jpeg" if data[:2] == b"\xff\xd8" else None
    if not mime:
        return None
    return "data:%s;base64,%s" % (mime, _sh_base64.b64encode(data).decode("ascii"))


def sheet_values(tb, page_scale, sheet_no, sheet_count):
    """Values for the pdm_* fields from GET /items/{id}/title-block."""
    labels = tb.get("labels") or {}
    v = {}
    for field, (key, english) in SHEET_LABELS.items():
        v["pdm_label_" + field] = (labels.get(key) or english).upper()
    drawn = tb.get("drawnBy") or {}
    approved = tb.get("approvedBy") or {}
    company = tb.get("company") or {}
    mass = tb.get("mass")
    v.update({
        "pdm_drawn_by": drawn.get("name") or "",
        "pdm_drawn_date": _sh_local_date(drawn.get("at")),
        "pdm_approved_by": approved.get("name") or "",
        "pdm_approved_date": _sh_local_date(approved.get("at")),
        "pdm_material": tb.get("material") or "",
        "pdm_mass": ("%s kg" % mass) if mass else "",
        "pdm_company": company.get("name") or "",
        "pdm_name": tb.get("name") or "",
        "pdm_name_translations": " \u00b7 ".join(
            "%s: %s" % (x.get("languageCode"), x.get("name")) for x in tb.get("nameTranslations") or []),
        "pdm_drawing_number": tb.get("drawingNumber") or "",
        "pdm_revision": tb.get("revisionLabel") or "",
        "pdm_scale": _sh_scale_text(page_scale),
        "pdm_sheet": "%d / %d" % (sheet_no, sheet_count),
    })
    return v


def sheet_tables(tb):
    labels = tb.get("labels") or {}
    lab = lambda k, d: (labels.get(k) or d).upper()
    processes = []
    for p in tb.get("processes") or []:
        tr = p.get("translations") or []
        processes.append({
            "name": p.get("name") or "",
            "description": p.get("description") or "",
            "nameTr": ["%s: %s" % (x.get("languageCode"), x.get("name")) for x in tr if x.get("name")],
            "descTr": ["%s: %s" % (x.get("languageCode"), x.get("description")) for x in tr if x.get("description")],
        })
    revisions = [{
        "label": r.get("revisionLabel") or "",
        "date": _sh_local_date(r.get("at")),
        "description": r.get("description") or "",
        "by": r.get("by") or "",
    } for r in tb.get("revisions") or []]
    return (processes, revisions,
            (lab("processNo", "No."), lab("process", "Process"), lab("processDescription", "Description")),
            (lab("revisionLetter", "Rev."), lab("revisionDate", "Date"), lab("revisionDescription", "Change description"), lab("revisionBy", "By")))


def is_pdm_template(tpl):
    try:
        return tpl is not None and any(k.startswith("pdm_") for k in dict(tpl.EditableTexts))
    except Exception:
        return False


def _sh_template_text(tpl):
    for path in (getattr(tpl, "Template", ""), getattr(tpl, "PageResult", "")):
        if path and os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                return f.read()
    return ""


def _sh_write_sheet(doc, page, svg):
    """A new file name on every refresh: FreeCAD reloads a template only when the path
    changes. Older working copies of the same page are removed; the .FCStd keeps its own."""
    d = os.path.join(App.getUserAppDataDir(), "EasyPDM", "sheets")
    os.makedirs(d, exist_ok=True)
    prefix = re.sub(r"[^A-Za-z0-9_-]+", "_", "%s_%s" % (doc.Name, page.Name))
    for old in _sh_glob.glob(os.path.join(d, prefix + "_*.svg")):
        try:
            os.remove(old)
        except OSError:
            pass
    path = os.path.join(d, "%s_%d.svg" % (prefix, int(_sh_time.time() * 1000)))
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(svg)
    return path


def _sh_refresh_page(doc, page, tb, sheet_no, sheet_count, logo_href, measure):
    """Rebuilds one page's sheet; returns False when nothing changed (the template is then
    left alone, so an unchanged document does not turn "modified")."""
    tpl = page.Template
    current = _sh_template_text(tpl) if tpl is not None else ""
    own = (not current) or (SHEET_TITLE_PREFIX in current)
    if own:
        m = re.search(re.escape(SHEET_TITLE_PREFIX) + r" (\w+)", current or "")
        base = build_base_sheet(m.group(1) if m and m.group(1) in SHEET_FORMATS else "A3")
    else:
        base = current
    values = sheet_values(tb, getattr(page, "Scale", 1.0), sheet_no, sheet_count)
    if not own:
        values.pop("pdm_label_format", None)
    processes, revisions, p_head, r_head = sheet_tables(tb)
    svg = fill_sheet(base, values, processes, revisions, p_head, r_head, logo_href, measure)

    old_texts = dict(tpl.EditableTexts) if tpl is not None else {}
    if tpl is not None and svg == current and all(old_texts.get(k) == v for k, v in values.items() if k in old_texts):
        return False
    if tpl is None:
        tpl = doc.addObject("TechDraw::DrawSVGTemplate", "EasyPDMTemplate")
        page.Template = tpl
    tpl.Template = _sh_write_sheet(doc, page, svg)
    texts = dict(tpl.EditableTexts)
    for k, val in old_texts.items():
        if not k.startswith("pdm_") and k in texts:
            texts[k] = val     # fields of a custom template keep what was typed into them
    for k, val in values.items():
        if k in texts:
            texts[k] = val
    tpl.EditableTexts = texts
    return True


def refresh_sheets(doc, item_id, ask_existing_page=None):
    """Fills every TechDraw page of `doc` that uses the EasyPDM sheet (pdm_* fields) with the
    item's title block data. When the document has pages but none with the sheet,
    `ask_existing_page(page)` -- if given -- decides whether the sheet goes on the first of
    them (its views stay). Never creates a page. Returns the pages that changed."""
    pages = [o for o in doc.Objects if o.TypeId == "TechDraw::DrawPage"]
    targets = [p for p in pages if is_pdm_template(p.Template)]
    if not targets and pages and ask_existing_page is not None and ask_existing_page(pages[0]):
        old = pages[0].Template
        tpl = doc.addObject("TechDraw::DrawSVGTemplate", "EasyPDMTemplate")
        pages[0].Template = tpl
        if old is not None and not old.InList:
            doc.removeObject(old.Name)
        targets = [pages[0]]
    if not targets:
        return []
    tb = api_get("/items/%s/title-block" % item_id)
    logo = _sh_logo_href(tb)
    measure = _sh_qt_measure()
    changed = [p for p in targets
               if _sh_refresh_page(doc, p, tb, pages.index(p) + 1, len(pages), logo, measure)]
    if changed:
        doc.recompute()
    return changed

# ===== END SHARED =====


MACROS = [os.path.join(os.path.dirname(os.path.abspath(__file__)), os.pardir, name)
          for name in ("EasyPDMUpload.FCMacro", "EasyPDMDownload.FCMacro")]
BEGIN = "# ===== BEGIN SHARED"
END = "# ===== END SHARED ====="


def _shared_block():
    with open(os.path.abspath(__file__), encoding="utf-8") as f:
        src = f.read()
    start = src.index(BEGIN)
    end = src.index(END, start) + len(END)
    return src[start:end]


def sync_macro(path):
    """Replaces the shared block in one macro with the one above."""
    path = os.path.abspath(path)
    with open(path, encoding="utf-8", newline="") as f:
        macro = f.read()
    start = macro.index(BEGIN)
    end = macro.index(END, start) + len(END)
    new = macro[:start] + _shared_block() + macro[end:]
    if new != macro:
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(new)
        print("updated shared block in", path)


if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    for name in SHEET_FORMATS:
        path = os.path.join(here, "EasyPDM_%s_Landscape.svg" % name)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(build_base_sheet(name))
        print("wrote", path)
    for macro in MACROS:
        sync_macro(macro)
