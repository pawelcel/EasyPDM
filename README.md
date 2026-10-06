# EasyPDM — PDM System for CAD Files

**English** | [Polski](README.pl.md) | [Deutsch](README.de.md)

[<img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" width="136">](https://buymeacoffee.com/easypdm)

EasyPDM is where your Parts and Assemblies get one, shared order for the whole team:
every item has its own number, revision, status and change history, and assemblies get
a ready-made bill of materials (BOM). No more `bracket_v3_FINAL_FOR_REAL.SLDPRT` on a
shared drive and the question "which version is the current one?". Ready-made macros
for FreeCAD, SolidWorks and Autodesk Inventor send and fetch files straight from within
the CAD program — everything else (the browser app, material/manufacturer catalogs, BOM)
works the same regardless of what you design in.

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
- **Notifications** — a bell icon shows what needs your attention: an item of yours
  waiting for review, released, or reverted to "In progress", a new revision, a verdict
  from the client on something you designed, being added to or removed from a project, or
  (administrators) low disk space — each type can be turned off individually in Settings.

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
- *Native install, no Docker* — download the ready-made `EasyPDM-Linux-x64_v<version>.tar.gz`
  package from the [Releases page](https://github.com/pawelcel/EasyPDM/releases), or clone
  the repo yourself, then:
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

A brand new, empty database also gets one sample project on first startup — an assembly
with two parts, something to explore instead of a blank slate. A notification points it
out and reminds you to clear it (Settings → File storage → Danger zone) before real use.

After logging in: pick a project (or create a new one, if you have permission) — that's
the container for your files and assembly structure — and install the macro for your
CAD program, see below.

## Working from FreeCAD / SolidWorks / Inventor

The macros add two simple operations inside the CAD program: **Upload** (send the active
document to the PDM) and **Download** (fetch a Part/Assembly from the PDM, together with
the whole assembly, and open it in the program).

Installation and details:
- FreeCAD: [`EasyPDM.FreeCad/README.md`](EasyPDM.FreeCad/README.md)
- SolidWorks: [`EasyPDM.SolidWorks/README.md`](EasyPDM.SolidWorks/README.md)
- Autodesk Inventor: [`EasyPDM.Inventor/README.md`](EasyPDM.Inventor/README.md)

**Upload** — you have a saved file open, you click Upload. Your browser opens
(automatically logged in) and asks: new item, duplicate of an existing one (copies its
properties, no files), or attach a new version to an already-existing item. You choose,
confirm in the browser — the macro detects completion on its own and finishes the upload
(renames the local file to the PDM number, attaches the file, and — if you tick the boxes
in the browser — exports a STEP and/or a PDF, and saves a picture of the model). For a whole assembly with new,
not-yet-uploaded components: the macro detects them on its own and walks through each one
before sending the main file. Each of those components can be created without assigning it
to any project, so that a part which only exists as a BOM entry does not clutter the
project tree on its own.

**Technical drawings** are recognized as such (a `.SLDDRW`/`.idw`/`.dwg` file, or a FreeCAD
TechDraw page) and matched to the Part/Assembly they document — by reading which models the
drawing's views actually point at, not by guessing from the file name. The drawing uploads
as its own attachment, one per revision, alongside the model's own CAD file, and can
optionally be exported to PDF. If a drawing documents something that has never been
uploaded, the macro offers to send that Part/Assembly first and then continues straight
into the drawing.

The SolidWorks and Inventor macros also write the item's name into a document property
called `EasyPDM_Name`, so your drawing templates and title blocks can pull it in. Both also fill in
the **mass and the material** by themselves: the macro writes them into the document, reads
them back and puts them on the item in EasyPDM. In SolidWorks the two properties hold a
SolidWorks expression rather than a value, so they keep up with the model on their own; in
Inventor they are a snapshot taken at upload. The material also comes along when the browser
opens to create the item, so the Material field starts filled in rather than leaving you to
guess — and one EasyPDM has never seen is added to the materials catalog automatically, so
you can pick it again and filter by it. That is
what makes dropping the name from the file name practical: the name still travels with the
document, just not in its file name.

**Download** — you click Download, and in the browser you point to the Part/Assembly to
fetch. For an assembly, the ENTIRE component tree is fetched right away, and the main
file opens automatically in the CAD program. A current drawing, if there is one, is saved
next to the model file without being opened.

## Working in the browser

### Projects and structure

Every project has a tree: Folders (plain containers for organizing), Parts and
Assemblies (have a number/status/revision), and Other files (any document with no
structure of its own underneath). An Assembly can contain Parts and other Assemblies
(BOM) — the same component can be used in several assemblies and projects at once, so a
change in one place is visible everywhere that component is used.

An item can be **detached from the structure** (stays in the database, only disappears
from that spot in the tree) or **deleted completely** (administrator only). Deleting an
Assembly removes just that Assembly — its components stay, losing only that one BOM
entry, because an Assembly merely *uses* its parts while a Folder *owns* its contents.
Deleting a Folder therefore does take its contents with it, minus anything that also
sits somewhere outside it. A Part/Assembly can also be **duplicated** — the copy gets its
own number and lands right next to the original, with its properties copied over.

Both kinds of removal also work on **several items at once**. Select them with
**Ctrl+click** on a tree row (or with the "Select multiple" button and checkboxes, if the
mouse suits you better); the bar at the top then shows how many are selected and lets you
remove them from the structure, delete them completely, tag them or change their status.
Removing several works exactly like removing one: the items stay in the database and
remain findable under "Whole database".

A finished project can be **closed** with one button in the toolbar — it drops out of the
project selector and the "add item" picker, but nothing else about it changes: its items
stay fully searchable through "Whole database", and the same button opens it again.

A project itself can also be deleted (administrator only) — this does NOT delete its
Parts/Assemblies: they become project-less and stay fully intact (files, attachments,
tags, history, BOM relations), reachable afterward through "Whole database".

### Order documents and who runs the project

Next to the project's own properties sit the documents that come with the job: a **quote**
and an **order confirmation** each have their own slot, plus an open category for
everything else (correspondence, the client's specifications, meeting notes). The two
named slots take several files rather than replacing the previous one — a quote gets
revised and re-sent, and the earlier version is worth keeping — and every file shows when
it was uploaded and by whom.

A project can also name the **person leading it on the client's side**, picked from that
client's contacts (either the client's own, or one belonging to the specific Name 2 the
project is linked to). A button next to the field shows their phone, e-mail and position
without leaving the project.

### Parts and Assemblies — kinds and properties

A Part has one of four **kinds**, each with a different set of fields:

| Kind | Additional fields |
|---|---|
| Manufactured | Material, Price, Additional notes |
| Purchased | Manufacturer, Series/Type, Subtype, Order number 1/2, Price, Additional notes |
| Standard | Material, Norm, Additional notes |
| Client-supplied | Client, Name 2, Additional notes |

**Mass** sits above those, because it is the one field every kind shares — a Part of any
kind and an Assembly alike. The CAD macros fill it in on upload, and changing an item's kind
leaves it alone.

An Assembly has one of three **kinds**: Manufactured, Purchased (Manufacturer,
Series/Type and Subtype) or Client-supplied (Client). Whatever the kind, it can also carry
any custom properties of its own.

**Client** — for the Client-supplied kind, picked from the same catalog as the Clients tab.
Next to it, **Name 2** — one of that client's second names/trade variants from the catalog
(a client can have several, e.g. different subsidiaries) — locked until you pick a client.

**Series/Type** is an entry from the chosen manufacturer's list (Manufacturers tab), and
**Subtype** narrows it down within that series (e.g. series "Cylindrical roller bearings"
→ subtypes NU/NJ/NUP), both shown side by side. Series/Type is locked until you pick a
manufacturer, and Subtype until you pick a series; changing the manufacturer or the series
clears whatever is below.

**How the number looks** is set once, in Settings → Numbering: each kind can get its own
letter prefix (say `C` for client-supplied parts), numbers can be zero-padded to a fixed
width, and the item's own name can be dropped from the record name altogether — so an item
reads `C0001(plate)`, or just `C0001`, instead of `1(plate)`. All three are stamped onto
an item when it is created and never recalculated afterwards — the CAD macros build each
file's name out of that number, so changing it later would leave the files on disk saying
something different. Worth setting before the first real item, then. The one exception is
an early mistake: correcting a Part's kind also corrects its prefix — but only while its
CAD/drawing/PDF/3D slots are still empty. Once a file is in one of them the kind is fixed
too, since the file on disk already carries the number that the kind decides.

### The model preview

Above an item's properties sits a preview box with a **2D/3D** switch: 2D shows the PDF
drawing, 3D shows a **picture of the model** that the CAD macro takes when you upload.

That picture replaces what used to happen here. The STEP file was downloaded and rendered in
your browser every time you opened an item — which produced a still image anyway, since there
was never anything to rotate. It was slow, it got slower the more detailed the model was
(regardless of how small the file looked), and it made the whole machine struggle when the
server ran on the same computer. Taking one picture at upload time, on the machine that
already has the model open, does the same job once instead of over and over.

Two things follow from that:

- **The picture comes with the STEP.** If you clear "Export and upload STEP model" in the
  upload window, there is no picture either, and the box tells you so.
- **Deleting the STEP deletes the picture.** It only ever depicted that model.

The STEP file itself is unchanged — exported, uploaded and downloadable exactly as before. It
simply no longer has to be rendered to show you what the part looks like. Items uploaded
before this version keep their STEP but show no picture until you send them up again.

For large models the upload window warns you before you confirm: exporting a STEP happens
inside your CAD program, and for a big assembly that can take minutes with the program busy
throughout.

### Sending an assembly

The macro needs one form per new component. It no longer opens a browser tab for each of them, and no longer asks you to click OK before each one: the tab you already have open picks the request up and shows the form in place. On an assembly with forty parts that used to be forty clicks and forty tabs.

If no browser is open, the macro notices within a few seconds and falls back to the old way — a message and a new tab — so nothing is lost either way.

### Watching an upload or a download

While a macro is sending files to the PDM or fetching them back, the app shows a list of those files on the right. Each one is ticked off as it completes, the one in progress spins, and a counter says "3 of 7". If something fails it is marked in red and the rest carries on. An assembly's list is laid out like its tree — sub-assemblies and parts indented under the assembly they belong to — and a part used in several places is listed once, under the first of them.

The list's **Cancel** button stops the rest of the transfer, after asking you to confirm. Whatever has gone through stays; the macro stops before the next file, so the one already on its way is finished first. A form from the macro that is waiting on screen goes away, and the notifications get a report of how far it got.

This matters most for an assembly. Sending one used to be a wait with nothing to look at — no way to tell whether the macro was on the second component or the last, or which file it was working on. The list appears about a second after the macro starts and stays for a moment at the end with everything ticked, which is your confirmation that the whole thing went up.

It behaves identically from SolidWorks, Inventor and FreeCAD, and in both directions. If the connection drops or the server restarts mid-way, the list simply stops updating — the upload or download itself carries on untouched.

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

An **Assembly can't get ahead of its own bill of materials**. It goes "under review" only
once every component one level below it is at least under review, and "released" only once
every one of them is released. Only the direct children are checked — what sits deeper is
guarded by the same rule applied to the sub-assembly when its turn comes, so the message
always names something you can see on screen. If a component is behind, the status change
is offered rather than refused: a window lists exactly which ones and asks whether to move
them along with the assembly. Declining changes nothing; accepting moves the components
first and the assembly second, in one go, and each moved component gets its own history
entry. Two cases stop short of that offer — a **sub-assembly** that is behind has a bill of
materials of its own, so it is named and left for you to handle separately, and a component
that is **cancelled, locked by someone else, or in a project you can't access** is named
together with the reason, untouched.

### Client verification

Once a Part/Assembly is **released**, a "Client verification" button in the toolbar opens
the running record of what the client said about it. Each entry carries a result —
**Verified**, **Needs work**, or no result at all, which simply means it went out and you
are waiting — an optional comment, and its own attachments, for example the confirming
e-mail. Entries accumulate, so a round of remarks followed by an acceptance stays readable
as history, with who added it and when.

The project panel shows the whole thing split into three tables (needs work, in progress,
verified), each row with a button that jumps straight to that item in the structure. The
latest result also shows as a badge in the item's own panel, with the date and the revision
it covered. When a verdict arrives, the person who created the item gets a notification.

Verification belongs to the item **in that project**, not to the item alone: the same Part
used for two customers is accepted by two different people, so each project keeps its own
record, and none of it shows up in "Whole database", where there is no project context.
Each entry also remembers which revision it covered — after a new revision is released, the
old acceptance stays visible but is clearly marked as no longer covering what the item is
now.

### Who's editing — item locking

The creator of a Part/Assembly immediately becomes its owner, and the item is locked —
while the lock lasts, only the owner can edit its properties (not even an administrator
bypasses this). An administrator can, however, take over or release someone else's lock
and change a locked item's status — e.g. when a coworker is away and their unfinished
item needs to be unblocked. In the tree this is shown by the color of the lock icon:
green — locked by you, yellow — by someone else, open — released (anyone can lock it).
A released item is always released (unlocked).

### Bill of materials (BOM)

An Assembly shows the list of its components: position, name, quantity, material, norm,
manufacturer, order numbers — together with the components of nested assemblies. The
position order can be changed by dragging or by typing a number directly. CSV export
comes in two variants: full (every occurrence listed separately) or aggregated (the same
component used several times — one row with the combined quantity).

The reverse view is also available — **Used in**: an item's detail panel shows every
assembly, at any depth and across projects, that contains it, with a button to jump
straight there.

### Materials, Manufacturers and Clients

Separate, company-wide catalogs (the **Materials list**, **Manufacturers** and
**Clients** tabs in the main menu) — a material has a name and group/subgroup, a
manufacturer has a name, contact people and a Series/Type + Subtype list of what it
supplies. You pick them from a list when filling in a Part's/Assembly's properties,
instead of typing them in by hand.

A **Client** has a name (plus an optional location), a list of second names/trade variants
(add as many as you need — typing an existing client's name into "Add client" switches to
adding it a new one instead of creating a duplicate), its own contact people, and its own
document tree for e.g. norms or reference files — separate from project files. A Project
can optionally be linked to a Client — and, when that client has several, to one specific
Name 2. The project selector then shows "Project (Client, Name 2)", and the client's detail
panel lists every Project assigned to it, with a button to jump straight there.

### Search and the whole database

The **Whole database** tab searches all items regardless of project — by name, number,
tags, kind. Found filters can be saved for reuse, and a "Clear filters" button resets
everything at once.

### Downloadable documentation

From a Project, Assembly or Part you can download the complete set of attached files as
a ZIP (choosing which file extensions to include) — handy for e.g. sending a complete
set of drawings to a client.

### Notifications

A bell icon (top right, next to your name) shows a list of events: an item you own
waiting for review, released, or reverted to "In progress", a new revision on your
item, a client verification result (remarks or acceptance) on an item you created,
being assigned to or removed from a project, an assigned project being deleted,
your password being changed by an administrator, or (administrators only) low disk
space on the file storage. A CAD macro run that has finished also leaves a report here:
how many files went up or came down, which ones failed, and which were skipped because
they were already in the PDM. Each notification can be marked as read or deleted
individually, and each type can be turned off in Settings → Notifications.

## Accounts and access

Two roles: **administrator** (full access, sees all projects, manages accounts and
server settings) and **user** (sees and works only in the projects they've been assigned
to). Everyone manages their own interface language (Polish/English/German) and
light/dark theme in Settings.

## For administrators and developers

Server installation (Docker / Linux / Windows), architecture, the full list of API
endpoints, and known limitations — see [`TECHNICAL.md`](TECHNICAL.md).
