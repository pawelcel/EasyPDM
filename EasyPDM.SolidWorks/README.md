# EasyPDM — SolidWorks macros

**English** | [Polski](README.pl.md) | [Deutsch](README.de.md)

Two `.bas` macros, each fully self-contained (separate file, separate VBA module, no
dependencies between them beyond a shared spot in the Windows registry for the login
session):

- **`EasyPDMUpload.bas`** — sends the active SolidWorks document to EasyPDM. The choice
  of project/new-vs-existing/duplicate/item properties happens in the browser (the same
  pattern as the FreeCAD macros), with one exception — see "Differences from the FreeCAD
  macros" below.
- **`EasyPDMDownload.bas`** — fetches a Part/Assembly from EasyPDM (together with all of
  the assembly's components) and opens it in SolidWorks; the choice of WHICH item also
  happens in the browser.

## Status

**Live-verified end-to-end on real SolidWorks 2026**, across several rounds of testing
and bug-fixing (starting 2026-08-20, most recently 2026-08-27): login, the browser
ticket flow (new item/duplicate/attach to existing), file upload/registration, STEP/PDF
attachment export, automatic assembly-tree detection with per-component browser tickets,
and `EasyPDMDownload.bas`'s recursive component download — all confirmed working against
a live server, not just reviewed statically (there is still no VBA compiler in the
environment these files are edited from, so every fix here came from the user
reproducing an issue live and pasting the macro's own log, not from a build step). See
"History of fixed issues" below for the specific bugs this process found.

A few narrow, low-traffic code paths remain genuinely untested in practice (marked
`UNVERIFIED` inline in the source) — see "Known risks" below.

## Differences from the FreeCAD macros

These are **counterparts**, not a 1:1 port — VBA has no built-in JSON and no dialog
windows without separate binary files (SolidWorks/VBA UserForm), so several things are
solved differently:

- **JSON**: its own, minimal parser/builder inside the macro itself (duplicated in both
  files, not shared — VBA has no reliable way to import one module from another) —
  sufficient for the response shapes of this specific API, not general-purpose.
- **No `UserForm`** — instead of FreeCAD's Qt forms, all native windows are plain
  `InputBox`/`MsgBox` (`UserForm` is a separate binary file, it can't be bundled into a
  single `.bas` imported through "File → Import File..."). The login password can't be
  masked with asterisks using a plain `InputBox`. Waiting for the browser (see below)
  shows progress in SolidWorks' status bar instead of in a window with a Cancel button —
  Escape is the only available cancel gesture.
- **Choice of project/new-vs-existing/duplicate/item properties in the browser** — the
  same ticket+`GET /api/auth/browser-login`+"pending request from a CAD macro" popup
  pattern as the FreeCAD macros, **with one deliberate exception**: if the document is
  ALREADY linked to a PDM item (see the point below), the macro does NOT open the browser
  at all — it asks locally (native `MsgBox`) for consent to a new revision, plus whether
  to export STEP and/or PDF (see below), exactly as before this change. SolidWorks then
  knows the item with 100% certainty, so the browser wouldn't add anything here; FreeCAD
  always goes to the browser (or, since a recent change, a native confirmation first when
  a label match is found) because it has no equally reliable local mechanism.
- **Recognizing an "already sent" document** (`EasyPDMUpload.bas`): NOT through a
  label/file name (SolidWorks has no equivalent of FreeCAD's free-form label) — through
  the document's **Custom Properties** (`EasyPDM_ItemId`, `EasyPDM_ItemNumber`), saved
  into the file itself after a successful upload. This is actually a **more durable**
  approach than in FreeCAD — it also works in a NEW SolidWorks session, and is also used
  for EVERY assembly component individually (see "assembly tree detection" below), not
  just for the main document.
- **New assembly components go through the browser too, one at a time** — each
  not-yet-linked component discovered while walking the tree opens its own ticket +
  browser tab (leaves first, same New item/Duplicate/Attach to existing choice as the
  top-level document), sequentially, never several tabs at once — this is the same
  pattern the FreeCAD macro was later ported to match. Since only one browser tab can
  reliably grab Windows' foreground focus per macro run, a native "click OK to continue"
  `MsgBox` appears right before each subsequent tab opens — clicking it counts as fresh
  user input that lets the next browser window take focus instead of opening silently in
  the background (check the taskbar if a step seems to hang).
- **STEP and PDF export are both optional everywhere** — a checkbox for each in the
  ticket-driven browser paths (new item/duplicate/attach to existing, at both the
  top-level document and per assembly component), or two native Yes/No prompts
  (`ExportStepPrompt`/`ExportPdfPrompt`, STEP defaulting to Yes, PDF to No) on the
  browser-less "already linked" path. PDF export uses SolidWorks' own "Save As PDF",
  tagged as the `"pdf"` attachment role, separate from and independent of the STEP
  attachment.

## What `EasyPDMUpload.bas` does

1. **Login** — on first run (or when the saved session has expired/been invalidated) it
   asks for the API address, username, and password. The session token is saved in the
   Windows registry (`HKEY_CURRENT_USER\Software\VB and VBA Program Settings\EasyPDM`) via
   the built-in `SaveSetting`/`GetSetting` — subsequent runs (also after restarting
   SolidWorks, also from `EasyPDMDownload.bas` — the session is shared) don't ask to log
   in again as long as the session is valid (30 days).
2. **Saves the active document**, if it hasn't been saved yet (SolidWorks' standard "Save
   As" window).
3. **If the active document is an Assembly**: resolves any Lightweight components first
   (`ResolveAllLightWeightComponents`, called directly on the assembly model — calling it
   through `.Extension` instead silently no-ops and was a real bug caught in testing:
   `GetModelDoc2()` returns `Nothing` for a still-Lightweight component, making it
   indistinguishable from "component not found" until resolved), then detects the
   component tree (`IAssemblyDoc.GetComponents`, recursively). A pre-walk summary
   `MsgBox` lists every discovered component, flagging already-linked ones with their
   target PDM item number and filename — a deliberate guard, since SolidWorks' own
   Save-As silently copies Custom Properties, which can make a genuinely new part
   falsely "recognize" itself as an existing item if you're not paying attention to this
   summary. If confirmed, it walks leaves first (this document last); for each
   NOT-yet-linked component it opens its own browser ticket (same New item/Duplicate/
   Attach to existing choice as the top-level document, see "Differences from the
   FreeCAD macros" for the focus-stealing `MsgBox` shown before each tab). Newly created
   components get their own STEP/PDF export (per that component's own checkbox choice in
   the browser) and `EasyPDM_ItemId` entry, and are automatically attached under their
   parent in the BOM structure. **Already-linked components are only ever referenced**,
   never re-uploaded — regardless of status — just attached to the BOM with the
   calculated quantity; if one of them currently has status "Under review" or
   "Released", it's listed in the final success message too, as a reminder that any
   local changes to it were NOT sent. **Components removed from the assembly since the
   last upload are flagged too** — for every parent (the top-level document and every
   sub-assembly), before attaching its current local children the macro checks whether
   PDM still has a BOM relation to a child no longer present locally, and asks natively
   for confirmation before removing that link (the items themselves are never deleted,
   only their attachment under this specific parent). Confirming also unassigns the
   removed child from ANY project instead of dumping it into the current project's
   root — it stays fully visible and findable via the global "whole database" search,
   it just stops cluttering the structure of a project it no longer has anything to do
   with (the same mechanism the web application's own "Remove from structure" uses).
4. Checks the main document's **Custom Properties**:
   - **Already linked** (has a saved `EasyPDM_ItemId`) — asks locally for consent to
     attach the current version as a new revision, without opening the browser (see
     "Differences from the FreeCAD macros"), followed by two more native Yes/No prompts
     for STEP export (default Yes) and PDF export (default No).
   - **Not yet linked** — opens the system browser (already logged in, token→cookie
     bridge) to the "pending request from a CAD macro" popup, with three options to
     choose THERE: **New item** (project, optionally a parent, type, name — for Parts
     additionally the kind and its dependent fields: Manufactured → Material; Purchased →
     Manufacturer/Series-Type/Subtype/Order numbers/Mass; Standard part → Material/Norm; Client's →
     Client + Name 2 (only once a Client is picked); **an Assembly has kinds of its own** (Manufactured; Purchased →
     Manufacturer/Series-Type/Subtype; Client's → Client + Name 2) plus an always-optional Mass — plus
     STEP and PDF export checkboxes), **Duplicate** (points to an existing item, copies
     its properties to a new one, no files) or **Attach to existing** (search across the
     whole database + the same STEP/PDF checkboxes). The macro waits (polls every ~2s,
     10-minute limit, Escape cancels, progress shown in SolidWorks' status bar) and
     continues automatically once the choice is confirmed in the browser.
   - **Existing item with "Released" status** (both paths above): asks for consent to a
     new revision and an optional comment — exactly the same mechanism as in the web
     application, the only decision deliberately staying local even on the browser path.
   - **Existing item with "Under review" status**: attaching is instead **hard blocked**
     with a native error message — the macro does NOT silently flip the status back to
     "In progress" and upload anyway (that was a real, fixed bug: someone reviewing the
     item could have it silently reset out from under them by a re-upload). Whoever is
     reviewing needs to move the item out of "Under review" in the web application first.
5. **Copies** the current document file into the PDM under the name
   `number (name).REVISION.extension` (the same convention as in the web application and
   the FreeCAD macros). **The local file is NOT touched** — it is neither moved nor
   deleted. If the PDM storage is visible from this machine (`GET /api/config`), the copy
   goes to the shared `storage/components/` and is **registered** without re-uploading
   over HTTP (preserves revision history); if not (typical when SolidWorks and the
   EasyPDM service run as different Windows users — the storage is under
   `C:\ProgramData\...`, which a regular user usually has no write access to), a plain
   HTTP upload (fallback, without preserving revision history, and uses
   `WinHttp.WinHttpRequest.5.1` specifically for this call — see "History of fixed
   issues" below) — **this is not a bug**, just a security measure working correctly.
   This fallback path also checks for and deletes any existing `"cad"`-role attachment
   with the exact same filename before uploading, so re-saving at the same revision
   letter doesn't accumulate duplicate attachments.
6. When STEP/PDF export is enabled (checkbox in the browser, or the native prompts on the
   browser-less path — see point 4): exports the visible geometry to a temporary
   `.step`/`.pdf` file (`IModelDocExtension.SaveAs`) and uploads it as an attachment with
   the role `"step"`/`"pdf"`, replacing the previous attachment of that same role — feeds
   the persistent 3D preview (STEP) in the web application. An export error (e.g. no
   visible geometry) does NOT abort the rest of the operation.
7. Saves `EasyPDM_ItemId`/`EasyPDM_ItemNumber` into the document's Custom Properties and
   shows a confirmation.

## What `EasyPDMDownload.bas` does

1. **Login** — as above (session shared with `EasyPDMUpload.bas`).
2. **Choosing the item to download happens in the browser** — the same "pending request
   from a CAD macro" popup as when uploading, except right away with just the search (no
   New/Duplicate choice, irrelevant when downloading). The macro waits the same way as
   when uploading (Escape cancels, 10-minute limit). The only thing that stays a local
   `InputBox` is the **target folder** (defaults to suggesting the last one used — the
   same preference as the target folder in `EasyPDMUpload.bas`, so sent and downloaded
   files can land in one place).
3. For an Assembly: it also fetches **all of its components recursively** (direct
   children, then their children, and so on — the whole BOM), into the SAME folder as
   the main file. Without this, an assembly built on external references to saved files
   (typical in SolidWorks) would have nothing to open against.
4. For each file: if there's already a file in the folder with EXACTLY the same name
   (i.e., the same revision) and the same size as on the server — it's skipped (not
   downloaded again). If there's a file for this item at a different (older) revision,
   and there's a newer one on the server — it asks whether to download the newer one.
5. At the end, it opens the main (chosen) file in SolidWorks (`swApp.OpenDoc6`) —
   component files stay only on disk, SolidWorks resolves the assembly references to
   them on its own.

Where it gets the files to download from: EasyPDM stores the current CAD file as an
attachment (there's no separate "item file" mechanism), and earlier revisions stay as
separate attachments alongside it — `EasyPDMDownload.bas` recognizes the naming
convention set by `EasyPDMUpload.bas`, in order to land on the attachment corresponding
to the CURRENT revision; if an item has never gone through any CAD macro (attached
manually in the web application), it simply takes the most recently uploaded attachment.

## How to check whether it worked

Three independent ways, from the fastest to the most detailed:

1. **The message at the end** — after a successful operation the macro shows a window
   with a summary (e.g. "Sent to EasyPDM: item #67 (revision B)." or a download log). A
   window with "Error: ..." means something went wrong.
2. **The web application** — the most reliable proof for an upload: go into the project
   (or "Whole database"), find the item by the number from the message and check whether
   it has an attached file (properties panel → Attachments) and correct properties.
3. **The macro log** — every run appends (does not overwrite) a detailed, timestamped
   record to a plain text file (a separate file for each macro, so they don't get mixed
   up):

   ```
   %TEMP%\EasyPDM_macro.log            <- EasyPDMUpload.bas
   %TEMP%\EasyPDM_download_macro.log   <- EasyPDMDownload.bas
   ```

   (paste `%TEMP%` into Windows Explorer's address bar to get there). Contains, among
   other things, every API call with the response code (`GET /items -> 200`), the login
   result, whether the file storage was visible from this machine, whether the file was
   copied/registered/downloaded successfully, and the full error text if something
   failed. This is the first place to check when something isn't working — the path to
   it is also appended in the error/success window at the end.

## Installation

SolidWorks has no plain-text macro format (like `.FCMacro` in FreeCAD) — macros are VBA
projects. `.bas` is the standard export/import format for a VBA **module** (not the
whole macro project), so for EACH of the two files separately:

1. SolidWorks → **Tools → Macro → New...** — create a new (empty) macro project and save
   it (e.g. `EasyPDMUpload.swp` / `EasyPDMDownload.swp`).
2. In the opened VBA editor: **File → Import File...** → point to `EasyPDMUpload.bas` or
   `EasyPDMDownload.bas`. If the project still has an empty, auto-generated module
   (typically `Module1`/`Upload1`) — remove it (right-click in the project tree →
   Remove... → No, when asked about exporting), so as not to leave two modules at once.
3. Run it via **Tools → Macro → Run** (pointing to the saved `.swp` file) or directly from
   the VBA editor (F5, **with the cursor inside `Sub main()`** — if the project has other
   procedures, F5 runs whichever one the cursor is currently in, not always `main`
   automatically).
4. A separate `Sub Logout` (in each of the modules) logs out of EasyPDM — it can be bound
   to your own button/shortcut in SolidWorks.

The API address (default `http://localhost:5000/api`) is saved automatically after being
entered once at login — shared by both macros.

## History of fixed issues

Found and fixed on 2026-08-20, during the first real test of `EasyPDMUpload.bas` on live
SolidWorks 2026 — left here as a documented example of the kind of VBA-specific gotcha
this codebase has hit before (both macros now share this fixed infrastructure):

1. **No session token in the login body** — `MSXML2.XMLHTTP.6.0` did not give reliable
   access to the `Set-Cookie` header. Fixed server-side (`POST /auth/login` adds
   `sessionToken` straight into the JSON body) — applies to both macros.
2. **Undeclared SolidWorks constants** (`swCustomInfoText`,
   `swCustomPropertyReplaceValue`, `swDocPART`, `swDocASSEMBLY`) — the whole module
   deliberately uses late binding (type `Object` instead of `SldWorks.*`), so the bare
   names of SolidWorks enums had nowhere to get their values from. Fixed with explicit
   `Long` constants with documented values from the SolidWorks API, grouped right at the
   start of the module.
3. **`swApp` undeclared** — CONTRARY to an earlier assumption, `swApp` is NOT
   automatically visible in every VBA module of a project, only in the one SolidWorks
   itself generated on "New...". An imported module needs its own declaration and
   assignment (`Set swApp = Application.SldWorks`) at the start of `main()`.
4. **Mojibake of Polish characters** — VBA did not import the `.bas` file as UTF-8;
   Polish characters (in comments AND in windows visible to the user) came out as
   garbage. Fixed by translating the whole file into plain ASCII (English).
5. **`MSXML2.XMLHTTP.send()` rejected a bare `Byte()` array** ("The parameter is
   incorrect") when uploading through the HTTP fallback. An initial attempt wrapped the
   bytes in a binary `ADODB.Stream` sent through the same `MSXML2.XMLHTTP` object — this
   itself still failed on a real upload with a generic "NO CONNECTION" error. The actual
   fix: `ApiUploadFile` switched specifically to `WinHttp.WinHttpRequest.5.1`, which
   accepts a `Byte()` array directly via `.Send()`; every other call (`ApiGet`, login,
   etc.) still uses `MSXML2.XMLHTTP.6.0`.

Later rounds of live testing (through 2026-08-27) found and fixed several more issues,
summarized here rather than item-by-item — see git history for the full detail:
Lightweight assembly components not being detected (`ResolveAllLightWeightComponents`
must be called directly on the model, not through `.Extension`, which silently no-ops);
`MSXML2.XMLHTTP.6.0` GET requests being served from Windows' local HTTP cache
indefinitely, fixed by adding `Cache-Control`/`Pragma` headers plus a `_ts=`
cache-busting query parameter to every `ApiGet` call in **both** files (each has its own
independent copy of `ApiGet`, and `EasyPDMDownload.bas` additionally needed the same fix
in `ApiGetBinary`, since a stale cached response there would silently corrupt the
downloaded file's actual bytes, not just a status field); a new assembly component
showing up as an unwanted duplicate at the project root, fixed by explicitly hiding
newly-created leaf items from the tree root once attached to their real parent; and
repeated "cad"-role attachments accumulating on every save at the same revision, fixed
by checking for and deleting an existing same-filename attachment before the plain-HTTP
upload fallback.

## Known risks / places to check first

The main flows are now live-verified (see "Status" above). What remains genuinely
untested are a few narrow, low-traffic details, marked `UNVERIFIED` inline in the
source:

1. **`IModelDocExtension.SaveAs` exact parameter signature** for `.step`/`.pdf` export
   (`UploadStepAttachment`/`UploadPdfAttachment`) — written from the SolidWorks API
   documentation; if SolidWorks reports an argument error, check the exact signature of
   the installed version in the VBA editor (F1 on `SaveAs`).
2. **The "save in the same format" path** for a document that was already saved once
   this session (skips the full Save As dialog) — the common "just re-save and re-run
   the macro" case is well exercised, this specific branch less so.
3. **`SW_SAVE_AS_SILENT`**'s exact constant value, used to suppress SolidWorks' own
   confirmation dialogs during the STEP/PDF export `SaveAs` calls.

None of these have caused a reported failure so far — they're listed here as the first
place to look if something SolidWorks-version-specific ever goes wrong with export or
save behavior.

## Limitations (deliberately out of scope for this version)

- `EasyPDMDownload.bas`: does not try to download a SPECIFIC older revision — always
  targets the current one. All files (main + components) land flat in one folder,
  without recreating the BOM structure as subfolders.
- No equivalent of FreeCAD's "target folder for local copies" — `RenameAndUpload`
  copies/sends the file straight to the PDM storage, doesn't leave a renamed copy
  alongside the user's working files.
- The login password is not masked (plain `InputBox`, with no dedicated `UserForm`).
- The download's target folder is a plain `InputBox` with a path as text, not a system
  file browser.
- Copying/registering/downloading a file through `storage/` assumes this folder is
  visible in this machine's filesystem — same as in the FreeCAD macros.
