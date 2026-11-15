# EasyPDM for FreeCAD

An **EasyPDM workbench** for FreeCAD: **Upload** the active document to an
[EasyPDM](https://github.com/pawelcel/EasyPDM) server, **Download** an item with its whole
assembly, **Log in** (approved in the browser, so FreeCAD never sees your password) and
**Log out**, from its menu and toolbar. The same menu can be shown in every workbench: tick
**EasyPDM menu in every workbench** in the EasyPDM menu (off by default). EasyPDM is a free, self-hosted PDM for small engineering firms: numbering,
revisions, statuses, BOM and drawing title blocks filled in from the PDM.

> This repository is generated: it is copied from
> [`EasyPDM.FreeCad/`](https://github.com/pawelcel/EasyPDM/tree/main/EasyPDM.FreeCad) in the
> EasyPDM repository on every EasyPDM release. Report issues and send changes
> [there](https://github.com/pawelcel/EasyPDM/issues).

## Installing

1. FreeCAD → **Edit → Preferences → Addon Manager** → **Custom repositories**: add
   `https://github.com/pawelcel/EasyPDM-FreeCAD` with branch `main`.
2. **Tools → Addon manager**: install **EasyPDM** from the list and restart FreeCAD.

Updates arrive the same way.

Or unpack this repository into FreeCAD's `Mod` folder as `EasyPDM` (Linux usually
`~/.local/share/FreeCAD/v1-1/Mod`, Windows `%APPDATA%\FreeCAD\v1-1\Mod`).

## What works

- **Upload** of parts, assemblies (FreeCAD's native Assembly workbench, with their components as
  BOM positions) and TechDraw drawings: the item is created or picked in the browser, the file is
  renamed to the EasyPDM record name, STEP, PDF and a preview go along, mass and material are
  stored, and the EasyPDM drawing sheet is filled in.
- **Download** of an item with its whole component tree into one folder, opened in FreeCAD.
- **Log in** through the browser (EasyPDM servers from 0.9; "Log in with password…" for older
  ones) and **Log out**.
- Menu texts in English, Polish and German, following FreeCAD's language.

Upload and Download are the EasyPDM FreeCAD macros, live-tested on FreeCAD 1.1; the add-on runs
them unchanged.

Not supported: other assembly workbenches (A2plus, Assembly3, Assembly4), and downloading an
older revision (Download always fetches the current one).

## What it connects to

Only the EasyPDM server whose address you enter when logging in, over HTTP(S). Nothing is sent
anywhere else, and nothing is contacted until you use a command. The login is a revocable
EasyPDM token stored in FreeCAD's preferences (`User parameter:BaseApp/EasyPDM`), shared with
the EasyPDM macros.

## Documentation

- [CAD Integrations](https://github.com/pawelcel/EasyPDM/wiki/CAD-Integrations) on the EasyPDM wiki
- [FreeCAD macros README](https://github.com/pawelcel/EasyPDM/blob/main/EasyPDM.FreeCad/README.md):
  what Upload and Download do, drawing sheets, limitations

FreeCAD 1.0 or newer. MIT licence.
