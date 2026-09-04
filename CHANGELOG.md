# Changelog

All notable changes to EasyPDM are documented in this file.

## [0.2]

### Added
- Notifications: a bell icon (top right, next to your name) shows a scrollable list
  of events — an item you own waiting for review/released/reverted to "In progress",
  a new revision on your item, being assigned to or removed from a project, an
  assigned project being deleted, your password being changed by an admin, or (admins
  only) low disk space on the file storage. Each type can be turned off individually
  in Settings → Notifications, and each notification can be marked as read or deleted
  individually (an "X" button next to each one in the bell's dropdown).
- Item detail panel now shows a "Used in" section (right above History): every
  assembly that contains the item, directly or through a sub-assembly, across
  projects — with a button to jump straight to it. Scrollable, capped at 5
  visible rows like History.
- Assemblies now have a kind of their own — Manufactured, Purchased or Client's —
  picked when creating one and changeable later, just like a Part's kind. Purchased
  and client assemblies are numbered with the prefix of the matching Part kind; only
  manufactured assemblies keep their own prefix (the existing "Assembly" one, now
  labelled accordingly in Settings → Numbering).
- Manufacturers can now have a two-level catalog of what they supply: Series/Types,
  and Subtypes within a series, added from one row (a series picker plus a subtype
  field plus Add) and listed in a filterable table below (Manufacturers tab). On a
  purchased item — Part or Assembly — the "Series/Type" and "Subtype" fields sit next
  to Manufacturer, always visible, side by side; each is simply disabled until the
  level above it is set (Series/Type needs a manufacturer, Subtype needs a
  series/type) and offers exactly the entries belonging to it, and changing a higher
  level clears the lower ones. In "Whole database" the kind filter now covers Parts and
  Assemblies together (choosing "Purchased" lists both), and the same
  Manufacturer → Series/Type → Subtype chain appears as dependent filters next to it.
- Client detail panel now lists the Projects assigned to that client (with a button
  to jump straight to each one), scoped to what the current user can actually see.
- A brand new, empty database now gets one sample project on first startup (an
  assembly with two parts in different statuses, forming a small BOM, plus a tag) —
  something to explore instead of a blank slate. A notification points it out and
  reminds you to clear it (Settings → File storage → Danger zone) before real use.
  Only ever created once, on a genuinely empty database.
- An assembly's BOM table (and both its CSV exports) now shows a "Norm" column,
  filled in from a Standard part's own Norm field.
- "Whole database" gained a "Clear filters" button next to the other filters —
  resets search, tag, and every filter dropdown in one click. Disabled when nothing
  is currently filtered.
- New item status, "Cancelled" — for a released Part/Assembly that turns out not to be
  needed. Selectable only from "Released", and reversible back to "In progress" (same
  revision bump + comment as coming back from "Released"). An assembly can't itself
  become "Released" while anything in its BOM — at any nesting depth — is cancelled;
  the attempt names the cancelled item(s) right in the status confirmation dialog.
  Cancelled items are always ownerless, same as released ones, and their icon in the
  tree/list turns red.
- The Client-supplied kind (Part or Assembly) now has a "Client" field, picked from
  the Clients catalog — previously there was no way to record which client a
  client-supplied item actually belongs to. Next to it, on the same line, "Name 2" —
  that client's second name, offered only once a client is picked and cleared again
  if you change it.

### Changed
- Admins can now bypass another user's item lock for three actions: changing its
  status, taking over the lock (locking it to themselves), and releasing it —
  useful when a coworker is away and their in-progress item needs to move forward.
  Editing properties still requires actually being the owner.
- Deleting a project no longer deletes its Parts/Assemblies. It now only removes the
  project itself — the items become project-less (same state as "Remove from
  structure"), still fully intact with their files, attachments, tags, history, and
  BOM relations, reachable through "Whole database". This also protects items shared
  into another project's BOM: deleting the owning project used to silently remove
  that shared item from the other project's BOM too — it no longer does.

### Fixed
- Switching a Part or Assembly's kind (e.g. Purchased → Standard) didn't clear the
  fields that belonged only to the old kind — a Manufacturer typed in under
  "Purchased" stayed in the item's data even after switching away, invisible in the
  UI but still turning up in "Whole database" search. Now cleared as part of the
  kind change, both when editing an existing item and while still filling in the
  "New item" dialog.
- An Assembly's generic Properties editor duplicated its kind, Manufacturer,
  Series/Type and Subtype as plain, freely-editable rows underneath the dedicated
  fields for them further up — redundant, and easy to accidentally desync from the
  real fields.
- Adding a *new* item under a locked assembly bypassed the owner lock entirely —
  anyone with project access could insert a new BOM row under someone else's locked
  assembly, even though every other change to that assembly was correctly blocked.
- Editing a BOM row's quantity had no error handling — a failed save (e.g. the
  parent got locked by someone else) silently left the input showing the unsaved
  value with no indication anything went wrong.
- Deleting a Material had no confirmation dialog and no error handling — the only
  one-click, unconfirmed delete left in the app.
- The automatic backup schedule wrote a local-time timestamp into a column that
  expects UTC. If that write failed (or silently stored the wrong time), the "did
  it already run today" check could never engage, and the service would keep
  retrying — creating a fresh backup every 15 minutes and pruning older, legitimate
  ones well within a day.
- Deleting a contact (Clients/Manufacturers) had no confirmation dialog and no error
  handling — the last one-click, unconfirmed delete left in the app besides Materials
  (fixed above).
- A number of inline "save on blur"/"save on click" fields had no error handling —
  item name, custom properties, price/currency, part kind, removing a tag, saved
  filters, and per-project user access checkboxes. A failed save could look like it
  went through with no indication anything was wrong.
- Dragging to reorder items in the project tree, and "Remove from structure" there,
  silently swallowed errors with no feedback (the equivalent actions inside a BOM
  already showed errors correctly).
- Deleting or demoting the last administrator had a narrow race: two near-simultaneous
  requests (e.g. two admins demoting each other, or one deleting the other at the same
  moment) could both pass a stale "is this the last admin" check and leave the system
  with zero administrators.
- Releasing an item's owner lock as part of releasing it to "Released" status wasn't
  recorded in the item's History (unlike releasing it explicitly).
- Quickly switching the selected project while adding a new item without a fixed
  project could show parent-folder options from the previously selected project.
- The Logs page could show content for the wrong date if you switched dates or hit
  "Refresh" again before the previous request finished.
- Re-sending a status change that didn't actually change anything (e.g. re-confirming
  "Released" on an item already Released) could still fire a duplicate notification.
- The status-change confirmation dialog showed two buttons, "Cancel" and "Confirm",
  even when it was only displaying a blocking error (e.g. an assembly rejected from
  "Released" because it contains a cancelled item) — "Confirm" did nothing in that
  case. Now shows a single "OK" button instead.

## [0.1.1]

### Added
- "Whole database" view: filter/search items and projects by Client.
- Clearing the database now lets you pick which categories to wipe (Whole
  database/Projects, Materials, Manufacturers, Clients) instead of all-or-nothing —
  each is its own checkbox in the confirmation dialog, all checked by default.
- Docker images now have a real release process: `:latest` (what `docker-compose.yml`
  pulls) only moves when a version tag (`vX.Y.Z`) is pushed, via the new
  `publish-docker-release.yml` workflow, which also tags the exact version
  (`:v0.1.1`, etc.). Every push to `main` still publishes a separate `:edge` tag for
  checking the newest state before cutting a release — it no longer touches `:latest`.
  Previously `:latest` was republished on every push to `main`, so a Docker deployment
  had no way to get a specific, deliberately released version.

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
- "Change storage location" persisted the new path to `appsettings.json`, but on every
  deployment path (Windows installer's `appsettings.Production.json`, Docker/Linux's
  `StorageRoot` environment variable, or a developer's `appsettings.Local.json`) a
  higher-precedence config source still had the *old* path — so the change silently
  reverted to the old, already-deleted location on the next restart even though the
  database and files had already moved. Now persisted to `appsettings.Local.json`,
  which this app always loads last (highest precedence) regardless of deployment.
- The automatic backup schedule's "already ran today" check compared a UTC timestamp
  from the database against the server's local clock — for schedules close to
  midnight in a non-UTC timezone, the guard could never engage, so the service kept
  re-running the backup every minute until local time caught up with the UTC offset.
- Six confirm-delete dialogs (delete manufacturer, delete client, delete a client
  file/folder, change item status, delete user, reset item-numbering sequence) had the
  same missing error-handling issue already fixed elsewhere: a failed request left the
  dialog open with no feedback (or closed it before the result was known), with no
  guard against double-submitting.
- Manually editing a BOM row's L.p. (position) had a race: two near-simultaneous edits
  could both pass the "is this number free?" check and end up giving two different
  parts the same position. Added a database-level unique constraint (migration 036,
  deferred so the drag-to-reorder feature's temporary in-flight swaps still work) and
  made the check-and-update atomic.
- `AddTagRow` (adding a tag to an item, or to several at once in bulk) cleared the
  input and moved on regardless of whether the request actually succeeded — a failed
  add (e.g. a duplicate tag) silently discarded what you typed with no error shown.
- Fixed the same "stale response overwrites the screen" race (already fixed once for
  BOM cross-navigation) in the search behind "Whole database", the Manufacturers list,
  the Clients list, and a client's file search — fast typing could show results for an
  older, broader query instead of the latest one.
- The attachments panel and the item preview box could briefly show a previous item's
  attachments/preview for a moment after switching to a different item.
- `db/schema.sql` was missing the `GRANT` on the `sessions` table that migration 012
  already has — harmless on both official install paths (where `pdm_user` owns the
  database outright) but a real drift from a fresh schema-only install.
- The Windows installer's PostgreSQL-version detection compared version folder names
  as plain text, which would pick an old PostgreSQL 9.x install over a newer 10+ one
  if both existed on the same machine (`"9.6" > "18"` alphabetically). Now compares
  the leading version number instead.

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
