# EasyPDM — PDM System for CAD Files

**English** | [Polski](README.pl.md) | [Deutsch](README.de.md)

[<img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" width="136">](https://buymeacoffee.com/easypdm)

EasyPDM is where your Parts and Assemblies get one, shared order for the whole team:
every item has its own number, revision, status and change history, and assemblies get
a ready-made bill of materials (BOM). No more `bracket_v3_FINAL_FOR_REAL.SLDPRT` on a
shared drive and the question "which version is the current one?". Ready-made macros
for FreeCAD and SolidWorks send and fetch files straight from within the CAD program —
everything else (the browser app, material/manufacturer catalogs, BOM) works the same
regardless of what you design in.

I'm a mechanical design engineer and I knew exactly what such a tool should look like
and how it should work day to day — what I was missing when working with CAD files.
I didn't write the code myself: the whole application was written for me by Claude (an
AI model from Anthropic) based on my requirements and descriptions. I built EasyPDM for
my own use, and since it already exists and works — why not share it with others.

## What it gives you

- **One number, one history** — every Part and Assembly gets an automatically assigned
  number that no one else will ever get again. You can see who changed what and when,
  who currently has an item "on their bench", and which revision is current.
- **Instant bill of materials** — an assembly shows the list of its own components with
  quantities, material, manufacturer, order numbers — ready to export to CSV.
- **Shared material and manufacturer catalogs** — pick from a list instead of typing it
  in by hand every time, so names don't drift apart between projects.
- **Search across the whole company database**, not just the current project — handy
  when you want to check whether a similar part already exists somewhere.
- **Item locking** — while you're working on something, no one else can overwrite your
  changes without your consent (an administrator can take over or release someone
  else's lock if needed — e.g. when the owner is away).

## First run

EasyPDM is installed ONCE — on a single computer in the company (not necessarily some
special "server", an ordinary computer that's simply left switched on works fine too).
From then on, everyone connects to it with a regular web browser, just like any
website — only at an address visible exclusively inside your company network, not on
the public internet.

**If EasyPDM is already running at your company** — ask whoever installed it for the
address (it will look something like `http://192.168.1.20:5000`, or
`http://localhost:5000` if EasyPDM is running on your own computer). Type it into your
browser's address bar, just like any other website address, and log in.

**If nobody has installed it yet and it's up to you:**

**Windows** (no IT knowledge required) — go to the
[Releases page of this repository](https://github.com/pawelcel/EasyPDM/releases),
download the latest `EasyPDM_Windows_v<version>.exe` file and run it — the installation wizard will
walk you through the rest step by step and leave a shortcut to EasyPDM on your desktop
(the only thing it might ask about: whether you already have PostgreSQL installed, the
program that stores the data — if not, it will point you to where to download it before
it can continue).

**Linux** (some comfort with a terminal is enough — pick one):

- *Docker* (recommended if Docker is already installed on the machine):
  ```bash
  git clone https://github.com/pawelcel/EasyPDM.git
  cd EasyPDM
  ./install-easypdm-docker.sh
  ```
- *Native install, no Docker* — download the ready-made `EasyPDM-Linux-x64_v<version>` package
  (built automatically by this repo's CI — grab it from the
  [Actions tab](https://github.com/pawelcel/EasyPDM/actions/workflows/build-linux-package.yml),
  latest successful run, "Artifacts" section) or clone the repo yourself, then:
  ```bash
  tar xzf EasyPDM-Linux-x64_v<version>.tar.gz && cd EasyPDM-Linux-x64_v<version>   # if you downloaded the package
  sudo ./install-easypdm-linux.sh
  ```
  This installs PostgreSQL (if missing) and EasyPDM itself as a `systemd` service that
  starts automatically with the machine.

Either way, EasyPDM ends up at `http://localhost:5000` (or the machine's address on
your network, from another computer). Full details, updating, and uninstalling: see
[`TECHNICAL.md`](TECHNICAL.md).

First login on a freshly installed EasyPDM: username `admin`, password `admin` — change
this password right after logging in (Settings → Users → find the `admin` account in
the list → change password).

After logging in: pick a project (or create a new one, if you have permission) — that's
the container for your files and assembly structure — and install the macro for your
CAD program, see below.

## Working from FreeCAD / SolidWorks

The macros add two simple operations inside the CAD program: **Upload** (send the active
document to the PDM) and **Download** (fetch a Part/Assembly from the PDM, together with
the whole assembly, and open it in the program).

Installation and details:
- FreeCAD: [`EasyPDM.FreeCad/README.md`](EasyPDM.FreeCad/README.md)
- SolidWorks: [`EasyPDM.SolidWorks/README.md`](EasyPDM.SolidWorks/README.md)

**Upload** — you have a saved file open, you click Upload. Your browser opens
(automatically logged in) and asks: new item, duplicate of an existing one (copies its
properties, no files), or attach a new version to an already-existing item. You choose,
confirm in the browser — the macro detects completion on its own and finishes the
upload (renames the local file to the PDM number, attaches the file, exports a STEP
preview). For a whole assembly with new, not-yet-uploaded components: the macro detects
them on its own and asks for each one's data individually before sending the main file.

**Download** — you click Download, and in the browser you point to the Part/Assembly to
fetch. For an assembly, the ENTIRE component tree is fetched right away, and the main
file opens automatically in the CAD program.

## Working in the browser

### Projects and structure

Every project has a tree: Folders (plain containers for organizing), Parts and
Assemblies (have a number/status/revision), and Other files (any document with no
structure of its own underneath). An Assembly can contain Parts and other Assemblies
(BOM) — the same component can be used in several assemblies and projects at once, so a
change in one place is visible everywhere that component is used.

An item can be **detached from the structure** (stays in the database, only disappears
from that spot in the tree) or **deleted completely** (administrator only) — complete
deletion is safe for shared components: an item with a parent outside the deleted
subtree will not disappear along with it. A Part/Assembly can also be **duplicated** — the
copy gets its own number and lands right next to the original, with its properties
copied over.

### Parts and Assemblies — kinds and properties

A Part has one of four **kinds**, each with a different set of fields:

| Kind | Additional fields |
|---|---|
| Manufactured | Material, Price |
| Purchased | Manufacturer, Series/Type, Subtype, Order number 1/2, Mass, Price |
| Standard | Material, Norm |
| Client-supplied | Client |

An Assembly has one of three **kinds**: Manufactured, Purchased (Manufacturer,
Series/Type and Subtype) or Client-supplied (Client). Whatever the kind, it can also carry
an optional Mass and any custom properties.

**Client** — for the Client-supplied kind, picked from the same catalog as the Clients tab.
Next to it, **Name 2** — that client's second name from the catalog (if it has one) —
locked until you pick a client.

**Series/Type** is an entry from the chosen manufacturer's list (Manufacturers tab), and
**Subtype** narrows it down within that series (e.g. series "Cylindrical roller bearings"
→ subtypes NU/NJ/NUP), both shown side by side. Series/Type is locked until you pick a
manufacturer, and Subtype until you pick a series; changing the manufacturer or the series
clears whatever is below.

### Status and revisions

Parts/Assemblies move through four statuses: **in progress → under review → released**,
plus **→ cancelled** from released (for an item that turns out not to be needed). In
status "in progress" everything can be edited; outside of it, the name and properties are
locked (price is always editable) and the item is always released (no owner). Going back
from "released" OR "cancelled" to "in progress" bumps the revision by one letter
(A → B → C...) and lets you add a comment on what changed. An assembly with a cancelled
item anywhere in its BOM (even deeply nested) can't itself become "released" — the attempt
shows which item is cancelled. In the tree/list, a cancelled item's icon turns red. At the
bottom of an item's panel you can see the full **history**: who created it, every status
change, every revision with its comment, every added/removed attachment, every
lock/release.

### Who's editing — item locking

The creator of a Part/Assembly immediately becomes its owner, and the item is locked —
while the lock lasts, only the owner can edit its properties (not even an administrator
bypasses this). An administrator can, however, take over or release someone else's lock
and change a locked item's status — e.g. when a coworker is away and their unfinished
item needs to be unblocked. In the tree this is shown by the color of the lock icon:
green — locked by you, yellow — by someone else, open — released (anyone can lock it).
A released item is always released (unlocked).

### Bill of materials (BOM)

An Assembly shows the list of its components: position, name, quantity, material,
manufacturer, order numbers — together with the components of nested assemblies. The
position order can be changed by dragging or by typing a number directly. CSV export
comes in two variants: full (every occurrence listed separately) or aggregated (the same
component used several times — one row with the combined quantity).

### Materials and Manufacturers

Separate, company-wide catalogs (the **Materials list** and **Manufacturers** tabs in
the main menu) — a material has a name and group/subgroup, a manufacturer has a name and
contact people. You pick them from a list when filling in a Part's properties, instead
of typing them in by hand.

### Search and the whole database

The **Whole database** tab searches all items regardless of project — by name, number,
tags, kind. Found filters can be saved for reuse.

### Downloadable documentation

From a Project, Assembly or Part you can download the complete set of attached files as
a ZIP (choosing which file extensions to include) — handy for e.g. sending a complete
set of drawings to a client.

## Accounts and access

Two roles: **administrator** (full access, sees all projects, manages accounts and
server settings) and **user** (sees and works only in the projects they've been assigned
to). Everyone manages their own interface language (Polish/English/German) and
light/dark theme in Settings.

## For administrators and developers

Server installation (Docker / Linux / Windows), architecture, the full list of API
endpoints, and known limitations — see [`TECHNICAL.md`](TECHNICAL.md).
