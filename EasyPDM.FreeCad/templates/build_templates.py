#!/usr/bin/env python3
"""Builds the EasyPDM TechDraw drawing sheet templates (SVG) for FreeCAD.

    python3 build_templates.py

writes EasyPDM_A3/A2/A1_Landscape.svg next to this file AND copies the shared block below (between
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
# Landscape formats: width, height (mm) and the number of grid reference fields across and
# down, as ISO 5457 sets them. The fields are 50 mm, laid out symmetrically from the centre
# lines, so the outermost ones absorb the remainder (A1: 50.5 mm at the left) instead of
# leaving a sliver next to the frame.
SHEET_FORMATS = {
    "A3": (420, 297, 8, 6),
    "A2": (594, 420, 12, 8),
    "A1": (841, 594, 16, 12),
}

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


def build_base_sheet(fmt_name, rec=None):
    """The bare template for one format: frame, grid reference, title block with empty
    pdm_* fields and the three empty groups.

    `rec`, when given ({"lines": [], "texts": [], "anchors": {}}), also receives every line
    (x1, y1, x2, y2, width) and text (x, baseline_y, text, size, bold, anchor, field) in mm,
    y down from the top edge -- what build_templates.py turns into the SolidWorks macro that
    draws the same sheet format."""
    W, H, fields_x, fields_y = SHEET_FORMATS[fmt_name]
    o = []
    add = o.append

    def ln(x1, y1, x2, y2, width):
        if rec is not None:
            rec["lines"].append((x1, y1, x2, y2, width))
        return _sh_line(x1, y1, x2, y2)

    def box(x, y, w, h, width):
        if rec is not None:
            for seg in ((x, y, x + w, y), (x + w, y, x + w, y + h), (x + w, y + h, x, y + h), (x, y + h, x, y)):
                rec["lines"].append(seg + (width,))

    def txt(x, y, text, size, bold=False, anchor="start", field=None):
        if rec is not None:
            rec["texts"].append((x, y, text, size, bold, anchor, field))
    add('<g id="sheet" style="font-family:%s">' % SHEET_FONT)

    FL, FT, FR, FB = 20, 10, W - 10, H - 10      # drawing frame
    OL, OT, OR, OB = 15, 5, W - 5, H - 5         # outer line of the grid strip
    cx, cy = W / 2.0, H / 2.0
    box(FL, FT, FR - FL, FB - FT, 0.7)
    add('<rect id="drawing_frame" x="%s" y="%s" width="%s" height="%s" style="%s"/>'
        % (FL, FT, _sh_f(FR - FL), _sh_f(FB - FT), _THICK))
    xs = [cx + 50 * k for k in range(-(fields_x // 2 - 1), fields_x // 2)]
    ys = [cy + 50 * k for k in range(-(fields_y // 2 - 1), fields_y // 2)]
    add('<g id="grid_reference" style="%s">' % _THIN)
    box(OL, OT, OR - OL, OB - OT, 0.35)
    add('<rect x="%s" y="%s" width="%s" height="%s"/>' % (OL, OT, _sh_f(OR - OL), _sh_f(OB - OT)))
    for x in xs:
        add(ln(x, OT, x, FT, 0.35))
        add(ln(x, FB, x, OB, 0.35))
    for y in ys:
        add(ln(OL, y, FL, y, 0.35))
        add(ln(FR, y, OR, y, 0.35))
    add("</g>")
    cols, rows = [FL] + xs + [FR], [FT] + ys + [FB]
    add('<g id="grid_reference_markers" style="font-size:3.5px;text-anchor:middle;fill:#000000;stroke:none">')
    for i in range(len(cols) - 1):
        m = (cols[i] + cols[i + 1]) / 2
        for y in (OT + 3.75, FB + 3.75):
            txt(m, y, str(i + 1), 3.5, anchor="middle")
            add('<text x="%s" y="%s">%d</text>' % (_sh_f(m), _sh_f(y), i + 1))
    for i in range(len(rows) - 1):
        m = (rows[i] + rows[i + 1]) / 2 + 1.25
        for x in ((OL + FL) / 2.0, (FR + OR) / 2.0):
            txt(x, m, ZONE_LETTERS[i], 3.5, anchor="middle")
            add('<text x="%s" y="%s">%s</text>' % (_sh_f(x), _sh_f(m), ZONE_LETTERS[i]))
    add("</g>")
    add('<g id="centring_marks" style="%s">' % _THICK)
    add(ln(cx, 0, cx, FT + 5, 0.7))
    add(ln(cx, H, cx, FB - 5, 0.7))
    add(ln(0, cy, FL + 5, cy, 0.7))
    add(ln(W, cy, FR - 5, cy, 0.7))
    add("</g>")

    # Title block 180 x 44 mm in the bottom-right corner.
    x0, y0 = FR - 180, FB - 44
    X = lambda dx: x0 + dx
    Y = lambda dy: y0 + dy
    add('<g id="title_block_lines" style="%s">' % _THIN)
    add(ln(X(70), Y(0), X(70), Y(44), 0.35))
    for dy in (11, 22, 33):
        add(ln(X(0), Y(dy), X(70), Y(dy), 0.35))
    add(ln(X(46), Y(0), X(46), Y(22), 0.35))
    add(ln(X(22), Y(33), X(22), Y(44), 0.35))
    add(ln(X(40), Y(33), X(40), Y(44), 0.35))
    add(ln(X(70), Y(11), X(180), Y(11), 0.35))
    add(ln(X(70), Y(28), X(180), Y(28), 0.35))
    add(ln(X(163), Y(11), X(163), Y(28), 0.35))
    add(ln(X(140), Y(28), X(140), Y(44), 0.35))
    add(ln(X(162), Y(28), X(162), Y(44), 0.35))
    add("</g>")
    box(x0, y0, 180, 44, 0.7)
    add('<rect id="title_block_frame" x="%s" y="%s" width="180" height="44" style="%s"/>'
        % (_sh_f(x0), _sh_f(y0), _THICK))

    def label(key, x, y):
        txt(x, y, SHEET_LABELS[key][1], 2.0, field="pdm_label_" + key)
        add('<text freecad:editable="pdm_label_%s" x="%s" y="%s" style="font-size:2px;fill:#000000">'
            "<tspan>%s</tspan></text>" % (key, _sh_f(x), _sh_f(y), _sh_esc(SHEET_LABELS[key][1])))

    def value(key, x, y, size=3.5, text="", bold=False, anchor="start", autofill=None):
        style = "font-size:%spx;fill:#000000" % _sh_f(size)
        if bold:
            style += ";font-weight:bold"
        if anchor != "start":
            style += ";text-anchor:%s" % anchor
        txt(x, y, text, size, bold, anchor, "pdm_" + key)
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

    if rec is not None:
        rec["anchors"].update({"logo": (X(163), Y(11), 17.0), "processes": (x0, y0, 180.0), "revisions": (FR, FT, 160.0)})
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
        if r is not rows[-1]:
            o_lines.append(_sh_line(x0, y + h, right, y + h))   # the last one is the frame's edge
        y += h
    bottom = y
    for cx in (x0 + w_rev, x0 + w_rev + w_date, right - w_by):
        o_lines.append(_sh_line(cx, top, cx, bottom))
    o = ['<g style="%s">' % _THIN] + o_lines + ["</g>"]
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
    if not pages or (not targets and ask_existing_page is None):
        return []
    # Data first, changes after: a server without this endpoint (older than 0.8.1) fails
    # here, before an ordinary page has had its template swapped for an empty one.
    tb = api_get("/items/%s/title-block" % item_id)
    if not targets and ask_existing_page(pages[0]):
        old = pages[0].Template
        tpl = doc.addObject("TechDraw::DrawSVGTemplate", "EasyPDMTemplate")
        pages[0].Template = tpl
        if old is not None and not old.InList:
            doc.removeObject(old.Name)
        targets = [pages[0]]
    if not targets:
        return []
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



# ---------------------------------------------------------------------------------------
# SolidWorks: the same sheet drawn through the API by a one-time setup macro
# ---------------------------------------------------------------------------------------
SW_MACRO = os.path.join(os.path.dirname(os.path.abspath(__file__)), os.pardir, os.pardir,
                        "EasyPDM.SolidWorks", "EasyPDMSheetFormat.bas")

# Fields SolidWorks fills by itself; everything else links to the drawing's own custom
# property of the same name ($PRP:"pdm_drawn_by"), which the Upload/Download macros write.
_SW_NATIVE_FIELDS = {
    "pdm_scale": '$PRP:""SW-Sheet Scale""',
    "pdm_sheet": '$PRP:""SW-Current Sheet"" / $PRP:""SW-Total Sheets""',
}

_SW_HEADER = """Attribute VB_Name = "EasyPDMSheetFormat"
Option Explicit

' ============================================================================
' EasyPDMSheetFormat -- one-time setup macro: draws the EasyPDM sheet format (A3, A2 or A1
' landscape) on the current sheet of the active drawing and saves it as a sheet format
' (.slddrt) and a drawing template (.drwdot) in Documents\\EasyPDM\\SolidWorks.
'
' GENERATED by EasyPDM.FreeCad/templates/build_templates.py from the same coordinates as the
' FreeCAD templates -- edit the script, not this file, and rerun it.
'
' How to use:
'   1. File > New > Drawing; in Sheet Properties pick paper size A3, A2 or A1 (ISO,
'      landscape) and NO sheet format ("Display sheet format" off / custom size).
'   2. Run this macro (Sub main). It draws the frame, grid references and title block in
'      the sheet format, links the title block fields to custom properties and saves the
'      two files.
'   3. Tools > Options > File Locations: add Documents\\EasyPDM\\SolidWorks to "Sheet
'      Formats" and "Document Templates", then start new drawings from the EasyPDM template.
'
' Field values are links to the drawing's own custom properties ($PRP:"pdm_drawn_by",
' $PRP:"pdm_label_drawn_by", ...); the EasyPDM Upload/Download macros write those
' properties from the server. Scale and sheet number use SolidWorks's own sheet properties.
' A custom sheet format works with EasyPDM if its notes link to the same property names.
'
' ASCII only, and every module-level declaration before the first Sub/Function (both are
' hard rules for VBA import in SolidWorks, see EasyPDMUpload.bas).
' A log of every step goes to %TEMP%\\EasyPDM_sheetformat.log -- send it when something
' looks wrong.
' ============================================================================

Private Const SW_DOC_DRAWING As Long = 3                    ' swDocumentTypes_e.swDocDRAWING
Private Const SW_CUSTOM_INFO_TEXT As Long = 30              ' swCustomInfoType_e.swCustomInfoText
Private Const SW_CUSTOM_PROPERTY_REPLACE As Long = 2        ' swCustomPropertyAddOption_e.swCustomPropertyReplaceValue
Private Const SW_SAVEAS_SILENT_COPY As Long = 3             ' swSaveAsOptions_Silent + swSaveAsOptions_Copy
Private Const THICK_MM As Double = 0.7
Private Const THIN_MM As Double = 0.35

Private swApp As Object
Private swModel As Object
Private gH As Double
Private gLog As String
Private gThick As Collection
Private gThin As Collection
Private gOffsets As Object
Private gLang As String
Private gLines As Long
Private gNotes As Long
Private gFailed As Long
"""

_SW_BODY = r"""
Function T(ByVal key As String) As String
    Select Case gLang
        Case "pl"
            Select Case key
                Case "Title": T = "Arkusz EasyPDM"
                Case "NotDrawing": T = "Otworz rysunek (nowy, pusty), na ktorym ma powstac format arkusza EasyPDM."
                Case "WrongSize": T = "Ustaw w Wlasciwosciach arkusza rozmiar A3, A2 albo A1 (poziomo) i uruchom makro ponownie. Obecny rozmiar: "
                Case "HasFormat": T = "Ten arkusz ma juz format arkusza: "
                Case "HasFormat2": T = "Zacznij od nowego rysunku bez formatu arkusza (Wlasciwosci arkusza: bez wyswietlania formatu)."
                Case "Confirm": T = "Narysowac format arkusza EasyPDM {f} na biezacym arkuszu i zapisac go jako format arkusza i szablon rysunku?"
                Case "Done": T = "Gotowe. Zapisano:"
                Case "Done2": T = "Dodaj ten folder w Narzedzia > Opcje > Lokalizacje plikow (Formaty arkuszy i Szablony dokumentow)."
                Case "Problems": T = "Czesc elementow sie nie udala -- wyslij log: "
                Case "Failed": T = "Nie udalo sie przygotowac formatu arkusza: "
            End Select
        Case "de"
            Select Case key
                Case "Title": T = "EasyPDM-Blatt"
                Case "NotDrawing": T = "Oeffnen Sie eine (neue, leere) Zeichnung, auf der das EasyPDM-Blattformat entstehen soll."
                Case "WrongSize": T = "Stellen Sie in den Blatteigenschaften A3, A2 oder A1 (quer) ein und starten Sie das Makro erneut. Aktuelle Groesse: "
                Case "HasFormat": T = "Dieses Blatt hat bereits ein Blattformat: "
                Case "HasFormat2": T = "Beginnen Sie mit einer neuen Zeichnung ohne Blattformat (Blatteigenschaften: Blattformat nicht anzeigen)."
                Case "Confirm": T = "Das EasyPDM-Blattformat {f} auf dem aktuellen Blatt zeichnen und als Blattformat und Zeichnungsvorlage speichern?"
                Case "Done": T = "Fertig. Gespeichert:"
                Case "Done2": T = "Fuegen Sie diesen Ordner unter Extras > Optionen > Dateipfade hinzu (Blattformate und Dokumentvorlagen)."
                Case "Problems": T = "Einige Elemente sind fehlgeschlagen -- senden Sie das Protokoll: "
                Case "Failed": T = "Das Blattformat konnte nicht erstellt werden: "
            End Select
    End Select
    If T <> "" Then Exit Function
    Select Case key
        Case "Title": T = "EasyPDM sheet"
        Case "NotDrawing": T = "Open the (new, empty) drawing the EasyPDM sheet format should be drawn on."
        Case "WrongSize": T = "Set the paper size to A3, A2 or A1 (landscape) in Sheet Properties and run the macro again. Current size: "
        Case "HasFormat": T = "This sheet already has a sheet format: "
        Case "HasFormat2": T = "Start from a new drawing with no sheet format (Sheet Properties: do not display a sheet format)."
        Case "Confirm": T = "Draw the EasyPDM {f} sheet format on the current sheet and save it as a sheet format and a drawing template?"
        Case "Done": T = "Done. Saved:"
        Case "Done2": T = "Add this folder under Tools > Options > File Locations (Sheet Formats and Document Templates)."
        Case "Problems": T = "Some elements failed -- please send the log: "
        Case "Failed": T = "Could not prepare the sheet format: "
    End Select
End Function

Sub LogLine(ByVal s As String)
    gLog = gLog & Format(Now, "hh:nn:ss") & "  " & s & vbCrLf
End Sub

Function LogPath() As String
    LogPath = Environ("TEMP") & "\EasyPDM_sheetformat.log"
End Function

Sub WriteLog()
    Dim f As Integer
    On Error Resume Next
    f = FreeFile
    Open LogPath() For Output As #f
    Print #f, gLog;
    Close #f
End Sub

Function NearlyEqual(ByVal a As Double, ByVal b As Double) As Boolean
    NearlyEqual = Abs(a - b) < 0.002
End Function

Function DetectFormat(ByVal w As Double, ByVal h As Double) As String
    DetectFormat = ""
""" + "{detect}" + r"""
End Function

Sub EnsureFolder(ByVal path As String)
    Dim parts() As String, cur As String, i As Integer
    parts = Split(path, "\")
    cur = parts(0)
    For i = 1 To UBound(parts)
        If parts(i) <> "" Then
            cur = cur & "\" & parts(i)
            If Dir(cur, vbDirectory) = "" Then MkDir cur
        End If
    Next i
End Sub

' Offset between a note's position and the bottom-left of its text, measured once per text
' height on a probe note "H" (no descender): SolidWorks positions a note by its own anchor,
' not by the baseline, so the probe tells where the baseline lands.
Function NoteOffset(ByVal sizeMm As Double) As Variant
    Dim key As String, nt As Object, ann As Object, tf As Object, ext As Variant, pos As Variant
    key = CStr(sizeMm)
    If gOffsets.Exists(key) Then
        NoteOffset = gOffsets(key)
        Exit Function
    End If
    Dim res(1) As Double
    res(0) = 0#
    res(1) = sizeMm / 1000#
    On Error Resume Next
    swModel.ClearSelection2 True
    Set nt = swModel.InsertNote("H")
    If Not nt Is Nothing Then
        Set ann = nt.GetAnnotation
        Set tf = ann.GetTextFormat(0)
        tf.CharHeight = sizeMm / 1000#
        ann.SetTextFormat 0, False, tf
        ann.SetPosition2 0.1, 0.1, 0
        ext = nt.GetExtent
        pos = ann.GetPosition
        If IsArray(ext) And IsArray(pos) Then
            res(0) = pos(0) - ext(0)
            res(1) = pos(1) - ext(1)
            LogLine "probe " & key & " mm: offset x=" & Format(res(0) * 1000, "0.00") & " y=" & Format(res(1) * 1000, "0.00") & " mm"
        Else
            LogLine "probe " & key & " mm: no extent, using defaults"
        End If
        ann.Select3 False, Nothing
        swModel.EditDelete
    Else
        LogLine "probe " & key & " mm: InsertNote failed"
    End If
    If Err.Number <> 0 Then LogLine "probe error " & Err.Number & ": " & Err.Description
    On Error GoTo 0
    gOffsets.Add key, res
    NoteOffset = res
End Function

' A line in sheet millimetres, y measured down from the top edge (as in the SVG templates).
Sub L(ByVal x1 As Double, ByVal y1 As Double, ByVal x2 As Double, ByVal y2 As Double, ByVal widthMm As Double)
    Dim seg As Object
    On Error Resume Next
    Set seg = swModel.SketchManager.CreateLine(x1 / 1000#, gH - y1 / 1000#, 0, x2 / 1000#, gH - y2 / 1000#, 0)
    If seg Is Nothing Or Err.Number <> 0 Then
        gFailed = gFailed + 1
        LogLine "line failed " & x1 & "," & y1 & " -> " & x2 & "," & y2 & " (" & Err.Description & ")"
        Err.Clear
        Exit Sub
    End If
    On Error GoTo 0
    gLines = gLines + 1
    If widthMm > 0.5 Then gThick.Add seg Else gThin.Add seg
End Sub

' A note whose text starts at x (or is centred on it) with its baseline at y, in mm.
Sub N(ByVal x As Double, ByVal y As Double, ByVal txt As String, ByVal sizeMm As Double, ByVal bold As Boolean, ByVal centre As Boolean, ByVal typicalLen As Integer)
    Dim nt As Object, ann As Object, tf As Object, off As Variant, xLeft As Double
    off = NoteOffset(sizeMm)
    On Error Resume Next
    swModel.ClearSelection2 True
    Set nt = swModel.InsertNote(txt)
    If nt Is Nothing Or Err.Number <> 0 Then
        gFailed = gFailed + 1
        LogLine "note failed: " & txt & " (" & Err.Description & ")"
        Err.Clear
        Exit Sub
    End If
    Set ann = nt.GetAnnotation
    Set tf = ann.GetTextFormat(0)
    tf.CharHeight = sizeMm / 1000#
    tf.Bold = bold
    ann.SetTextFormat 0, False, tf
    xLeft = x
    ' Centred texts are placed by their typical width -- a linked value's real length is
    ' not known until the property is filled in.
    If centre Then xLeft = x - typicalLen * sizeMm * 0.33
    ann.SetPosition2 xLeft / 1000# + off(0), gH - y / 1000# + off(1), 0
    If Err.Number <> 0 Then
        gFailed = gFailed + 1
        LogLine "note position failed: " & txt & " (" & Err.Description & ")"
        Err.Clear
    Else
        gNotes = gNotes + 1
    End If
    On Error GoTo 0
End Sub

Sub ApplyWidths(ByVal segs As Collection, ByVal widthMm As Double)
    Dim seg As Object
    On Error Resume Next
    swModel.ClearSelection2 True
    For Each seg In segs
        seg.Select4 True, Nothing
    Next seg
    swModel.SetLineWidthCustom widthMm / 1000#
    If Err.Number <> 0 Then LogLine "line width " & widthMm & " mm failed: " & Err.Description
    swModel.ClearSelection2 True
    On Error GoTo 0
End Sub

Sub SetProp(ByVal mgr As Object, ByVal name As String, ByVal value As String)
    On Error Resume Next
    mgr.Add3 name, SW_CUSTOM_INFO_TEXT, value, SW_CUSTOM_PROPERTY_REPLACE
    If Err.Number <> 0 Then LogLine "property " & name & " failed: " & Err.Description
    On Error GoTo 0
End Sub

Sub main()
    Dim sheet As Object, w As Double, h As Double, fmt As String, folder As String
    Dim fmtPath As String, tplPath As String, existing As String, errs As Long, warns As Long
    On Error GoTo Fail
    Set swApp = Application.SldWorks
    gLang = "en"
    Select Case LCase(swApp.GetCurrentLanguage)
        Case "polish": gLang = "pl"
        Case "german": gLang = "de"
    End Select
    LogLine "EasyPDMSheetFormat start, SolidWorks " & swApp.RevisionNumber & ", language " & swApp.GetCurrentLanguage

    Set swModel = swApp.ActiveDoc
    If swModel Is Nothing Then
        MsgBox T("NotDrawing"), vbExclamation, T("Title")
        Exit Sub
    End If
    If swModel.GetType <> SW_DOC_DRAWING Then
        MsgBox T("NotDrawing"), vbExclamation, T("Title")
        Exit Sub
    End If

    Set sheet = swModel.GetCurrentSheet
    sheet.GetSize w, h
    fmt = DetectFormat(w, h)
    LogLine "sheet " & sheet.GetName & ": " & Format(w * 1000, "0.0") & " x " & Format(h * 1000, "0.0") & " mm -> " & IIf(fmt = "", "?", fmt)
    If fmt = "" Then
        MsgBox T("WrongSize") & Format(w * 1000, "0") & " x " & Format(h * 1000, "0") & " mm", vbExclamation, T("Title")
        WriteLog
        Exit Sub
    End If
    existing = sheet.GetTemplateName
    LogLine "current sheet format: '" & existing & "'"
    If existing <> "" Then
        MsgBox T("HasFormat") & existing & vbCrLf & vbCrLf & T("HasFormat2"), vbExclamation, T("Title")
        WriteLog
        Exit Sub
    End If
    If MsgBox(Replace(T("Confirm"), "{f}", fmt), vbYesNo + vbQuestion, T("Title")) <> vbYes Then Exit Sub

    ' Label properties first, so the linked notes show text while they are being placed.
    Dim mgr As Object
    Set mgr = swModel.Extension.CustomPropertyManager("")
""" + "{props}" + r"""
    SetProp mgr, "pdm_sheet_format", "EasyPDM " & fmt

    gH = h
    Set gThick = New Collection
    Set gThin = New Collection
    Set gOffsets = CreateObject("Scripting.Dictionary")
    swModel.EditTemplate
    LogLine "editing sheet format"
    swModel.SketchManager.AddToDB = True
    Select Case fmt
""" + "{dispatch}" + r"""
    End Select
    swModel.SketchManager.AddToDB = False
    ApplyWidths gThick, THICK_MM
    ApplyWidths gThin, THIN_MM
    swModel.EditSheet
    swModel.ForceRebuild3 False
    LogLine "drawn: " & gLines & " lines, " & gNotes & " notes, " & gFailed & " failed"

    folder = Environ("USERPROFILE") & "\Documents\EasyPDM\SolidWorks"
    EnsureFolder folder
    fmtPath = folder & "\EasyPDM_" & fmt & "_Landscape.slddrt"
    tplPath = folder & "\EasyPDM_" & fmt & "_Landscape.drwdot"
    sheet.SaveFormat fmtPath
    LogLine "sheet format saved: " & fmtPath & " exists=" & (Dir(fmtPath) <> "")
    swModel.Extension.SaveAs tplPath, 0, SW_SAVEAS_SILENT_COPY, Nothing, errs, warns
    LogLine "drawing template saved: " & tplPath & " exists=" & (Dir(tplPath) <> "") & " errors=" & errs & " warnings=" & warns
    WriteLog

    Dim msg As String
    msg = T("Done") & vbCrLf & fmtPath & vbCrLf & tplPath & vbCrLf & vbCrLf & T("Done2")
    If gFailed > 0 Then msg = msg & vbCrLf & vbCrLf & T("Problems") & LogPath()
    MsgBox msg, vbInformation, T("Title")
    Exit Sub
Fail:
    LogLine "ERROR " & Err.Number & ": " & Err.Description
    On Error Resume Next
    swModel.SketchManager.AddToDB = False
    swModel.EditSheet
    WriteLog
    MsgBox T("Failed") & Err.Description & vbCrLf & LogPath(), vbCritical, T("Title")
End Sub
"""


def _vb_num(v):
    return ("%.3f" % v).rstrip("0").rstrip(".") or "0"


def _vb_str(text):
    return '"' + text.replace('"', '""') + '"'


def build_solidworks_macro():
    detect, dispatch, subs = [], [], []
    for name, (W, H, _fx, _fy) in SHEET_FORMATS.items():
        detect.append('    If NearlyEqual(w, %s) And NearlyEqual(h, %s) Then DetectFormat = "%s"'
                      % (_vb_num(W / 1000.0), _vb_num(H / 1000.0), name))
        dispatch.append("        Case \"%s\": Draw%s" % (name, name))
        rec = {"lines": [], "texts": [], "anchors": {}}
        build_base_sheet(name, rec)
        body = ["Sub Draw%s()" % name]
        for x1, y1, x2, y2, wd in rec["lines"]:
            body.append("    L %s, %s, %s, %s, %s" % tuple(_vb_num(v) for v in (x1, y1, x2, y2, wd)))
        for x, y, text, size, bold, anchor, field in rec["texts"]:
            if field is None:
                content, typical = _vb_str(text), len(text)
            elif field in _SW_NATIVE_FIELDS:
                content, typical = '"' + _SW_NATIVE_FIELDS[field] + '"', 3
            elif field == "pdm_format":
                content, typical = _vb_str(name), len(name)
            else:
                content, typical = '"$PRP:""%s"""' % field, (1 if field == "pdm_revision" else len(text) or 1)
            body.append("    N %s, %s, %s, %s, %s, %s, %d" % (
                _vb_num(x), _vb_num(y), content, _vb_num(size), "True" if bold else "False",
                "True" if anchor == "middle" else "False", typical))
        body.append("End Sub")
        subs.append("\n".join(body))
    props = "\n".join('    SetProp mgr, "pdm_label_%s", %s' % (k, _vb_str(v[1])) for k, v in SHEET_LABELS.items())
    text = (_SW_HEADER + _SW_BODY.replace("{detect}", "\n".join(detect))
            .replace("{dispatch}", "\n".join(dispatch)).replace("{props}", props)
            + "\n" + "\n\n".join(subs) + "\n")
    text.encode("ascii")  # VBA import breaks on anything else
    return text.replace("\r\n", "\n").replace("\n", "\r\n")


def write_solidworks_macro():
    path = os.path.abspath(SW_MACRO)
    with open(path, "w", encoding="ascii", newline="") as f:
        f.write(build_solidworks_macro())
    print("wrote", path)

if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__))
    for name in SHEET_FORMATS:
        path = os.path.join(here, "EasyPDM_%s_Landscape.svg" % name)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(build_base_sheet(name))
        print("wrote", path)
    for macro in MACROS:
        sync_macro(macro)
    write_solidworks_macro()
