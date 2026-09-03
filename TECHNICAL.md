# EasyPDM — technical documentation

**English** | [Polski](TECHNICAL.pl.md) | [Deutsch](TECHNICAL.de.md)

This document is for the administrator who installs/maintains EasyPDM, and for
developers. A description of the tool itself (what it's for and how to use it while
designing) is in [README.md](README.md).

## Status

Projects and items are created manually through the web application (uploading a file
straight into the API's storage) or through the FreeCAD (`EasyPDM.FreeCad/`) or
SolidWorks (`EasyPDM.SolidWorks/`) macro, which call the same API.
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
- **`db/migrations/`** — migrations `002`–`041` for an already existing database:
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
  and manufacturer series/types with their subtypes. Since migration 027, files in this folder are embedded in the program
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
  renames the local file to `number (name)`) and `EasyPDMDownload.FCMacro` (the opposite
  direction: pick a Part/Assembly in the browser, fetch it together with the WHOLE tree
  of the Assembly's components — so that `App::Link` references resolve — and open it
  in FreeCAD right away; skips already-downloaded files, asks before overwriting an
  older revision with a newer one). **Both macros are untested on live FreeCAD in their
  current version** (the browser-based flow) — see `EasyPDM.FreeCad/README.md`.
- **`EasyPDM.SolidWorks/`** — the SolidWorks counterpart of the above (VBA macros
  `EasyPDMUpload.bas`/`EasyPDMDownload.bas`), with the same browser-based flow, STEP
  export, and automatic assembly-tree detection. **Unverified on live SolidWorks** — see
  `EasyPDM.SolidWorks/README.md` for details and known risks.
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
hides the root, the record stays) and **"Delete completely"** (recursive, but safe for
shared components — an item with a parent outside the deleted subtree does not
disappear; administrator only). A Part/Assembly can also be **duplicated** (the copy
gets a new number, a fresh status and owner) — from the tree, the copy lands right
under the original.

A Part has four **kinds** (`properties.rodzaj`), each with a different set of fields and
a different icon in the tree: **Manufactured** (Material, Price, Additional notes),
**Purchased** (Manufacturer, Series/Type, Subtype, Order number 1/2, Mass, Price, Additional
notes), **Standard** (Material, Norm, Additional notes), **Client-supplied** (no
additional fields besides Additional notes).

An Assembly has three kinds of its own in the same `properties.rodzaj`: **Wykonywane**
(manufactured), **Zakupowe** (purchased — Manufacturer, Series/Type, Subtype) and **Klienta**
(client-supplied). The strings deliberately differ from the Part ones ("Zakupowe" vs
"Zakupowa"), because that value doubles as the numbering-prefix key — the one shared
string is "Klienta", which shares its prefix too. Beyond its kind's fields an Assembly
still has the generic property editor (Mass and any custom keys). Assemblies created
before this version have no kind and show a hint prompting you to pick one.

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
w_pracy` (in progress → under review → (in progress | released) → in progress; going
back from `wydany`/released bumps the revision number, with an optional comment on the
revision). Outside of the `w_pracy`/in-progress status, editing the name/properties is
locked — exception: price/currency/price type are always editable. At the bottom of a
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
that BOM — or by dragging the row), Name, Quantity, Material, Manufacturer, Order
number 1/2 (missing fields shown as "-"), together with nested items (parts of nested
assemblies, position in the form `2.1`). CSV export in two variants: full (every
occurrence listed separately) and aggregated (the same component used several times in
different places — one row with the combined quantity, expanded through the whole
chain).

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

### API endpoints

| Method | Path | What it does |
|---|---|---|
| POST | `/api/auth/login` \| `/logout` | login / logout — login is the only endpoint that doesn't require a session |
| GET/PATCH | `/api/auth/me` \| `/password` | the logged-in user's data / changing YOUR OWN password |
| GET | `/api/auth/browser-login` | token→cookie bridge for CAD macros (opens the browser already logged in) |
| GET/POST/PATCH/DELETE | `/api/users[/{id}]` | account management — **administrator only** |
| GET/POST/PATCH/DELETE | `/api/projects[/{id}]` | list/create/edit/delete a project (writes — administrator only; list filtered by access) |
| GET/POST/DELETE | `/api/project-users`, `/api/projects/{projectId}/users/{userId}` | managing user-to-project assignments — **administrator only** |
| GET | `/api/items?search=&tag=&projectId=` | filtered item list (filtered by project access) |
| GET | `/api/items/{id}` | item details |
| POST | `/api/projects/{projectId}/nodes` | creates a Folder/Part/Assembly/File without an upload (optionally with a ticket for a CAD macro) |
| POST | `/api/projects/{projectId}/items` | **multipart/form-data**: file upload (optional `parentId`) |
| GET | `/api/items/{id}/file` | download the uploaded file |
| POST | `/api/items/{id}/duplicate` | duplicates a Part/Assembly (new number, status, owner) |
| PATCH | `/api/items/{id}/name` \| `/visibility` \| `/status` \| `/project` | rename / change tree visibility / change status / move to another project |
| POST | `/api/items/{id}/lock` \| `/release` | lock (take ownership) / release an item |
| DELETE | `/api/items/{id}` | complete deletion (recursive, safe for shared items) — **administrator only** |
| GET | `/api/projects/{projectId}/relations` | parent-child relations (structure/BOM) of a given project |
| POST/DELETE | `/api/items/{parentId}/children[/{childId}]` | add/detach a child item |
| PATCH | `/api/items/{parentId}/children/{childId}/position` \| `/reorder` | change BOM position (a single position or the whole new order) |
| PATCH | `/api/projects/{projectId}/roots/reorder` | change the order of a project's tree roots |
| GET | `/api/items/{id}/bom` \| `/bom/csv` \| `/bom/aggregated-csv` | nested BOM (JSON) / CSV export (full / aggregated) |
| GET | `/api/items/{id}/documentation/extensions`, `/documentation` | file extensions available to download / a ZIP with attachments (item + subtree) |
| GET | `/api/projects/{projectId}/documentation/extensions`, `/documentation` | the same, for a whole project |
| GET | `/api/tags` | tag list |
| POST/DELETE | `/api/items/{id}/tags[/{tagName}]` | tag management |
| PATCH/DELETE | `/api/items/{id}/properties[/{key}]` | property management (locked outside the `w_pracy`/in-progress status and while owner-locked — exception: price fields) |
| GET | `/api/items/{id}/revisions` | revision comment history (only revisions with a comment) |
| GET | `/api/items/{id}/history` | full history: creation, status changes, revisions, attachment added/removed, owner lock/release (when/who/description), chronologically |
| GET/POST/PATCH/DELETE | `/api/materials[/{id}]` | material catalog (name + group/subgroup) |
| GET/POST/PATCH/DELETE | `/api/manufacturers[/{id}]`, `/api/manufacturers/{id}/contacts[/{contactId}]`, `/api/manufacturers/{id}/product-types[/{typeId}][/subtypes[/{subtypeId}]]` | manufacturer catalog + contact people + series/types and their subtypes |
| GET/POST/DELETE | `/api/items/{itemId}/attachments[/{id}]`, `/register`, `/api/attachments/{id}/file` | attachments (upload/register an existing file/list/download/delete) |
| GET/POST/DELETE | `/api/saved-filters[/{id}]` | saved filter sets for the "Whole database" view (private per user) |
| GET/POST | `/api/create-tickets/{ticket}`, `/attach-existing` | CAD macro ↔ browser correlation (see `EasyPDM.FreeCad/README.md`) |
| GET | `/api/config` | file storage location (used e.g. by the FreeCAD macro) |
| GET/POST | `/api/settings/storage`, `/storage/move`, `/backup`, `/restore` | storage location/stats, moving it, backup (pg_dump + files in a ZIP), restore from backup — **administrator only** |
| GET/PATCH | `/api/settings/backup-schedule` | automatic backup schedule (enable/disable, frequency, day, time, number of kept copies) — **administrator only** |
| GET/PATCH | `/api/settings/item-number-prefixes[/{rodzaj}]` | item number letter prefixes per kind (the 4 Part kinds plus `Zlozenie` = manufactured assembly; purchased/client assemblies reuse the Part kind's prefix) — **administrator only** |
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
new code, the container is recreated, and the program **applies new database migrations
on its own on startup** (built into the executable, tracked in the `schema_migrations`
table — see `MigrationRunner.cs`) — nothing else needs to be done manually. The
`docker-entrypoint-initdb.d` step with `schema.sql` only runs on the FIRST, completely
empty start of the `pgdata` volume (fresh install); on an update it's not touched at all,
since the volume already exists.

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

**Update**: build a new `EasyPDM_Windows_v<version>.exe` (as above) and run it again — `PrepareToInstall`
in the `.iss` script stops the service BEFORE replacing the files (otherwise Windows
would block overwriting a running `.exe`), the installer detects the existing role/
database (skips creating the schema) and the existing service (starts it back up instead
of registering it again). The program applies new database migrations on its own
automatically on startup.

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
