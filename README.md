<p align="center"><img src="EasyPDM.Web/public/favicon.svg" alt="EasyPDM logo" width="110"></p>

# EasyPDM — PDM System for CAD Files

**English** | [Polski](README.pl.md) | [Deutsch](README.de.md)

[![Release](https://img.shields.io/github/v/release/pawelcel/EasyPDM?label=release)](https://github.com/pawelcel/EasyPDM/releases/latest) [![License: MIT](https://img.shields.io/github/license/pawelcel/EasyPDM)](LICENSE) [![Tests](https://img.shields.io/github/actions/workflow/status/pawelcel/EasyPDM/build.yml?branch=main&label=tests)](https://github.com/pawelcel/EasyPDM/actions/workflows/build.yml) [![Wiki](https://img.shields.io/badge/docs-wiki-blue)](https://github.com/pawelcel/EasyPDM/wiki) [![YouTube](https://img.shields.io/badge/YouTube-video_series-FF0000?logo=youtube&logoColor=white)](https://www.youtube.com/playlist?list=PLe2vSF7-ieUA)

**A free, self-hosted PDM for small engineering teams.** Part numbers, revisions, statuses,
a ready-made BOM and drawing title blocks that fill themselves in — straight from
SolidWorks, FreeCAD and SolidCraft. No more `bracket_v3_FINAL_FOR_REAL.SLDPRT` on a shared
drive and "which version is the current one?".

**[⬇ Download the latest release](https://github.com/pawelcel/EasyPDM/releases/latest)** ·
**[Try the live demo](https://easypdmdemo.celmer.info/)** (log in as `admin` / `admin`, reset daily) ·
**[Watch the videos](https://www.youtube.com/playlist?list=PLe2vSF7-ieUA)**

## Why EasyPDM

- **One number, one history** — every Part and Assembly gets its own number, revision,
  status and a record of who changed what and when.
- **Send and fetch from the CAD program** — one click uploads a part or a whole assembly,
  one click brings it back with all its components.
- **Instant BOM** — every assembly lists its components with quantities, material,
  manufacturer and order numbers, ready to export to CSV.
- **Drawings fill themselves in** — name, drawing number, revision, material, processes,
  company logo and, on assembly drawings, a parts list numbered like the balloons.
- **Statuses, reviews and locks** — in progress → under review → released, and nobody
  overwrites what someone else is working on.
- **Yours, on your own computer** — no cloud account, no subscription, open source (MIT).

## Works with your CAD

| CAD program | What you get |
|---|---|
| **SolidWorks** | Add-in with an EasyPDM tab and document panel, drawing sheets — [repository](https://github.com/pawelcel/EasyPDM-SolidWorks-Addon) · [wiki](https://github.com/pawelcel/EasyPDM-SolidWorks-Addon/wiki) |
| **FreeCAD** | EasyPDM workbench with a document panel, drawing sheets — [repository](https://github.com/pawelcel/EasyPDM-FreeCAD-Addon) · [wiki](https://github.com/pawelcel/EasyPDM-FreeCAD-Addon/wiki) |
| **SolidCraft** | Plugin and companion app — [repository](https://github.com/pawelcel/EasyPDM-SolidCraft-Addon) · [wiki](https://github.com/pawelcel/EasyPDM-SolidCraft-Addon/wiki) |
| Autodesk Inventor | Upload and Download macros, no longer developed — [details](EasyPDM.Inventor/README.md) |

Everything else — the browser app, catalogs, BOM — is the same whatever you design in.

## Quick start

1. **Install the server once**, on any computer in the company that stays on:
   - **Windows** — run `EasyPDM_Windows_v<version>.exe` from the
     [latest release](https://github.com/pawelcel/EasyPDM/releases/latest); the wizard does
     the rest.
   - **Docker** —
     ```bash
     git clone https://github.com/pawelcel/EasyPDM.git && cd EasyPDM && ./install-easypdm-docker.sh
     ```
   - **Linux without Docker** — the `.tar.gz` package from the same release,
     [step by step on the wiki](https://github.com/pawelcel/EasyPDM/wiki/Installation).
2. **Open `http://localhost:5000`** (or the computer's address from another machine) and log
   in as `admin` / `admin` — then change that password. A sample project is waiting for you.
3. **Add your CAD program** — install the add-on from the table above and log in from it.

Everyone else in the team just opens the address in a browser.

## Videos

On YouTube, in English — [the whole playlist](https://www.youtube.com/playlist?list=PLe2vSF7-ieUA):

1. [What a PDM is and why EasyPDM](https://youtu.be/OaqyJQCHkt4)
2. [Installation — Windows, Docker or Linux](https://youtu.be/ZwAWCJN9tWg)
3. [Projects and structure](https://youtu.be/ZB3pXQY6-e0)
4. [From CAD to PDM — the upload macro](https://youtu.be/vMQPxwWQrGc)
5. [A whole assembly — upload, download, update and drawings](https://youtu.be/vXHYbRlPu_s)
6. [Every field of a part — statuses, locks, materials and processes](https://youtu.be/JxMLm7bmuck)
7. [PDM → Drawing — the title block fills itself in](https://youtu.be/ZJiMQNJRsB0)
8. [BOM and documentation — the bill of materials nobody retypes](https://youtu.be/mJW8TSQ7EpU)

## Learn more

- 📖 **[Wiki](https://github.com/pawelcel/EasyPDM/wiki)** — with screenshots:
  [Installation](https://github.com/pawelcel/EasyPDM/wiki/Installation) ·
  [Using EasyPDM](https://github.com/pawelcel/EasyPDM/wiki/Using-EasyPDM) ·
  [CAD Integrations](https://github.com/pawelcel/EasyPDM/wiki/CAD-Integrations) ·
  [Settings](https://github.com/pawelcel/EasyPDM/wiki/Settings) ·
  [Technical Overview](https://github.com/pawelcel/EasyPDM/wiki/Technical-Overview)
- 🛠 **Administrators and developers** — architecture, API and server details in
  [`TECHNICAL.md`](TECHNICAL.md)
- 📝 **What's new** — [`CHANGELOG.md`](CHANGELOG.md)

## About

I'm a mechanical design engineer and knew exactly what I was missing when working with CAD
files. I didn't write the code myself: Claude, an AI model from Anthropic, wrote it from my
requirements. I built EasyPDM for my own use, and since it works — why not share it.

Found a bug or missing something? [Open an issue](https://github.com/pawelcel/EasyPDM/issues).
If EasyPDM saves you time, you can
[buy me a coffee](https://buymeacoffee.com/easypdm). Licensed under [MIT](LICENSE).
