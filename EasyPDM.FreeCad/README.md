# EasyPDM — FreeCAD macros

**English** | [Polski](README.pl.md) | [Deutsch](README.de.md)

Two independent macros, one for uploading, one for downloading/opening:

- **`EasyPDMUpload.FCMacro`** sends the active FreeCAD document to EasyPDM. The macro asks
  NOTHING locally except the save folder — even the choice of "new item or attach to an
  existing one" is decided in the **system browser** (described further down in this
  file), on the same form/bar as in the web application.
- **`EasyPDMDownload.FCMacro`** downloads a Part/Assembly from EasyPDM (together with ALL
  of its components, if it's an Assembly) and opens it in FreeCAD right away (described in
  a separate section at the end of this file).

Both share the same login and API address (the same FreeCAD preferences) — logging in
with one is enough for the other.

## Installation

Nothing needs to be installed as a workbench. For each of the two macros separately, in
FreeCAD:

- **Macro → Macros… → Add path to macro path list** and point it at this folder
  (`EasyPDM.FreeCad/`), then run the macro from the list — **or**
- **Macro → Macros… → Execute** and point directly at the file (`EasyPDMUpload.FCMacro`
  or `EasyPDMDownload.FCMacro`), each time from any location on disk.

The API address (default `http://localhost:5000/api`) is remembered in FreeCAD
preferences (`User parameter:BaseApp/EasyPDM`) after the first run — it can be changed in
the "Send to PDM" window (the first window on every run — target folder + account), in
the "API address" field.

## Login

EasyPDM.Api requires being logged in for every call except login itself — so the macro
also logs in. On the first run (or when the saved session has expired or been
invalidated), a login window appears (username + password, the same accounts as in the
web application). The session token goes into the same FreeCAD preferences as the API
address, so **subsequent macro runs — even after restarting FreeCAD — do NOT ask to log
in again**, as long as the session is valid (30 days, same as in the web application). The
"Send to PDM" window shows who is logged in, and lets you **log out** from there (the "Log
out" button) — this invalidates the session on the server side and clears the locally
saved token, so the next macro run immediately asks to log in again.

**Why does the macro need its own session at all, if the new/existing decision and the
whole form live in the browser?** Because the macro itself must be able to authenticate
the whole time, in order to (a) poll whether the browser has finished (`GET
/api/create-tickets/{ticket}`), and (b) attach the CAD file itself to the indicated item
— this is still done by the macro, not the browser, because otherwise all the automation
around renaming the file / exporting STEP / building the assembly BOM would disappear.
The very same macro session is also what logs the browser in AUTOMATICALLY — `GET
/api/auth/browser-login` exchanges the macro's token for a browser cookie — so logging in
in the macro and the browser's "free" login are **one and the same operation**, not two
separate ones.

## What it does

1. **Saves the active document** — if the document doesn't yet have a path on disk, the
   macro will ask for "Save as" (the standard FreeCAD dialog) before sending anything.

1a. **Asks for the target folder** for local copies (Save As) of all documents sent
    during this session (see step 3 below) — by default it suggests the last one used
    (**a preference shared with `EasyPDMDownload.FCMacro`** — set the same folder in both
    macros so uploaded and downloaded files end up together in one place). Asked ONCE,
    right at the start — this also covers the automatically detected assembly tree
    (parts/sub-assemblies sent before the main window even opens).

2. **No native window appears anymore at this point.** The macro immediately opens the
   **system browser**, already logged in (token→cookie bridge, see "Login" above), on a
   bar saying "a request from a CAD macro is pending" (visible on EVERY screen of the web
   application, as long as the ticket is waiting). The bar shows an **explicit
   three-button choice** — the choice of "new item, duplicate, or attach to an existing
   one" is made there, not locally and not by an accidental click on just any "Add"
   button somewhere in the application (deliberately NOT how it happens):
   - **"New item"** — opens a **self-contained popup**, with no need to navigate the
     project panel on the left beforehand: it's only WITHIN THIS POPUP that you pick the
     project, optionally a parent item, the type (Part/Assembly), the name (pre-filled by
     default from the document's label), the kind, and the fields that depend on it: for
     a Part the kind is required — **Manufactured** → Material, **Purchased** →
     Manufacturer/Order number 1 and 2/Mass, **Standard** → Material/Norm,
     **Client-supplied** → no additional fields. For an Assembly the kind is optional and
     limited to Manufactured/Purchased/Standard (no "Client-supplied"), and Mass is
     always visible regardless of the chosen kind — an Assembly never has a Material
     field (only a Part does). The popup also has an **"Export and send STEP model"
     checkbox**. The ticket is EXPLICITLY pinned to this one specific popup — no OTHER
     "Add" button in the application (in the project tree, the details panel) will ever
     accidentally "swallow" it. **Cancel** in the popup goes back to the "New
     item"/"Duplicate"/"Attach to existing" choice without creating anything.
   - **"Duplicate"** — first a search box lets you point to the **source** item
     (Part/Assembly) from the whole database, then it opens the SAME popup as "New item",
     just pre-filled with its properties (kind/material/manufacturer/order numbers/norm/
     mass) — **without copying any file**. All fields can still be edited before saving —
     it's an ordinary creation of a new item, just pre-filled with data from the source.
   - **"Attach to existing"** — opens a search box for a Part/Assembly from the **whole
     database** (not just the current project, since a component can be shared), with
     suggestions as you type (by number or by name) and the same STEP checkbox. If the
     local document's label looks like `number (name).REVISION` (because this same macro
     already named it that way after an earlier upload), the search box immediately
     suggests the matching item — this is only a **suggestion**, the choice can always be
     changed. The selected item is not created anew — the current document is attached to
     it as the **current version of its current revision** (what happens next with the
     "Released" status/a new revision is described in step 2a below — this is the ONLY
     decision that deliberately stays local, in FreeCAD, right before the file is
     actually attached).

   One shared form/bar in the browser for EVERYTHING, so these rules can no longer drift
   apart between the macro and the web application. Once you've decided in the browser
   (via any of the three routes above), FreeCAD (the "Waiting for the browser" window,
   polling the server every ~2 s, 10-minute limit) detects completion on its own and
   continues from step 3 below — there's no need to go back to FreeCAD manually.
   Cancelling in the browser or in the waiting window ends the macro with a message,
   without creating/attaching a file (the item may already have been created in the PDM,
   if it was saved in the browser before cancelling — in that case, run the macro again
   and attach to it via the "pending request from the macro" bar).

2a. **The only decision that deliberately STAYED local** — for an item being attached to
    that has the **"Released"** status (in this status the PDM doesn't allow attaching
    files), the macro opens a "New revision" window: it asks whether to create a new
    revision, and lets you enter an optional **revision comment** (what changed) —
    exactly the same comment you can add in the web application on the same status
    change. This is a direct confirmation of what the macro is about to do to the file on
    disk, not item data in the PDM — hence it stayed local. Cancelling saves nothing;
    confirming changes the status to "In progress" (the same mechanism as in the web
    application — it bumps the revision number and saves the comment, if one was given)
    and only then sends the file.

3. At this point the item already exists in the PDM (new — created via the browser form;
   already existing — pointed to there via the same bar, see step 2). In both cases the
   macro **COPIES** the current document file into the PDM under the name
   `number (name).REVISION.extension` — the same number/name format the PDM displays
   Parts/Assemblies in everywhere else, plus the revision as an **uppercase letter** (A,
   B, C... — the same `revisionLabel()` convention as in the web application; the number
   in the database doesn't change, it's purely a matter of formatting). **The local
   document is also saved (Save As) under this SAME name**, in the folder chosen in step
   1a (or in the original's folder, if nothing actually needed to be moved there — the
   file was already there under the right name) — the old file stays untouched on disk,
   but `doc.FileName` now points to the new one. Thanks to this, an assembly linking
   (`App::Link`) to this document, saved AFTER it in the same session (the automatically
   detected assembly tree always saves the assembly last — see below), will save its
   reference already under the new name — exactly the one `EasyPDMDownload.FCMacro` later
   saves downloaded files under, so references line up right away after downloading.
   **Without this, the assembly would look for the original, pre-upload file name after
   downloading** (confirmed in practice by the FreeCAD message: `Link broken! ... File:
   <original_name>.FCStd`).
4. If the PDM storage is visible from this machine (see `GET /api/config`), the copy goes
   into the **SHARED folder** `storage/components/` (one for all projects — a
   Part/Assembly can be shared as a BOM component across projects) and is **REGISTERED
   WITHOUT being re-uploaded over HTTP** (`POST /api/items/{id}/attachments/register`) —
   the copy is already physically sitting in the server's storage. **The PDM KEEPS copies
   of previous revisions** — e.g. `67 (name).A.FCStd` stays when revision B is created;
   only the copy of THAT SAME revision gets overwritten (i.e. a repeat upload with no
   status change). If the storage is unreachable from this machine (e.g. FreeCAD on a
   different computer than the server), the copy goes through a plain HTTP upload instead
   (`POST /api/items/{id}/attachments`, the same mechanism as attaching CAD files from the
   properties panel in the web application) — in that case revision history is not
   preserved.
5. **Optionally exports STEP and uploads it automatically as an attachment with the
   "step" role** — the macro has no local choice left at this point. For a **new** item
   this is decided EXCLUSIVELY by the checkbox in the browser form (step 2 above). For
   attaching to an **existing** item, and for automatically detected assembly components
   (neither of which ever go through the browser), STEP is **always** exported, with no
   prompt. When it does export, it works through exactly the same mechanism as the manual
   "STEP" button in the Attachments panel of the web application, so it immediately feeds
   the item panel's persistent 3D preview. The whole **visible** geometry of the document
   is exported (every solid-bearing object whose visibility is on — for a Part this is
   usually a single solid/Body, for an automatically detected assembly it's the resolved
   `App::Link`s, so the STEP reflects the whole assembly). The previous attachment with
   the "step" role is **replaced** (deleted before uploading the new one), so the preview
   always shows the current revision. If the document has no visible solid (a bare
   sketch, an empty document) or the export/upload fails — the step is **silently
   skipped**, it does not abort or roll back the rest of the upload (the `.FCStd` file is
   already safely saved in the PDM at that point).

## Automatic assembly detection

If the active document links (`App::Link` — the standard way of building assemblies in
the Assembly/Assembly4 workbench) to **other, saved `.FCStd` files**, the macro asks,
**before** opening the main window, whether to send the whole tree automatically:

- **Detection**: it walks the document's objects, finds `App::Link`s pointing to other
  documents, and recurses down (a sub-assembly can also link further). Quantity is
  counted from the number of links to the same file — several separate links and link
  patterns/arrays (`ElementCount`) are counted together (e.g. 2 separate screws + a
  pattern of 2 of the same screw = 4 in the BOM).
- **Upload order**: leaves first (parts with no further links), then sub-assemblies, and
  finally the main document — so that every component exists in the PDM before being
  attached as a child item.
- **Already-uploaded components**: recognized by a label in the format
  `number (name).REVISION` (the same format the macro itself assigns after uploading) —
  if that number exists in the PDM, the component is NOT created again, only attached to
  the BOM with the calculated quantity.
- **New components**: for each not-yet-uploaded file a separate, short **native** window
  is shown (Project/Type/Name and — ONLY for Parts — Kind and the fields that depend on
  it; an Assembly has no kind at all, it only gets an optional Mass — the same rules as
  the browser form for a single new item, see above) — **deliberately without the
  browser**, so that uploading an assembly with many new components doesn't require as
  many browser windows as components; the type (Part/Assembly) is suggested based on
  whether that file itself has further links. The MAIN assembly document itself (the one
  the macro was run on) still goes through the browser form like any other new item —
  only its automatically detected COMPONENTS bypass the browser.
- Choosing **"No"** on the prompt about automatic sending uploads ONLY the current
  document, exactly as before (without child items) — the BOM structure is then left to
  be filled in manually in the web application, as before.

## First-version limitations

- **Step 2** (new item/attach to existing, both through the browser) requires the default
  system browser to be able to open the PDM server's address (the same one as the API
  address in the macro preferences) — on a typical installation (client and server on the
  same network) this works with no extra configuration. The waiting window in FreeCAD has
  a **10-minute** limit — once exceeded (or on Cancel) the macro ends with a message,
  without creating/attaching a file.
- Automatic assembly detection only works for references to **external, saved files**
  (`App::Link`) — not for assemblies kept in a single file as `App::Part` containers
  (these have no separate files to upload individually; they then need to be uploaded
  manually, part by part, as before).
- Recognizing an "already-uploaded" component relies on the document's label — the macro
  assigns it itself after uploading and saves it to disk right away (Save As under the
  PDM name), so it also works in a NEW FreeCAD session, as long as the renamed file (the
  one under the PDM name) is the one opened — the old, pre-upload copy (left untouched on
  disk) still has the original label and won't be recognized.
- **The local document file is MOVED to a new name (Save As) on every upload** — the old
  file (under the original name) stays on disk, but is no longer actively edited/open; a
  manual change to the old file will NOT automatically make it into the PDM (it needs to
  be uploaded back as another revision).
- **Copying/registering the file in `storage/` assumes that folder is visible in this
  machine's file system** — today the client (FreeCAD) and the server (`EasyPDM.Api`) run
  on the same disk, so this works with no extra configuration. If `GET /api/config` is
  unreachable or the path isn't writable (e.g. FreeCAD on a different machine than the
  server), the copy goes to the PDM via a plain HTTP upload instead — in this fallback
  mode revision history is NOT preserved (every upload overwrites the previous copy on
  the PDM side).
- The registration endpoint only accepts paths that lie inside the configured storage
  (`StorageRoot`) — it can't be used to "attach" an arbitrary file from the server's disk.
- The saved document is sent in its current state — the macro does not validate, for
  instance, whether the document has unsaved changes open in other linked files.
- **The "unsaved changes" asterisk next to the document's name can remain visible even
  AFTER a successful upload (both after the explicit `recompute()`+`save()` at the end,
  and on every document individually)** — confirmed to be independent of the macro: the
  same document gets the asterisk even after a plain, manual `doc.save()` typed directly
  into FreeCAD's Python console (in fact already just from using the console, even before
  `save()`), with no involvement of any code from this file. This is a behavior of
  FreeCAD itself (likely the Assembly workbench) — the macro has no way to prevent it,
  because the problem doesn't lie in what it does. It has not been verified whether the
  FILE on disk is nonetheless correctly saved with the current content (likely, since
  `save()` does actually run — only the "modified" indicator in the GUI doesn't clear).

## Verification

⚠️ **The tests below concern the version FROM BEFORE the "new-item form in the browser"
change** (described in step 2 above) — that change is **untested on live FreeCAD**.
Verified so far: syntax (`py_compile`), structural consistency (no orphaned references to
removed native-dialog fields), and end-to-end at the level of the backend endpoints
themselves (`GET /api/auth/browser-login` — cookie setting + redirect + open-redirect
protection, `POST /projects/{id}/nodes` with a ticket + `GET /create-tickets/{ticket}` —
returning correct item data) in an isolated test environment, see
`EasyPDM.Api.Tests/CreateTicketEndpointsTests.cs` and `AuthEndpointsTests.cs`. NOT
verified live: `webbrowser.open()` itself from within FreeCAD, the "Waiting for the
browser" window (`WaitForTicketDialog`) in a real GUI, and the whole flow from clicking
"Add" in the browser to automatically continuing in FreeCAD. On the first run, watch the
process carefully and report anything that doesn't work.

⚠️ **The tests below concern the version FROM BEFORE the "Save As of the local file
under the PDM name" change** (described in step 3 above) — in particular, the points
saying that the local file/`doc.FileName` "does not change"/"stays untouched" describe
the PREVIOUS behavior, not the current one.

The Save As mechanism itself **has been confirmed live by the user** — uploading an
assembly with a Part, then downloading it via `EasyPDMDownload.FCMacro`, opened WITHOUT a
"Link broken" error (previously, before this change, the assembly reported exactly that
error, looking for the original, pre-upload file name). Two NEWER additions, built right
afterward, have not yet been tested live: the **target-folder selection window** (step
1a — shared with the download folder in `EasyPDMDownload.FCMacro`) and the **improved
final message** (distinguishing whether the local file was actually moved, or was already
there).

The logic (without the dialog window itself) was tested automatically via `freecadcmd`
against a live `EasyPDM.Api`:
- **login/session**: an API call without a session is rejected (401); a wrong password is
  rejected with a plain error (the local token stays empty); a correct login saves the
  token and the displayed username in FreeCAD preferences, subsequent API calls with that
  token work; logging out clears the token/name locally AND invalidates the session
  server-side (a subsequent call with the same, now-invalidated token gets 401 again),
- **the four Part kinds** (Manufactured/Purchased/Standard/Client-supplied) in the combo
  box, with field visibility depending on the selected kind exactly as in
  `PartPropertyForm` (checked on the real `PdmUploadDialog` window, rendered by Qt in
  `offscreen` mode); the Assembly kind is limited to three options with no
  "Client-supplied", with Mass always visible regardless of kind, but no Material field
  (only a Part has it); creating a "Standard" Part (with Material and Norm) and a
  "Client-supplied" Part (no additional fields) was confirmed by reading back the saved
  properties from the server,
- **automatic assembly detection** (a three-level tree: a part linked 2× via separate
  links + 1× via a pattern of 2 pieces in the main assembly, plus the same part again
  inside a separate sub-assembly): the detected leaves-first upload order, correctly
  summed quantity (2+2=4) for a part used multiple times, correct parent-child edges at
  every level (including from a sub-assembly to its own part) — confirmed directly via
  `GET /api/projects/{projectId}/relations` after a full run of `process_assembly_tree`
  (with the new-component window swapped out, so it could be run without a GUI).
  Re-checking within the same session recognizes already-uploaded components by their
  label (without creating duplicates).
- creating a Part with a material, creating an Assembly under a Folder with a BOM link,
- **a file saved outside the storage (e.g. a simulated Desktop) stays where it was** —
  after uploading, the local file still exists under the same path and name,
  `doc.FileName` doesn't change, and only a COPY of it goes into `storage/components/`
  (byte-identical, confirmed by comparing the local file and the copy on the server),
- items from TWO different projects land in the same, shared `storage/components/` — with
  no per-project subfolders,
- the registration endpoint: rejects a path outside the storage (400) and a non-existent
  file (404), correct registration (confirmed via the `file_path` in the database),
- `revision_label()`: 1→A, 2→B, 26→Z, 27→AA,
- **full revision cycle**: the first upload creates a `N (name).A.ext` copy on the server
  with a single attachment (the local file stays unchanged the whole time); a repeat
  upload WITHOUT a status change only overwrites that same `.A.` copy (doesn't multiply
  copies, the local file untouched); uploading to an item with "Released" status,
  declined → nothing changes (the status stays "Released", the copy and attachment A
  untouched); confirmed → the status goes back to "In progress", the revision number
  increases, a NEW `.B.ext` copy is created on the server **alongside** `.A.ext` (the old
  one is NOT deleted, the local file stays under the same, unchanged name/path the whole
  time), and the PDM now has exactly TWO attachments — one per revision,
- **automatic detection of an existing item by name** (the only test that builds a real
  `PdmUploadDialog` window, without `exec()`): exactly one name match → the mode switches
  to "Existing item" and the item is selected right away; no match → it stays "New item";
  several items with the same name (different projects) → the mode switches and the
  search narrows to that name, but nothing is auto-selected; typing the name live into
  the "Name" field triggers the same check,
- **revision comment**: on confirming a new revision with a comment —
  `GET /api/items/{id}/revisions` returns exactly one entry with that comment and the
  correct revision number; declining the revision, or confirming with an EMPTY comment,
  add nothing there (revisions with no comment simply have no entry).

Everything passed correctly — including once live, by the user themself, in the FreeCAD
GUI (creating an item and revision A→B), which confirmed file and attachment behavior in
a real environment, not just in automated tests. The dialog window itself (PySide6,
including the existing-item search box and the "New revision" window with a comment) was
also checked manually — tested on FreeCAD 1.1.3 with PySide6.

---

# EasyPDMDownload.FCMacro — downloading and opening

The second macro: instead of uploading, it **downloads** a Part/Assembly from EasyPDM and
**opens** it in FreeCAD right away. Login and the API address are configured in exactly
the same way as in `EasyPDMUpload.FCMacro` (the same FreeCAD preferences) — a separate
install/run (see "Installation" above), but a shared session. Just as in
`EasyPDMUpload.FCMacro`, the item choice is made in the **system browser**, not in a
native window — the macro only asks locally about the target folder.

## What it does

1. A native window only asks about the target folder (by default suggesting the last one
   used, a separate FreeCAD preference `DownloadFolder`, with a **"..."** button to
   change it) and shows the account/**Log out**. Once confirmed, the macro immediately
   opens the **system browser**, already logged in (token→cookie bridge), on a bar
   saying "a download request from a CAD macro is pending" (visible on every screen of
   the web application) — only there do you search for a Part/Assembly from the **whole
   database** (by number or name) and confirm the choice. FreeCAD (the "Waiting for the
   browser" window, polling every ~2 s, **10-minute** limit) detects the confirmation on
   its own and the download starts automatically — there's no need to go back to FreeCAD
   manually. Cancelling in the browser/waiting window ends the macro without downloading
   anything.
2. For an **Assembly**: it also downloads ALL of its components recursively (direct
   children, then their children, and so on — the whole BOM), into the SAME folder as the
   main file. Without this, an assembly built on `App::Link` references to external,
   saved files (standard in the Assembly/Assembly4 workbench) would have nothing to open
   against — FreeCAD only resolves these references when opening the document, so the
   component files must already be sitting on disk BEFORE the main file is opened.
3. If the target folder already has a file with **exactly the same name** (i.e. the same
   revision) and the same size as on the server — it skips it, not downloading it a
   second time.
4. If the folder already has a file of the SAME item, but at a **different (older)
   revision**, and the server has a newer one — it asks whether to download the newer
   one, instead of silently overwriting or silently leaving the outdated file.
5. Finally it **opens** the main (chosen) file in FreeCAD (`App.openDocument`) — the
   component files stay only on disk, they are not automatically opened as separate
   documents (exactly the way FreeCAD itself opens an assembly: it loads linked files in
   the background).

## Where it gets files from

EasyPDM has no separate "item file" for a Part/Assembly — the current CAD file is an
attachment (`item_attachments`), and on EVERY new-revision upload via
`EasyPDMUpload.FCMacro` the previous copy STAYS (a new attachment alongside the old one,
with different names: `number (name).REVISION.extension`) — so revision history is, in
practice, reconstructable from the attachment list alone, with no need for a separate
"old file versions" API. The macro recognizes this naming convention in order to hit the
attachment corresponding to the item's CURRENT revision; if an item has never gone through
any CAD macro (e.g. attached manually in the web application, with attachments under
arbitrary, original names), it simply takes the most recently uploaded attachment as the
best approximation.

## First-version limitations

- **Choosing the item** (step 1 above) requires the default system browser to be able to
  open the PDM server's address (the same one as the API address in the macro
  preferences) — on a typical installation (client and server on the same network) this
  works with no extra configuration. The waiting window in FreeCAD has a **10-minute**
  limit — once exceeded (or on Cancel) the macro ends without downloading anything.
- It always targets the CURRENT revision — it cannot be used to download a specific,
  chosen older revision (older local copies serve only to detect "you have an outdated
  version", point 4 above).
- All files (main + components) land flat in ONE folder, without recreating the BOM
  structure as subfolders. This is the safest default choice for `App::Link` references
  saved as paths RELATIVE to the document's folder (typical for Assembly4), but if the
  original model was built with files in separate subfolders, or with references saved as
  ABSOLUTE paths from a different machine, the references may still not resolve
  automatically — in that case they need to be fixed manually in FreeCAD (Assembly4 has a
  "Make link relative"/change-link-path tool for this).
- A shared component (used in several places in the tree) is downloaded only once
  (recognized by item ID) — the same as when uploading in `EasyPDMUpload.FCMacro`.
- Recognizing "this is the file for this item" relies on the same naming convention as
  uploading (`number (name).REVISION.extension`) — an item whose ONLY attachment has a
  completely different name (never went through any CAD macro) will still be downloaded
  (it takes the newest attachment), but the "you already have an older revision"
  detection (point 4) won't work then, since there's nothing to recognize a revision
  letter from in the local file's name.

## Verification status

⚠️ **Untested on live FreeCAD** — unlike `EasyPDMUpload.FCMacro` (which went through a
full cycle of tests via `freecadcmd` against a live server, plus manual verification in
the GUI), this macro could only be verified: syntactically (`ast.parse`), for correctness
of Polish characters (a script checking character frequency — zero corruption), and
through a careful review of the logic against the actual API endpoints (`GET
/api/items`, `/items/{id}/attachments`, `/items/{id}/children`, `/attachments/{id}/file`,
each checked against the `EasyPDM.Api` code). On the first run on live FreeCAD, watch the
process (the log in the window at the end, and in the FreeCAD Report view console) and
report anything that doesn't work — the riskiest spots are: recognizing attachment names
via regex (if a file has an unusual name), and whether the `App::Link` references in the
downloaded assembly actually resolve automatically once all files are placed in one flat
folder (this depends on how the link paths are saved in the original file — see
"Limitations" above).

Choosing the item via the browser (step 1 above) is the MOST RECENT change in this file,
built directly on the same, already-verified mechanism from `EasyPDMUpload.FCMacro` (`GET
/api/auth/browser-login`, the ticket `GET /api/create-tickets/{ticket}` + `POST
/create-tickets/{ticket}/attach-existing`, the "pending request from the macro" bar in
the web application) — the backend/frontend of this part were tested end-to-end (`curl`
in an isolated environment), but `EasyPDMDownload.FCMacro` itself (`webbrowser.open`,
`WaitForTicketDialog` in a real GUI) hasn't been run on live FreeCAD yet.
