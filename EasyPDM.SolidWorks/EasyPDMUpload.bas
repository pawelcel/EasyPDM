Attribute VB_Name = "EasyPDMUpload"
Option Explicit

' ============================================================================
' EasyPDMUpload -- SolidWorks macro that uploads the active document to EasyPDM.
'
' All comments/strings are in plain English (ASCII only) on purpose -- VBA's file import
' does not reliably handle UTF-8, and Polish diacritics got corrupted (mojibake) both in
' the code editor and in dialogs shown to the actual user. Confirmed in practice.
'
' This is the COUNTERPART of EasyPDM.FreeCad/EasyPDMUpload.FCMacro, not a 1:1 port -- VBA
' has no built-in JSON and no dialog windows without separate binary files (UserForm), so:
'   - JSON: own minimal parser/builder (below), good enough for this specific API's
'     response shapes -- NOT general purpose.
'   - Dialogs: plain InputBox/MsgBox instead of dropdowns/Qt forms -- a plain InputBox
'     cannot mask a typed password with asterisks. NO UserForm anywhere in this file --
'     see the "browser ticket flow" section below for how the wait-for-browser step works
'     without one.
'   - Recognizing an "already uploaded" document: NOT via label/filename (SolidWorks has
'     no equivalent of FreeCAD's free-form label) -- via document Custom Properties
'     (EasyPDM_ItemId/EasyPDM_ItemNumber), written into the file itself after a successful
'     upload. More durable than the FreeCAD approach (also works in a brand NEW session,
'     no need to manually save after a label change) -- and reliable enough that THIS ONE
'     decision point stays fully local/native, see "What it does" below. CAVEAT (confirmed
'     in practice, no code-level defense possible): SolidWorks's own native "Save As" (done
'     manually by the user, outside this macro) COPIES Custom Properties along with
'     everything else -- Save-As'ing an already-linked part to start a genuinely DIFFERENT
'     part silently inherits the old EasyPDM_ItemId/EasyPDM_ItemNumber, so the macro would
'     otherwise "recognize" the new part as the OLD item and overwrite its content on the
'     next upload. The "already linked, attach as new revision?" confirmation (see "What it
'     does" below) shows the linked item's own number/name specifically so the user has a
'     chance to notice a mismatch before confirming -- if this ever happens, decline (No)
'     and link the new part to the CORRECT item manually via the browser's "Attach to
'     existing" instead of "already linked".
'
' What it does (top-level document, Sub "main"):
'   - Document ALREADY linked to a PDM item (Custom Property present): asks locally
'     whether to attach the current version as a new revision, then uploads -- exactly
'     like before. No browser involved: SolidWorks already knows the target with
'     certainty, a browser round-trip would add nothing.
'   - Document NOT yet linked: opens the SAME web browser flow as EasyPDMUpload.FCMacro
'     (already logged in via a token->cookie bridge link, see "Logowanie" below) on the
'     "pending request from a CAD macro" popup -- "New item" / "Duplicate" / "Attach to
'     existing" all decided THERE, not locally, exactly like the FreeCAD macro. The macro
'     waits (WaitForTicket, polling GET /create-tickets/{ticket}; Escape cancels, no
'     UserForm needed) and then finishes the job: rename+upload the file, and (see below)
'     export+upload a STEP and/or PDF attachment.
'   - STEP/PDF export: for any ticket path (top-level document OR an auto-detected assembly
'     component, see next point), whether to export EACH of STEP and PDF is its OWN,
'     independent checkbox in the browser (same as FreeCAD for STEP; PDF is checked
'     separately and defaults to off, unlike STEP which defaults to on) -- see
'     UploadStepAttachment/UploadPdfAttachment. For the "already linked" native path (top-
'     level document only), where there is no browser round-trip to host a checkbox in,
'     the SAME two choices are instead asked as plain native Yes/No questions right after
'     the "attach as new revision?" confirmation -- a couple of native prompts for a SINGLE
'     document is not the "opening N popups" problem that drove the assembly components
'     over to the browser ticket flow (next point). Components already in PDM discovered
'     while walking an assembly tree are never re-uploaded at all (nothing about them is
'     assumed to have changed), so this question does not apply to them.
'   - Assembly components: if the active document is an Assembly, the macro first walks
'     its component tree (IAssemblyDoc.GetComponents, recursively) and offers to send any
'     component NOT yet linked to a PDM item, leaves-first -- same idea as FreeCAD's
'     App::Link auto-detection. Each new component gets the SAME browser ticket flow as
'     the top-level document above (New item/Duplicate/Attach to existing decided in the
'     browser) -- opened ONE TAB AT A TIME, one component after another, never several at
'     once (confusing to juggle); cancelling any single ticket aborts the whole remaining
'     walk. Each new component gets linked via Custom Properties too, so re-running the
'     macro on it later (alone or as part of another assembly) recognizes it as done.
'     Declining ("No") skips creating/uploading new components, but components ALREADY in
'     PDM are still attached into the BOM structure -- "No" is not "send nothing at all".
'   - Local "Save As" under the PDM name: every document actually uploaded (the top-level
'     one AND every new assembly component) is also locally SAVED AS "number (name).
'     REVISION.ext" in a target folder asked for ONCE at the very start of the run (see
'     RenameAndUpload/GetDownloadFolder -- same shared folder as EasyPDMDownload.bas's
'     download folder). Components are processed leaves-first (see "Assembly components"
'     above), so by the time an assembly itself gets saved, its references to any
'     just-processed component already point at the new path -- avoids the "Link broken"
'     problem this exact ordering was designed to prevent in EasyPDMUpload.FCMacro.
'
' Installation:
'   SolidWorks -> Tools -> Macro -> New... (create any empty macro project),
'   in the VBA editor: File -> Import File... -> pick EasyPDMUpload.bas. Run via
'   Tools -> Macro -> Run (or F5 inside the VBA editor with the cursor inside Sub "main"),
'   Sub "main". A separate Sub "Logout" logs out and can be bound to your own toolbar
'   button/shortcut.
'
' The API address (default http://localhost:5000/api) and session token are stored in the
' Windows registry via SaveSetting/GetSetting (branch "HKEY_CURRENT_USER\Software\VB and
' VBA Program Settings\EasyPDM\Connection") -- same purpose as FreeCAD's User parameter,
' just a different storage mechanism appropriate for VBA.
' ============================================================================

Private Const APP_SETTINGS_NAME As String = "EasyPDM"
Private Const SETTINGS_SECTION As String = "Connection"
Private Const DEFAULT_BASE_URL As String = "http://localhost:5000/api"
Private Const SESSION_COOKIE_NAME As String = "pdm_session"

' Names of the document Custom Properties used to store the PDM link -- see module header.
Private Const CUSTPROP_ITEM_ID As String = "EasyPDM_ItemId"
Private Const CUSTPROP_ITEM_NUMBER As String = "EasyPDM_ItemNumber"

' Own error numbers (Err.Raise) -- distinguish "missing/expired session" (ERR_AUTH, should
' trigger a fresh login) from a plain API error (ERR_API, just show the message).
Private Const ERR_AUTH As Long = vbObjectError + 1001
Private Const ERR_API As Long = vbObjectError + 1002

' SolidWorks application object -- CONTRARY to this module's earlier (wrong) assumption,
' "swApp" is NOT automatically visible in EVERY VBA module of the project, only in the one
' SolidWorks itself generates via "Tools -> Macro -> New" (that one gets its own "Dim
' swApp"). This file is imported as a SEPARATE module, so it needs its own declaration --
' and its own assignment at the start of main() via "Application.SldWorks" (the standard
' way to obtain the application object from VBA hosted inside SolidWorks itself). Declared
' here, grouped with the module's other Private/Const declarations, all BEFORE any
' Sub/Function -- moved back here (from after the T/T_PL/T_EN/T_DE Function blocks) as the
' suspected fix for "Variable not defined" seen on swApp on a live SolidWorks 2026 install;
' not yet confirmed which exact factor was the real cause, but this restores the same
' "declarations before any Sub/Function" layout the module had before the translation
' functions were inserted in between.
Private swApp As Object

' Values from SolidWorks enums (swDocumentTypes_e, swCustomInfoType_e,
' swCustomPropertyAddOption_e) -- declared here EXPLICITLY as numbers instead of relying on
' the bare constant names (swDocPART, swCustomInfoText, ...) resolving on their own through
' a SolidWorks type library reference. This whole module deliberately uses late binding
' (Object type instead of SldWorks.*), so without this, "Option Explicit" at the top of the
' file treats those bare names as undeclared variables ("Variable not defined" at compile
' time), confirmed in practice. Values are stable, documented in the SolidWorks API,
' unchanged across many versions (including 2026). Grouped here at the very top of the
' module (not next to where they are used) on purpose -- pure module-level Const/Dim
' placement should never matter in VBA, but grouping them here rules that out completely
' as a possible cause if "Variable not defined" is ever seen on one of these again.
Private Const SW_DOC_PART As Long = 1                      ' swDocumentTypes_e.swDocPART
Private Const SW_DOC_ASSEMBLY As Long = 2                   ' swDocumentTypes_e.swDocASSEMBLY
Private Const SW_CUSTOM_INFO_TEXT As Long = 30              ' swCustomInfoType_e.swCustomInfoText
Private Const SW_CUSTOM_PROPERTY_REPLACE As Long = 2        ' swCustomPropertyAddOption_e.swCustomPropertyReplaceValue
Private Const SW_SAVE_AS_SILENT As Long = 1                 ' swSaveAsOptions_e.swSaveAsOptions_Silent -- UNVERIFIED against a
                                                             ' live SolidWorks install, confirm on first real test (see
                                                             ' UploadStepAttachment).

' Win32 API used ONLY by WaitForTicket (below) to poll the ticket endpoint while keeping
' SolidWorks responsive and letting the user cancel with Escape -- this module has no
' UserForm (see file header), so there is no button to click during the wait; Escape is
' the only available cancel gesture without one. "#If VBA7" is the standard compatibility
' guard for 32/64-bit Office/host installs (SolidWorks 2026 is VBA7, but the guard is
' cheap and future-proof).
#If VBA7 Then
    Private Declare PtrSafe Sub Sleep Lib "kernel32" (ByVal dwMilliseconds As Long)
    Private Declare PtrSafe Function GetAsyncKeyState Lib "user32" (ByVal vKey As Long) As Integer
#Else
    Private Declare Sub Sleep Lib "kernel32" (ByVal dwMilliseconds As Long)
    Private Declare Function GetAsyncKeyState Lib "user32" (ByVal vKey As Long) As Integer
#End If
Private Const VK_ESCAPE As Long = &H1B

' Auto-wykrycie jezyka interfejsu Windows (UI language, nie tylko regionalny locale) przez
' Win32 API GetUserDefaultUILanguage -- zwraca LANGID (WORD); dolne 10 bitow to Primary
' Language ID (stale z winnt.h: LANG_POLISH=&H15, LANG_GERMAN=&H07, LANG_ENGLISH=&H09).
' Fallback na angielski dla kazdego innego jezyka albo bledu wywolania.
#If VBA7 Then
    Private Declare PtrSafe Function GetUserDefaultUILanguage Lib "kernel32" () As Integer
#Else
    Private Declare Function GetUserDefaultUILanguage Lib "kernel32" () As Integer
#End If

Private g_Lang As String

Private Function DetectLanguage() As String
    Dim langId As Integer
    Dim primaryLang As Integer
    On Error GoTo Fallback
    langId = GetUserDefaultUILanguage()
    primaryLang = langId And &H3FF
    Select Case primaryLang
        Case &H15
            DetectLanguage = "pl"
        Case &H7
            DetectLanguage = "de"
        Case Else
            DetectLanguage = "en"
    End Select
    Exit Function
Fallback:
    DetectLanguage = "en"
End Function

Private Function GetLang() As String
    If g_Lang = "" Then g_Lang = DetectLanguage()
    GetLang = g_Lang
End Function


' ============================================================================
' Translations (PL/EN/DE) -- every user-facing string literal (MsgBox/InputBox text and
' titles, status bar text) goes through T(key), dispatching to T_PL/T_EN/T_DE based on the
' auto-detected Windows UI language (see DetectLanguage above). Internal LogLine messages
' stay hardcoded/untranslated on purpose (see LogLine's own comment) -- only what the user
' actually sees in a dialog goes through this layer. Strings needing a dynamic value (an
' item number, a file name, an error description, ...) keep only their static part here;
' the call site concatenates the dynamic value with "&" exactly like before.
'
' T_PL/T_DE MUST stay plain ASCII (transliterate diacritics: a/c/e/l/n/o/s/z/z for Polish,
' ae/oe/ue/ss for German) -- see the file-header note above ("Input past end of file" on
' VBA import, confirmed in practice when this was violated once already).
' ============================================================================

Private Function T(ByVal key As String) As String
    Select Case GetLang()
        Case "pl": T = T_PL(key)
        Case "de": T = T_DE(key)
        Case Else: T = T_EN(key)
    End Select
End Function

Private Function T_PL(ByVal key As String) As String
    Select Case key
        Case "StatusWaitingForBrowser": T_PL = "EasyPDM: oczekiwanie na przegladarke... (Esc anuluje)"
        Case "TitleLoginToPdm": T_PL = "Logowanie do PDM"
        Case "PromptApiAddress": T_PL = "Adres API EasyPDM:"
        Case "PromptUsername": T_PL = "Nazwa uzytkownika:"
        Case "PromptPassword": T_PL = "Haslo (UWAGA: to pole nie maskuje wpisywanych znakow):"
        Case "LoggedInAsPrefix": T_PL = "Zalogowano jako "
        Case "AppTitle": T_PL = "EasyPDM"
        Case "LoginFailedPrefix": T_PL = "Logowanie nie powiodlo sie: "
        Case "LoginFailedNoSession": T_PL = "Logowanie nie powiodlo sie -- serwer nie zwrocil sesji."
        Case "TitleNewRevision": T_PL = "Nowa rewizja"
        Case "ItemStatusReleasedPrefix": T_PL = "Element nr "
        Case "ItemStatusReleasedSuffix": T_PL = " ma status ""Wydany"" -- podpiecie pliku wymaga nowej rewizji. Utworzyc nowa rewizje?"
        Case "PromptRevisionComment": T_PL = "Komentarz do nowej rewizji (opcjonalnie):"
        Case "PromptTargetFolder": T_PL = "Folder docelowy na lokalne kopie (nazwane pod numerem PDM):"
        Case "TitleTargetFolder": T_PL = "Folder docelowy"
        Case "AssemblyDetectedTitle": T_PL = "Wykryto zlozenie"
        Case "NewComponentBrowserPromptPrefix": T_PL = "Nowy element w drzewie: "
        Case "NewComponentBrowserPromptSuffix": T_PL = ". Kliknij OK, aby otworzyc przegladarke i go wprowadzic (moze otworzyc sie w tle -- sprawdz pasek zadan)."
        Case "AssemblyLinksPart1": T_PL = "To zlozenie odwoluje sie do "
        Case "AssemblyLinksPart2": T_PL = " innego(-ych) pliku(-ow) (czesci/podzespoly):"
        Case "AssemblyLinksPart3": T_PL = "Wyslac je automatycznie razem z tym dokumentem (najpierw liscie drzewa, ten dokument na koncu)?"
        Case "AssemblyLinksPart4": T_PL = "'Nie' nie wysle/nie utworzy ZADNYCH nowych komponentow -- ale komponenty JUZ istniejace w PDM i tak zostana podpiete do struktury tego zlozenia."
        Case "CreatedButFailedAttachPart1": T_PL = "Utworzono "
        Case "CreatedButFailedAttachPart2": T_PL = ", ale nie udalo sie podpiac go pod "
        Case "CreatedButFailedAttachPart3": T_PL = ": "
        Case "FailedToSaveDocument": T_PL = "Nie udalo sie zapisac dokumentu -- zapisz go recznie (Ctrl+S) i uruchom makro ponownie."
        Case "MustRunInsideSolidWorks": T_PL = "To makro musi byc uruchomione z poziomu SolidWorks."
        Case "NoActiveSavedDocument": T_PL = "Brak aktywnego, zapisanego dokumentu."
        Case "ExportStepPrompt": T_PL = "Wyeksportowac i wyslac model STEP (podglad 3D)?"
        Case "ExportPdfPrompt": T_PL = "Wyeksportowac i wyslac plik PDF?"
        Case "AlreadyLinkedConfirm": T_PL = "Ten dokument jest juz powiazany z elementem PDM. Podpiac biezaca wersje jako nowa rewizje/aktualizacje?"
        Case "AlreadyLinkedConfirmPrefix": T_PL = "Ten dokument jest juz powiazany z elementem PDM nr "
        Case "AlreadyLinkedConfirmSuffix": T_PL = ". Jesli to NIE jest ta sama czesc (np. zrobiles 'Zapisz jako' z innej, juz podpietej czesci) -- kliknij Nie i podepnij ten plik recznie do wlasciwego elementu. Podpiac biezaca wersje jako nowa rewizje/aktualizacje TEGO elementu?"
        Case "StaleLinkCleared": T_PL = "Element PDM, z ktorym ten dokument byl powiazany, juz nie istnieje (zostal usuniety) -- stary link zostal wyczyszczony, dokument zostanie potraktowany jako jeszcze niewyslany."
        Case "CancelledNothingSent": T_PL = "Anulowano -- nic nie zostalo wyslane."
        Case "FailedToAttachSubComponent": T_PL = "Nie udalo sie podpiac jednego z podkomponentow pod element glowny: "
        Case "UploadedSuccessPart1": T_PL = "Przeslano do EasyPDM: element nr "
        Case "UploadedSuccessPart2": T_PL = " (rewizja "
        Case "RunLogPrefix": T_PL = "Log przebiegu: "
        Case "SessionExpiredPrompt": T_PL = "Sesja wygasla -- uruchom makro ponownie, aby sie zalogowac."
        Case "ErrorPrefix": T_PL = "Blad: "
        Case "LoggedOutMessage": T_PL = "Wylogowano z EasyPDM."
        Case "ServerErrorPrefix": T_PL = "Blad serwera ("
        Case "NoConnectionPrefix": T_PL = "Brak polaczenia z "
        Case "AttachmentRegistrationFailedPrefix": T_PL = "Rejestracja zalacznika nie powiodla sie: "
        Case "ItemAlreadyChangedPart1": T_PL = "Status/rewizja elementu nr "
        Case "ItemAlreadyChangedPart2": T_PL = " zostaly juz zmienione na serwerze (rewizja "
        Case "ItemAlreadyChangedPart3": T_PL = ", status 'w_pracy'), ale podpiecie pliku nie powiodlo sie: "
        Case "ItemAlreadyChangedPart4": T_PL = "Element w PDM ma teraz nowa rewizje, ale nadal plik z poprzedniej wersji -- popraw to recznie w aplikacji webowej (podepnij plik ponownie) albo sprobuj ponownie z tego makra."
    End Select
End Function

Private Function T_EN(ByVal key As String) As String
    Select Case key
        Case "StatusWaitingForBrowser": T_EN = "EasyPDM: waiting for the browser... (Esc to cancel)"
        Case "TitleLoginToPdm": T_EN = "Log in to PDM"
        Case "PromptApiAddress": T_EN = "EasyPDM API address:"
        Case "PromptUsername": T_EN = "Username:"
        Case "PromptPassword": T_EN = "Password (NOTE: this field does not mask typed characters):"
        Case "LoggedInAsPrefix": T_EN = "Logged in as "
        Case "AppTitle": T_EN = "EasyPDM"
        Case "LoginFailedPrefix": T_EN = "Login failed: "
        Case "LoginFailedNoSession": T_EN = "Login failed -- the server did not return a session."
        Case "TitleNewRevision": T_EN = "New revision"
        Case "ItemStatusReleasedPrefix": T_EN = "Item #"
        Case "ItemStatusReleasedSuffix": T_EN = " is in status ""Released"" -- attaching a file requires a new revision. Create a new revision?"
        Case "PromptRevisionComment": T_EN = "New revision comment (optional):"
        Case "PromptTargetFolder": T_EN = "Target folder for local copies (named under the PDM number):"
        Case "TitleTargetFolder": T_EN = "Target folder"
        Case "AssemblyDetectedTitle": T_EN = "Assembly detected"
        Case "NewComponentBrowserPromptPrefix": T_EN = "New component in the tree: "
        Case "NewComponentBrowserPromptSuffix": T_EN = ". Click OK to open the browser and fill it in (it may open in the background -- check the taskbar)."
        Case "AssemblyLinksPart1": T_EN = "This assembly links to "
        Case "AssemblyLinksPart2": T_EN = " other file(s) (parts/sub-assemblies):"
        Case "AssemblyLinksPart3": T_EN = "Send them automatically together with this document (leaves first, this document last)?"
        Case "AssemblyLinksPart4": T_EN = "'No' will not send/create any NEW components -- but components ALREADY in PDM will still be attached to this assembly's structure."
        Case "CreatedButFailedAttachPart1": T_EN = "Created "
        Case "CreatedButFailedAttachPart2": T_EN = ", but failed to attach it under "
        Case "CreatedButFailedAttachPart3": T_EN = ": "
        Case "FailedToSaveDocument": T_EN = "Failed to save the document -- save it manually (Ctrl+S) and run the macro again."
        Case "MustRunInsideSolidWorks": T_EN = "This macro must be run from inside SolidWorks."
        Case "NoActiveSavedDocument": T_EN = "No active, saved document."
        Case "ExportStepPrompt": T_EN = "Export and upload STEP model (3D preview)?"
        Case "ExportPdfPrompt": T_EN = "Export and upload a PDF file?"
        Case "AlreadyLinkedConfirm": T_EN = "This document is already linked to a PDM item. Attach the current version as a new revision/update?"
        Case "AlreadyLinkedConfirmPrefix": T_EN = "This document is already linked to PDM item #"
        Case "AlreadyLinkedConfirmSuffix": T_EN = ". If this is NOT the same part (e.g. you did a Save As from a different, already-linked part) -- click No and link this file manually to the correct item instead. Attach the current version as a new revision/update to THIS item?"
        Case "StaleLinkCleared": T_EN = "The PDM item this document was linked to no longer exists (it was deleted) -- the stale link has been cleared, this document will be treated as not yet sent."
        Case "CancelledNothingSent": T_EN = "Cancelled -- nothing was sent."
        Case "FailedToAttachSubComponent": T_EN = "Failed to attach one of the sub-components under the main element: "
        Case "UploadedSuccessPart1": T_EN = "Uploaded to EasyPDM: item #"
        Case "UploadedSuccessPart2": T_EN = " (revision "
        Case "RunLogPrefix": T_EN = "Run log: "
        Case "SessionExpiredPrompt": T_EN = "Session expired -- run the macro again to log in."
        Case "ErrorPrefix": T_EN = "Error: "
        Case "LoggedOutMessage": T_EN = "Logged out of EasyPDM."
        Case "ServerErrorPrefix": T_EN = "Server error ("
        Case "NoConnectionPrefix": T_EN = "No connection to "
        Case "AttachmentRegistrationFailedPrefix": T_EN = "Attachment registration failed: "
        Case "ItemAlreadyChangedPart1": T_EN = "The status/revision of item #"
        Case "ItemAlreadyChangedPart2": T_EN = " have already been changed on the server (revision "
        Case "ItemAlreadyChangedPart3": T_EN = ", status 'w_pracy'), but attaching the file failed: "
        Case "ItemAlreadyChangedPart4": T_EN = "The item in PDM now has a new revision, but still the file of the previous version -- fix this manually in the web app (attach the file again) or retry from this macro."
    End Select
End Function

Private Function T_DE(ByVal key As String) As String
    Select Case key
        Case "StatusWaitingForBrowser": T_DE = "EasyPDM: Warten auf den Browser... (Esc zum Abbrechen)"
        Case "TitleLoginToPdm": T_DE = "Anmeldung bei PDM"
        Case "PromptApiAddress": T_DE = "EasyPDM-API-Adresse:"
        Case "PromptUsername": T_DE = "Benutzername:"
        Case "PromptPassword": T_DE = "Passwort (HINWEIS: Dieses Feld maskiert die eingegebenen Zeichen nicht):"
        Case "LoggedInAsPrefix": T_DE = "Angemeldet als "
        Case "AppTitle": T_DE = "EasyPDM"
        Case "LoginFailedPrefix": T_DE = "Anmeldung fehlgeschlagen: "
        Case "LoginFailedNoSession": T_DE = "Anmeldung fehlgeschlagen -- der Server hat keine Sitzung zurueckgegeben."
        Case "TitleNewRevision": T_DE = "Neue Revision"
        Case "ItemStatusReleasedPrefix": T_DE = "Element Nr. "
        Case "ItemStatusReleasedSuffix": T_DE = " hat den Status ""Freigegeben"" -- das Anhaengen einer Datei erfordert eine neue Revision. Neue Revision erstellen?"
        Case "PromptRevisionComment": T_DE = "Kommentar zur neuen Revision (optional):"
        Case "PromptTargetFolder": T_DE = "Zielordner fuer lokale Kopien (benannt nach der PDM-Nummer):"
        Case "TitleTargetFolder": T_DE = "Zielordner"
        Case "AssemblyDetectedTitle": T_DE = "Baugruppe erkannt"
        Case "NewComponentBrowserPromptPrefix": T_DE = "Neue Komponente im Baum: "
        Case "NewComponentBrowserPromptSuffix": T_DE = ". Klicken Sie OK, um den Browser zu oeffnen und sie einzugeben (er kann im Hintergrund geoeffnet werden -- pruefen Sie die Taskleiste)."
        Case "AssemblyLinksPart1": T_DE = "Diese Baugruppe verweist auf "
        Case "AssemblyLinksPart2": T_DE = " weitere Datei(en) (Teile/Unterbaugruppen):"
        Case "AssemblyLinksPart3": T_DE = "Sollen sie automatisch zusammen mit diesem Dokument gesendet werden (zuerst die Blaetter, dieses Dokument zuletzt)?"
        Case "AssemblyLinksPart4": T_DE = "'Nein' sendet/erstellt KEINE neuen Komponenten -- aber bereits in PDM vorhandene Komponenten werden trotzdem in die Struktur dieser Baugruppe eingebunden."
        Case "CreatedButFailedAttachPart1": T_DE = "Erstellt "
        Case "CreatedButFailedAttachPart2": T_DE = ", aber die Zuordnung unter "
        Case "CreatedButFailedAttachPart3": T_DE = " ist fehlgeschlagen: "
        Case "FailedToSaveDocument": T_DE = "Speichern des Dokuments fehlgeschlagen -- speichern Sie es manuell (Strg+S) und starten Sie das Makro erneut."
        Case "MustRunInsideSolidWorks": T_DE = "Dieses Makro muss innerhalb von SolidWorks ausgefuehrt werden."
        Case "NoActiveSavedDocument": T_DE = "Kein aktives, gespeichertes Dokument."
        Case "ExportStepPrompt": T_DE = "STEP-Modell exportieren und hochladen (3D-Vorschau)?"
        Case "ExportPdfPrompt": T_DE = "PDF-Datei exportieren und hochladen?"
        Case "AlreadyLinkedConfirm": T_DE = "Dieses Dokument ist bereits mit einem PDM-Element verknuepft. Die aktuelle Version als neue Revision/Aktualisierung anhaengen?"
        Case "AlreadyLinkedConfirmPrefix": T_DE = "Dieses Dokument ist bereits mit PDM-Element Nr. "
        Case "AlreadyLinkedConfirmSuffix": T_DE = " verknuepft. Falls dies NICHT dasselbe Teil ist (z. B. haben Sie ein 'Speichern unter' von einem anderen, bereits verknuepften Teil gemacht) -- klicken Sie Nein und verknuepfen Sie diese Datei stattdessen manuell mit dem richtigen Element. Die aktuelle Version als neue Revision/Aktualisierung DIESES Elements anhaengen?"
        Case "StaleLinkCleared": T_DE = "Das PDM-Element, mit dem dieses Dokument verknuepft war, existiert nicht mehr (wurde geloescht) -- die veraltete Verknuepfung wurde entfernt, dieses Dokument wird als noch nicht gesendet behandelt."
        Case "CancelledNothingSent": T_DE = "Abgebrochen -- es wurde nichts gesendet."
        Case "FailedToAttachSubComponent": T_DE = "Eine der Unterkomponenten konnte nicht unter dem Hauptelement angehaengt werden: "
        Case "UploadedSuccessPart1": T_DE = "Zu EasyPDM hochgeladen: Element Nr. "
        Case "UploadedSuccessPart2": T_DE = " (Revision "
        Case "RunLogPrefix": T_DE = "Ausfuehrungsprotokoll: "
        Case "SessionExpiredPrompt": T_DE = "Sitzung abgelaufen -- fuehren Sie das Makro erneut aus, um sich anzumelden."
        Case "ErrorPrefix": T_DE = "Fehler: "
        Case "LoggedOutMessage": T_DE = "Von EasyPDM abgemeldet."
        Case "ServerErrorPrefix": T_DE = "Serverfehler ("
        Case "NoConnectionPrefix": T_DE = "Keine Verbindung zu "
        Case "AttachmentRegistrationFailedPrefix": T_DE = "Registrierung des Anhangs fehlgeschlagen: "
        Case "ItemAlreadyChangedPart1": T_DE = "Der Status/die Revision von Element Nr. "
        Case "ItemAlreadyChangedPart2": T_DE = " wurden auf dem Server bereits geaendert (Revision "
        Case "ItemAlreadyChangedPart3": T_DE = ", Status 'w_pracy'), aber das Anhaengen der Datei ist fehlgeschlagen: "
        Case "ItemAlreadyChangedPart4": T_DE = "Das Element in PDM hat jetzt eine neue Revision, aber immer noch die Datei der vorherigen Version -- beheben Sie dies manuell in der Web-App (Datei erneut anhaengen) oder versuchen Sie es erneut ueber dieses Makro."
    End Select
End Function


' ============================================================================
' Log -- the only way to see step by step what the macro actually did (and exactly where it
' failed), since there is no console here like in FreeCAD/the browser. A plain text file in
' %TEMP%, appended to (not overwritten) on every run -- open it with Notepad. The path is
' also shown in the summary dialog at the end of "main".
' ============================================================================

Function LogFilePath() As String
    LogFilePath = Environ$("TEMP") & "\EasyPDM_macro.log"
End Function

Sub LogLine(ByVal message As String)
    On Error Resume Next
    Dim fileNum As Integer
    fileNum = FreeFile
    Open LogFilePath() For Append As #fileNum
    Print #fileNum, Format(Now, "yyyy-mm-dd hh:nn:ss") & "  " & message
    Close #fileNum
    On Error GoTo 0
End Sub


' ============================================================================
' Settings (API address / session token / saved display name) -- Windows registry via
' SaveSetting/GetSetting, VBA's standard built-in mechanism for this purpose.
' ============================================================================

Function GetBaseUrl() As String
    Dim url As String
    url = GetSetting(APP_SETTINGS_NAME, SETTINGS_SECTION, "ApiBaseUrl", DEFAULT_BASE_URL)
    If Right(url, 1) = "/" Then url = Left(url, Len(url) - 1)
    GetBaseUrl = url
End Function

Sub SetBaseUrl(ByVal url As String)
    If Right(url, 1) = "/" Then url = Left(url, Len(url) - 1)
    SaveSetting APP_SETTINGS_NAME, SETTINGS_SECTION, "ApiBaseUrl", url
End Sub

Function GetSessionToken() As String
    GetSessionToken = GetSetting(APP_SETTINGS_NAME, SETTINGS_SECTION, "SessionToken", "")
End Function

Sub SetSessionToken(ByVal token As String)
    SaveSetting APP_SETTINGS_NAME, SETTINGS_SECTION, "SessionToken", token
End Sub

Function GetSavedDisplayName() As String
    GetSavedDisplayName = GetSetting(APP_SETTINGS_NAME, SETTINGS_SECTION, "DisplayName", "")
End Function

Sub SetSavedDisplayName(ByVal displayName As String)
    SaveSetting APP_SETTINGS_NAME, SETTINGS_SECTION, "DisplayName", displayName
End Sub

' Same registry key as the download folder in EasyPDMDownload.bas -- deliberately shared,
' so uploaded (locally re-saved under the PDM name) and downloaded files land in the same
' place by default.
Function GetDownloadFolder() As String
    GetDownloadFolder = GetSetting(APP_SETTINGS_NAME, SETTINGS_SECTION, "DownloadFolder", "")
End Function

Sub SetDownloadFolder(ByVal folder As String)
    SaveSetting APP_SETTINGS_NAME, SETTINGS_SECTION, "DownloadFolder", folder
End Sub

' Recursively creates path and all missing parent directories -- identical to
' EasyPDMDownload.bas's own copy (duplicated per this module's no-shared-import
' convention, see file header).
Sub EnsureDirectory(ByVal path As String)
    If path = "" Then Exit Sub
    If Dir(path, vbDirectory) <> "" Then Exit Sub

    Dim parent As String
    Dim sepPos As Long
    Dim trimmed As String
    trimmed = path
    If Right(trimmed, 1) = "\" Then trimmed = Left(trimmed, Len(trimmed) - 1)
    sepPos = InStrRev(trimmed, "\")
    If sepPos > 0 Then
        parent = Left(trimmed, sepPos - 1)
        ' Stop recursing once we hit a drive root ("C:") or UNC root -- Dir()/MkDir cannot
        ' go any higher than that anyway.
        If Len(parent) > 2 And Right(parent, 1) <> ":" Then
            EnsureDirectory parent
        End If
    End If

    On Error Resume Next
    MkDir trimmed
    On Error GoTo 0
End Sub


' ============================================================================
' Minimal JSON -- own parser/builder, good enough for THIS API's response shapes (plain
' objects/arrays/strings/numbers/bool/null), NOT general purpose.
' JSON object -> Scripting.Dictionary, JSON array -> Collection.
' ============================================================================

Function JsonStringEscape(ByVal s As String) As String
    Dim result As String
    result = s
    result = Replace(result, "\", "\\")
    result = Replace(result, """", "\""")
    result = Replace(result, vbCrLf, "\n")
    result = Replace(result, vbCr, "\n")
    result = Replace(result, vbLf, "\n")
    result = Replace(result, vbTab, "\t")
    JsonStringEscape = result
End Function

Function JsonStr(ByVal s As String) As String
    JsonStr = """" & JsonStringEscape(s) & """"
End Function

Function JsonParse(ByVal jsonText As String) As Object
    Dim pos As Long
    pos = 1
    Dim ch As String
    JsonSkipWhitespace jsonText, pos
    ch = Mid(jsonText, pos, 1)
    If ch = "[" Then
        Set JsonParse = JsonParseArray(jsonText, pos)
    Else
        Set JsonParse = JsonParseObject(jsonText, pos)
    End If
End Function

Private Sub JsonSkipWhitespace(ByVal s As String, ByRef pos As Long)
    Dim ch As String
    Do While pos <= Len(s)
        ch = Mid(s, pos, 1)
        If ch <> " " And ch <> vbTab And ch <> vbCr And ch <> vbLf Then Exit Do
        pos = pos + 1
    Loop
End Sub

Private Function JsonParseValue(ByVal s As String, ByRef pos As Long) As Variant
    JsonSkipWhitespace s, pos
    Dim ch As String
    ch = Mid(s, pos, 1)
    If ch = "{" Then
        Set JsonParseValue = JsonParseObject(s, pos)
    ElseIf ch = "[" Then
        Set JsonParseValue = JsonParseArray(s, pos)
    ElseIf ch = """" Then
        JsonParseValue = JsonParseString(s, pos)
    ElseIf ch = "t" Then
        pos = pos + 4 ' "true"
        JsonParseValue = True
    ElseIf ch = "f" Then
        pos = pos + 5 ' "false"
        JsonParseValue = False
    ElseIf ch = "n" Then
        pos = pos + 4 ' "null"
        JsonParseValue = Null
    Else
        JsonParseValue = JsonParseNumber(s, pos)
    End If
End Function

Private Function JsonParseObject(ByVal s As String, ByRef pos As Long) As Object
    Dim dict As Object
    Set dict = CreateObject("Scripting.Dictionary")

    JsonSkipWhitespace s, pos
    If Mid(s, pos, 1) <> "{" Then
        Set JsonParseObject = dict
        Exit Function
    End If
    pos = pos + 1 ' "{"
    JsonSkipWhitespace s, pos
    If Mid(s, pos, 1) = "}" Then
        pos = pos + 1
        Set JsonParseObject = dict
        Exit Function
    End If

    Dim key As String
    Do
        JsonSkipWhitespace s, pos
        key = JsonParseString(s, pos)
        JsonSkipWhitespace s, pos
        pos = pos + 1 ' ":"
        dict.Add key, JsonParseValue(s, pos)
        JsonSkipWhitespace s, pos
        If Mid(s, pos, 1) = "," Then
            pos = pos + 1
        Else
            Exit Do
        End If
    Loop
    JsonSkipWhitespace s, pos
    pos = pos + 1 ' "}"
    Set JsonParseObject = dict
End Function

Private Function JsonParseArray(ByVal s As String, ByRef pos As Long) As Object
    Dim coll As New Collection
    pos = pos + 1 ' "["
    JsonSkipWhitespace s, pos
    If Mid(s, pos, 1) = "]" Then
        pos = pos + 1
        Set JsonParseArray = coll
        Exit Function
    End If
    Do
        coll.Add JsonParseValue(s, pos)
        JsonSkipWhitespace s, pos
        If Mid(s, pos, 1) = "," Then
            pos = pos + 1
        Else
            Exit Do
        End If
    Loop
    JsonSkipWhitespace s, pos
    pos = pos + 1 ' "]"
    Set JsonParseArray = coll
End Function

Private Function JsonParseString(ByVal s As String, ByRef pos As Long) As String
    Dim result As String
    Dim ch As String
    Dim nextCh As String
    Dim hexCode As String
    result = ""
    If Mid(s, pos, 1) = """" Then pos = pos + 1 ' skip opening "
    Do While pos <= Len(s)
        ch = Mid(s, pos, 1)
        If ch = """" Then
            pos = pos + 1
            Exit Do
        ElseIf ch = "\" Then
            nextCh = Mid(s, pos + 1, 1)
            Select Case nextCh
                Case """": result = result & """"
                Case "\": result = result & "\"
                Case "/": result = result & "/"
                Case "n": result = result & vbLf
                Case "r": result = result & vbCr
                Case "t": result = result & vbTab
                Case "u"
                    hexCode = Mid(s, pos + 2, 4)
                    result = result & ChrW(Val("&H" & hexCode))
                    pos = pos + 4
                Case Else
                    result = result & nextCh
            End Select
            pos = pos + 2
        Else
            result = result & ch
            pos = pos + 1
        End If
    Loop
    JsonParseString = result
End Function

Private Function JsonParseNumber(ByVal s As String, ByRef pos As Long) As Double
    Dim startPos As Long
    startPos = pos
    Do While pos <= Len(s) And InStr("0123456789+-.eE", Mid(s, pos, 1)) > 0
        pos = pos + 1
    Loop
    ' Val() (not CDbl!) -- CDbl depends on the current system locale's decimal separator
    ' (e.g. a comma on Polish Windows), which would misinterpret "0.1" from JSON; Val()
    ' always expects a dot, regardless of locale.
    JsonParseNumber = Val(Mid(s, startPos, pos - startPos))
End Function

' Safe read of a string field from a parsed object (Dictionary) -- returns defaultValue if
' the object is Nothing, the field does not exist, or it is a JSON null.
Function JsonGetString(ByVal obj As Object, ByVal key As String, Optional ByVal defaultValue As String = "") As String
    If obj Is Nothing Then
        JsonGetString = defaultValue
        Exit Function
    End If
    If Not obj.Exists(key) Then
        JsonGetString = defaultValue
        Exit Function
    End If
    If IsNull(obj.Item(key)) Then
        JsonGetString = defaultValue
    Else
        JsonGetString = CStr(obj.Item(key))
    End If
End Function

Function JsonGetLong(ByVal obj As Object, ByVal key As String, Optional ByVal defaultValue As Long = 0) As Long
    If obj Is Nothing Then
        JsonGetLong = defaultValue
        Exit Function
    End If
    If Not obj.Exists(key) Then
        JsonGetLong = defaultValue
        Exit Function
    End If
    If IsNull(obj.Item(key)) Then
        JsonGetLong = defaultValue
    Else
        JsonGetLong = CLng(obj.Item(key))
    End If
End Function


' ============================================================================
' HTTP -- MSXML2.XMLHTTP (synchronous), with the session cookie attached MANUALLY to every
' request (we don't rely on automatic cookie handling by the COM component).
' ============================================================================

Private Function NewHttpRequest() As Object
    Set NewHttpRequest = CreateObject("MSXML2.XMLHTTP.6.0")
End Function

' Used ONLY by ApiUploadFile (binary/multipart body) -- see the comment there for why plain
' MSXML2.XMLHTTP is not used for that one call. WinHttpRequest is a standard, universally
' available Windows component (WinHTTP, present since Windows XP SP2/Server 2003).
Private Function NewBinaryHttpRequest() As Object
    Set NewBinaryHttpRequest = CreateObject("WinHttp.WinHttpRequest.5.1")
End Function

Private Function AuthCookieHeader() As String
    Dim token As String
    token = GetSessionToken()
    If token <> "" Then
        AuthCookieHeader = SESSION_COOKIE_NAME & "=" & token
    Else
        AuthCookieHeader = ""
    End If
End Function

Private Sub RaiseForStatus(ByVal status As Long, ByVal responseText As String)
    If status = 401 Then
        Err.Raise ERR_AUTH, "EasyPDM", responseText
    ElseIf status < 200 Or status >= 300 Then
        Err.Raise ERR_API, "EasyPDM", T("ServerErrorPrefix") & status & "): " & responseText
    End If
End Sub

Function ApiGet(ByVal path As String) As Object
    Dim http As Object
    Set http = NewHttpRequest()
    ' MSXML2.XMLHTTP.6.0 GETs can be served from Windows' local HTTP cache -- confirmed in
    ' practice: after a status/revision change on the server, a later GET for the SAME URL
    ' kept returning the response from the VERY FIRST time this URL was ever fetched, making
    ' the macro believe an item was permanently stuck at its original status/revision. The
    ' Cache-Control/Pragma headers below are the standard way to ask for a fresh response,
    ' but the query-string cache-buster guarantees one regardless of whether those headers
    ' are actually honored (a different URL can never hit an old cache entry).
    Dim cacheBuster As String
    cacheBuster = IIf(InStr(path, "?") > 0, "&", "?") & "_ts=" & Format(Now, "yyyymmddhhnnss") & CStr(Timer)
    http.Open "GET", GetBaseUrl() & path & cacheBuster, False
    http.setRequestHeader "Cache-Control", "no-cache, no-store"
    http.setRequestHeader "Pragma", "no-cache"
    Dim cookie As String
    cookie = AuthCookieHeader()
    If cookie <> "" Then http.setRequestHeader "Cookie", cookie

    On Error GoTo NetErr
    http.send
    On Error GoTo 0

    LogLine "GET " & path & " -> " & http.Status
    RaiseForStatus http.Status, http.responseText
    If Trim(http.responseText) = "" Then
        Set ApiGet = Nothing
    Else
        Set ApiGet = JsonParse(http.responseText)
    End If
    Exit Function
NetErr:
    LogLine "GET " & path & " -> NO CONNECTION: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", T("NoConnectionPrefix") & GetBaseUrl() & ": " & Err.Description
End Function

Function ApiPostJson(ByVal path As String, ByVal bodyJson As String) As Object
    Dim http As Object
    Set http = NewHttpRequest()
    http.Open "POST", GetBaseUrl() & path, False
    http.setRequestHeader "Content-Type", "application/json"
    Dim cookie As String
    cookie = AuthCookieHeader()
    If cookie <> "" Then http.setRequestHeader "Cookie", cookie

    On Error GoTo NetErr
    http.send bodyJson
    On Error GoTo 0

    LogLine "POST " & path & " -> " & http.Status
    RaiseForStatus http.Status, http.responseText
    If Trim(http.responseText) = "" Then
        Set ApiPostJson = Nothing
    Else
        Set ApiPostJson = JsonParse(http.responseText)
    End If
    Exit Function
NetErr:
    LogLine "POST " & path & " -> NO CONNECTION: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", T("NoConnectionPrefix") & GetBaseUrl() & ": " & Err.Description
End Function

Function ApiPatchJson(ByVal path As String, ByVal bodyJson As String) As Object
    Dim http As Object
    Set http = NewHttpRequest()
    http.Open "PATCH", GetBaseUrl() & path, False
    http.setRequestHeader "Content-Type", "application/json"
    Dim cookie As String
    cookie = AuthCookieHeader()
    If cookie <> "" Then http.setRequestHeader "Cookie", cookie

    On Error GoTo NetErr
    http.send bodyJson
    On Error GoTo 0

    LogLine "PATCH " & path & " -> " & http.Status
    RaiseForStatus http.Status, http.responseText
    If Trim(http.responseText) = "" Then
        Set ApiPatchJson = Nothing
    Else
        Set ApiPatchJson = JsonParse(http.responseText)
    End If
    Exit Function
NetErr:
    LogLine "PATCH " & path & " -> NO CONNECTION: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", T("NoConnectionPrefix") & GetBaseUrl() & ": " & Err.Description
End Function

Function ApiRegisterAttachment(ByVal itemId As String, ByVal filePath As String, Optional ByVal role As String = "") As Object
    Dim bodyJson As String
    bodyJson = "{""filePath"":" & JsonStr(filePath)
    If role <> "" Then bodyJson = bodyJson & ",""role"":" & JsonStr(role)
    bodyJson = bodyJson & "}"
    Set ApiRegisterAttachment = ApiPostJson("/items/" & itemId & "/attachments/register", bodyJson)
End Function

' Reads the whole file as a byte array -- used for the plain HTTP upload (when the PDM
' storage is not visible in this machine's file system).
Private Function ReadFileBytes(ByVal filePath As String) As Byte()
    Dim fileNum As Integer
    Dim buffer() As Byte
    fileNum = FreeFile
    Open filePath For Binary Access Read As #fileNum
    If LOF(fileNum) > 0 Then
        ReDim buffer(1 To LOF(fileNum))
        Get #fileNum, , buffer
    Else
        ReDim buffer(0 To -1) ' empty array
    End If
    Close #fileNum
    ReadFileBytes = buffer
End Function

Private Sub CopyBytesInto(ByRef dest() As Byte, ByRef destOffset As Long, ByRef src() As Byte)
    Dim n As Long
    n = UBound(src) - LBound(src) + 1
    If n <= 0 Then Exit Sub
    Dim i As Long
    For i = 0 To n - 1
        dest(destOffset + i) = src(LBound(src) + i)
    Next i
    destOffset = destOffset + n
End Sub

' Plain HTTP upload (multipart/form-data), used as a fallback when the PDM storage is not
' visible in this machine's file system -- the same mechanism as attaching CAD files from
' the properties panel in the web app.
Function ApiUploadFile(ByVal path As String, ByVal filePath As String, Optional ByVal overrideFilename As String = "", Optional ByVal extraFieldName As String = "", Optional ByVal extraFieldValue As String = "") As Object
    Dim boundary As String
    boundary = "----EasyPDMBoundary" & Format(Now, "yyyymmddhhnnss") & CStr(Int(Rnd * 100000))

    Dim fileName As String
    If overrideFilename <> "" Then
        fileName = overrideFilename
    Else
        fileName = Mid(filePath, InStrRev(filePath, "\") + 1)
    End If

    ' Extra plain form field BEFORE the file part -- "role=step" (see UploadStepAttachment)
    ' or "role=cad" (see RenameAndUpload, tags the actual uploaded CAD file so the web app
    ' can show it separately from ordinary, manually-added attachments).
    Dim head As String
    head = ""
    If extraFieldName <> "" Then
        head = head & "--" & boundary & vbCrLf & _
               "Content-Disposition: form-data; name=""" & extraFieldName & """" & vbCrLf & vbCrLf & _
               extraFieldValue & vbCrLf
    End If
    head = head & "--" & boundary & vbCrLf & _
           "Content-Disposition: form-data; name=""file""; filename=""" & fileName & """" & vbCrLf & _
           "Content-Type: application/octet-stream" & vbCrLf & vbCrLf

    Dim tail As String
    tail = vbCrLf & "--" & boundary & "--" & vbCrLf

    Dim headBytes() As Byte, tailBytes() As Byte, fileBytes() As Byte
    headBytes = StrConv(head, vbFromUnicode)
    tailBytes = StrConv(tail, vbFromUnicode)
    fileBytes = ReadFileBytes(filePath)

    Dim totalLen As Long
    totalLen = (UBound(headBytes) - LBound(headBytes) + 1) + _
               (UBound(fileBytes) - LBound(fileBytes) + 1) + _
               (UBound(tailBytes) - LBound(tailBytes) + 1)

    Dim body() As Byte
    ReDim body(0 To totalLen - 1)
    Dim offset As Long
    offset = 0
    CopyBytesInto body, offset, headBytes
    CopyBytesInto body, offset, fileBytes
    CopyBytesInto body, offset, tailBytes

    ' WinHttp.WinHttpRequest.5.1 (NOT MSXML2.XMLHTTP -- see NewHttpRequest/NewBinaryHttpRequest)
    ' for this one call: MSXML2.XMLHTTP.send() rejects a raw Byte() array ("The parameter is
    ' incorrect") and sending it wrapped in an ADODB.Stream instead -- the commonly documented
    ' workaround for that -- was ITSELF unreliable in practice (a real upload failed with a
    ' generic "NO CONNECTION"/WinINet-style error while plain JSON requests to the very same
    ' server succeeded moments earlier, on a live SolidWorks 2026 install). WinHttpRequest
    ' accepts a Byte() array directly via .Send and handles Content-Length for it reliably --
    ' standard, well-documented approach for binary/multipart POST bodies from VBA.
    Dim http As Object
    Set http = NewBinaryHttpRequest()
    http.Open "POST", GetBaseUrl() & path, False
    http.SetRequestHeader "Content-Type", "multipart/form-data; boundary=" & boundary
    Dim cookie As String
    cookie = AuthCookieHeader()
    If cookie <> "" Then http.SetRequestHeader "Cookie", cookie

    On Error GoTo NetErr
    If totalLen > 0 Then
        http.Send body
    Else
        http.Send
    End If
    On Error GoTo 0

    LogLine "POST (upload, " & totalLen & " B) " & path & " -> " & http.Status
    RaiseForStatus http.Status, http.ResponseText
    If Trim(http.ResponseText) = "" Then
        Set ApiUploadFile = Nothing
    Else
        Set ApiUploadFile = JsonParse(http.ResponseText)
    End If
    Exit Function
NetErr:
    LogLine "POST (upload) " & path & " -> NO CONNECTION: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", T("NoConnectionPrefix") & GetBaseUrl() & ": " & Err.Description
End Function

Sub ApiDeleteRequest(ByVal path As String)
    Dim http As Object
    Set http = NewHttpRequest()
    http.Open "DELETE", GetBaseUrl() & path, False
    Dim cookie As String
    cookie = AuthCookieHeader()
    If cookie <> "" Then http.setRequestHeader "Cookie", cookie

    On Error GoTo NetErr
    http.send
    On Error GoTo 0

    LogLine "DELETE " & path & " -> " & http.Status
    RaiseForStatus http.Status, http.responseText
    Exit Sub
NetErr:
    LogLine "DELETE " & path & " -> NO CONNECTION: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", T("NoConnectionPrefix") & GetBaseUrl() & ": " & Err.Description
End Sub


' ============================================================================
' Browser ticket flow -- lets the web app (already running, same backend) decide "new item
' vs duplicate vs attach to existing" instead of a native dialog, exactly like
' EasyPDM.FreeCad/EasyPDMUpload.FCMacro's submit_via_browser. The macro: generates a GUID
' ticket, opens the browser on the token->cookie bridge (GET /api/auth/browser-login) with
' a deep-link to "?ticket=...", waits (WaitForTicket) while the user resolves it in the
' "pending request from a CAD macro" popup, then reads back what happened via
' GET /api/create-tickets/{ticket}.
' ============================================================================

' A v4-ish GUID good enough for a short-lived, purely correlational ticket (never stored
' anywhere persistent) -- VBA has no built-in GUID generator, this is the standard
' workaround. Scriptlet.TypeLib.Guid returns "{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}";
' strip the braces defensively via InStr rather than assuming fixed positions, in case a
' particular Windows/VBA combination pads it differently.
Function NewGuid() As String
    Dim raw As String
    raw = CreateObject("Scriptlet.TypeLib").Guid

    Dim openBrace As Long, closeBrace As Long
    openBrace = InStr(raw, "{")
    closeBrace = InStr(raw, "}")
    If openBrace > 0 And closeBrace > openBrace Then
        raw = Mid(raw, openBrace + 1, closeBrace - openBrace - 1)
    End If
    If Len(raw) > 36 Then raw = Left(raw, 36)
    NewGuid = LCase(raw)
End Function

' Percent-encoding for a query string component -- VBA has no built-in URL encoder.
' Operates on UTF-8 BYTES (not characters), so a document name with Polish diacritics
' encodes correctly, not just plain ASCII tickets/names.
Function UrlEncode(ByVal s As String) As String
    If Len(s) = 0 Then
        UrlEncode = ""
        Exit Function
    End If

    Dim stream As Object
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2 ' adTypeText
    stream.Charset = "utf-8"
    stream.Open
    stream.WriteText s
    stream.Position = 0
    stream.Type = 1 ' adTypeBinary
    Dim bytes() As Byte
    bytes = stream.Read
    stream.Close

    ' ADODB.Stream prepends a UTF-8 BOM (EF BB BF) when converting text->binary this way
    ' (confirmed in practice) -- must be stripped, or it corrupts the very first encoded
    ' character.
    If UBound(bytes) >= 2 Then
        If bytes(0) = &HEF And bytes(1) = &HBB And bytes(2) = &HBF Then
            Dim trimmed() As Byte
            ReDim trimmed(0 To UBound(bytes) - 3)
            Dim k As Long
            For k = 3 To UBound(bytes)
                trimmed(k - 3) = bytes(k)
            Next k
            bytes = trimmed
        End If
    End If

    Dim result As String
    Dim i As Long, b As Byte
    result = ""
    For i = LBound(bytes) To UBound(bytes)
        b = bytes(i)
        If (b >= 65 And b <= 90) Or (b >= 97 And b <= 122) Or (b >= 48 And b <= 57) _
           Or b = 45 Or b = 95 Or b = 46 Or b = 126 Then ' A-Z a-z 0-9 - _ . ~
            result = result & Chr(b)
        Else
            result = result & "%" & Right("0" & Hex(b), 2)
        End If
    Next i
    UrlEncode = result
End Function

Sub OpenUrlInBrowser(ByVal url As String)
    CreateObject("WScript.Shell").Run """" & url & """", 1, False
End Sub

' Address that logs the browser in (token->cookie bridge, same secret the macro already
' holds for its own API calls) and deep-links straight to the "pending request from a CAD
' macro" popup for THIS ticket -- see pending-create-ticket.ts/PendingTicketBanner in the
' web app. Deliberately WITHOUT a suggested item number hint (unlike the FreeCAD version):
' this macro already has a MORE reliable way to recognize "already uploaded" via Custom
' Properties (see GetLinkedItemId), so there is nothing useful to suggest here.
Function BuildBrowserCreateUrl(ByVal ticket As String, ByVal name As String) As String
    Dim redirectPath As String
    redirectPath = "/?ticket=" & UrlEncode(ticket)
    If name <> "" Then redirectPath = redirectPath & "&name=" & UrlEncode(name)

    BuildBrowserCreateUrl = GetBaseUrl() & "/auth/browser-login?token=" & UrlEncode(GetSessionToken()) & "&redirect=" & UrlEncode(redirectPath)
End Function

' Polls GET /create-tickets/{ticket} until the user resolves it in the browser, the wait
' times out (10 minutes), or the user presses Escape. Returns the parsed ticket data
' (Dictionary with itemId/itemNumber/name/exportStep/existing) on success, or Nothing on
' cancel/timeout -- the caller treats both the same way ("nothing was sent").
'
' No UserForm exists in this file (see file header) to host a visible "Cancel" button, so
' Escape (checked every tick via GetAsyncKeyState) is the only available cancel gesture;
' progress is shown in SolidWorks's own status bar instead of a dialog. Uses tick/poll
' COUNTERS rather than Timer()/Now() on purpose -- Timer() resets at midnight, which would
' misfire the 10-minute timeout for a wait that happens to straddle it.
Function WaitForTicket(ByVal ticket As String) As Object
    Const TICK_MS As Long = 400
    Const POLL_EVERY_MS As Long = 2000
    Const TIMEOUT_MS As Long = 600000 ' 10 minutes, same as EasyPDMUpload.FCMacro

    Dim elapsedMs As Long, sincePollMs As Long
    elapsedMs = 0
    sincePollMs = POLL_EVERY_MS ' poll right away on the very first tick

    On Error Resume Next
    swApp.Frame.SetStatusBarText T("StatusWaitingForBrowser")
    On Error GoTo 0

    Do While elapsedMs < TIMEOUT_MS
        Sleep TICK_MS
        DoEvents
        elapsedMs = elapsedMs + TICK_MS
        sincePollMs = sincePollMs + TICK_MS

        If (GetAsyncKeyState(VK_ESCAPE) And &H8000) <> 0 Then
            LogLine "WaitForTicket: cancelled by Escape."
            GoTo TimedOutOrCancelled
        End If

        If sincePollMs >= POLL_EVERY_MS Then
            sincePollMs = 0
            Dim data As Object
            Set data = Nothing
            On Error Resume Next
            Set data = ApiGet("/create-tickets/" & ticket)
            On Error GoTo 0
            If Not data Is Nothing Then
                If JsonGetString(data, "itemId", "") <> "" Then
                    Set WaitForTicket = data
                    GoTo Cleanup
                End If
            End If
        End If
    Loop
    LogLine "WaitForTicket: timed out after " & (TIMEOUT_MS \ 1000) & "s."

TimedOutOrCancelled:
    Set WaitForTicket = Nothing
Cleanup:
    On Error Resume Next
    swApp.Frame.SetStatusBarText ""
    On Error GoTo 0
End Function


' ============================================================================
' Login / session
' ============================================================================

Function ApiLogin(ByVal username As String, ByVal password As String) As Object
    Dim http As Object
    Set http = NewHttpRequest()
    http.Open "POST", GetBaseUrl() & "/auth/login", False
    http.setRequestHeader "Content-Type", "application/json"

    Dim bodyJson As String
    bodyJson = "{""username"":" & JsonStr(username) & ",""password"":" & JsonStr(password) & "}"

    On Error GoTo NetErr
    http.send bodyJson
    On Error GoTo 0

    LogLine "POST /auth/login -> " & http.Status & " (user: " & username & ")"
    If http.Status = 401 Or http.Status < 200 Or http.Status >= 300 Then
        LogLine "Login failed: " & http.responseText
        Err.Raise ERR_API, "EasyPDM", T("LoginFailedPrefix") & http.responseText
    End If

    Dim user As Object
    Set user = JsonParse(http.responseText)

    ' Token read FIRST from the JSON response body ("sessionToken") -- MSXML2.XMLHTTP.6.0
    ' does not reliably expose the Set-Cookie header (known limitation of COM/WinHTTP
    ' components), so the server also puts the token there specifically for this reason.
    ' The cookie stays as a fallback source (older server versions, in case the API was
    ' not updated).
    Dim token As String
    token = JsonGetString(user, "sessionToken", "")
    If token = "" Then token = ExtractSessionCookie(http)
    If token = "" Then
        LogLine "Login: server responded 2xx, but no session token was found either in the response body or in the headers."
        Err.Raise ERR_API, "EasyPDM", T("LoginFailedNoSession")
    End If

    SetSessionToken token
    Dim displayName As String
    displayName = JsonGetString(user, "displayName", "")
    If displayName = "" Then displayName = JsonGetString(user, "username", username)
    SetSavedDisplayName displayName
    LogLine "Logged in as " & displayName & "."

    Set ApiLogin = user
    Exit Function
NetErr:
    LogLine "POST /auth/login -> NO CONNECTION: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", T("NoConnectionPrefix") & GetBaseUrl() & ": " & Err.Description
End Function

' getResponseHeader("Set-Cookie") is sometimes filtered out by MSXML/WinINet security
' measures -- as a fallback we search for the same header in the FULL header list, if the
' first attempt returns nothing. Kept as a secondary source now that the token is normally
' read from the JSON response body instead (see ApiLogin).
Private Function ExtractSessionCookie(ByVal http As Object) As String
    Dim raw As String
    Dim result As String

    On Error Resume Next
    raw = http.getResponseHeader("Set-Cookie")
    On Error GoTo 0
    result = FindCookieInText(raw)
    If result <> "" Then
        ExtractSessionCookie = result
        Exit Function
    End If

    On Error Resume Next
    raw = http.getAllResponseHeaders()
    On Error GoTo 0
    ExtractSessionCookie = FindCookieInText(raw)
End Function

Private Function FindCookieInText(ByVal text As String) As String
    Dim normalized As String
    normalized = Replace(text, vbCrLf, vbLf)
    normalized = Replace(normalized, vbCr, vbLf)

    Dim marker As String
    marker = SESSION_COOKIE_NAME & "="
    Dim startPos As Long
    startPos = InStr(1, normalized, marker, vbTextCompare)
    If startPos = 0 Then
        FindCookieInText = ""
        Exit Function
    End If

    Dim rest As String
    rest = Mid(normalized, startPos + Len(marker))
    Dim semiPos As Long, linePos As Long, endPos As Long
    semiPos = InStr(1, rest, ";")
    linePos = InStr(1, rest, vbLf)
    If linePos > 0 And (semiPos = 0 Or linePos < semiPos) Then
        endPos = linePos
    Else
        endPos = semiPos
    End If

    If endPos = 0 Then
        FindCookieInText = Trim(rest)
    Else
        FindCookieInText = Trim(Left(rest, endPos - 1))
    End If
End Function

Sub ApiLogout()
    Dim token As String
    token = GetSessionToken()
    If token <> "" Then
        On Error Resume Next
        Dim http As Object
        Set http = NewHttpRequest()
        http.Open "POST", GetBaseUrl() & "/auth/logout", False
        http.setRequestHeader "Cookie", SESSION_COOKIE_NAME & "=" & token
        http.send
        On Error GoTo 0
    End If
    SetSessionToken ""
    SetSavedDisplayName ""
End Sub

' Checks whether the saved session is still valid (GET /auth/me); if there is none, or it
' expired/was revoked, shows the login dialog. Returns True if there is an active session
' after this call (can continue), False if the user cancelled the login.
Function EnsureLoggedIn() As Boolean
    If GetSessionToken() <> "" Then
        Dim result As Object
        On Error Resume Next
        Err.Clear
        Set result = ApiGet("/auth/me")
        Dim errNum As Long
        errNum = Err.Number
        On Error GoTo 0

        If errNum = 0 Then
            EnsureLoggedIn = True
            Exit Function
        ElseIf errNum = ERR_AUTH Then
            SetSessionToken "" ' token expired/invalid -- don't try it again
        Else
            ' E.g. no connection -- don't block with a login prompt at this check stage;
            ' the error will surface clearly at the first real API call anyway.
            EnsureLoggedIn = True
            Exit Function
        End If
    End If

    EnsureLoggedIn = PromptLogin()
End Function

Private Function PromptLogin() As Boolean
    Dim serverUrl As String
    serverUrl = InputBox(T("PromptApiAddress"), T("TitleLoginToPdm"), GetBaseUrl())
    If Trim(serverUrl) = "" Then
        PromptLogin = False
        Exit Function
    End If
    SetBaseUrl serverUrl

    Dim attempt As Integer
    For attempt = 1 To 3
        Dim username As String, password As String
        username = InputBox(T("PromptUsername"), T("TitleLoginToPdm"))
        If Trim(username) = "" Then
            PromptLogin = False
            Exit Function
        End If
        ' A plain InputBox does not mask typed text with asterisks -- a limitation of this
        ' simplified macro (no custom UserForm with a password field).
        password = InputBox(T("PromptPassword"), T("TitleLoginToPdm"))
        If password = "" Then
            PromptLogin = False
            Exit Function
        End If

        Dim user As Object
        Dim loginErrNum As Long, loginErrDesc As String
        On Error Resume Next
        Err.Clear
        Set user = ApiLogin(username, password)
        loginErrNum = Err.Number
        loginErrDesc = Err.Description
        On Error GoTo 0

        If loginErrNum = 0 Then
            MsgBox T("LoggedInAsPrefix") & JsonGetString(user, "displayName", username) & ".", vbInformation, T("AppTitle")
            PromptLogin = True
            Exit Function
        Else
            MsgBox T("LoginFailedPrefix") & loginErrDesc, vbExclamation, T("AppTitle")
        End If
    Next attempt
    PromptLogin = False
End Function


' ============================================================================
' Helpers -- filename sanitization, revision label formatting, Part/Assembly properties.
' ============================================================================

Function SanitizeFilename(ByVal name As String) As String
    Dim result As String
    result = name
    Dim badChars As String
    badChars = "\/:*?""<>|"
    Dim i As Long
    For i = 1 To Len(badChars)
        result = Replace(result, Mid(badChars, i, 1), "_")
    Next i
    result = Trim(result)
    If result = "" Then result = "unnamed"
    SanitizeFilename = result
End Function

' Revisions as uppercase letters instead of digits: 1->A, 2->B, ..., 26->Z, 27->AA... (like
' spreadsheet column numbering) -- the same conversion as revisionLabel() in the web
' frontend and in the FreeCAD macro; the actual number in the database (revisionNumber)
' does not change.
Function RevisionLabel(ByVal n As Long) As String
    Dim label As String
    Dim remainder As Long
    label = ""
    Do While n > 0
        remainder = (n - 1) Mod 26
        label = Chr(65 + remainder) & label
        n = (n - 1) \ 26
    Loop
    If label = "" Then label = "A"
    RevisionLabel = label
End Function

' ============================================================================
' PDM file storage -- if visible in this machine's file system (client and server on the
' same disk), the copy goes directly there and is REGISTERED without a second HTTP
' upload; otherwise a plain upload (see ApiUploadFile).
' ============================================================================

Function GetStorageRoot() As String
    Dim result As String
    result = ""
    On Error Resume Next
    Dim config As Object
    Set config = ApiGet("/config")
    If Not config Is Nothing Then result = JsonGetString(config, "storageRoot", "")
    On Error GoTo 0
    GetStorageRoot = result
End Function

' Shared ending of both modes: locally SAVES AS the current document under the name
' "number (name).REVISION.extension" in targetFolder (same convention as itemDisplayLabel
' in the frontend and _save_local_as_pdm_name/_rename_and_upload in the FreeCAD macro --
' so that an assembly referencing this document, saved AFTER it in the same or a later
' macro run, picks up the new file path automatically instead of reporting a broken link),
' then uploads THAT (possibly just-renamed) file into PDM. Skips the Save As if the file is
' ALREADY at the target path (re-uploading the same revision without a new one -- a no-op
' Save As onto the document's own current path). The ORIGINAL file at its old path/name, if
' different, is left on disk untouched -- neither moved nor deleted. Every upload is tagged
' role="cad" (both the storage-copy register path and the plain HTTP upload path below), so
' the web app can show it under its "CAD attachments" section, separately from ordinary,
' attachments -- one per revision (unique filename per revision means these ACCUMULATE,
' unlike the single-slot "pdf"/"step" roles which replace the previous attachment).
Function RenameAndUpload(ByVal swModel As Object, ByVal filePath As String, ByVal itemId As String, ByVal itemNumber As Long, ByVal name As String, ByVal revision As Long, ByVal targetFolder As String) As Boolean
    Dim ext As String
    Dim dotPos As Long
    dotPos = InStrRev(filePath, ".")
    If dotPos > 0 Then ext = Mid(filePath, dotPos) Else ext = ""

    Dim newFilename As String
    newFilename = itemNumber & " (" & SanitizeFilename(name) & ")." & RevisionLabel(revision) & ext

    ' UNVERIFIED against a live SolidWorks install for this SPECIFIC use (same-format
    ' native Save As, as opposed to UploadStepAttachment's format-CONVERTING SaveAs) --
    ' written from documented SolidWorks API behavior (IModelDocExtension.SaveAs to the
    ' same file extension updates the open document's own identity/path, same as File ->
    ' Save As). A failure here is logged but NOT fatal -- the upload below still proceeds
    ' from the original path, matching the tolerant style already used for STEP export.
    Dim newLocalPath As String
    newLocalPath = targetFolder & "\" & newFilename
    If LCase(newLocalPath) <> LCase(filePath) Then
        Dim saveErrors As Long, saveWarnings As Long
        Dim saveOk As Boolean
        Dim saveAsErrNum As Long
        On Error Resume Next
        Err.Clear
        saveOk = swModel.Extension.SaveAs(newLocalPath, 0, SW_SAVE_AS_SILENT, Nothing, saveErrors, saveWarnings)
        saveAsErrNum = Err.Number
        On Error GoTo 0
        If saveOk And saveAsErrNum = 0 Then
            LogLine "Saved local copy under PDM name: " & newLocalPath
            filePath = newLocalPath
        Else
            LogLine "Local Save As to """ & newLocalPath & """ failed (errors=" & saveErrors & ", warnings=" & saveWarnings & ", err=" & saveAsErrNum & ") -- uploading from the original path instead."
        End If
    End If

    ' Embed the PDM link into the file itself BEFORE uploading -- setting the Custom
    ' Property alone only changes the in-memory document; SolidWorks only writes it to disk
    ' on the NEXT save, so without this extra save (even when no SaveAs happened above) the
    ' copy of the file that lands on the server -- and therefore any later download of this
    ' item via EasyPDMDownload.bas -- would never carry the link (confirmed in practice:
    ' downloaded files were missing EasyPDM_ItemId/EasyPDM_ItemNumber entirely, because the
    ' property used to be set only AFTER the upload already happened, from the caller's side
    ' -- see main()/ProcessAssemblyTree, whose own SetLinkedItem/SetLinkedItemOn calls after
    ' a successful upload are now a redundant final refresh, not the only place it happens).
    ' A failure here is logged but NOT fatal, matching this function's existing tolerant
    ' style -- the upload still proceeds even if the file ends up missing the embedded link.
    SetLinkedItemOn swModel, itemId, CStr(itemNumber)
    Dim linkSaveErrors As Long, linkSaveWarnings As Long
    On Error Resume Next
    Err.Clear
    swModel.Save3 0, linkSaveErrors, linkSaveWarnings
    If Err.Number <> 0 Then
        LogLine "Warning: could not re-save after setting the PDM link Custom Property (" & Err.Description & ") -- the uploaded copy may be missing it."
    End If
    On Error GoTo 0

    LogLine "RenameAndUpload: item #" & itemNumber & ", local file """ & filePath & """, new name """ & newFilename & """"

    Dim storageRoot As String
    storageRoot = GetStorageRoot()
    If storageRoot <> "" Then
        LogLine "PDM storage visible locally: " & storageRoot
    Else
        LogLine "PDM storage not visible from this machine (GET /config failed or empty response) -- will use plain HTTP upload."
    End If

    Dim targetPath As String
    targetPath = ""
    If storageRoot <> "" Then
        Dim componentsDir As String
        componentsDir = storageRoot & "\components"
        On Error Resume Next
        If Dir(componentsDir, vbDirectory) = "" Then MkDir componentsDir
        On Error GoTo 0

        If Dir(componentsDir, vbDirectory) <> "" Then
            Dim candidate As String
            candidate = componentsDir & "\" & newFilename
            Dim copyErrNum As Long
            On Error Resume Next
            Err.Clear
            FileCopy filePath, candidate
            copyErrNum = Err.Number
            On Error GoTo 0
            If copyErrNum = 0 Then
                targetPath = candidate
                LogLine "Copied file to: " & candidate
            Else
                LogLine "Copying to storage failed (error " & copyErrNum & ") -- falling back to plain HTTP upload."
            End If
        End If
    End If

    If targetPath <> "" Then
        ' If a copy of THE SAME revision (exact same name) is already registered, there is
        ' nothing to do -- the fresh bytes are already there (FileCopy overwrote in place),
        ' and registering the same path a second time would fail anyway (unique file_path
        ' in the database). Attachments of other revisions (different letter) stay
        ' untouched.
        Dim alreadyRegistered As Boolean
        alreadyRegistered = False
        On Error Resume Next
        Dim existingAttachments As Object
        Set existingAttachments = ApiGet("/items/" & itemId & "/attachments")
        On Error GoTo 0
        If Not existingAttachments Is Nothing Then
            Dim a As Variant
            For Each a In existingAttachments
                If JsonGetString(a, "fileName", "") = newFilename Then
                    alreadyRegistered = True
                    Exit For
                End If
            Next a
        End If

        If alreadyRegistered Then
            LogLine "Attachment """ & newFilename & """ already registered -- storage copy overwritten with fresh bytes, no re-registration."
        Else
            Dim registerErrNum As Long, registerErrDesc As String
            On Error Resume Next
            Err.Clear
            ApiRegisterAttachment itemId, targetPath, "cad"
            registerErrNum = Err.Number
            registerErrDesc = Err.Description
            On Error GoTo 0

            If registerErrNum <> 0 Then
                ' Registration failed -- without cleanup the copy in storage/components/
                ' would be orphaned: never registered as an attachment, taking up space on
                ' the server disk with no matching database entry.
                LogLine "Attachment registration failed: " & registerErrDesc & " -- deleting orphaned copy " & targetPath
                On Error Resume Next
                Kill targetPath
                On Error GoTo 0
                Err.Raise ERR_API, "EasyPDM", T("AttachmentRegistrationFailedPrefix") & registerErrDesc
            End If
            LogLine "Registered attachment: " & targetPath
        End If

        RenameAndUpload = True
        Exit Function
    End If

    ' Unlike the register path above (which overwrites the SAME physical file in place via
    ' FileCopy, so re-registering an unchanged name is correctly a no-op), a plain HTTP
    ' upload always lands at a BRAND NEW server-generated path with no relation to
    ' "newFilename" -- without this check, repeated uploads while the revision letter stays
    ' the same (e.g. several saves in a row while still "w_pracy", no status change) would
    ' each add ANOTHER "cad" attachment instead of replacing the one for THIS revision,
    ' accumulating indefinitely (confirmed in practice). Delete any previous attachment with
    ' the exact same name first, so re-uploading the same revision replaces it instead.
    Dim existingCadAttachments As Object
    On Error Resume Next
    Set existingCadAttachments = ApiGet("/items/" & itemId & "/attachments")
    On Error GoTo 0
    If Not existingCadAttachments Is Nothing Then
        Dim existingCad As Variant
        For Each existingCad In existingCadAttachments
            If JsonGetString(existingCad, "fileName", "") = newFilename Then
                On Error Resume Next
                ApiDeleteRequest "/attachments/" & JsonGetString(existingCad, "id", "")
                On Error GoTo 0
                Exit For
            End If
        Next existingCad
    End If

    LogLine "Plain HTTP upload: /items/" & itemId & "/attachments as """ & newFilename & """"
    ApiUploadFile "/items/" & itemId & "/attachments", filePath, newFilename, "role", "cad"
    RenameAndUpload = True
End Function

' Exports swModel's visible geometry to a temporary .step file and uploads it as an
' attachment tagged role="step" (replacing any previous "step" attachment first) -- feeds
' the item's 2D/3D preview in the web app, same purpose as _upload_step_attachment in
' EasyPDMUpload.FCMacro. Deliberately swallows ALL errors (On Error Resume Next for the
' whole body): by the time this is called, the real upload (the .FCStd/.SLDPRT/.SLDASM
' file itself) has already succeeded, so a failed STEP export (e.g. no visible geometry --
' an empty assembly, a pure sketch) must not look like the whole operation failed.
'
' UNVERIFIED against a live SolidWorks install: the exact IModelDocExtension.SaveAs
' parameter count/meaning (SaveAsVersion/SaveAsOptions) below is written from documented
' SolidWorks API behavior, not tested here (no SolidWorks in this environment) -- confirm
' on the first real run and adjust SW_SAVE_AS_SILENT/argument order if SolidWorks reports
' a different signature.
Sub UploadStepAttachment(ByVal swModel As Object, ByVal itemId As String, ByVal itemNumber As Long, ByVal name As String, ByVal revision As Long)
    On Error Resume Next

    Dim tempPath As String
    tempPath = Environ$("TEMP") & "\EasyPDM_step_" & Format(Now, "yyyymmddhhnnss") & CStr(Int(Rnd * 100000)) & ".step"

    Dim saveErrors As Long, saveWarnings As Long
    Dim saveOk As Boolean
    saveOk = swModel.Extension.SaveAs(tempPath, 0, SW_SAVE_AS_SILENT, Nothing, saveErrors, saveWarnings)
    If Not saveOk Or Dir(tempPath) = "" Then
        LogLine "STEP export failed for item " & itemId & " (SaveAs errors=" & saveErrors & ", warnings=" & saveWarnings & ")."
        Exit Sub
    End If

    ' Uploaded under the SAME "number (name).REVISION.ext" convention as RenameAndUpload's
    ' local Save As, so the STEP attachment shown/downloaded from the web app is immediately
    ' recognizable instead of a meaningless temp filename. "One file per role" (a new STEP
    ' replaces any previous one, physically deleted from disk too) is enforced server-side
    ' now (see ReplaceExistingRoleAttachmentAsync in AttachmentEndpoints.cs) -- no need to
    ' fetch/delete the old one from here anymore.
    Dim stepDisplayName As String
    stepDisplayName = itemNumber & " (" & SanitizeFilename(name) & ")." & RevisionLabel(revision) & ".step"

    ApiUploadFile "/items/" & itemId & "/attachments", tempPath, stepDisplayName, "role", "step"
    LogLine "Uploaded STEP attachment for item " & itemId & " as """ & stepDisplayName & """ (from " & tempPath & ")."

    Kill tempPath
    On Error GoTo 0
End Sub

' Same idea as UploadStepAttachment, but exports to PDF instead of STEP and tags the
' attachment role="pdf" -- an independent opt-in choice from the browser ticket form (see
' file header/main()), NOT tied to whether STEP export was also requested. SolidWorks
' supports "Save As PDF" directly from a Part/Assembly document (renders the current view),
' not just from a Drawing -- same tolerant, error-swallowing style as UploadStepAttachment:
' a failed PDF export must not look like the whole upload failed.
'
' UNVERIFIED against a live SolidWorks install, same caveat as UploadStepAttachment above --
' confirm on the first real run.
Sub UploadPdfAttachment(ByVal swModel As Object, ByVal itemId As String, ByVal itemNumber As Long, ByVal name As String, ByVal revision As Long)
    On Error Resume Next

    Dim tempPath As String
    tempPath = Environ$("TEMP") & "\EasyPDM_pdf_" & Format(Now, "yyyymmddhhnnss") & CStr(Int(Rnd * 100000)) & ".pdf"

    Dim saveErrors As Long, saveWarnings As Long
    Dim saveOk As Boolean
    saveOk = swModel.Extension.SaveAs(tempPath, 0, SW_SAVE_AS_SILENT, Nothing, saveErrors, saveWarnings)
    If Not saveOk Or Dir(tempPath) = "" Then
        LogLine "PDF export failed for item " & itemId & " (SaveAs errors=" & saveErrors & ", warnings=" & saveWarnings & ")."
        Exit Sub
    End If

    ' Same "number (name).REVISION.pdf" naming convention as the STEP attachment -- see
    ' UploadStepAttachment's comment for why, and ReplaceExistingRoleAttachmentAsync for
    ' why no manual pre-delete of the previous "pdf" attachment is needed here.
    Dim pdfDisplayName As String
    pdfDisplayName = itemNumber & " (" & SanitizeFilename(name) & ")." & RevisionLabel(revision) & ".pdf"

    ApiUploadFile "/items/" & itemId & "/attachments", tempPath, pdfDisplayName, "role", "pdf"
    LogLine "Uploaded PDF attachment for item " & itemId & " as """ & pdfDisplayName & """ (from " & tempPath & ")."

    Kill tempPath
    On Error GoTo 0
End Sub


' ============================================================================
' PDM core: new item / attach to an existing item (with revision handling).
' ============================================================================

' Attaches the current document as the current file of an ALREADY EXISTING Part/Assembly --
' without creating a new record. If the item's status is "wydany" (released -- PDM does not
' allow attaching files in that status), asks for consent to create a new revision plus an
' optional comment -- the exact same mechanism as in the web app (PATCH /items/{id}/status
' bumps the revision number). Returns Nothing if the user declined the new revision.
Function PushToExistingItem(ByVal swModel As Object, ByVal itemId As String, ByVal filePath As String, ByVal targetFolder As String) As Object
    Dim item As Object
    Set item = ApiGet("/items/" & itemId)

    Dim revision As Long
    revision = JsonGetLong(item, "revisionNumber", 1)
    If revision = 0 Then revision = 1
    Dim currentStatus As String
    currentStatus = JsonGetString(item, "status", "")
    Dim itemNumber As Long
    itemNumber = JsonGetLong(item, "itemNumber", 0)
    Dim fileName As String
    fileName = JsonGetString(item, "fileName", "")
    LogLine "Attaching to existing item #" & itemNumber & " (id " & itemId & "), status """ & currentStatus & """, current revision " & revision

    Dim statusChanged As Boolean
    statusChanged = False

    If currentStatus = "wydany" Then
        Dim proceed As VbMsgBoxResult
        proceed = MsgBox(T("ItemStatusReleasedPrefix") & itemNumber & T("ItemStatusReleasedSuffix"), vbYesNo + vbQuestion, T("TitleNewRevision"))
        If proceed <> vbYes Then
            Set PushToExistingItem = Nothing
            Exit Function
        End If

        Dim comment As String
        comment = InputBox(T("PromptRevisionComment"), T("TitleNewRevision"))

        Dim statusBody As String
        If Trim(comment) <> "" Then
            statusBody = "{""status"":""w_pracy"",""comment"":" & JsonStr(comment) & "}"
        Else
            statusBody = "{""status"":""w_pracy"",""comment"":null}"
        End If

        Dim statusResult As Object
        Set statusResult = ApiPatchJson("/items/" & itemId & "/status", statusBody)
        Dim newRevision As Long
        newRevision = JsonGetLong(statusResult, "revisionNumber", 0)
        If newRevision > 0 Then
            revision = newRevision
        Else
            revision = revision + 1
        End If
        statusChanged = True
    ElseIf currentStatus <> "" And currentStatus <> "w_pracy" Then
        ' Any status other than "w_pracy" (in progress) blocks attaching files on the
        ' backend, not just "wydany" (released). For "sprawdzany" (under review), just
        ' going back to "w_pracy" is enough without asking about a new revision: the
        ' revision is bumped ONLY by the wydany -> w_pracy transition.
        ApiPatchJson "/items/" & itemId & "/status", "{""status"":""w_pracy""}"
        statusChanged = True
    End If

    Dim uploadErrNum As Long, uploadErrDesc As String
    On Error Resume Next
    Err.Clear
    RenameAndUpload swModel, filePath, itemId, itemNumber, fileName, revision, targetFolder
    uploadErrNum = Err.Number
    uploadErrDesc = Err.Description
    On Error GoTo 0

    If uploadErrNum <> 0 Then
        If statusChanged Then
            ' Status/revision were already bumped on the server (they had to be, for
            ' attaching a file to be allowed at all) -- since the file copy/registration
            ' itself failed, the PDM item is now in an inconsistent state: new
            ' revision/status in the database, but still the file of the previous
            ' revision. Rolling back is NOT safely possible here -- the backend does not
            ' allow a direct "w_pracy" -> "wydany" transition -- so instead of a silent/
            ' confusing rollback attempt, we state loudly exactly what happened.
            Err.Raise ERR_API, "EasyPDM", _
                T("ItemAlreadyChangedPart1") & itemNumber & T("ItemAlreadyChangedPart2") & _
                revision & T("ItemAlreadyChangedPart3") & _
                uploadErrDesc & vbCrLf & vbCrLf & _
                T("ItemAlreadyChangedPart4")
        Else
            Err.Raise ERR_API, "EasyPDM", uploadErrDesc
        End If
    End If

    Dim result As Object
    Set result = CreateObject("Scripting.Dictionary")
    result.Add "itemId", itemId
    result.Add "itemNumber", itemNumber
    result.Add "name", fileName
    result.Add "revision", revision
    Set PushToExistingItem = result
End Function

' Looks up a Part/Assembly by the number visible to the user (e.g. in the name
' "67 (Name)") -- NOT by GUID, which a regular user never sees anywhere. Searches the
' WHOLE database, not just the current project -- a component can be used across multiple
' projects.
' ============================================================================
' Assembly tree auto-detection -- walks an Assembly's components (IAssemblyDoc, native SW
' structure, no equivalent of FreeCAD's "App::Link vs native geometry" distinction needed:
' every SolidWorks assembly is inherently built from separate component documents) and
' offers to send any component not yet linked to a PDM item, leaves-first, exactly like
' EasyPDM.FreeCad/EasyPDMUpload.FCMacro's discover_component_tree/process_assembly_tree --
' each new component gets the SAME browser ticket flow as the top-level document (one tab
' at a time, never several at once -- see file header and ProcessAssemblyTree below).
' Confirmed working on a live SolidWorks 2026 install (late-bound GetComponents/
' GetModelDoc2/Name2 all resolve fine without a type library reference, as expected).
'
' Declining to send new components (MsgBox "No") does NOT skip the tree entirely -- see
' "sendNewComponents" below: components ALREADY linked to PDM still get attached into the
' BOM structure (no upload needed, nothing about them changed); only not-yet-linked ones
' are skipped, and any relation involving one of those (as parent or child) is skipped too.
' ============================================================================

' Recursively visits DIRECT children of parentModel (if it is itself an Assembly), THEN
' recurses into any child that is itself an Assembly, appending to "order" only AFTER
' recursing -- this is what makes "order" come out leaves-first. "qty" for an edge is the
' number of sibling instances of the SAME referenced file directly under THIS parent
' (SolidWorks allows the same part to appear multiple times in one assembly, e.g. 4
' identical bolts) -- mirrors FreeCAD's ElementCount aggregation. Suppressed/unresolved/
' virtual (no file) components are skipped: GetModelDoc2() returning Nothing or an empty
' GetPathName() is used as the "cannot inspect, skip it" signal rather than hardcoding a
' swComponentSuppressionState_e enum value that cannot be verified without SolidWorks.
Private Sub VisitAssemblyComponents(ByVal parentModel As Object, ByVal parentPath As String, ByRef order As Collection, ByRef models As Object, ByRef edges As Collection, ByRef visited As Object)
    If parentModel Is Nothing Then Exit Sub
    If parentModel.GetType() <> SW_DOC_ASSEMBLY Then Exit Sub

    Dim comps As Variant
    comps = parentModel.GetComponents(True) ' top-level components of THIS assembly only

    ' Diagnostic -- GetComponents(True) returning Empty/nothing usable is exactly what makes
    ' the whole tree walk silently look like "no components at all" further up (no popup, no
    ' further log lines), which is otherwise indistinguishable from a genuinely empty
    ' assembly. Logged unconditionally (not just on failure) so a real run always shows what
    ' SolidWorks actually reported here.
    If IsEmpty(comps) Then
        LogLine "VisitAssemblyComponents: GetComponents(True) on """ & parentPath & """ returned Empty (no top-level components)."
    Else
        LogLine "VisitAssemblyComponents: GetComponents(True) on """ & parentPath & """ returned " & (UBound(comps) - LBound(comps) + 1) & " component(s)."
    End If

    ' Group by referenced file path first, so a part used several times under the same
    ' parent becomes ONE edge with qty>1 instead of several qty=1 edges.
    Dim qtyByPath As Object
    Set qtyByPath = CreateObject("Scripting.Dictionary")
    Dim modelByPath As Object
    Set modelByPath = CreateObject("Scripting.Dictionary")

    Dim i As Long
    If Not IsEmpty(comps) Then
        For i = LBound(comps) To UBound(comps)
            Dim comp As Object
            Set comp = comps(i)
            If comp Is Nothing Then
                LogLine "Skipping Nothing entry at index " & i & " in GetComponents(True) result."
                GoTo NextComp
            End If

            Dim childModel As Object
            Set childModel = comp.GetModelDoc2()
            If childModel Is Nothing Then
                ' Raw suppression state (swComponentSuppressionState_e) logged as a plain
                ' number -- deliberately not decoded/hardcoded here (cannot be verified
                ' without a live SolidWorks session), but the number itself tells us
                ' immediately whether this is suppression, lightweight, or something else.
                Dim suppErrNum As Long, suppErrDesc As String, suppState As Long
                On Error Resume Next
                Err.Clear
                suppState = comp.GetSuppression2()
                suppErrNum = Err.Number
                suppErrDesc = Err.Description
                On Error GoTo 0
                If suppErrNum <> 0 Then
                    LogLine "Skipping component with no resolvable document: " & comp.Name2 & " (GetSuppression2 failed: " & suppErrDesc & ")"
                Else
                    LogLine "Skipping component with no resolvable document: " & comp.Name2 & " (GetSuppression2 = " & suppState & ")"
                End If
                GoTo NextComp
            End If

            Dim childPath As String
            childPath = childModel.GetPathName()
            If childPath = "" Then
                LogLine "Skipping component with no file on disk (virtual/embedded): " & comp.Name2
                GoTo NextComp
            End If

            If qtyByPath.Exists(childPath) Then
                qtyByPath(childPath) = qtyByPath(childPath) + 1
            Else
                qtyByPath.Add childPath, 1
                Set modelByPath(childPath) = childModel
            End If
NextComp:
        Next i
    End If

    Dim pathKey As Variant
    For Each pathKey In qtyByPath.Keys
        Dim childModel2 As Object
        Set childModel2 = modelByPath(pathKey)

        If Not visited.Exists(pathKey) Then
            visited.Add pathKey, True
            VisitAssemblyComponents childModel2, CStr(pathKey), order, models, edges, visited
            If Not models.Exists(pathKey) Then
                Set models(pathKey) = childModel2
                order.Add pathKey
            End If
        End If

        Dim edge As Object
        Set edge = CreateObject("Scripting.Dictionary")
        edge.Add "parent", parentPath
        edge.Add "child", pathKey
        edge.Add "qty", qtyByPath(pathKey)
        edges.Add edge
    Next pathKey
End Sub

' Returns a Dictionary with "order" (Collection of file paths, leaves-first, EXCLUDING
' topModel itself), "models" (Dictionary path->ModelDoc2) and "edges" (Collection of
' Dictionary{parent,child,qty} for every parent-child pair in the tree, INCLUDING edges
' where the parent is topModel -- the caller attaches those separately once topModel has
' its own item id, see ProcessAssemblyTree/main()). Mirrors discover_component_tree.
Function DiscoverComponentTree(ByVal topModel As Object) As Object
    Dim order As New Collection
    Dim models As Object
    Set models = CreateObject("Scripting.Dictionary")
    Dim edges As New Collection
    Dim visited As Object
    Set visited = CreateObject("Scripting.Dictionary")

    Dim topPath As String
    topPath = topModel.GetPathName()
    visited.Add topPath, True
    VisitAssemblyComponents topModel, topPath, order, models, edges, visited

    Dim result As Object
    Set result = CreateObject("Scripting.Dictionary")
    result.Add "order", order
    result.Add "models", models
    result.Add "edges", edges
    Set DiscoverComponentTree = result
End Function

' If topModel is an Assembly, discovers its component tree and (on user consent) sends
' every component NOT yet linked to a PDM item (leaves-first), wiring up parent-child
' relations as it goes -- mirrors process_assembly_tree. Newly created components are
' ALSO linked via Custom Properties on their OWN document (SetLinkedItem), so re-running
' the macro on one of them later (standalone or inside another assembly) recognizes it as
' already done -- an improvement FreeCAD cannot make (it has no equally reliable local
' link), made possible here because GetLinkedItemId/SetLinkedItem already exist for the
' top-level document.
'
' Returns True if the user cancelled (edgesForTop may be partially filled from components
' created before the cancellation -- like the rest of this macro, already-created PDM
' items are NOT rolled back). "edgesForTop" collects (childItemId, qty) pairs where the
' parent is topModel ITSELF, for the caller to attach once topModel has its own item id.
Function ProcessAssemblyTree(ByVal topModel As Object, ByRef edgesForTop As Collection, ByVal targetFolder As String) As Boolean
    ProcessAssemblyTree = False
    If topModel.GetType() <> SW_DOC_ASSEMBLY Then Exit Function

    ' Force full resolution of any Lightweight components BEFORE walking the tree --
    ' IComponent2.GetModelDoc2() returns Nothing for a component that is still Lightweight
    ' (a SolidWorks performance optimization: only partial data loaded, unrelated to whether
    ' it LOOKS fully displayed in the graphics area), which otherwise makes every lightweight
    ' part silently indistinguishable from "no components at all" -- confirmed in practice
    ' (GetComponents(True) found 2 components, GetModelDoc2() returned Nothing for both).
    ' ResolveAllLightWeightComponents is an IAssemblyDoc method -- called directly on
    ' topModel (same as GetComponents below), NOT via .Extension (ModelDocExtension does not
    ' have this method; a first attempt through .Extension silently failed, see log below).
    ' Recursive: resolves the whole tree, not just this assembly's direct children.
    Dim resolveErrNum As Long, resolveErrDesc As String
    Dim resolvedOk As Boolean
    On Error Resume Next
    Err.Clear
    resolvedOk = topModel.ResolveAllLightWeightComponents(True)
    resolveErrNum = Err.Number
    resolveErrDesc = Err.Description
    On Error GoTo 0
    If resolveErrNum <> 0 Then
        LogLine "ResolveAllLightWeightComponents failed (" & resolveErrNum & "): " & resolveErrDesc
    Else
        LogLine "ResolveAllLightWeightComponents returned " & resolvedOk
    End If

    Dim tree As Object
    Set tree = DiscoverComponentTree(topModel)
    Dim order As Collection
    Set order = tree("order")
    LogLine "ProcessAssemblyTree: discovered " & order.Count & " component(s) in the tree (excluding the top-level document itself)."
    If order.Count = 0 Then Exit Function

    Dim summary As String
    Dim p As Variant
    For Each p In order
        summary = summary & "- " & Mid(p, InStrRev(p, "\") + 1) & vbCrLf
    Next p

    Dim choice As VbMsgBoxResult
    choice = MsgBox(T("AssemblyLinksPart1") & order.Count & T("AssemblyLinksPart2") & vbCrLf & vbCrLf & _
                     summary & vbCrLf & _
                     T("AssemblyLinksPart3") & vbCrLf & vbCrLf & _
                     T("AssemblyLinksPart4"), _
                     vbYesNoCancel + vbQuestion, T("AssemblyDetectedTitle"))
    If choice = vbCancel Then
        ProcessAssemblyTree = True
        Exit Function
    End If
    ' "No" does NOT skip the whole tree -- it only skips CREATING/UPLOADING components not
    ' yet in PDM. Components already linked still get attached into the BOM structure below
    ' (no file upload needed for those, since nothing about them changed); a not-yet-linked
    ' component with sendNewComponents=False is skipped entirely (never added to
    ' pathToItemId below), so any relation involving it as parent or child is naturally
    ' skipped too by the existing pathToItemId.Exists(...) guards further down.
    Dim sendNewComponents As Boolean
    sendNewComponents = (choice = vbYes)

    Dim models As Object
    Set models = tree("models")
    Dim edges As Collection
    Set edges = tree("edges")

    ' path -> item id, for components processed so far in this run (existing OR
    ' newly created) -- needed to resolve child ids when attaching parent-child relations.
    Dim pathToItemId As Object
    Set pathToItemId = CreateObject("Scripting.Dictionary")

    ' Paths CREATED in this run (as opposed to already-linked components merely being
    ' referenced) -- once such an item's relation to its real parent is attached below, it
    ' gets hidden from the project root (show_in_tree=false). It was never an independent
    ' item; its creation here is purely a side effect of the leaves-first upload order
    ' (create the leaf, THEN attach it under its actual parent). An ALREADY-linked component
    ' is a pre-existing, possibly intentionally independent catalog item -- attaching it here
    ' must NOT touch its existing root visibility (same reasoning as linking an existing item
    ' to an assembly from the web UI).
    Dim newlyCreatedPaths As Object
    Set newlyCreatedPaths = CreateObject("Scripting.Dictionary")

    Dim filePath As Variant
    For Each filePath In order
        Dim childModel As Object
        Set childModel = models(filePath)

        Dim existingItemId As String
        existingItemId = GetLinkedItemIdOn(childModel)
        If existingItemId <> "" Then
            If Not ItemStillExists(existingItemId) Then
                LogLine "Component's linked PDM item " & existingItemId & " no longer exists (deleted?) -- clearing stale link: " & filePath
                SetLinkedItemOn childModel, "", ""
                existingItemId = ""
            End If
        End If

        If existingItemId <> "" Then
            pathToItemId.Add filePath, existingItemId
            LogLine "Component already linked to PDM item " & existingItemId & ": " & filePath
        ElseIf sendNewComponents Then
            ' Same "let the browser decide new/duplicate/attach-existing" flow as the
            ' top-level document (BuildBrowserCreateUrl/WaitForTicket, see file header) --
            ' opened ONE TAB AT A TIME, one component after another in the same
            ' leaves-first order as everything else here (never several tabs at once --
            ' confusing to juggle). Cancelling any single ticket aborts the whole
            ' remaining tree walk, same as every other hiccup in this loop.
            ' A native MsgBox right before opening each browser tab -- confirmed necessary
            ' in practice: Windows' foreground-stealing protection lets the FIRST
            ' programmatic browser-open of a run take focus, but silently opens the SECOND
            ' one (and later) in a background tab with no visible cue, leaving WaitForTicket
            ' polling forever with nothing for the user to see or fill in. Clicking OK here
            ' counts as fresh user input, which lets the immediately-following browser-open
            ' take focus reliably.
            Dim compSuggestedName As String
            compSuggestedName = BaseNameFromPath(CStr(filePath))
            MsgBox T("NewComponentBrowserPromptPrefix") & compSuggestedName & T("NewComponentBrowserPromptSuffix"), vbInformation, T("AppTitle")

            Dim compTicket As String
            compTicket = NewGuid()
            OpenUrlInBrowser BuildBrowserCreateUrl(compTicket, compSuggestedName)

            Dim compTicketData As Object
            Set compTicketData = WaitForTicket(compTicket)
            If compTicketData Is Nothing Then
                ProcessAssemblyTree = True
                Exit Function
            End If

            Dim compExportStep As Boolean
            compExportStep = True
            If compTicketData.Exists("exportStep") Then
                If Not IsNull(compTicketData.Item("exportStep")) Then compExportStep = CBool(compTicketData.Item("exportStep"))
            End If

            Dim compExportPdf As Boolean
            compExportPdf = False
            If compTicketData.Exists("exportPdf") Then
                If Not IsNull(compTicketData.Item("exportPdf")) Then compExportPdf = CBool(compTicketData.Item("exportPdf"))
            End If

            Dim compIsExisting As Boolean
            compIsExisting = False
            If compTicketData.Exists("existing") Then
                If compTicketData.Item("existing") = True Then compIsExisting = True
            End If

            Dim compTicketItemId As String
            compTicketItemId = JsonGetString(compTicketData, "itemId", "")

            Dim created As Object
            If compIsExisting Then
                Set created = PushToExistingItem(childModel, compTicketItemId, CStr(filePath), targetFolder)
                If created Is Nothing Then
                    ' Existing item is "wydany" and the user declined a new revision --
                    ' treat like any other cancellation here (see comment above).
                    ProcessAssemblyTree = True
                    Exit Function
                End If
            Else
                ' New item -- it already exists server-side (the browser called POST
                ' /nodes with this ticket), so finish DIRECTLY with the file upload;
                ' creating another item here through the API would create a SECOND item --
                ' same reasoning as the top-level document's own ticket handling in main().
                Dim compTicketItemNumber As Long
                compTicketItemNumber = JsonGetLong(compTicketData, "itemNumber", 0)
                Dim compTicketName As String
                compTicketName = JsonGetString(compTicketData, "name", compSuggestedName)

                RenameAndUpload childModel, CStr(filePath), compTicketItemId, compTicketItemNumber, compTicketName, 1, targetFolder
                Set created = CreateObject("Scripting.Dictionary")
                created.Add "itemId", compTicketItemId
                created.Add "itemNumber", compTicketItemNumber
                created.Add "name", compTicketName
                created.Add "revision", 1
            End If

            Dim newItemId As String
            newItemId = JsonGetString(created, "itemId", "")
            If compExportStep Then UploadStepAttachment childModel, newItemId, JsonGetLong(created, "itemNumber", 0), JsonGetString(created, "name", ""), JsonGetLong(created, "revision", 1)
            If compExportPdf Then UploadPdfAttachment childModel, newItemId, JsonGetLong(created, "itemNumber", 0), JsonGetString(created, "name", ""), JsonGetLong(created, "revision", 1)
            ' Redundant final refresh -- RenameAndUpload/PushToExistingItem's own upload
            ' path already set (and saved) this same Custom Property BEFORE uploading, so
            ' the file actually sent to the server already carries it. Kept here as cheap
            ' insurance.
            SetLinkedItemOn childModel, newItemId, CStr(JsonGetLong(created, "itemNumber", 0))
            pathToItemId.Add filePath, newItemId
            newlyCreatedPaths.Add filePath, True
        Else
            ' Not yet linked and the user declined sending new components -- skip it
            ' entirely (never added to pathToItemId), so it simply cannot take part in any
            ' relation below, neither as parent nor as child.
            LogLine "Component not yet linked, skipping (user declined sending new components): " & filePath
        End If

        ' Attach relations where THIS file is the parent, now that it has an item id --
        ' every child in "edges" at this point already has one too, since "order" is
        ' leaves-first (children were processed in earlier iterations of this same loop).
        ' Guarded by pathToItemId.Exists(filePath): a component skipped just above (declined
        ' new component) never got an item id, so it cannot be a relation parent either.
        If pathToItemId.Exists(filePath) Then
            Dim edge2 As Variant
            For Each edge2 In edges
                If edge2("parent") = filePath Then
                    If pathToItemId.Exists(edge2("child")) Then
                        Dim relErr As String
                        relErr = ""
                        On Error Resume Next
                        Err.Clear
                        ApiPostJson "/items/" & pathToItemId(filePath) & "/children", "{""childId"":" & JsonStr(pathToItemId(edge2("child"))) & ",""quantity"":" & edge2("qty") & "}"
                        If Err.Number <> 0 Then relErr = Err.Description
                        On Error GoTo 0
                        If relErr <> "" Then
                            MsgBox T("CreatedButFailedAttachPart1") & Mid(CStr(edge2("child")), InStrRev(CStr(edge2("child")), "\") + 1) & _
                                   T("CreatedButFailedAttachPart2") & Mid(filePath, InStrRev(filePath, "\") + 1) & T("CreatedButFailedAttachPart3") & relErr, vbExclamation, T("AppTitle")
                        ElseIf newlyCreatedPaths.Exists(edge2("child")) Then
                            ' Now properly nested under its real parent -- hide it from the
                            ' project root (see "newlyCreatedPaths" comment above). Left
                            ' visible at root if the attach above failed, so it can still be
                            ' found and fixed manually.
                            On Error Resume Next
                            ApiPatchJson "/items/" & pathToItemId(edge2("child")) & "/visibility", "{""showInTree"":false}"
                            On Error GoTo 0
                        End If
                    End If
                End If
            Next edge2
        End If
    Next filePath

    ' Relations where topModel itself is the parent could not be attached above (topModel
    ' does not have an item id yet -- that is decided by the rest of main()) -- return them
    ' for the caller to attach once it does.
    Dim topPath As String
    topPath = topModel.GetPathName()
    Dim edge3 As Variant
    For Each edge3 In edges
        If edge3("parent") = topPath Then
            If pathToItemId.Exists(edge3("child")) Then
                edgesForTop.Add Array(pathToItemId(edge3("child")), edge3("qty"), newlyCreatedPaths.Exists(edge3("child")))
            End If
        End If
    Next edge3
End Function


' ============================================================================
' SolidWorks -- active document, save, Custom Properties (PDM link).
' "swApp" is declared and assigned at the start of Sub main() (see module header) -- we do
' NOT rely on SolidWorks providing it automatically.
' ============================================================================

' Returns the file path, the PDM item type ("part"/"assembly"/"file") derived from the
' SolidWorks document type, and a default name (file name without extension). Saves the
' document if it was not saved yet -- the user must then choose a path via SolidWorks's
' standard "Save As" dialog (Save3 with an empty file name opens it automatically).
' Strips the directory and extension from a full file path -- "C:\...\a.SLDPRT" -> "a".
' Used for the suggested/default name shown to the user: SolidWorks's own GetTitle() and a
' raw path both include the extension, which does not belong in a PDM item name.
Function BaseNameFromPath(ByVal path As String) As String
    Dim baseName As String
    baseName = Mid(path, InStrRev(path, "\") + 1)
    Dim dotPos As Long
    dotPos = InStrRev(baseName, ".")
    If dotPos > 0 Then baseName = Left(baseName, dotPos - 1)
    BaseNameFromPath = baseName
End Function

Function GetActiveDocInfo(ByRef filePath As String, ByRef itemTypeGuess As String, ByRef defaultName As String) As Boolean
    Dim swModel As Object
    Set swModel = swApp.ActiveDoc
    If swModel Is Nothing Then
        GetActiveDocInfo = False
        Exit Function
    End If

    Dim saveErr As Long, saveWarn As Long
    filePath = swModel.GetPathName()
    Dim saveOk As Boolean
    saveOk = swModel.Save3(0, saveErr, saveWarn)
    If Not saveOk Then
        MsgBox T("FailedToSaveDocument"), vbExclamation, T("AppTitle")
        GetActiveDocInfo = False
        Exit Function
    End If
    filePath = swModel.GetPathName()
    If filePath = "" Then
        GetActiveDocInfo = False
        Exit Function
    End If

    Select Case swModel.GetType()
        Case SW_DOC_PART
            itemTypeGuess = "part"
        Case SW_DOC_ASSEMBLY
            itemTypeGuess = "assembly"
        Case Else
            itemTypeGuess = "file"
    End Select

    defaultName = BaseNameFromPath(filePath)

    GetActiveDocInfo = True
End Function

Private Function GetCustPropMgrOn(ByVal model As Object) As Object
    Set GetCustPropMgrOn = model.Extension.CustomPropertyManager("")
End Function

' Reads the PDM item id saved in ANY document's Custom Properties (if it was already
' uploaded via this macro before) -- empty string if not yet linked to any PDM item.
' Parametrized by model (not just the active document) so ProcessAssemblyTree can check
' this on each COMPONENT's own document, not only on whatever is active in SolidWorks.
Function GetLinkedItemIdOn(ByVal model As Object) As String
    Dim mgr As Object
    Set mgr = GetCustPropMgrOn(model)
    Dim valOut As String, resolvedOut As String
    On Error Resume Next
    mgr.Get4 CUSTPROP_ITEM_ID, False, valOut, resolvedOut
    On Error GoTo 0
    GetLinkedItemIdOn = valOut
End Function

' Saves the document-to-PDM-item link as Custom Properties on ANY document -- unlike the
' FreeCAD approach (changing the label, NOT saved to disk), this works reliably in a brand
' NEW SolidWorks session too, since Properties are part of the file itself.
Sub SetLinkedItemOn(ByVal model As Object, ByVal itemId As String, ByVal itemNumberText As String)
    Dim mgr As Object
    Set mgr = GetCustPropMgrOn(model)
    mgr.Add3 CUSTPROP_ITEM_ID, SW_CUSTOM_INFO_TEXT, itemId, SW_CUSTOM_PROPERTY_REPLACE
    mgr.Add3 CUSTPROP_ITEM_NUMBER, SW_CUSTOM_INFO_TEXT, itemNumberText, SW_CUSTOM_PROPERTY_REPLACE
End Sub

' Thin wrappers over the active document -- kept so the rest of the file (Sub main, which
' always deals with swApp.ActiveDoc) does not need to pass it explicitly everywhere.
Function GetLinkedItemId() As String
    GetLinkedItemId = GetLinkedItemIdOn(swApp.ActiveDoc)
End Function

Sub SetLinkedItem(ByVal itemId As String, ByVal itemNumberText As String)
    SetLinkedItemOn swApp.ActiveDoc, itemId, itemNumberText
End Sub

' Checks whether a linked PDM item still exists on the server -- recovers from a STALE
' Custom Property link (e.g. the item was deleted in the web app after this document was
' linked to it; deleting server-side does not touch the document's own Custom Property, so
' the macro would otherwise keep "believing" the link is good and fail with a raw 404 the
' moment it tries to use it, confirmed in practice). Only a genuine 404 counts as "gone" --
' any other outcome (200, a different error, no connection) is treated as "still there", so
' a transient network hiccup can never be misread as "deleted" and silently start a brand
' new item instead of updating the real one.
Function ItemStillExists(ByVal itemId As String) As Boolean
    Dim http As Object
    Set http = NewHttpRequest()
    http.Open "GET", GetBaseUrl() & "/items/" & itemId, False
    Dim cookie As String
    cookie = AuthCookieHeader()
    If cookie <> "" Then http.setRequestHeader "Cookie", cookie

    On Error Resume Next
    Err.Clear
    http.send
    If Err.Number <> 0 Then
        ItemStillExists = True ' network error -- assume still there, don't guess
        Exit Function
    End If
    On Error GoTo 0

    ItemStillExists = (http.Status <> 404)
End Function


' ============================================================================
' Entry point -- run via Tools -> Macro -> Run (or F5 in the VBA editor).
' ============================================================================

Sub main()
    LogLine "=== EasyPDM macro started ==="

    On Error Resume Next
    Set swApp = Application.SldWorks
    On Error GoTo 0

    If swApp Is Nothing Then
        MsgBox T("MustRunInsideSolidWorks"), vbCritical, T("AppTitle")
        Exit Sub
    End If

    If Not EnsureLoggedIn() Then
        LogLine "Login cancelled -- done."
        Exit Sub
    End If

    Dim filePath As String, itemTypeGuess As String, defaultName As String
    If Not GetActiveDocInfo(filePath, itemTypeGuess, defaultName) Then
        MsgBox T("NoActiveSavedDocument"), vbExclamation, T("AppTitle")
        LogLine "No active/saved document -- done."
        Exit Sub
    End If
    LogLine "Active document: """ & filePath & """, detected type: " & itemTypeGuess

    ' Target folder for local "Save As under the PDM name" copies -- asked ONCE, up front,
    ' before Step 1, so it covers BOTH the auto-detected assembly components (leaves-first,
    ' see ProcessAssemblyTree) AND the top-level document itself. Same registry key as
    ' EasyPDMDownload.bas's download folder (see GetDownloadFolder) -- shared on
    ' purpose, so uploaded and downloaded files land together by default, same as the
    ' FreeCAD macros.
    Dim targetFolder As String
    targetFolder = Trim(InputBox(T("PromptTargetFolder"), T("TitleTargetFolder"), GetDownloadFolder()))
    If targetFolder = "" Then
        LogLine "Target folder prompt cancelled -- done."
        Exit Sub
    End If
    If Right(targetFolder, 1) = "\" Then targetFolder = Left(targetFolder, Len(targetFolder) - 1)
    EnsureDirectory targetFolder
    SetDownloadFolder targetFolder

    On Error GoTo Failed

    ' Step 1: assembly component tree -- offer to auto-detect/send new components first
    ' (leaves-first), before deciding anything about the top-level document itself. See
    ' file header / ProcessAssemblyTree for the one-browser-tab-at-a-time ticket flow used
    ' for each new component. Each new component gets its own local Save As (see
    ' RenameAndUpload) BEFORE this function returns, so by the time the top-level document
    ' below is itself saved, any of its references to a just-processed component already
    ' point at the new, PDM-named path.
    Dim edgesForTop As New Collection
    If itemTypeGuess = "assembly" Then
        If ProcessAssemblyTree(swApp.ActiveDoc, edgesForTop, targetFolder) Then
            LogLine "Cancelled during assembly tree processing -- done."
            Exit Sub
        End If
    End If

    Dim swActiveModel As Object
    Set swActiveModel = swApp.ActiveDoc

    Dim linkedItemId As String
    linkedItemId = GetLinkedItemId()

    If linkedItemId <> "" Then
        If Not ItemStillExists(linkedItemId) Then
            LogLine "Linked PDM item " & linkedItemId & " no longer exists on the server (deleted?) -- clearing the stale local link, treating this document as not yet linked."
            SetLinkedItem "", ""
            MsgBox T("StaleLinkCleared"), vbInformation, T("AppTitle")
            linkedItemId = ""
        End If
    End If

    Dim resultInfo As Object

    If linkedItemId <> "" Then
        ' Already linked -- SolidWorks knows the target with certainty (Custom Property),
        ' no browser round-trip needed. Whether to export STEP/PDF is asked here as two
        ' plain native Yes/No questions instead -- unlike the per-component assembly tree
        ' walk (see file header), this happens ONCE per run for a single document, so a
        ' couple of native prompts is not the "opening N popups" problem that drove the
        ' assembly components over to the browser ticket flow.
        '
        ' Shows the LINKED ITEM'S OWN number/name in the confirmation, not just a generic
        ' "already linked?" question -- SolidWorks's native "Save As" (done manually by the
        ' user, not through this macro) COPIES Custom Properties along with everything else:
        ' Save-As'ing an already-linked part to start a genuinely DIFFERENT part would
        ' silently inherit the old EasyPDM_ItemId, and without this the user would have no
        ' way to notice before overwriting the WRONG item's content with the new part's
        ' file. Falls back to the generic wording if this lookup itself fails (a transient
        ' error) -- the item's own existence was already confirmed moments ago above via
        ' ItemStillExists.
        Dim linkedItemInfo As Object
        On Error Resume Next
        Set linkedItemInfo = ApiGet("/items/" & linkedItemId)
        On Error GoTo 0

        Dim confirmText As String
        If Not linkedItemInfo Is Nothing Then
            confirmText = T("AlreadyLinkedConfirmPrefix") & JsonGetLong(linkedItemInfo, "itemNumber", 0) & _
                          " (" & JsonGetString(linkedItemInfo, "fileName", "") & ")" & T("AlreadyLinkedConfirmSuffix")
        Else
            confirmText = T("AlreadyLinkedConfirm")
        End If

        Dim confirmUpdate As VbMsgBoxResult
        confirmUpdate = MsgBox(confirmText, vbYesNo + vbQuestion, T("AppTitle"))
        If confirmUpdate <> vbYes Then Exit Sub

        ' Defaults match the browser checkboxes' own defaults (STEP on, PDF off) via
        ' vbDefaultButton1/2 -- Enter alone picks the same answer the browser form would
        ' start with.
        Dim nativeExportStep As Boolean
        nativeExportStep = (MsgBox(T("ExportStepPrompt"), vbYesNo + vbQuestion + vbDefaultButton1, T("AppTitle")) = vbYes)
        Dim nativeExportPdf As Boolean
        nativeExportPdf = (MsgBox(T("ExportPdfPrompt"), vbYesNo + vbQuestion + vbDefaultButton2, T("AppTitle")) = vbYes)

        Set resultInfo = PushToExistingItem(swActiveModel, linkedItemId, filePath, targetFolder)
        If Not resultInfo Is Nothing Then
            If nativeExportStep Then UploadStepAttachment swActiveModel, linkedItemId, JsonGetLong(resultInfo, "itemNumber", 0), JsonGetString(resultInfo, "name", ""), JsonGetLong(resultInfo, "revision", 1)
            If nativeExportPdf Then UploadPdfAttachment swActiveModel, linkedItemId, JsonGetLong(resultInfo, "itemNumber", 0), JsonGetString(resultInfo, "name", ""), JsonGetLong(resultInfo, "revision", 1)
        End If
    Else
        ' Not yet linked -- "new item vs duplicate vs attach to existing" is decided in
        ' the browser, not locally, exactly like EasyPDMUpload.FCMacro's
        ' submit_via_browser. See BuildBrowserCreateUrl/WaitForTicket above.
        Dim ticket As String
        ticket = NewGuid()
        OpenUrlInBrowser BuildBrowserCreateUrl(ticket, defaultName)

        Dim ticketData As Object
        Set ticketData = WaitForTicket(ticket)
        If ticketData Is Nothing Then
            MsgBox T("CancelledNothingSent"), vbInformation, T("AppTitle")
            LogLine "=== Finished: browser ticket cancelled/timed out ==="
            Exit Sub
        End If

        Dim exportStep As Boolean
        exportStep = True
        If ticketData.Exists("exportStep") Then
            If Not IsNull(ticketData.Item("exportStep")) Then exportStep = CBool(ticketData.Item("exportStep"))
        End If

        Dim exportPdf As Boolean
        exportPdf = False
        If ticketData.Exists("exportPdf") Then
            If Not IsNull(ticketData.Item("exportPdf")) Then exportPdf = CBool(ticketData.Item("exportPdf"))
        End If

        Dim ticketItemId As String
        ticketItemId = JsonGetString(ticketData, "itemId", "")

        Dim isExisting As Boolean
        isExisting = False
        If ticketData.Exists("existing") Then
            If ticketData.Item("existing") = True Then isExisting = True
        End If

        If isExisting Then
            Set resultInfo = PushToExistingItem(swActiveModel, ticketItemId, filePath, targetFolder)
            If resultInfo Is Nothing Then
                MsgBox T("CancelledNothingSent"), vbInformation, T("AppTitle")
                LogLine "=== Finished: existing item declined a new revision ==="
                Exit Sub
            End If
            linkedItemId = ticketItemId
            If exportStep Then UploadStepAttachment swActiveModel, ticketItemId, JsonGetLong(resultInfo, "itemNumber", 0), JsonGetString(resultInfo, "name", ""), JsonGetLong(resultInfo, "revision", 1)
            If exportPdf Then UploadPdfAttachment swActiveModel, ticketItemId, JsonGetLong(resultInfo, "itemNumber", 0), JsonGetString(resultInfo, "name", ""), JsonGetLong(resultInfo, "revision", 1)
        Else
            ' New item -- it already exists server-side (the browser called POST /nodes
            ' with this ticket), so finish DIRECTLY with the file upload; creating another
            ' item here through the API would create a SECOND item.
            Dim ticketItemNumber As Long
            ticketItemNumber = JsonGetLong(ticketData, "itemNumber", 0)
            Dim ticketName As String
            ticketName = JsonGetString(ticketData, "name", defaultName)

            RenameAndUpload swActiveModel, filePath, ticketItemId, ticketItemNumber, ticketName, 1, targetFolder
            linkedItemId = ticketItemId
            Set resultInfo = CreateObject("Scripting.Dictionary")
            resultInfo.Add "itemId", ticketItemId
            resultInfo.Add "itemNumber", ticketItemNumber
            resultInfo.Add "name", ticketName
            resultInfo.Add "revision", 1
            If exportStep Then UploadStepAttachment swActiveModel, ticketItemId, ticketItemNumber, ticketName, 1
            If exportPdf Then UploadPdfAttachment swActiveModel, ticketItemId, ticketItemNumber, ticketName, 1
        End If
    End If

    ' Step 3: attach components discovered in step 1 under THIS element, now that it has
    ' an item id -- regardless of whether it was brand new or already existing.
    If Not resultInfo Is Nothing Then
        Dim edgeVariant As Variant
        For Each edgeVariant In edgesForTop
            Dim topRelErr As String
            topRelErr = ""
            On Error Resume Next
            Err.Clear
            ApiPostJson "/items/" & linkedItemId & "/children", "{""childId"":" & JsonStr(edgeVariant(0)) & ",""quantity"":" & edgeVariant(1) & "}"
            If Err.Number <> 0 Then topRelErr = Err.Description
            On Error GoTo 0
            If topRelErr <> "" Then
                MsgBox T("FailedToAttachSubComponent") & topRelErr, vbExclamation, T("AppTitle")
            ElseIf edgeVariant(2) Then
                ' Newly created purely as a leaves-first side effect of this upload (see
                ' ProcessAssemblyTree's "newlyCreatedPaths") -- now properly nested under
                ' THIS document, hide it from the project root. Left visible if the attach
                ' above failed, so it can still be found and fixed manually.
                On Error Resume Next
                ApiPatchJson "/items/" & edgeVariant(0) & "/visibility", "{""showInTree"":false}"
                On Error GoTo 0
            End If
        Next edgeVariant
    End If

    If Not resultInfo Is Nothing Then
        ' Redundant final refresh -- RenameAndUpload (called by whichever path produced
        ' resultInfo above) already set and saved this same Custom Property BEFORE
        ' uploading, so the file actually sent to the server already carries it. Kept here
        ' as cheap insurance for the top-level document specifically.
        SetLinkedItem linkedItemId, CStr(JsonGetLong(resultInfo, "itemNumber", 0))
        LogLine "=== Finished successfully: item #" & JsonGetLong(resultInfo, "itemNumber", 0) & _
                ", revision " & RevisionLabel(JsonGetLong(resultInfo, "revision", 1)) & " ==="
        MsgBox T("UploadedSuccessPart1") & JsonGetLong(resultInfo, "itemNumber", 0) & _
               T("UploadedSuccessPart2") & RevisionLabel(JsonGetLong(resultInfo, "revision", 1)) & ")." & vbCrLf & vbCrLf & _
               T("RunLogPrefix") & LogFilePath(), vbInformation, T("AppTitle")
    Else
        LogLine "=== Finished without uploading (cancelled or no new revision) ==="
    End If
    Exit Sub

Failed:
    LogLine "=== ERROR (" & Err.Number & "): " & Err.Description & " ==="
    If Err.Number = ERR_AUTH Then
        MsgBox T("SessionExpiredPrompt") & vbCrLf & vbCrLf & _
               T("RunLogPrefix") & LogFilePath(), vbExclamation, T("AppTitle")
        SetSessionToken ""
    Else
        MsgBox T("ErrorPrefix") & Err.Description & vbCrLf & vbCrLf & T("RunLogPrefix") & LogFilePath(), vbCritical, T("AppTitle")
    End If
End Sub

' Separate Sub -- can be bound to your own toolbar button/shortcut to log out of EasyPDM
' without running the whole upload flow (the next run of "main" will ask to log in again).
Sub Logout()
    ApiLogout
    MsgBox T("LoggedOutMessage"), vbInformation, T("AppTitle")
End Sub
