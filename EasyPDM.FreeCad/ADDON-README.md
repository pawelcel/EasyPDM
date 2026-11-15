# EasyPDM for FreeCAD

An **EasyPDM** menu in FreeCAD: **Upload** the active document to an
[EasyPDM](https://github.com/pawelcel/EasyPDM) server, **Download** an item with its whole
assembly, **Log in** (approved in the browser, so FreeCAD never sees your password) and
**Log out**. EasyPDM is a free, self-hosted PDM for small engineering firms: numbering,
revisions, statuses, BOM and drawing title blocks filled in from the PDM.

> This repository is generated: it is copied from
> [`EasyPDM.FreeCad/`](https://github.com/pawelcel/EasyPDM/tree/main/EasyPDM.FreeCad) in the
> EasyPDM repository on every EasyPDM release. Report issues and send changes
> [there](https://github.com/pawelcel/EasyPDM/issues).

## Installing

FreeCAD → **Tools → Addon manager** → ⚙ (preferences) → **Custom repositories**: add
`https://github.com/pawelcel/EasyPDM-FreeCAD` with branch `main`, then install **EasyPDM** from
the list and restart FreeCAD. Updates arrive the same way.

Or unpack this repository into FreeCAD's `Mod` folder as `EasyPDM` (Linux usually
`~/.local/share/FreeCAD/v1-1/Mod`, Windows `%APPDATA%\FreeCAD\v1-1\Mod`).

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
