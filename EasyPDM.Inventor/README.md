# EasyPDM — Autodesk Inventor macros

**English** | [Polski](README.pl.md) | [Deutsch](README.de.md)

Two `.bas` macros, each fully self-contained (separate file, separate VBA module, no
dependencies between them beyond a shared spot in the Windows registry for the login
session) — the Inventor counterparts of `EasyPDM.SolidWorks/EasyPDMUpload.bas` and
`EasyPDMDownload.bas`:

- **`EasyPDMUpload.bas`** — sends the active Inventor document to EasyPDM. The choice of
  project/new-vs-existing/duplicate/item properties happens in the browser (the same
  pattern as the SolidWorks and FreeCAD macros), with one exception — see "Differences
  from the SolidWorks macros" below.
- **`EasyPDMDownload.bas`** — fetches a Part/Assembly from EasyPDM (together with all of
  the assembly's components) and opens it in Inventor; the choice of WHICH item also
  happens in the browser.

## Status

**NOT yet live-tested** — this environment has no Autodesk Inventor installation
available, so unlike the SolidWorks macros (which went through several rounds of live
testing and bug-fixing on real SolidWorks 2026), this port has only been verified
statically: ASCII-only source, balanced `Sub`/`Function`/`If`/`For`/`Do`/`Select Case`
blocks, no duplicate procedure names, correct CRLF line endings. The overall shape of the
code — JSON parsing, HTTP, the browser ticket flow, login, revision naming — is identical
to the already-verified SolidWorks macros and shares no Inventor-specific risk. The parts
that genuinely need confirming on a real Inventor install are the narrow set of Inventor
Automation API calls listed in "Known risks / places to check first" below — please read
that section, and the macro's own log file, before relying on this in production.

## Differences from the SolidWorks macros

These are **ports**, not new designs — the same architecture as
`EasyPDM.SolidWorks/EasyPDMUpload.bas`/`EasyPDMDownload.bas`, with the same Sub/Function
names wherever Inventor's own API allows it, so the two file families stay easy to
compare side by side. Only the actual CAD API calls differ:

- **Document type detection**: `TypeName(oDoc)` (`"PartDocument"`/`"AssemblyDocument"`/
  `"DrawingDocument"`) instead of SolidWorks' `GetType()` compared against hardcoded
  `swDocumentTypes_e` values — deliberately chosen instead of guessing Inventor's
  `DocumentTypeEnum` integers, which cannot be verified without a live install.
- **Custom Properties → iProperties**: `oDoc.PropertySets.Item("Inventor User Defined
  Properties")` instead of SolidWorks' `CustomPropertyManager` — see "Known risks" for
  the exact property-set name, which may need adjusting on older Inventor versions.
- **Assembly tree walk**: `AssemblyDocument.ComponentDefinition.Occurrences` (top-level
  components only, same scope as SolidWorks' `GetComponents(True)`), `occ.Suppressed`
  (a plain Boolean — simpler than SolidWorks' opaque `GetSuppression2()` enum),
  `occ.Definition.Document`, `occ.Name`.
- **No equivalent of `ResolveAllLightWeightComponents`** — Inventor has no identical
  per-assembly "resolve lightweight components" concept, so this step is simply omitted
  (with an explanatory comment at the call site that would have held it), rather than
  guessing a nonexistent API call.
- **Drawing views**: nested `For Each oSheet In drawDoc.Sheets: For Each oView In
  oSheet.DrawingViews` instead of SolidWorks' flat `GetFirstView`/`GetNextView` walk;
  `view.ReferencedDocumentDescriptor.ReferencedDocument` instead of
  `view.ReferencedDocument`.
- **STEP/PDF export**: Inventor's `TranslatorAddIn` mechanism (`ApplicationAddIns.
  ItemById`, `TranslationContext`/`NameValueMap`/`DataMedium`, `SaveCopyAs`) instead of
  SolidWorks' `IModelDocExtension.SaveAs` — see "Known risks" for the translator CLSIDs,
  which are UNVERIFIED.
- **Opening a document**: `InvApp.Documents.Open(path, Visible:=True)` (simpler
  signature, errors via COM exception) instead of SolidWorks' `OpenDoc6` (which needs an
  explicit document-type argument and returns errors via `ByRef` parameters).
- **File extensions**: `.ipt`/`.iam`/`.idw` instead of `.sldprt`/`.sldasm`/`.slddrw`.

Everything else — the browser ticket flow, JSON parsing, HTTP layer, login, revision
naming convention, STEP/PDF-checkbox behavior, "already linked" detection and its
Save-Copy-As caveat, per-component browser tickets for new assembly children, the
removed-component detection, PDF/STEP being optional everywhere — is unchanged from the
SolidWorks macros; see their own README for the full behavioral description, which
applies here identically.

## What `EasyPDMUpload.bas` does

1. **Login** — on first run (or when the saved session has expired/been invalidated) it
   asks for the API address, username, and password. The session token is saved in the
   Windows registry (`HKEY_CURRENT_USER\Software\VB and VBA Program Settings\EasyPDM`) —
   subsequent runs (also after restarting Inventor, also from `EasyPDMDownload.bas`, and
   also shared with the SolidWorks/FreeCAD macros — the session is shared across all of
   them) don't ask to log in again as long as the session is valid (30 days).
2. **Saves the active document**, if it hasn't been saved yet.
3. **If the active document is an Assembly**: detects the component tree
   (`AssemblyDocument.ComponentDefinition.Occurrences`, recursively). A pre-walk summary
   `MsgBox` lists every discovered component, flagging already-linked ones with their
   target PDM item number and filename. If confirmed, it walks leaves first (this
   document last); for each NOT-yet-linked component it opens its own browser ticket
   (same New item/Duplicate/Attach to existing choice as the top-level document — one
   browser tab at a time, a native "click OK to continue" `MsgBox` before each subsequent
   tab, since only one program-initiated browser tab per macro run can reliably grab
   Windows' foreground focus). Newly created components get their own STEP/PDF export
   (per that component's own checkbox choice in the browser) and an `EasyPDM_ItemId`
   iProperty, and are automatically attached under their parent in the BOM structure.
   Already-linked components are only ever referenced, never re-uploaded, regardless of
   status. Components removed from the assembly since the last upload are flagged too,
   with a confirmation before removing their PDM link (the items themselves are never
   deleted, only their attachment under this specific parent).
4. Checks the main document's **iProperties**:
   - **Already linked** (has a saved `EasyPDM_ItemId`) — asks locally for consent to
     attach the current version as a new revision, without opening the browser, followed
     by two more native Yes/No prompts for STEP export (default Yes) and PDF export
     (default No).
   - **Not yet linked** — opens the system browser (already logged in, token→cookie
     bridge) to the "pending request from a CAD macro" popup, with three options to
     choose THERE: **New item**, **Duplicate**, or **Attach to existing** — same fields
     and item-kind logic as the SolidWorks/FreeCAD macros. The macro waits (polls every
     ~2s, 10-minute limit, Escape cancels, progress shown in Inventor's status bar) and
     continues automatically once the choice is confirmed in the browser.
   - **Existing item with "Released" status**: asks for consent to a new revision and an
     optional comment.
   - **Existing item with "Under review" status**: attaching is hard blocked with a
     native error message.
5. **Copies** the current document file into the PDM under the name
   `number (name).REVISION.extension` (the same convention as the web application and
   the other CAD macros). The local file is NOT touched. If the PDM storage is visible
   from this machine, the copy goes straight there and is registered without a second
   HTTP upload; otherwise a plain HTTP upload (fallback, via
   `WinHttp.WinHttpRequest.5.1`, same as the SolidWorks macros).
6. When STEP/PDF export is enabled: exports via Inventor's `TranslatorAddIn` mechanism
   to a temporary `.step`/`.pdf` file and uploads it as an attachment with the role
   `"step"`/`"pdf"`, replacing the previous attachment of that same role. An export error
   does NOT abort the rest of the operation.
7. Saves `EasyPDM_ItemId`/`EasyPDM_ItemNumber` into the document's iProperties and shows
   a confirmation.

## What `EasyPDMDownload.bas` does

1. **Login** — as above (session shared with `EasyPDMUpload.bas`).
2. **Choosing the item to download happens in the browser** — the same "pending request
   from a CAD macro" popup as when uploading, except right away with just the search. The
   only thing that stays a local `InputBox` is the **target folder** (defaults to the
   last one used — same preference shared with `EasyPDMUpload.bas`'s target folder).
3. For an Assembly: also fetches **all of its components recursively** into the SAME
   folder as the main file.
4. For each file: skips it if the folder already has a file with exactly the same name
   and size; asks before replacing an older local revision with a newer one on the
   server.
5. At the end, opens the main (chosen) file in Inventor (`InvApp.Documents.Open`) —
   component files stay only on disk, Inventor resolves the assembly references to them
   on its own.

Where it gets the files to download from: EasyPDM stores the current CAD file as an
attachment, and earlier revisions stay as separate attachments alongside it —
`EasyPDMDownload.bas` recognizes the naming convention set by `EasyPDMUpload.bas` to land
on the attachment corresponding to the CURRENT revision.

## How to check whether it worked

1. **The message at the end** — after a successful operation the macro shows a window
   with a summary. A window with "Error: ..." means something went wrong.
2. **The web application** — go into the project (or "Whole database"), find the item by
   the number from the message and check whether it has an attached file and correct
   properties.
3. **The macro log** — every run appends a detailed, timestamped record to a plain text
   file:

   ```
   %TEMP%\EasyPDM_inventor_macro.log            <- EasyPDMUpload.bas
   %TEMP%\EasyPDM_inventor_download_macro.log   <- EasyPDMDownload.bas
   ```

   (paste `%TEMP%` into Windows Explorer's address bar to get there). This is the first
   place to check when something isn't working — the path to it is also appended in the
   error/success window at the end.

## Installation

Like SolidWorks, Inventor has no plain-text macro format — macros are VBA projects.
`.bas` is the standard export/import format for a VBA **module**:

1. Autodesk Inventor → **Tools** tab → **Macro** panel → **Visual Basic Editor** (or
   Alt+F11).
2. In the VBA editor: **File → Import File...** → pick `EasyPDMUpload.bas` or
   `EasyPDMDownload.bas`. Import into any VBA project — the "global" project shared
   across all documents is usually the right choice so the macro is available regardless
   of which document is active.
3. Run via **Tools** tab → **Macro** panel → **Macros...** → select `main` → Run (or F5
   inside the VBA editor, **with the cursor inside `Sub main()`**).
4. A separate `Sub Logout` (in each of the modules) logs out of EasyPDM — it can be bound
   to your own toolbar button/shortcut.

The API address (default `http://localhost:5000/api`) is saved automatically after being
entered once at login — shared with the SolidWorks/FreeCAD macros too.

## Known risks / places to check first

This port was written entirely from documented Inventor Automation API behavior, without
access to a live install — everything below is marked `UNVERIFIED` inline in the source
and should be the first place to look if something goes wrong on the first real run:

1. **`InvApp.StatusBarText`** as a settable property (used by `WaitForTicket` to show
   progress while waiting for the browser) — wrapped in `On Error Resume Next`, so a
   wrong assumption degrades to "no status bar text" instead of a crash, but worth
   confirming.
2. **Translator CLSIDs** for STEP/PDF export
   (`ExportViaTranslator`/`UploadStepAttachment`/`UploadPdfAttachment`) — publicly
   documented values, not confirmed here:
   - STEP: `{90AF7F40-0C01-11D5-8E83-0010B541CD80}`
   - PDF: `{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}`
   Also `oContext.Type = 2` (intended as `kFileBrowseIOMechanism`) in the same export
   path.
3. **`PropertySets.Item("Inventor User Defined Properties")`** — the exact property-set
   name; some Inventor versions may use a different name (e.g. without the "Inventor "
   prefix). The write path already falls back from "set" to "add" when a property
   doesn't exist yet, but a wrong set name would fail both.
4. **`oDoc.Save`/`oDoc.SaveAs(path, False)`** exact behavior and error signaling (COM
   exception, not `ByRef` output parameters like SolidWorks' `Save3`) — the general shape
   is well documented, but not exercised against a real file yet.
5. **`oDoc.DisplayName`** as the equivalent of SolidWorks' `GetTitle()`.
6. **`view.ReferencedDocumentDescriptor.ReferencedDocument`** — the exact property path
   for resolving a drawing view's referenced model; expected to return `Nothing` when
   the model isn't currently open, same limitation as the SolidWorks macro's
   `view.ReferencedDocument`.
7. **No equivalent of `ResolveAllLightWeightComponents`** — deliberately omitted (see
   "Differences from the SolidWorks macros"); if Inventor turns out to have its own
   lightweight-component gotcha, this is where it would need to be added.

## Limitations (deliberately out of scope for this version)

- `EasyPDMDownload.bas`: does not try to download a SPECIFIC older revision — always
  targets the current one. All files (main + components) land flat in one folder,
  without recreating the BOM structure as subfolders.
- The login password is not masked (plain `InputBox`, no dedicated `UserForm`).
- The download's target folder is a plain `InputBox` with a path as text, not a system
  file browser.
- Copying/registering/downloading a file through `storage/` assumes this folder is
  visible in this machine's filesystem — same as the SolidWorks/FreeCAD macros.
