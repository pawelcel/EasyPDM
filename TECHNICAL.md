# EasyPDM — technical documentation

**English** | [Polski](TECHNICAL.pl.md) | [Deutsch](TECHNICAL.de.md)

This document is for the administrator who installs/maintains EasyPDM, and for
developers. A description of the tool itself (what it's for and how to use it while
designing) is in [README.md](README.md).

## Status

Projects and items are created manually through the web application (uploading a file
straight into the API's storage) or through the FreeCAD (`EasyPDM.FreeCad/`), SolidWorks
(`EasyPDM.SolidWorks/`) or Autodesk Inventor (`EasyPDM.Inventor/`) macro, which call the
same API.
The earlier disk-scanning approach (`EasyPDM.Core`, `EasyPDM.Indexer`) has been removed
from the repo — it was incompatible with the schema since migration `002` and was never
used by `Api`.

The frontend is a separate **React 19 + Vite + TypeScript** application (`EasyPDM.Web/`)
built straight into `EasyPDM.Api/wwwroot/`. The interface is fully translated
(Polish/English/German) and has a light/dark theme. Tested live on: CachyOS, .NET 10,
PostgreSQL 18.

## What's here

- **`db/schema.sql`** — the full schema from scratch (current state after all
  migrations).
- **`db/migrations/`** — migrations `002`–`046` for an already existing database:
  projects, item types, tree visibility, status/revisions, materials (+ groups/
  subgroups), attachments, BOM ordering, revision comments, login and roles, project
  properties, cascading deletes, tree root ordering, manufacturers, saved filters,
  per-user project access, item owner/lock, removal of the dead revision/checkout
  schema, history (status/revisions/attachments/lock), automatic backup schedule,
  tracking of applied migrations, attachment preview/CAD role, item number letter
  prefix per kind, Clients (catalog + own file tree), project-less items (an item can
  exist with no project, reachable only through "Whole database"), manufacturer/client
  contact address, BOM position default/uniqueness, notifications + per-type
  preferences, the sample-project marker, a small internal `system_state` flag table,
  manufacturer series/types with their subtypes, the "Cancelled" status, a client's
  Name 2 becoming a 1:N list instead of a single column, and each Name 2 getting its
  own address plus a `name2_id` on client contacts (NULL = belongs to the client
  itself, inherited read-only by every Name 2), the same `name2_id` split applied to
  `client_nodes` so each Name 2 can have its own files, independent of the client's
  own file tree, and a new `"drawing"` attachment role (alongside `pdf`/`step`/`cad`)
  for a SolidWorks drawing (.SLDDRW) uploaded next to its Part/Assembly's own CAD
  file. Since migration 027, files in this folder are embedded in the program
  (embedded resources) and applied **automatically on every startup** — see
  `MigrationRunner.cs` and "How to run" below — you no longer need to run them
  manually through psql.
- **`EasyPDM.Api/`** — ASP.NET Core (minimal API, Npgsql with no ORM), endpoints split
  by function under `Endpoints/` — full list below in "API endpoints". Also serves the
  built frontend from its own `wwwroot/`. A custom `FileLoggerProvider` (no extra NuGet
  package) writes program logs to `logs/` (daily rotation, 30-day retention), visible
  under Settings → Logs.
- **`EasyPDM.Web/`** — frontend: React 19 + Vite + TypeScript + Tailwind v4 + shadcn/ui
  (components based on Base UI, "base-nova" style), i18n (pl/en/de), light/dark theme.
- **`EasyPDM.Api.Tests/`** — integration tests (xUnit + `WebApplicationFactory`), run the
  WHOLE application against a real PostgreSQL (a separate `pdm_test` schema in the same
  database, reset before every test class). Locally: `dotnet test EasyPDM.Api.Tests`
  (the connection string defaults to a local `pdm`/`pdm_user` — overridable via the
  `EASYPDM_TEST_CONNECTION_STRING` variable, same as in CI).
- **`EasyPDM.FreeCad/`** — two macros: `EasyPDMUpload.FCMacro` (run from within FreeCAD,
  saves the active document, delegates the project/new-vs-existing/properties choice to
  the browser, creates a Part/Assembly in the PDM, attaches the file, exports STEP, and
  renames the local file to `number(name)`) and `EasyPDMDownload.FCMacro` (the opposite
  direction: pick a Part/Assembly in the browser, fetch it together with the WHOLE tree
  of the Assembly's components — so that `App::Link` references resolve — and open it
  in FreeCAD right away; skips already-downloaded files, asks before overwriting an
  older revision with a newer one). **Both macros are untested on live FreeCAD in their
  current version** (the browser-based flow) — see `EasyPDM.FreeCad/README.md`.
- **`EasyPDM.SolidWorks/`** — the SolidWorks counterpart of the above (VBA macros
  `EasyPDMUpload.bas`/`EasyPDMDownload.bas`), with the same browser-based flow, STEP
  export, and automatic assembly-tree detection. **Unverified on live SolidWorks** — see
  `EasyPDM.SolidWorks/README.md` for details and known risks.
- **`EasyPDM.Inventor/`** — the Autodesk Inventor counterpart of the above (VBA macros
  `EasyPDMUpload.bas`/`EasyPDMDownload.bas`), ported from `EasyPDM.SolidWorks/` with the
  same browser-based flow, STEP/PDF export, and automatic assembly-tree detection.
  **Unverified on live Inventor** — see `EasyPDM.Inventor/README.md` for details and
  known risks.
- **`Dockerfile`/`Dockerfile.postgres`/`docker-compose.yml`/`install-easypdm-docker.sh`**,
  **`install-easypdm-linux.sh`/`uninstall-easypdm-linux.sh`** and **`packaging/windows/`**
  (the `.exe` installer, Inno Setup) — three deployment paths without manually assembling
  the backend/frontend/database separately, see "How to run" below.
- **`.github/workflows/`** — seven CI workflows, all also runnable manually
  (`workflow_dispatch`) or via `gh workflow run <file>`:
  - `build.yml` — on every push/PR: backend build + integration tests
    (`EasyPDM.Api.Tests`, against a `postgres` service in CI) and frontend
    types/lint/build.
  - `build-windows-installer.yml` — builds `EasyPDM_Windows_v<version>.exe` (see above) and
    additionally **actually installs it** on a Windows runner (PostgreSQL via
    Chocolatey, `/VERYSILENT`), checking twice (fresh install + a simulated update)
    that the service starts and the server responds — the only way to check this
    without owning a physical/virtual Windows machine. Also declared as a reusable
    `workflow_call` (see `create-release-draft.yml` below).
  - `build-linux-package.yml` — builds `EasyPDM-Linux-x64_v<version>.tar.gz` (self-contained
    backend + built frontend + install/uninstall scripts + `db/schema.sql`) and actually
    installs it on a clean Ubuntu runner to verify the service starts. Also declared as a
    reusable `workflow_call`.
  - `test-linux-installer.yml` — actually runs `install-easypdm-linux.sh` on a clean
    Ubuntu (fresh install, "update", `uninstall-easypdm-linux.sh`), which the local
    development environment (no `sudo` password in this session) didn't allow doing.
  - `publish-docker-image.yml` — builds and publishes the `api` and `postgres` images
    (the latter with `db/schema.sql` baked in) to the GitHub Container Registry
    (`ghcr.io/pawelcel/easypdm-api`, `ghcr.io/pawelcel/easypdm-postgres`), tagged `:edge`
    (+ the commit SHA), on every push touching server code — for checking the latest
    state of `main` before cutting a release, see "Docker" below.
  - `publish-docker-release.yml` — same two images, but only on pushing a version tag
    (`v*`); this is the only workflow that updates `:latest` (what `docker-compose.yml`
    actually pulls), plus a matching `:vX.Y.Z` tag. See "Docker" below.
  - `create-release-draft.yml` — also on pushing a version tag (`v*`), independently of
    `publish-docker-release.yml`: first checks that `MyAppVersion` (`EasyPDM.iss`) and
    `APP_VERSION` (`version.ts`) actually match the tag (fails fast otherwise), then calls
    `build-windows-installer.yml`/`build-linux-package.yml` as reusable workflows and
    creates a **draft** GitHub Release with both artifacts attached and release notes
    extracted from the matching `## [X.Y]` section of `CHANGELOG.md`. Deliberately never
    publishes it automatically — someone still has to review the draft and click
    "Publish release".

### Data model — items and structure

Four item types (`item_type`): **Folder** (a plain container), **Part**/**Assembly**
(have a number from the global sequence, a status, a revision, and an owner), **Other
file** (any file with no structure underneath). The tree/BOM structure is a separate
`item_relations` table (`parent_id`, `child_id`, `quantity`, `position`) — this lets the
same Part/Assembly be a shared component in several assemblies/projects at once.

What's allowed as a child of what (enforced both in the backend and the frontend):

| Parent | Allowed children |
|---|---|
| Project / Folder | anything (Folder, Part, Assembly, File) |
| Assembly | only Part and Assembly (BOM) |
| Part / File | nothing — these are leaves of the structure |

Deleting an item has two modes: **"Remove from structure"** (detaches the relation /
hides the root, the record stays) and **"Delete completely"** (administrator only). The
second one recurses **only through Folders** — a Folder *owns* its contents, so deleting
it takes them along, while an Assembly only *uses* its components (the BOM relation means
"is part of", not "belongs to"), so deleting an Assembly deletes just that one record and
leaves every component in place, losing only that one relation. A Part/Assembly is a
first-class catalog entry (its own number, revisions, history, owner, attachments) and may
join any other assembly later, so it is never deleted as a side effect of deleting an
assembly it happened to be used in. Within a deleted Folder subtree, anything that also
has a parent outside it survives as well (see `survivors` in `ItemEndpoints.cs`).

Deleting completely also removes the item's **files from storage**, not just its rows.
`ON DELETE CASCADE` clears the database but never touches the disk, so the endpoint collects
every path *before* the `DELETE`: the item's own file, everything in `item_attachments` (CAD
file, drawing, PDF, STEP, preview picture, ordinary attachments) and the attachments hanging
off its client verifications — those live in their own table keyed by verification rather
than by item, which is exactly why they were missed at first and left behind with nothing
remaining to find them by. Deleting a Project does the same for `project_attachments`, after
the transaction commits, so a failed delete cannot take the files of a project that still
exists.

A Part/Assembly can also be **duplicated** (the copy
gets a new number, a fresh status and owner) — from the tree, the copy lands right
under the original.

A Project itself can also be deleted (`DELETE /api/projects/{id}`, administrator only)
— this does NOT delete its items: `project_id` is set to `NULL` on all of them (not
cascaded), so Parts/Assemblies survive with their files, attachments, tags, history and
BOM relations intact, reachable afterward only through "Whole database". This also
protects items shared into another project's BOM via `item_relations` — deleting the
owning project no longer breaks that other project's structure.

A Part has four **kinds** (`properties.rodzaj`), each with a different set of fields and
a different icon in the tree: **Manufactured** (Material, Price, Additional notes),
**Purchased** (Manufacturer, Series/Type, Subtype, Order number 1/2, Mass, Price, Additional
notes), **Standard** (Material, Norm, Additional notes), **Client-supplied** (Client,
Additional notes).

An Assembly has three kinds of its own in the same `properties.rodzaj`: **Wykonywane**
(manufactured), **Zakupowe** (purchased — Manufacturer, Series/Type, Subtype) and **Klienta**
(client-supplied — Client). The strings deliberately differ from the Part ones ("Zakupowe" vs
"Zakupowa"), because that value doubles as the numbering-prefix key — the one shared
string is "Klienta", which shares its prefix too. Beyond its kind's fields an Assembly
still has the generic property editor (Mass and any custom keys). Assemblies created
before this version have no kind and show a hint prompting you to pick one.

**Client** (`properties.client`, table `clients`) — for the Client-supplied kind, on a
Part or Assembly, picked from the Clients catalog (Clients tab) the same way as
Manufacturer/Material: linked by name, not a foreign key. Next to it, **Name 2**
(`properties.clientName2`) — one of that client's second names/trade variants from the
catalog (table `client_name2`, a 1:N relation to the client — one client can have several,
e.g. different subsidiaries trading under the same parent name — not a 1:1 column) —
locked until a client is picked; the option list offers every Name 2 that client has,
empty if it has none. Changing the client clears a previously chosen Name 2. The Clients
tab's own left-hand list reflects this directly — a flat Name/Name 2 table, one row per
Name 2 (a client with none gets a single row with a dash), so every variant is visible
without opening a client — with a delete button right on each row. The "Add client"
dialog's name field is itself a picker over the same catalog and is "dynamic": typing/
picking a name that already exists switches it from creating a duplicate client to adding
a new Name 2 to that existing one instead (confirmed with a single "OK" instead of
"Add") — this is what keeps one client (e.g. "Bosch") from fragmenting into several
near-duplicate catalog entries just to record different Name 2 variants.

Separately from `properties.client` above, the **Clients catalog** (`clients` table,
Clients tab) is its own first-class entity: name/location, its list of Name 2 entries
(`client_name2`), contact people (`client_contacts`), and its own document tree
(`client_nodes`) for e.g. norms or reference files, independent of `items`/`item_relations`.
A Project can optionally be linked to one (`projects.client_id`) — the client's detail
panel then lists every Project assigned to it (scoped to what the current user can
access), with a button to jump straight there. A Project can also optionally point at one
specific Name 2 of that client (`projects.client_name2_id`), picked right next to the
Client field in the project's own form — cleared when the client is changed, and set back
to null (not deleted) if that Name 2 itself is later removed. The project selector dropdown
and the project's own row at the top of its structure show "Project (Client, Name 2)" when
set, and everywhere the project list is sorted by client name, then Name 2, then the
project's own name.

A project can also point at a **Project lead** (`projects.lead_contact_id`) — one specific
person from that client's contact list (`client_contacts`), shown next to Client/Name 2 in
the project's own properties. Either a contact belonging to the client directly, or one
belonging to the exact Name 2 the project is linked to (never a contact of a *different*
Name 2 of the same client — enforced in `ProjectEndpoints.ValidateLeadContactAsync`, the
same "must actually belong together" check as `client_name2_id` itself). Cleared when the
client or the chosen Name 2 changes, and set back to null (not deleted) if that contact is
later removed.

A project also carries a `closed` flag, toggled by a button in its own properties. A closed
project drops out of the "active" lists (selector, "My projects", the new-item project
picker) but keeps existing exactly as before otherwise — its items remain fully searchable
through "Whole database", and the same button reopens it. `GET /api/projects` always
returns every project regardless of `closed`; each list decides for itself whether to filter
closed ones out (the project detail view, reached directly by id, deliberately does not, so
a closed project stays reachable and toggleable once you land on it).

**Series/Type** (`properties.productType`, table `manufacturer_product_types`) and
**Subtype** (`properties.productSubtype`, table `manufacturer_product_subtypes`, keyed to
the series) form a two-level catalog per manufacturer (Manufacturers tab). The link to an
item is by name only, like manufacturer and material, so deleting a catalog entry never
rewrites items that already reference it. The whole Manufacturer → Series/Type → Subtype
chain cascades both ways, but differently in the two places it shows up: on the item
properties form (`ProductTypeAndSubtypeFields`, property-fields.tsx) both fields are
ALWAYS visible, only disabled while the level above is empty (Series/Type with no
manufacturer, Subtype with no series) — deliberately, so nothing appears to vanish; in the
"Whole database" filters (`ProductTypeFilterSelect`/`ProductSubtypeFilterSelect`) the lower
filter appears only once the one above it is set. In both places, changing (or, in the
filters, clearing) a higher level clears/hides the lower ones. The subtype is optional — a
series with none simply offers an empty list.

A Part/Assembly has a state machine: `w_pracy → sprawdzany → (w_pracy | wydany) →
w_pracy` (in progress → under review → (in progress | released) → in progress), plus
`wydany → anulowana → w_pracy` (released → cancelled → in progress; going back from
`wydany`/released OR `anulowana`/cancelled bumps the revision number, with an optional
comment on the revision). `anulowana` is only ever reachable FROM `wydany` — an item must
have been released before it can turn out to be unneeded. An assembly with a cancelled
item anywhere in its BOM (recursively, at any nesting depth —
`FindCancelledDescendantLabelsAsync` in `ItemEndpoints.cs`, the same CTE pattern as
`BomEndpoints.FetchBomRowsAsync`) can't itself become `wydany`; `PATCH /status` rejects
that with a 400 naming the cancelled items, which lands directly in the frontend's status
confirmation dialog (`StatusControl`) with no separate dialog needed. `anulowana`, like
`wydany`, is always ownerless — `/lock`/`/release` reject both status values identically.
Outside of the `w_pracy`/in-progress status, editing the name/properties is locked —
exception: price/currency/price type are always editable. An item's icon in the tree/list
turns red for the `anulowana` status (`STATUS_ICON_COLOR` in `item-visuals.ts`). At the bottom of a
Part's/Assembly's properties panel you can see the **History**: when and who created
the item, every status change (when/who/from-to), every revision with its comment
(when/who/description), every attachment added/removed (when/who/file name), and every
owner lock/release (when/who), joined into one chronological list.

**Owner and lock** (`owner_id`/`owner_locked`) — independent of status. The creator of a
Part/Assembly immediately becomes its owner and the item is locked: while the lock
lasts, only the owner can edit it (properties, name, visibility, moving to another
project, attachments, the BOM structure underneath it) — **not even an administrator
bypasses this**. Anyone can lock a released item, becoming its new owner; only the
current owner can release it — **except an administrator, who can also take over
(`POST /lock`) or force-release (`POST /release`) a lock held by someone else, and can
change a locked item's status (`PATCH /status`) regardless of who owns it**, for cases
like a coworker being away. An item in the `wydany`/released status is always released
and has no owner — it cannot be locked. In the tree this is shown by a lock icon: green
(locked by you), yellow (by someone else), open (released).

An Assembly's BOM shows: position (editable by typing an integer — must be unique within
that BOM — or by dragging the row), Name, Quantity, Material, Norm, Manufacturer, Order
number 1/2 (missing fields shown as "-"), together with nested items (parts of nested
assemblies, position in the form `2.1`). CSV export in two variants: full (every
occurrence listed separately) and aggregated (the same component used several times in
different places — one row with the combined quantity, expanded through the whole
chain).

The reverse view is also available (`GET /api/items/{id}/used-in`) — every assembly, at
any depth and across projects, that contains a given item, shown on the item's own
detail panel above History.

Attachments (`item_attachments`) are a mechanism separate from the structure — any file
(e.g. CAD) can be attached to a Part/Assembly/File from the properties panel; they
cannot be added or removed through the tree on the left. From a Project/Assembly/Part
you can download **documentation** — a ZIP collected from all attachments within a given
scope (the whole project, or a given Assembly/Part together with its subtree), with a
choice of which file extensions to include.

An item's number (`item_number`) comes from a single, global PostgreSQL sequence —
deleting an item does NOT automatically free its number (standard sequence behavior).
An administrator can manually rewind the sequence to a given number (Settings →
Numbering) — this only works when no existing item already has that number or higher,
so it lets you reclaim the numbering "tail" left behind by deleted test items without
risking a collision.

What the user sees is that number dressed in three things, all stored **on the item** and
frozen when it is created: a letter prefix per kind (`item_number_prefix`, from
`item_number_prefixes`), a minimum width to zero-pad to (`item_number_digits`), and whether
the item's own name is appended in brackets at all (`item_number_with_name`) — the last two
from `system_state`. Changing any of these settings therefore affects only items created
afterwards. That is not conservatism for its own sake: the CAD macros build a
file's name out of this number, so the name lives on disk and inside `item_attachments`,
where nothing can rewrite it after the fact. `ItemNumbering.Label` composes the three
parts in one place and the API ships the result as `itemNumberLabel` next to
`itemNumber`/`itemNumberPrefix` — the same pattern as `revisionLabel`, so that the web
frontend and the three CAD macros never compose it themselves (and never disagree).

The one exception is a mistake caught early: changing a Part/Assembly's kind recomputes
the prefix for as long as the item has no attachment in any of the dedicated slots
(`preview_role` of `cad`, `drawing`, `pdf`, `step` or `image`). Those are the files whose names the
macros derive from the number; ordinary attachments keep their own names and block
nothing. Once a dedicated slot is filled, `PATCH /properties` REFUSES a kind change
outright rather than applying it with a stale prefix, and the item payload carries
`kindLocked` so the UI can grey the buttons out. Only a real change is refused — resending
the same kind passes. The number itself never changes.

The full name of an item — its **record name**, composed once by `ItemNumbering.RecordName`
and shipped as `recordName` — reads `prefix + padded number` immediately followed by the name
in brackets: `C0001(plate)`, or just `C0001` with the name switched off. The revision letter
and the extension are appended for a file on disk: `C0001(plate).A.sldprt`. Every macro's
name matching treats both the bracketed name and the space that older files were saved with
as optional, so it still recognizes files written under any earlier convention.

Because the name may be missing from the file name, both VBA macros also write it into a
document custom property, `EasyPDM_Name`, next to the link properties they already keep
there — that property is then the only place in the document where the name appears, and
drawing templates can pull it from there. Both also write `EasyPDM-Mass` and
`EasyPDM_Material`, and read them back onto the item's `mass`/`material` properties in a
single PATCH right after the save.

In SolidWorks neither property holds a value but an expression —
`"SW-Mass@@Default@<file name>"` and `"SW-Material@@Default@<file name>"` — which SolidWorks
resolves on rebuild/save, so both track the model by themselves; the macro reads the
*resolved* value. The surrounding double quotes are part of the value, not notation: without
them SolidWorks leaves the text alone and never evaluates it. When a value comes back still
looking like the expression (an unresolved one, e.g. a Part with no material assigned), it is
logged and dropped rather than sent — that is how `SW-Material@@Default@C0014.A.SLDPRT` once
reached an item's material field. Inventor has no equivalent expression, so there both
properties hold a snapshot read from `ComponentDefinition` at upload time and need a re-upload
to refresh.

Drawings are skipped entirely, material is written for Parts only (an Assembly has none of
its own), and a mass that is empty or not a plain number is logged and skipped — it is
normalized to digits and one decimal point first, since it can arrive with a unit and a
locale decimal comma.

Before any of that, when the macro opens the browser to create a new item it passes the
document's material along in the deep link (`&material=`), read straight from the CAD API
rather than from `EasyPDM_Material` — at that point nothing has been saved yet, so the
expression does not exist. `pending-create-ticket.ts` picks it up and `AddNodeDialog` shows it
in the Material field, so the value is visible while the item is being created instead of
appearing by itself right after the upload. A duplicate keeps the source item's properties:
that choice was explicit.

That field is then **read-only** (`materialLocked`, handed down to `MaterialField`): the macro
writes the material onto the item from `EasyPDM_Material` right after the upload anyway, so a
choice made here would be overwritten moments later — offering one was the confusing part. The
deliberate change is made on the item once it exists, where the field is editable as usual. A
duplicate's material stays editable, because it was copied from an item someone picked. The
locked variant is a plain disabled `Input`, not a disabled `Combobox`: the material may not be
in the catalog yet when the dialog opens, and a `Combobox` shows nothing for a value outside
its list.

A material the catalog does not know yet is inserted by `MaterialCatalog`
(`INSERT … ON CONFLICT (name) DO NOTHING`, so two macros sending the same one concurrently
cannot collide), called from **both** paths that can carry one: `PATCH /properties` (the macro
after an upload) and `POST /nodes` (creation, where the material comes from the dialog the
macro pre-filled). While only `PATCH` did it, an item created with a CAD material carried a
name the catalog did not have until the upload landed. The macros read the material off the
document rather than from a list, so without this the item would carry a material that could
neither be picked again nor used as a filter. Only the name is created; group and subgroup
stay empty.

### The model preview is a picture, not a rendered STEP

The preview box above an item's properties shows a **PNG screenshot** that the CAD macro takes
at upload time and sends as an attachment with `preview_role = 'image'`. The STEP file is still
exported and uploaded exactly as before (`preview_role = 'step'`) — it is there to be
downloaded; it simply no longer drives the display.

It used to. The browser fetched the STEP, parsed it with `occt-import-js` (OpenCascade compiled
to WebAssembly), tessellated every surface, ran `THREE.EdgesGeometry` over each resulting solid
and rendered the lot with three.js. All of that produced a **still image**: one
`renderer.render()`, no animation loop, no orbit controls, nothing to drag. The cost was paid on
every item a user opened, by every user, and it did not scale with the STEP file's size but with
the geometry's complexity — a 40 kB file full of fillets and splines tessellates into hundreds of
thousands of triangles. Dropping it removed **7.8 MB** from the published bundle, 7.6 MB of which
was the OpenCascade `.wasm` binary that every browser downloaded and compiled.

Taking the picture needs the document to be **active**: saving an image captures the active
viewport rather than the document the call names, and while an assembly is being sent the active
document is the assembly — so every component was handed a picture of the assembly it came from.
SolidWorks and Inventor therefore activate the document, capture, and activate the previous one
again; FreeCAD can take a named document's view without switching anything, so nothing moves on
screen there.

Two consequences follow from where the screenshot comes from:

- **No STEP, no picture.** The macro takes the screenshot inside its STEP-upload step, so
  clearing the "export STEP" checkbox leaves the item without a preview, and the box says so
  rather than showing an empty frame.
- **Deleting the STEP deletes the screenshot.** It exists only to depict that model, so
  `DELETE /api/attachments/{id}` on a `step` attachment also removes the item's `image` one
  (`DeleteRoleAttachmentsAsync`). Without that, the store would accumulate pictures nothing
  displays and nothing can be traced back to a model.

`image` is single-slot like `pdf`/`step` (not accumulating like `cad`/`drawing`): a screenshot
shows the model's current shape, so a new upload replaces the previous one.

Items uploaded before this change keep their STEP and lose the preview until they are sent up
again — there is no renderer left to fall back to, which was a deliberate choice: keeping one
would have meant keeping the 7.6 MB dependency for everyone.

STEP/IGES/STL are therefore no longer previewable anywhere in the app, including the attachment
preview dialog; they get a download button. `previewKindOf` now recognizes PDFs and raster
images only.

### Asking the browser for a form without opening a tab

Sending an assembly needs a form for every new component. The macro used to open a tab for
each one, preceded by a native message box — not to confirm anything, but because Windows'
foreground-stealing protection lets only the **first** programmatic browser-open of a run take
focus and opens every later one silently in the background. Without a click in between, the
form appeared in a tab nobody could see while `WaitForTicket` polled for input that could not
be given. On an assembly with forty parts that was forty clicks and forty tabs.

The browser is already open and already polling, so no new tab is needed. The macro leaves its
request in `CadRequestStore` (in memory, keyed by user, same reasoning as `CreateTicketStore`)
and the open tab picks it up through `use-cad-requests.ts`, handing it to the very same
`PendingTicketBanner` and `AddNodeDialog` that a URL-borne ticket feeds. Nothing about the form
itself changed.

A request is addressed to **one run of the macro**, not to the account. The CAD program
generates a run id, passes it to the browser in the deep link, and the tab keeps it in
`sessionStorage` — which survives a reload but reaches no other tab and no other machine. A tab
claims a request only when the id matches. Keying by user alone assumed one run per person, and
that stops being true the moment the same account is signed in to a browser on a second
computer: a request from one computer was handed to the tab on the other, which showed the form
to someone else entirely. That happened in practice, which is why the id exists.

It is also why the **first** component of a run still opens a tab: that open is how a tab on
this machine learns the run id, and until one has, the macro has nobody to hand a request to.
That first open no longer shows a message box, because the first browser-open of a run takes
focus by itself — the Windows rule above only bites from the second onwards. A normal run is
therefore one tab and no clicks, against one tab and one click per component before.

Once a tab has claimed a request, the macro calls `AppActivate "EasyPDM"` to hand focus back.
The CAD program comes forward in between for a good reason — for the previous component it saved
the file, exported the STEP and redrew the graphics window for the screenshot — but the next
thing needed is the form. It works because Windows lets the application that *currently* holds
focus give it away: the same rule that forced the clicks, used in the other direction. The match
is on the start of the window title, so nothing happens when EasyPDM sits in a background tab,
and a failure is swallowed, being a convenience rather than part of the upload.

The one thing that cannot be assumed is that a browser is watching at all — it may be closed,
or the server unreachable. So the macro publishes, then polls `GET /api/cad-requests/taken` for
a few seconds; the tab marks the request taken the moment it claims it. No signal means nobody
is there, and the macro falls back to the old path — message box, new tab — rather than waiting
on a form nobody will see. That fallback is where the message box still lives — ahead of the
tab it opens, and only from the second component onwards, where the focus rule applies.

`taken` is returned as a flat `1`/`0` rather than a boolean inside a nested object, because the
JSON parsers inside the VBA macros only have `JsonGetString` and `JsonGetLong`; a flat number is
the only shape they can read without being given a parser.

### The transfer progress list

While a CAD macro uploads or downloads, the app shows a list of the files involved on the right,
ticking them off as they go. The state lives in `TransferProgressStore` — in memory, no table,
the same choice and the same reasoning as `CreateTicketStore`: it is worth seconds to minutes,
nobody needs it afterwards, and a restart losing it costs nothing, because progress is
information about the work rather than part of it.

It is keyed by **user**, not by a session id. The browser therefore asks plainly "what is my
macro doing?" (`GET /api/progress`) without having to learn an identifier from anywhere, which
also works when the tab was opened before the macro ran. One run per person at a time is a safe
assumption — nobody clicks Upload in two CAD programs at once — and a new run simply replaces
the old one.

The macro declares the **whole list up front** and then marks entries off. Without the complete
list from the start the counter would lie: "3 of 3" would turn into "3 of 9" as another level of
the tree appeared.

- **Uploading** already knows the list: `DiscoverComponentTree` walks the assembly before the
  first file is sent, and the order is leaves-first because a parent cannot be given a BOM
  relation to a child that does not exist in the PDM yet.
- **Downloading** did not. `DownloadChildrenRecursive` descends level by level and at the start
  has no idea how many files there will be, so `GET /items/{id}/descendants` was added: one
  recursive query returning the item and its whole subtree. Its de-duplication matches the
  macro's own `seen` set, so a part used in several assemblies is counted once and the counter
  reaches its end. The list is ordered parent-first because that is the order the macro actually
  downloads in; sorting it any other way would make entries tick off out of sequence. For
  downloading, order has no bearing on correctness anyway — every file lands on disk before the
  top document is opened.

**Progress reporting must never break a transfer.** All of it goes through a private, silent HTTP
path in each macro rather than through `ApiPostJson`/`api_post_json`, which raise on failure. A
missing connection, a restarted server or an older server without these endpoints has no business
stopping the thing the user actually asked for. On an unknown key the server answers
`matched: false` rather than an error, for the same reason.

The browser polls every 1.5 s with a plain `setInterval`, the pattern already proven by
`use-notifications.ts`. An earlier attempt picked the interval dynamically through a `setTimeout`
that rescheduled itself; it was measurably fragile — one remount broke the chain and it never
resumed — and the saving was not worth it, since a poll is a dictionary lookup and a few dozen
bytes, never touching the database. A finished run stops being served after two minutes, so a
list from an hour ago does not greet whoever opens the app next.

### The run reports itself in notifications

When a run ends, the server composes a report and leaves it in the bell
(`cad_transfer_finished`). The macros used to end with a blocking message box — "uploaded as
item #X" — which was right while the person was still sitting in the CAD program. Since focus
is handed back to the browser after every component, that window is now made by a program in
the background, so it opens *behind* the browser and hangs there waiting for a click nobody
can see. The report therefore goes where the person is already looking, and unlike a window
it stays to be found later.

It is composed in `POST /api/progress/finish`, from the run the progress store already holds
— not in the macros. That is the same choice as counting progress on the server: one place for
three CAD programs, so the report reads the same whichever one sent it, and a macro needs no
code for it beyond the `finish` call it already made.

`Finish` returns the run **only on the first call** that ends it. A macro can legitimately call
`finish` twice — once on an error path, once on the normal one — and two identical reports in
the bell would be noise.

The data holds counts and the list: `kind`, `total`, `done`, `skipped`, `failed`, `pending`,
and up to forty entries with their labels. `done` deliberately excludes `skipped`: a skipped
component is one that was already in the PDM and did not need sending, so counting it as sent
would overstate the report. `pending` is separate from `failed` because an interrupted run did
not break anything, it simply never got there. The bell shows eight of those entries, failures
first — with twenty files and one failure, showing the first eight in list order would leave
the only entry that matters out of the frame.

The bell polls every 30 s, which is far too slow for something the person is watching happen,
so the progress panel fires a window event the moment the server reports the run finished and
`use-notifications` refetches on it. The panel also sits above dialogs at `z-60`, so the bell's
own dropdown had to go above that — otherwise the panel covered exactly the notification it
had just announced.

One window is kept on purpose: components already linked to a PDM item whose status is "in
review" or "released". The macro never updates those, so a local change to one of them did not
go up — and the file list cannot say so, because from its point of view nothing happened.

### Login, roles, and project access

Every request to `/api/*` (except `/api/auth/login`) requires being logged in — a
session is a random token in an httpOnly cookie (`pdm_session`, 30-day validity), stored
in the `sessions` table. Passwords are stored as PBKDF2 (a custom implementation in
`PasswordHasher.cs`, using only `System.Security.Cryptography` — no extra NuGet
packages).

Two roles (`users.role`): **administrator** (full access, sees all projects) and
**user** (access only to the projects they've been assigned — `project_users`, managed
under Settings → Users; a project they're not assigned to is invisible to them in the
list and has no structure). A regular user can detach items from the structure, but
cannot delete them completely from the database or manage accounts. The system makes
sure at least one administrator always remains (the last one cannot be deleted or
demoted). Language and Appearance settings are available to everyone; Users, File
storage, and Logs only to the administrator.

If the `users` table is empty when the API starts, it sets up a default
**`admin` / `admin`** account on its own (see the console on first run) — change this
password right after logging in (`PATCH /api/auth/password`, or from within the web
application).

### Notifications

Notifications (`notifications`/`notification_preferences` tables) are addressed to a
specific user and fired for ten event types: an owned item entering review/released/
reverted to in-progress (`status_review`/`status_released`/`status_regressed`), a new
revision (`new_revision`), being assigned to or removed from a project
(`project_assigned`/`project_unassigned`), an assigned project being deleted
(`project_deleted`), your password being changed by an admin (`password_changed`), low
disk space on the storage (`low_disk_space`, administrators only), and the one-time
sample-project notice (`sample_project`, fired when a genuinely empty database seeds one
demo project/assembly/two parts on first startup — gated by
`system_state.sample_project_seeded`, so it never re-fires, even after a manual Danger
Zone wipe). Each type can be individually disabled per user (Settings → Notifications);
a notification can be marked read or deleted (`DELETE /api/notifications/{id}`).

### API endpoints

| Method | Path | What it does |
|---|---|---|
| POST | `/api/auth/login` \| `/logout` | login / logout — login is the only endpoint that doesn't require a session |
| GET/PATCH | `/api/auth/me` \| `/password` | the logged-in user's data / changing YOUR OWN password |
| POST | `/api/auth/browser-bridge-ticket` | mints a one-time, short-lived login-bridge ticket for the caller's own session |
| GET | `/api/auth/browser-login` | exchanges a bridge ticket (not the raw session token) for a browser cookie, for CAD macros (opens the browser already logged in) |
| GET/POST/PATCH/DELETE | `/api/users[/{id}]` | account management — **administrator only** |
| GET/POST/PATCH/DELETE | `/api/projects[/{id}]` | list/create/edit/delete a project (writes — administrator only; list filtered by access) |
| GET/POST/DELETE | `/api/project-users`, `/api/projects/{projectId}/users/{userId}` | managing user-to-project assignments — **administrator only** |
| GET | `/api/items?search=&tag=&projectId=` | filtered item list (filtered by project access) |
| GET | `/api/items/{id}` | item details |
| GET | `/api/items/by-number/{itemNumber}` | item details by item number instead of guid — used by the SolidWorks macro to resolve a Drawing (.SLDDRW) to the Part/Assembly it documents |
| POST | `/api/projects/{projectId}/nodes` | creates a Folder/Part/Assembly/File without an upload (optionally with a ticket for a CAD macro) |
| POST | `/api/projects/{projectId}/items` | **multipart/form-data**: file upload (optional `parentId`) |
| GET | `/api/items/{id}/file` | download the uploaded file |
| POST | `/api/items/{id}/duplicate` | duplicates a Part/Assembly (new number, status, owner) |
| PATCH | `/api/items/{id}/name` \| `/visibility` \| `/status` \| `/project` | rename / change tree visibility / change status / move to another project. An Assembly moving to `sprawdzany`/`wydany` is refused while its DIRECT BOM children are behind; `promoteChildren: true` moves them and the assembly in one transaction |
| GET | `/api/items/{id}/status-precheck?target=` | what blocks that status change: sub-assemblies to handle separately, components that cannot be touched (cancelled / owner-locked / inaccessible project), and components that could be moved along. Read-only — `PATCH /status` enforces the same rule independently |
| POST | `/api/items/{id}/lock` \| `/release` | lock (take ownership) / release an item |
| DELETE | `/api/items/{id}` | complete deletion (recurses through Folders only — an Assembly's components are never deleted with it) — **administrator only** |
| GET | `/api/projects/{projectId}/relations` | parent-child relations (structure/BOM) of a given project |
| POST/DELETE | `/api/items/{parentId}/children[/{childId}]` | add/detach a child item |
| PATCH | `/api/items/{parentId}/children/{childId}/position` \| `/reorder` | change BOM position (a single position or the whole new order) |
| PATCH | `/api/projects/{projectId}/roots/reorder` | change the order of a project's tree roots |
| GET | `/api/items/{id}/bom` \| `/bom/csv` \| `/bom/aggregated-csv` | nested BOM (JSON) / CSV export (full / aggregated) |
| GET | `/api/items/{id}/used-in` | every assembly, at any depth, that contains this item — inverse of BOM |
| GET | `/api/items/{id}/documentation/extensions`, `/documentation` | file extensions available to download / a ZIP with attachments (item + subtree) |
| GET | `/api/projects/{projectId}/documentation/extensions`, `/documentation` | the same, for a whole project |
| GET | `/api/tags` | tag list |
| POST/DELETE | `/api/items/{id}/tags[/{tagName}]` | tag management |
| PATCH/DELETE | `/api/items/{id}/properties[/{key}]` | property management (locked outside the `w_pracy`/in-progress status and while owner-locked — exception: price fields) |
| GET | `/api/items/{id}/revisions` | revision comment history (only revisions with a comment) |
| GET | `/api/items/{id}/history` | full history: creation, status changes, revisions, attachment added/removed, owner lock/release (when/who/description), chronologically |
| GET/POST/PATCH/DELETE | `/api/materials[/{id}]` | material catalog (name + group/subgroup) |
| GET/POST/PATCH/DELETE | `/api/manufacturers[/{id}]`, `/api/manufacturers/{id}/contacts[/{contactId}]`, `/api/manufacturers/{id}/product-types[/{typeId}][/subtypes[/{subtypeId}]]` | manufacturer catalog + contact people + series/types and their subtypes |
| GET/POST/PATCH/DELETE | `/api/clients[/{id}]`, `/api/clients/{id}/contacts[/{contactId}]`, `/api/clients/{id}/name2[/{name2Id}]` | client catalog + contact people + their Name 2 list |
| GET/POST/PATCH/DELETE | `/api/clients/{id}/nodes[/{nodeId}]`, `/nodes/folder`, `/nodes/file`, `/nodes/{nodeId}/file`, `/nodes/search` | a client's own document tree (folders/files — upload/download/rename/delete/search) |
| GET/POST/DELETE | `/api/items/{itemId}/attachments[/{id}]`, `/register`, `/api/attachments/{id}/file` | attachments (upload/register an existing file/list/download/delete) |
| GET/POST/DELETE | `/api/saved-filters[/{id}]` | saved filter sets for the "Whole database" view (private per user) |
| GET/POST/DELETE | `/api/notifications[/{id}]`, `/{id}/read`, `/read-all` | notification list / mark read (one or all) / delete — for the logged-in user |
| GET/PATCH | `/api/notification-preferences` | per-type notification opt-out for the logged-in user |
| GET/POST | `/api/create-tickets/{ticket}`, `/attach-existing` | CAD macro ↔ browser correlation (see `EasyPDM.FreeCad/README.md`) |
| GET/POST | `/api/drawing-tickets/{ticket}`, `/resolve` | SolidWorks macro ↔ browser correlation for "which item does this Drawing belong to" when its views reference more than one already-linked item |
| GET | `/api/config` | file storage location (used e.g. by the FreeCAD macro) |
| GET/POST | `/api/settings/storage`, `/storage/move`, `/backup`, `/restore` | storage location/stats, moving it, backup (pg_dump + files in a ZIP), restore from backup — **administrator only** |
| GET/PATCH | `/api/settings/backup-schedule` | automatic backup schedule (enable/disable, frequency, day, time, number of kept copies) — **administrator only** |
| GET/PATCH | `/api/settings/item-number-prefixes[/{rodzaj}]` | item number letter prefixes per kind (the 4 Part kinds plus `Zlozenie` = manufactured assembly; purchased/client assemblies reuse the Part kind's prefix) — **administrator only** |
| GET/PATCH | `/api/settings/item-number-format` | number format for items created from now on: `digits` (minimum width to zero-pad to, 0 = no padding) and `withName` (whether the item's name is appended in brackets). Both optional on PATCH and saved independently, since they are two separate sections in the UI; frozen onto each item as it is created — **administrator only** |
| GET/POST | `/api/settings/item-number-sequence`, `/reset` | preview/rewind the item number sequence — **administrator only** |
| GET | `/api/settings/logs`, `/logs/{date}`, `/logs/{date}/download` | list of days with a saved log, the last N lines of a given day, download of the full file — **administrator only** |

## How to run

The backend reads the real access data (database password, storage path) from
`EasyPDM.Api/appsettings.Local.json` — **this file is NOT in the repository**
(gitignored, because it contains the password), so on a fresh clone you need to create
it from the template:

```bash
cp EasyPDM.Api/appsettings.Local.json.example EasyPDM.Api/appsettings.Local.json
# ...and fill in the real ConnectionString/StorageRoot for this machine.
```

The program **applies new database migrations on its own on every startup** (built
into the executable as embedded resources, tracked in the `schema_migrations` table —
see `MigrationRunner.cs`) — so on an already existing, known database, simply running it
is enough, no need to manually chase down `db/migrations/`. The only case where you need
to do something manually is a completely **fresh, empty** PostgreSQL — then first:

```bash
# If the role/database doesn't exist yet (fresh PostgreSQL):
sudo -u postgres psql -c "CREATE ROLE pdm_user LOGIN PASSWORD 'your-password';"
sudo -u postgres createdb -O pdm_user pdm

# ...and the base schema (from this point the program catches up on the rest itself):
psql -h localhost -U pdm_user -d pdm -f db/schema.sql

# Backend (also serves the built frontend from wwwroot/)
cd EasyPDM.Api
dotnet restore && dotnet build && dotnet run
```

Frontend — for UI work with live preview (proxying `/api` → `http://localhost:5000`):

```bash
cd EasyPDM.Web
npm install
npm run dev      # http://localhost:5173
```

For deployment: `npm run build` in `EasyPDM.Web/` overwrites `EasyPDM.Api/wwwroot/` —
`dotnet run` serves the result at `http://localhost:5000` with no extra configuration.

### Docker (recommended for server deployment)

**Simplest**: `./install-easypdm-docker.sh` — creates `.env` (generates a random
database password if you don't supply your own), picks a FREE host port on its own
(tries from 5000 upward — useful on a server where other services may already be
holding onto ports, which in practice is a common case), builds and starts the
containers. Run the same script again after `git pull` to update — it detects an
existing `.env` and doesn't overwrite anything in it.

Or manually:

```bash
cp .env.example .env      # set a real PDM_DB_PASSWORD
docker compose up -d --build
```

Starts two containers: `postgres` (the `postgres:18` image, data on the `pgdata`
volume, the `db/schema.sql` schema created automatically on an empty volume) and `api`
(built from the `Dockerfile` at the repo root — builds the frontend, publishes the
backend, additionally installs `postgresql-client-18` for the backup/restore feature in
Settings). File storage, automatic backups, and logs are kept on the `pdm-data` volume
(`/data` in the container) — they survive an image rebuild during an update. After
starting: `http://localhost:5000`. If port 5000 is already taken on this machine, set
`PDM_HOST_PORT=other_port` in `.env` (NOT via `docker-compose.override.yml` — Compose
CONCATENATES list values like `ports` between files instead of replacing them, so an
override with a different port would still try to bind both at once and fail on the one
already taken).

**Update**: `git pull && docker compose up -d --build` — the new `api` image gets the
new code, the container is recreated, and migrations apply automatically on startup, as
above — nothing else needs to be done manually. The `docker-entrypoint-initdb.d` step
with `schema.sql` only runs on the FIRST, completely empty start of the `pgdata` volume
(fresh install); on an update it's not touched at all, since the volume already exists.

#### Deployment WITHOUT cloning the repo (image only)

Two workflows publish ready-made images to the GitHub Container Registry —
`ghcr.io/pawelcel/easypdm-api` and `ghcr.io/pawelcel/easypdm-postgres` (the latter is a
plain `postgres:18` with `db/schema.sql` baked in — without it a fresh database would
stay empty, since `MigrationRunner.cs` deliberately does not create the base schema
itself):

- `publish-docker-image.yml` — on every push to `main` touching server code, tags both
  images `:edge` (+ the commit SHA). This is for checking the latest state of `main`
  before cutting a release (`docker pull ghcr.io/pawelcel/easypdm-api:edge`) — it never
  touches `:latest`. To actually run the `:edge` images with `docker-compose.yml`
  (which otherwise pulls `:latest`), layer `docker-compose.edge.yml` on top:
  `docker compose -f docker-compose.yml -f docker-compose.edge.yml pull && docker
  compose -f docker-compose.yml -f docker-compose.edge.yml up -d`.
- `publish-docker-release.yml` — only on pushing a version tag (`v0.1.2`, matching
  `EasyPDM.Web/src/version.ts` and `packaging/windows/EasyPDM.iss`'s `MyAppVersion`),
  tags both images `:latest` AND `:v0.1.2`. This is the ONLY workflow that moves
  `:latest` — so `docker-compose.yml` (which pulls `:latest`) always gets a deliberately
  released version, never an arbitrary commit on `main`. To cut a release:
  ```bash
  git tag v0.1.2
  git push origin v0.1.2
  ```

So deployment alone does NOT require cloning the whole repo (with all the CAD
macros/installers/tests the server doesn't need at all). Just two files are enough:

```bash
mkdir easypdm-deploy && cd easypdm-deploy
curl -O https://raw.githubusercontent.com/pawelcel/EasyPDM/main/docker-compose.yml
curl -O https://raw.githubusercontent.com/pawelcel/EasyPDM/main/.env.example
cp .env.example .env      # set a real PDM_DB_PASSWORD
docker compose pull
docker compose up -d
```

> As long as the repo (and the GHCR package) is private, `curl` above and
> `docker compose pull` require authentication — `curl` with an `Authorization: Bearer
> <token>` header, and before `docker compose pull` also `docker login ghcr.io -u
> <login> -p <token>` (a token with the `read:packages` permission). Once the
> repo/image is made public, no login will be needed anymore.
>
> **One-time, after the first publish**: EVERY package in GHCR defaults to PRIVATE
> regardless of the repo's own visibility — you need to switch it to public manually
> once, for BOTH packages (GitHub → the repo's **Packages** tab → `easypdm-api` /
> `easypdm-postgres` → **Package settings** → **Change visibility**), otherwise
> `docker compose pull` without a prior `docker login` gets a 403/404 even on a public
> repo.

**Update** this way: `docker compose pull && docker compose up -d` — no `git pull`
(there's nothing to pull, you don't have the repo here), it simply fetches whatever
`:latest` currently points to — i.e. the newest tagged release, not necessarily the
newest commit on `main`.

### Linux — native installation as a systemd service (no Docker)

```bash
sudo ./install-easypdm-linux.sh
```

One script: installs PostgreSQL if it isn't there yet (recognizes `pacman`/`apt`/`dnf`
— on Arch/CachyOS it additionally initializes the cluster itself, since that package,
unlike Debian's/Fedora's, doesn't do it automatically), creates the `pdm` role and
database (generates a random password if you don't supply your own via
`PDM_DB_PASSWORD=... sudo -E ./install-easypdm-linux.sh`), builds the frontend and
publishes the backend as a **self-contained single executable file**
(`dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true` — the finished
service no longer requires .NET to be installed, only at build time), creates a
dedicated, unprivileged system account `easypdm`, and installs a systemd service
(`easypdm.service`, autostart, `ProtectSystem=strict` + `ReadWritePaths` limited to
`/var/lib/easypdm` — the service cannot write anywhere else in the system). After
installation: `http://localhost:5000`, status via `systemctl status easypdm`, live logs
via `journalctl -u easypdm -f` (independent of the application's own log under Settings
→ Logs). Uninstalling: `sudo ./uninstall-easypdm-linux.sh` (deliberately does NOT touch
the database itself or PostgreSQL — that's a decision made manually, so data isn't
deleted by accident).

**Update**: `git pull`, then `sudo ./install-easypdm-linux.sh` again — it detects the
existing database/account (skips creating them), rebuilds and replaces only the
application, and explicitly **restarts the service** (`systemctl restart`, not just
`enable --now`, which would do nothing on an already-running service). The program
applies new database migrations on its own automatically on startup — nothing extra
needs to be done manually.

> The script builds from this repository's sources (like `run.sh`, only as a persistent
> service instead of a foreground process) — there isn't (yet) a separate, ready-made
> binary release to download. The self-contained published executable itself was
> actually run and checked (serves the frontend, logs), and the systemd unit's content
> was verified with `systemd-analyze verify`; the full script run (creating the
> role/database/system account via `sudo`) hasn't been executed end-to-end yet — watch
> the output on first run and report anything that doesn't work.

#### Ready-made package (no local .NET SDK/Node.js needed)

`.github/workflows/build-linux-package.yml` builds the frontend + self-contained backend
on a GitHub Ubuntu runner and packages them together with
`install-easypdm-linux.sh`/`uninstall-easypdm-linux.sh`/`db/schema.sql` into one
downloadable `EasyPDM-Linux-x64_v<version>.tar.gz` artifact (the version number comes
from `MyAppVersion` in `packaging/windows/EasyPDM.iss`, the one place in the repo that
tracks it) — rebuilt automatically on every push touching the backend/frontend/installer
scripts, same trigger pattern as `build-windows-installer.yml` below. The target machine
then needs only `sudo`, no `.NET SDK` or `Node.js` at all:

```bash
tar xzf EasyPDM-Linux-x64_v<version>.tar.gz
cd EasyPDM-Linux-x64_v<version>   # whatever directory you extracted into
sudo ./install-easypdm-linux.sh
```

`install-easypdm-linux.sh` detects the already-built `publish/` folder shipped inside
the archive (`PACKAGE_MODE=1`) and skips the build step entirely — everything else
(PostgreSQL setup, systemd service, updates by re-running the script from a newer
package) works exactly as described above for the git-checkout path. The workflow also
runs this exact install → verify → uninstall sequence for real on a clean Ubuntu runner
(mirroring `test-linux-installer.yml`, which only covers the build-from-checkout path),
so the packaged path is end-to-end tested too, not just the scripted one.

Download the artifact via `gh run download <id> -n EasyPDM-Linux-x64_v<version>` or from
the workflow run's page in the Actions tab — same as `EasyPDM_Windows_v<version>.exe`
below, this only happens automatically when run manually; pushing a version tag instead
attaches it to a draft GitHub Release automatically (see the note under "Windows —
installer").

### Windows — installer (`.exe`, Inno Setup)

**Simplest: `.github/workflows/build-windows-installer.yml`** builds a ready
`EasyPDM_Windows_v<version>.exe` (version from `MyAppVersion`/`OutputBaseFilename` in
`packaging/windows/EasyPDM.iss`) automatically on a GitHub Windows runner (which has the
Inno Setup Compiler preinstalled) on every push touching the backend/frontend/installer —
no need to have Windows or Inno Setup locally. Run it manually via `gh workflow run
build-windows-installer.yml`, wait (`gh run watch`), download the artifact (`gh run
download <id> -n EasyPDM_Windows_v<version>`).

> Run manually (`workflow_dispatch`, e.g. after a plain push to `main`), this workflow
> only uploads `EasyPDM_Windows_v<version>.exe` as a GitHub Actions run artifact
> (`actions/upload-artifact`) — it does **not** touch the repository's Releases page.
> Pushing a version tag (`vX.Y.Z`) is different: `create-release-draft.yml` calls this
> workflow (and `build-linux-package.yml`) and attaches both artifacts to a **draft**
> GitHub Release automatically — see the workflow list above. Publishing that draft
> (reviewing it, then clicking "Publish release") is still a manual, deliberate step.

Alternatively, to build locally on a Windows machine (.NET 10 SDK + Node.js +
[Inno Setup Compiler](https://jrsoftware.org/isinfo.php)):

```powershell
powershell -ExecutionPolicy Bypass -File packaging\windows\build.ps1
iscc packaging\windows\EasyPDM.iss
```

Produces `packaging\windows\Output\EasyPDM_Windows_v<version>.exe`. The installer: checks whether
PostgreSQL is already installed (if not — points to the download page and stops,
deliberately does NOT try to silently install a several-hundred-megabyte PostgreSQL
installer in the background), asks for the `postgres` superuser password (once, to
create its own `pdm_user` role and `pdm` database — the password itself is never
stored anywhere), sets up the schema, writes `appsettings.Production.json` with the
rest of the settings (storage/backups/logs in `%ProgramData%\EasyPDM`), registers
`EasyPDM.Api.exe` as a **Windows service** (autostart, runs in the background with no
console window), and creates a shortcut that opens `http://localhost:5000`.
Uninstalling stops and removes the service (the standard Inno Setup uninstaller) — same
as on Linux, it deliberately doesn't touch the database itself.

**Update**: build a new `EasyPDM_Windows_v<version>.exe` (as above) and run it again. The
existing installation is detected via the fixed `AppId` (its Uninstall key in the registry),
so Inno replaces it IN PLACE rather than installing alongside it. An update **does not ask
for the `postgres` superuser password** — the installer reads the `pdm_user` role's password
out of the previous installation's `appsettings.Production.json` and leaves the role and the
database alone entirely, so the role's password STAYS as it was (nothing else connecting to
that database — backup scripts, pgAdmin — breaks). `PrepareToInstall` stops the service
BEFORE replacing the files (otherwise Windows would block overwriting a running `.exe`) and
starts it back up instead of registering it again. The program applies new database
migrations itself on startup, and settings changed inside the application (e.g. the file
storage location) survive the update — they live in `appsettings.Local.json`, while the
installer only writes `Production.json`.

Installing an **older** version over a newer one is refused with a message: database
migrations only ever move forward, so an older build could not read the already-migrated
schema.

> The `.iss` script actually compiles (verified with a real Inno Setup Compiler in CI,
> not just by code review) — 5 real bugs specific to Inno Setup's Pascal Script dialect
> were caught and fixed along the way (among others: no local `const` sections in
> functions, `LoadStringFromFile` requiring `AnsiString`, no `Randomize`/`RandSeed`/
> `GetTickCount` — there is no documented way to manually seed the built-in `Random`, so
> it's used as-is). The actual end-to-end installation on a live machine with
> PostgreSQL hasn't been manually tested yet — watch the process on first run and
> report anything that doesn't work.

First login: **`admin` / `admin`** (the account is created automatically if the `users`
table is empty — see "Login, roles, and project access" above). Change this password
right after logging in.

## Known limitations

1. **No validation of uploaded file/attachment size or type** — any file goes through,
   regardless of extension or size.
2. **File storage (`storage/`) is a plain folder on the server's disk.** Backup/restore
   from Settings packs a `pg_dump` of the database together with the file storage into
   one ZIP; it can be downloaded manually, or you can enable an automatic backup
   (Settings -> File storage -> Automatic backup) with a choice of frequency
   (daily/weekly/monthly) plus day and time — checked every minute by the
   `ScheduledBackupService` in the background, saved to a separate `backups/` directory
   (independent of `storage/`, so a backup doesn't pack itself), with a configurable
   number of kept recent copies (14 by default — older ones are automatically deleted).
   File versioning on a revision change only works today in the FreeCAD macro flow
   (`storage/components/`, one file per revision, see `EasyPDM.FreeCad/README.md`) —
   plain attachments added from the web application have no automatic link to the
   revision number.
3. **Not every operation records "who did it"** — creating an item (`created_by`), a
   status change, a revision comment, adding/removing an attachment, and owner
   lock/release already do (visible in the "History"), but e.g. changing
   properties/name/tags does not record the author.
4. **In Docker, "Change location" for file storage (Settings -> File storage) does not
   survive an image rebuild** — this operation writes the new path into
   `appsettings.json` inside the `api` container (outside the `pdm-data` volume), so
   after `docker compose up --build` it reverts to the value from the `StorageRoot`
   environment variable set in the `Dockerfile`. Changing the location itself works
   correctly during the container's lifetime — the issue is only the persistence of this
   setting across rebuilds.

## Next steps (suggested order)

1. Upload validation (type/size) for items and attachments.
2. Recording the author of property/name/tag changes (point 3 above).
