# Changelog

All notable changes to EasyPDM are documented in this file.

## [0.1.1]

### Added
- "Whole database" view: filter/search items and projects by Client.
- Clearing the database now lets you pick which categories to wipe (Whole
  database/Projects, Materials, Manufacturers, Clients) instead of all-or-nothing —
  each is its own checkbox in the confirmation dialog, all checked by default.

### Fixed
- Clearing the database ("Danger zone") didn't delete project-less items (items with
  no project, only reachable via "Whole database" search) — they were left behind and
  could collide with the item-numbering sequence after a reset.
- Clearing the database didn't reset the item-numbering sequence, so the first item
  created afterwards continued the old numbering instead of starting at 1.
- Clearing the database left the Clients, Materials, and Manufacturers catalogs
  (contacts, groups/subgroups, and Clients' own file structure) untouched even though
  everything else was wiped.
- Selecting a Material on a Part didn't update the Group/Subgroup dropdowns shown
  right below it — they stayed on "All groups"/"All subgroups" instead of reflecting
  the chosen material's actual group/subgroup.
- Moving the storage location didn't rewrite file paths for Clients' own documents
  (`client_nodes`), only for items/attachments — after a move with "migrate existing
  files", every Client document permanently 404'd even though a copy existed at the
  new location.
- Adding a child to a BOM only checked project access for the parent item, never the
  child — a user with access only to project A could pull in an item from a private
  project B they have no access to.
- `PATCH /api/items/{id}/properties` and `POST /api/saved-filters` returned a raw 500
  instead of a clean 400 for malformed request bodies (missing/wrong-shaped JSON).
- Deleting/demoting a nonexistent user threw a 500 instead of a 404
  (`IsLastAdmin` DBNull/null cast bug, same class already fixed elsewhere).
- Closed two race conditions (TOCTOU) that could let two near-simultaneous requests
  double-bump an item's revision number or grant two different users a false sense of
  owning the same lock.
- Fast repeated clicks on "go to item" from a BOM row could show the wrong item if
  responses arrived out of order.
- Bulk delete (and other confirm-delete dialogs) had no error handling — a mid-batch
  failure left the dialog stuck open with no message and no refresh, and allowed
  double-submitting.
- Uploading a file into a folder that has no project (e.g. after "Remove from
  structure") silently failed — the button now shows an explanatory hint instead,
  since the backend has no route to create an item with no project directly.
- The project/item action buttons above the tree/list could render off-screen on a
  narrow window with a wide resized panel, and could visually overlap the bulk-selection
  controls when those wrapped to a second line.
- BOM child quantity wasn't validated server-side (zero/negative values could corrupt
  aggregated CSV totals).
- Database schema drift: `item_relations.position` had a default value on a fresh
  install but not on a database upgraded through the migration chain (migration 010
  never set it) — added migration 035 to fix upgraded installs.
- `uninstall-easypdm-linux.sh` aborted partway through (skipping the rest of the
  cleanup) if run a second time, because of an unguarded command under `set -e`.

### Changed
- Clearing the database now shows a single dialog that walks through
  confirming → in progress → done, and reloads the page after confirmation to avoid
  leftover stale state in the browser.

## [0.1]

Initial public release.

### Added
- Parts and Assemblies with automatic numbering, revisions, status
  (In progress / Under review / Released), and full change history.
- Automatic bill of materials (BOM) for assemblies, exportable to CSV.
- Shared, company-wide catalogs of materials, manufacturers, and clients.
- Search across the whole database, not just the current project.
- Item locking while working on something, so nobody else overwrites changes.
- STEP/PDF file preview in the browser.
- CAD macros for FreeCAD and SolidWorks — upload/download files straight from the
  CAD program.
- Admin panel, users, and per-project permissions.
- Docker, Windows installer, and native Linux install paths.
