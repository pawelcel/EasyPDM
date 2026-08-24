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

**The earlier version (no browser, native `InputBox`/`MsgBox` for choosing the
project/item/kind) was verified live** (SolidWorks 2026, 2026-08-20) — see "History of
fixed issues" below, still current for the shared login/JSON/HTTP infrastructure, which
the current version keeps in its entirety.

**The current version (browser-based pattern + STEP export + automatic assembly-tree
detection) is UNVERIFIED** — written without access to SolidWorks/a VBA compiler in the
environment where it was created (unlike the FreeCAD macros, where `py_compile` gave real
syntax verification, here the only verification is a manual code review). It requires a
full test on live SolidWorks before production use — see "Known risks" below, which
precisely points out the most uncertain parts.

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
  at all — it only asks locally for consent to a new revision, exactly as before this
  change. SolidWorks then knows the item with 100% certainty, so the browser wouldn't add
  anything here; FreeCAD always goes to the browser because it has no such reliable local
  mechanism.
- **Recognizing an "already sent" document** (`EasyPDMUpload.bas`): NOT through a
  label/file name (SolidWorks has no equivalent of FreeCAD's free-form label) — through
  the document's **Custom Properties** (`EasyPDM_ItemId`, `EasyPDM_ItemNumber`), saved
  into the file itself after a successful upload. This is actually a **more durable**
  approach than in FreeCAD — it also works in a NEW SolidWorks session, and (new) is now
  also used for EVERY assembly component individually (see "assembly tree detection"
  below), not just for the main document.
- **New assembly components are collected via an `InputBox` sequence, not the browser** —
  the same reasoning as the native component dialog in FreeCAD (N browser tabs for N new
  assembly components would be worse UX than one native prompt per component), all the
  more justified here by the complete absence of `UserForm` in this file at all.
- **STEP export for browser-less paths always exports** (a document already linked;
  automatically detected assembly components) — there's no browser form there for a
  checkbox to live in. Only the ticket path (new item/duplicate/attach to existing) has
  the STEP checkbox in the browser, same as in FreeCAD.

## What `EasyPDMUpload.bas` does

1. **Login** — on first run (or when the saved session has expired/been invalidated) it
   asks for the API address, username, and password. The session token is saved in the
   Windows registry (`HKEY_CURRENT_USER\Software\VB and VBA Program Settings\EasyPDM`) via
   the built-in `SaveSetting`/`GetSetting` — subsequent runs (also after restarting
   SolidWorks, also from `EasyPDMDownload.bas` — the session is shared) don't ask to log
   in again as long as the session is valid (30 days).
2. **Saves the active document**, if it hasn't been saved yet (SolidWorks' standard "Save
   As" window).
3. **If the active document is an Assembly**: it detects its component tree
   (`IAssemblyDoc.GetComponents`, recursively) and asks whether to automatically send
   along with it all components that are NOT yet linked to the PDM (recognized by Custom
   Properties on EACH component individually, see below) — leaves first, this document
   last. For each new component: a short `InputBox` sequence (Project → Type → Name, and
   for Parts additionally Kind and its dependent fields — the same rules as below; an
   Assembly has no kind, only an optional Mass), NOT the browser (see "Differences from
   the FreeCAD macros"). Newly created components immediately get a STEP export and their
   own `EasyPDM_ItemId` entry, and are automatically attached under their parent in the
   BOM structure.
4. Checks the main document's **Custom Properties**:
   - **Already linked** (has a saved `EasyPDM_ItemId`) — asks locally for consent to
     attach the current version as a new revision, without opening the browser (see
     "Differences from the FreeCAD macros"). STEP export always happens.
   - **Not yet linked** — opens the system browser (already logged in, token→cookie
     bridge) to the "pending request from a CAD macro" popup, with three options to
     choose THERE: **New item** (project, optionally a parent, type, name — for Parts
     additionally the kind and its dependent fields: Manufactured → Material; Purchased →
     Manufacturer/Order numbers/Mass; Standard part → Material/Norm; Client's → no
     additional fields; **an Assembly has no kind at all** — only an optional Mass — plus
     a STEP export checkbox), **Duplicate** (points to an existing item, copies its
     properties to a new one, no files) or **Attach to existing** (search across the
     whole database + the same STEP checkbox). The macro waits (polls every ~2s,
     10-minute limit, Escape cancels, progress shown in SolidWorks' status bar) and
     continues automatically once the choice is confirmed in the browser.
   - **Existing item with "Released" status** (both paths above): asks for consent to a
     new revision and an optional comment — exactly the same mechanism as in the web
     application, the only decision deliberately staying local even on the browser path.
5. **Copies** the current document file into the PDM under the name
   `number (name).REVISION.extension` (the same convention as in the web application and
   the FreeCAD macros). **The local file is NOT touched** — it is neither moved nor
   deleted. If the PDM storage is visible from this machine (`GET /api/config`), the copy
   goes to the shared `storage/components/` and is **registered** without re-uploading
   over HTTP (preserves revision history); if not (typical when SolidWorks and the
   EasyPDM service run as different Windows users — the storage is under
   `C:\ProgramData\...`, which a regular user usually has no write access to), a plain
   HTTP upload (fallback, without preserving revision history) — **this is not a bug**,
   just a security measure working correctly.
6. When STEP export is enabled (checkbox in the browser, or always for browser-less paths
   — see point 4): it exports the visible geometry to a temporary `.step` file
   (`IModelDocExtension.SaveAs`) and uploads it as an attachment with the role `"step"`,
   replacing the previous attachment of the same role — feeds the persistent 3D preview
   in the web application. An export error (e.g. no visible geometry) does NOT abort the
   rest of the operation.
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

## History of fixed issues (from the first real test of `EasyPDMUpload.bas`)

Found and fixed on 2026-08-20 on live SolidWorks 2026 — left here as a documented example
of what to watch out for when verifying `EasyPDMDownload.bas` (which uses the same,
already-fixed infrastructure, but also has its OWN, still-unverified code — regex,
recursive downloading, `OpenDoc6`):

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
   incorrect") when uploading through the HTTP fallback. Fixed by wrapping the bytes in
   a binary `ADODB.Stream` and sending the stream instead of the array.

## Known risks / places to check first

Parts not yet tested — written without access to SolidWorks/a VBA compiler, so not even
the SYNTAX has been checked automatically (unlike the FreeCAD macros, where `py_compile`
gave real verification). Suggested test order: a plain Part not yet linked → the same
Part again (native path) → Attach/Duplicate in the browser → an Assembly with new
components → Escape/timeout while waiting → `EasyPDMDownload.bas`.

**Shared by both files (new in this round):**
1. **`WaitForTicket`** — a `Sleep`/`DoEvents`/`GetAsyncKeyState(VK_ESCAPE)` loop +
   polling `GET /create-tickets/{ticket}` every ~2s, `swApp.Frame.SetStatusBarText` to
   show progress. The mechanism (Win32 API via `Declare`, no `UserForm`) has no direct
   counterpart elsewhere in this repo — to check: whether Escape actually breaks the
   loop, whether the status bar updates/clears correctly, whether SolidWorks stays
   responsive (`DoEvents`) for the whole waiting period.
2. **`Scriptlet.TypeLib.Guid`** (`NewGuid`) and manual `UrlEncode` (percent-encoding via
   `ADODB.Stream` to UTF-8 + manual BOM skipping) — both standard, documented workarounds
   for VBA's lack of a built-in GUID/URL encoder, but untested in this specific use (a
   document name with Polish characters in `name=` is worth checking separately).

**`EasyPDMUpload.bas` (new in this round):**
3. **`IModelDocExtension.SaveAs` to `.step`** (`UploadStepAttachment`) — the exact
   number/meaning of the parameters (`SaveAsVersion`/`SaveAsOptions`) written from the
   API documentation, NOT tested — if SolidWorks reports an argument error, check the
   exact signature of the installed version in the VBA editor (F1 on `SaveAs`).
4. **`IAssemblyDoc.GetComponents`/`IComponent2.GetModelDoc2`/`Name2`/`GetTitle`**
   (`VisitAssemblyComponents`/`ProcessAssemblyTree`) — assumes that a late-bound
   `ModelDoc2` for an Assembly can be called directly with `IAssemblyDoc` methods (a
   typical, documented pattern in SolidWorks macros, but untested here). Suppressed/
   unresolved/virtual components are skipped by checking `GetModelDoc2() Is Nothing`/an
   empty `GetPathName()` instead of a hardcoded suppression enum value — to be confirmed
   that this is actually enough.

**`EasyPDMDownload.bas` (unchanged from the previous round):**
5. **`swApp.OpenDoc6`** — the signature (`FileName, Type, Options, Configuration,
   Errors, Warnings`) is a well-documented, standard API, but has not been tested live.
   `Options = 0` (no special flags) should be a safe default value.
6. **`VBScript.RegExp`** (`NewRevisionRegex`) — used to recognize the file naming
   convention (`number (name).REVISION.extension`) when detecting the current revision
   and older local copies. A standard, stable mechanism, but untested in this specific
   use.
7. **Recursive downloading of assembly components** (`DownloadChildrenRecursive`) —
   analogous to `_download_children_recursive` in `EasyPDMDownload.FCMacro` (including
   the same response shape from `GET /items/{id}/children`: the item nested under the
   `"item"` key), but untested on the SolidWorks side.
8. **`EnsureDirectory`** — a manual implementation of creating nested folders (VBA's
   `MkDir` only creates one level at a time, unlike `os.makedirs`) — simple logic, but
   worth checking on a path with several non-existent levels at once.

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
