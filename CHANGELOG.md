# Changelog

All notable changes to EasyPDM are documented in this file.

## [0.1.1]

### Added
- "Whole database" view: filter/search items and projects by Client.

### Fixed
- Clearing the database ("Danger zone") didn't delete project-less items (items with
  no project, only reachable via "Whole database" search) — they were left behind and
  could collide with the item-numbering sequence after a reset.
- Clearing the database didn't reset the item-numbering sequence, so the first item
  created afterwards continued the old numbering instead of starting at 1.
- Clearing the database didn't delete the Clients catalog (contacts and their file
  structure) even though everything else was wiped.

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
