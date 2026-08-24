Attribute VB_Name = "EasyPDMDownload"
Option Explicit

' ============================================================================
' EasyPDMDownload -- SolidWorks macro that downloads a Part/Assembly from EasyPDM and
' opens it.
'
' This is the COUNTERPART of EasyPDM.FreeCad/EasyPDMDownload.FCMacro, not a 1:1 port -- see
' EasyPDMUpload.bas's header for the general reasons (own minimal JSON, no UserForm
' anywhere -- VBA UserForms are separate binary resources, not embeddable in a plain .bas
' text file).
'
' This file is fully SELF-CONTAINED (does not depend on EasyPDMUpload.bas being present in
' the same VBA project) -- it duplicates the login/JSON/HTTP/browser-ticket helpers rather
' than importing them, the same way EasyPDM.FreeCad/EasyPDMDownload.FCMacro duplicates
' rather than imports from EasyPDMUpload.FCMacro. The API address, session token and
' download folder are shared with EasyPDMUpload.bas through the SAME Windows registry keys
' (identical constants below) -- logging in from one macro is enough for both.
'
' All comments/strings are plain English (ASCII only) -- VBA's file import does not
' reliably handle UTF-8 (confirmed in practice while building EasyPDMUpload.bas).
'
' What it does:
'   1. Picks WHICH item to download the SAME way EasyPDMUpload.bas picks a new item's
'      home -- opens the system browser, already logged in (token->cookie bridge), on the
'      "pending request from a CAD macro" popup (mode=download), where a search box picks
'      the Part/Assembly, exactly like EasyPDMDownload.FCMacro. The macro waits
'      (WaitForTicket, polling GET /create-tickets/{ticket}; Escape cancels, no UserForm
'      needed) and then re-fetches the full item (GET /items/{id}) once resolved. Only the
'      TARGET FOLDER stays a native InputBox (defaults to the last one used -- shared with
'      EasyPDMUpload.bas's target folder).
'   2. For an Assembly: also downloads ALL of its components recursively (direct children,
'      then their children, and so on -- the whole BOM), into the SAME folder as the main
'      file. Without this, an assembly built on links to external, saved files would have
'      nothing to resolve those links against when opened.
'   3. For each file: if the target folder already has a file with the EXACT same name
'      (i.e. the same revision) and the same size as on the server, it is skipped (not
'      downloaded again).
'   4. If the folder already has a file for the same item but an OLDER revision, and the
'      server has a newer one, asks whether to download the newer one instead of silently
'      overwriting or silently keeping the outdated file.
'   5. Finally opens the main (selected) file in SolidWorks (swApp.OpenDoc6) -- component
'      files are left on disk only, not opened as separate documents (SolidWorks resolves
'      the assembly's own references to them automatically when it opens the assembly).
'
' Where the downloaded files come from:
'   EasyPDM stores the CURRENT CAD file of a Part/Assembly as an attachment (there is no
'   separate "item file" mechanism) -- and every time a new revision is uploaded, the
'   previous copy STAYS (a new attachment next to the old one, different names:
'   "number (name).REVISION.extension", the same convention EasyPDMUpload.bas uses), so
'   revision history is in practice reconstructable from the attachment list. This macro
'   recognizes that convention to find the attachment matching the item's CURRENT revision
'   (item.revisionNumber) -- if an item never went through any CAD macro (e.g. attached
'   manually in the web app, so attachments have arbitrary original names), it simply picks
'   the most recently uploaded attachment as the best approximation.
'
' Limitations (deliberately out of scope for this version):
'   - Does not try to download a SPECIFIC older revision -- always targets the current one;
'     older local copies are only used to detect "you have an outdated version" (see 4).
'   - All files (main + components) land flat in ONE folder, without recreating the BOM
'     structure as subfolders -- the safest choice for links saved as paths RELATIVE to the
'     document folder, but if the original model was built with files in separate
'     subfolders, links may still not resolve automatically -- fix them manually in
'     SolidWorks in that case.
'   - A shared component (used in several places in the tree) is downloaded only once
'     (recognized by item id), same as in EasyPDMUpload.bas.
' ============================================================================

Private Const APP_SETTINGS_NAME As String = "EasyPDM"
Private Const SETTINGS_SECTION As String = "Connection"
Private Const DEFAULT_BASE_URL As String = "http://localhost:5000/api"
Private Const SESSION_COOKIE_NAME As String = "pdm_session"

' Own error numbers (Err.Raise) -- distinguish "missing/expired session" (ERR_AUTH, should
' trigger a fresh login) from a plain API error (ERR_API, just show the message).
Private Const ERR_AUTH As Long = vbObjectError + 1001
Private Const ERR_API As Long = vbObjectError + 1002

' Values from the SolidWorks swDocumentTypes_e enum -- declared explicitly as numbers, same
' reasoning as EasyPDMUpload.bas (late binding + Option Explicit -> bare constant names
' with no type library reference fail to compile as "Variable not defined", confirmed in
' practice). Grouped at the very top of the module on purpose.
Private Const SW_DOC_PART As Long = 1        ' swDocumentTypes_e.swDocPART
Private Const SW_DOC_ASSEMBLY As Long = 2    ' swDocumentTypes_e.swDocASSEMBLY
Private Const SW_DOC_DRAWING As Long = 3     ' swDocumentTypes_e.swDocDRAWING

' Win32 API used ONLY by WaitForTicket (below) to poll the ticket endpoint while keeping
' SolidWorks responsive and letting the user cancel with Escape -- see EasyPDMUpload.bas's
' copy of this same block for the full reasoning (duplicated here per this file's own
' self-containment rule, see file header).
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
' titles, status bar text, and the AppendLog progress lines that end up in the final
' summary MsgBox) goes through T(key), dispatching to T_PL/T_EN/T_DE based on the
' auto-detected Windows UI language (see DetectLanguage above). LogLine-only messages stay
' hardcoded/untranslated on purpose -- only what the user actually sees goes through this
' layer. Strings needing a dynamic value keep only their static part here; the call site
' concatenates the dynamic value with "&" exactly like before.
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
        Case "ServerErrorPrefix": T_PL = "Blad serwera ("
        Case "NoConnectionPrefix": T_PL = "Brak polaczenia z "
        Case "SessionExpiredError": T_PL = "Sesja wygasla."
        Case "MustRunInsideSolidWorks": T_PL = "To makro musi byc uruchomione z poziomu SolidWorks."
        Case "SessionExpiredPrompt": T_PL = "Sesja wygasla -- uruchom makro ponownie, aby sie zalogowac."
        Case "RunLogPrefix": T_PL = "Log przebiegu: "
        Case "ErrorPrefix": T_PL = "Blad: "
        Case "LoggedOutMessage": T_PL = "Wylogowano z EasyPDM."
        Case "CancelledNothingDownloaded": T_PL = "Anulowano -- nic nie zostalo pobrane."
        Case "SelectedItemLoadFailed": T_PL = "Nie udalo sie wczytac wybranego elementu."
        Case "PromptTargetFolder": T_PL = "Folder docelowy:"
        Case "TitleDownloadFromPdm": T_PL = "Pobieranie z PDM"
        Case "TitleNewerRevision": T_PL = "Nowsza rewizja"
        Case "NewerRevisionPart1": T_PL = "Folder zawiera juz "
        Case "NewerRevisionPart2": T_PL = " w rewizji "
        Case "NewerRevisionPart3": T_PL = ", ale biezaca wersja na serwerze to rewizja "
        Case "DownloadNewestQuestion": T_PL = "Pobrac najnowsza wersje?"
        Case "Dl_FetchAttachmentsFailed": T_PL = ": nie udalo sie pobrac listy zalacznikow."
        Case "Dl_NoAttachments": T_PL = ": brak plikow CAD do pobrania (element nie ma zalacznikow)."
        Case "Dl_AlreadyInFolder": T_PL = ": juz jest w folderze (biezaca rewizja), pomijanie."
        Case "Dl_KeepingExistingRevisionPrefix": T_PL = ": zachowywanie istniejacej lokalnej rewizji ("
        Case "Dl_DownloadingPrefix": T_PL = "  Pobieranie "
        Case "Dl_ErrorDownloadingMid": T_PL = ": BLAD podczas pobierania "
        Case "Dl_Saved": T_PL = "  Zapisano: "
        Case "Dl_FetchChildrenFailed": T_PL = "): nie udalo sie pobrac listy komponentow."
        Case "Dl_DownloadingToPrefix": T_PL = "Pobieranie "
        Case "Dl_DownloadingToMiddle": T_PL = ") do "
        Case "Dl_FailedToOpenPrefix": T_PL = "Nie udalo sie otworzyc "
        Case "Dl_FailedToOpenSuffix": T_PL = " w SolidWorks (kod bledu "
        Case "Dl_Opened": T_PL = "Otwarto: "
        Case "Dl_CouldNotDetermineMainFile": T_PL = "Nie udalo sie ustalic glownego pliku do otwarcia."
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
        Case "ServerErrorPrefix": T_EN = "Server error ("
        Case "NoConnectionPrefix": T_EN = "No connection to "
        Case "SessionExpiredError": T_EN = "Session expired."
        Case "MustRunInsideSolidWorks": T_EN = "This macro must be run from inside SolidWorks."
        Case "SessionExpiredPrompt": T_EN = "Session expired -- run the macro again to log in."
        Case "RunLogPrefix": T_EN = "Run log: "
        Case "ErrorPrefix": T_EN = "Error: "
        Case "LoggedOutMessage": T_EN = "Logged out of EasyPDM."
        Case "CancelledNothingDownloaded": T_EN = "Cancelled -- nothing was downloaded."
        Case "SelectedItemLoadFailed": T_EN = "The selected item could not be loaded."
        Case "PromptTargetFolder": T_EN = "Target folder:"
        Case "TitleDownloadFromPdm": T_EN = "Download from PDM"
        Case "TitleNewerRevision": T_EN = "Newer revision"
        Case "NewerRevisionPart1": T_EN = "The folder already has "
        Case "NewerRevisionPart2": T_EN = " at revision "
        Case "NewerRevisionPart3": T_EN = ", but the current one on the server is revision "
        Case "DownloadNewestQuestion": T_EN = "Download the newest version?"
        Case "Dl_FetchAttachmentsFailed": T_EN = ": failed to fetch the attachment list."
        Case "Dl_NoAttachments": T_EN = ": no CAD files to download (the item has no attachments)."
        Case "Dl_AlreadyInFolder": T_EN = ": already in the folder (current revision), skipping."
        Case "Dl_KeepingExistingRevisionPrefix": T_EN = ": keeping the existing local revision ("
        Case "Dl_DownloadingPrefix": T_EN = "  Downloading "
        Case "Dl_ErrorDownloadingMid": T_EN = ": ERROR downloading "
        Case "Dl_Saved": T_EN = "  Saved: "
        Case "Dl_FetchChildrenFailed": T_EN = "): failed to fetch the component list."
        Case "Dl_DownloadingToPrefix": T_EN = "Downloading "
        Case "Dl_DownloadingToMiddle": T_EN = ") to "
        Case "Dl_FailedToOpenPrefix": T_EN = "Failed to open "
        Case "Dl_FailedToOpenSuffix": T_EN = " in SolidWorks (error code "
        Case "Dl_Opened": T_EN = "Opened: "
        Case "Dl_CouldNotDetermineMainFile": T_EN = "Could not determine the main file to open."
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
        Case "ServerErrorPrefix": T_DE = "Serverfehler ("
        Case "NoConnectionPrefix": T_DE = "Keine Verbindung zu "
        Case "SessionExpiredError": T_DE = "Sitzung abgelaufen."
        Case "MustRunInsideSolidWorks": T_DE = "Dieses Makro muss innerhalb von SolidWorks ausgefuehrt werden."
        Case "SessionExpiredPrompt": T_DE = "Sitzung abgelaufen -- fuehren Sie das Makro erneut aus, um sich anzumelden."
        Case "RunLogPrefix": T_DE = "Ausfuehrungsprotokoll: "
        Case "ErrorPrefix": T_DE = "Fehler: "
        Case "LoggedOutMessage": T_DE = "Von EasyPDM abgemeldet."
        Case "CancelledNothingDownloaded": T_DE = "Abgebrochen -- es wurde nichts heruntergeladen."
        Case "SelectedItemLoadFailed": T_DE = "Das ausgewaehlte Element konnte nicht geladen werden."
        Case "PromptTargetFolder": T_DE = "Zielordner:"
        Case "TitleDownloadFromPdm": T_DE = "Download von PDM"
        Case "TitleNewerRevision": T_DE = "Neuere Revision"
        Case "NewerRevisionPart1": T_DE = "Der Ordner enthaelt bereits "
        Case "NewerRevisionPart2": T_DE = " in Revision "
        Case "NewerRevisionPart3": T_DE = ", aber die aktuelle Version auf dem Server ist Revision "
        Case "DownloadNewestQuestion": T_DE = "Neueste Version herunterladen?"
        Case "Dl_FetchAttachmentsFailed": T_DE = ": Abrufen der Anhangsliste fehlgeschlagen."
        Case "Dl_NoAttachments": T_DE = ": keine CAD-Dateien zum Herunterladen (das Element hat keine Anhaenge)."
        Case "Dl_AlreadyInFolder": T_DE = ": bereits im Ordner vorhanden (aktuelle Revision), wird uebersprungen."
        Case "Dl_KeepingExistingRevisionPrefix": T_DE = ": vorhandene lokale Revision wird beibehalten ("
        Case "Dl_DownloadingPrefix": T_DE = "  Herunterladen von "
        Case "Dl_ErrorDownloadingMid": T_DE = ": FEHLER beim Herunterladen von "
        Case "Dl_Saved": T_DE = "  Gespeichert: "
        Case "Dl_FetchChildrenFailed": T_DE = "): Abrufen der Komponentenliste fehlgeschlagen."
        Case "Dl_DownloadingToPrefix": T_DE = "Herunterladen von "
        Case "Dl_DownloadingToMiddle": T_DE = ") nach "
        Case "Dl_FailedToOpenPrefix": T_DE = "Oeffnen von "
        Case "Dl_FailedToOpenSuffix": T_DE = " in SolidWorks fehlgeschlagen (Fehlercode "
        Case "Dl_Opened": T_DE = "Geoeffnet: "
        Case "Dl_CouldNotDetermineMainFile": T_DE = "Die zu oeffnende Hauptdatei konnte nicht ermittelt werden."
    End Select
End Function


' SolidWorks application object -- declared and assigned at the start of Sub main(), same
' reasoning as EasyPDMUpload.bas ("swApp" is only auto-provided in the module SolidWorks
' itself generates via "Tools -> Macro -> New", not in an imported module like this one).
Private swApp As Object

' Accumulates one run's progress messages (plain text, not a Collection -- fewer moving
' parts), so DownloadItem/DownloadChildrenRecursive can report progress via AppendLog
' (they call it directly since they live in the same module) and Sub main() can show it
' all in one summary dialog at the end. Reset to "" at the start of every run.
Private gLogText As String


' ============================================================================
' Log -- same mechanism as EasyPDMUpload.bas, a separate file so the two macros' logs don't
' interleave and confuse troubleshooting.
' ============================================================================

Function LogFilePath() As String
    LogFilePath = Environ$("TEMP") & "\EasyPDM_download_macro.log"
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
' Settings (API address / session token / saved display name / download folder) --
' Windows registry via SaveSetting/GetSetting, SAME keys as EasyPDMUpload.bas so both
' macros share the login session and the target folder.
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

' Same registry key as the target folder in EasyPDMUpload.bas -- deliberately shared, so
' downloaded and uploaded files can land in the same place.
Function GetDownloadFolder() As String
    GetDownloadFolder = GetSetting(APP_SETTINGS_NAME, SETTINGS_SECTION, "DownloadFolder", "")
End Function

Sub SetDownloadFolder(ByVal folder As String)
    SaveSetting APP_SETTINGS_NAME, SETTINGS_SECTION, "DownloadFolder", folder
End Sub


' ============================================================================
' Minimal JSON -- identical to EasyPDMUpload.bas (own parser/builder, good enough for this
' API's response shapes, not general purpose). Duplicated rather than shared -- see module
' header.
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
    ' Val() (not CDbl!) -- CDbl depends on the current system locale's decimal separator.
    JsonParseNumber = Val(Mid(s, startPos, pos - startPos))
End Function

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

Function JsonGetObject(ByVal obj As Object, ByVal key As String) As Object
    If obj Is Nothing Then Exit Function
    If Not obj.Exists(key) Then Exit Function
    If IsObject(obj.Item(key)) Then Set JsonGetObject = obj.Item(key)
End Function


' ============================================================================
' HTTP -- MSXML2.XMLHTTP (synchronous), with the session cookie attached MANUALLY to every
' request. Same as EasyPDMUpload.bas.
' ============================================================================

Private Function NewHttpRequest() As Object
    Set NewHttpRequest = CreateObject("MSXML2.XMLHTTP.6.0")
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
    http.Open "GET", GetBaseUrl() & path, False
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

' Like ApiGet, but returns the raw response bytes -- for downloading file (attachment)
' content, which is not JSON. Uses "responseBody" (a Byte SAFEARRAY), not "responseText"
' (which would corrupt binary data through character/codepage conversion).
Function ApiGetBinary(ByVal path As String) As Byte()
    Dim http As Object
    Set http = NewHttpRequest()
    http.Open "GET", GetBaseUrl() & path, False
    Dim cookie As String
    cookie = AuthCookieHeader()
    If cookie <> "" Then http.setRequestHeader "Cookie", cookie

    On Error GoTo NetErr
    http.send
    On Error GoTo 0

    LogLine "GET (binary) " & path & " -> " & http.Status
    If http.Status = 401 Then
        Err.Raise ERR_AUTH, "EasyPDM", T("SessionExpiredError")
    ElseIf http.Status < 200 Or http.Status >= 300 Then
        Err.Raise ERR_API, "EasyPDM", T("ServerErrorPrefix") & http.Status & ")."
    End If

    ApiGetBinary = http.responseBody
    Exit Function
NetErr:
    LogLine "GET (binary) " & path & " -> NO CONNECTION: " & Err.Description
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


' ============================================================================
' Browser ticket flow -- lets the web app pick WHICH item to download instead of a native
' number-entry InputBox, exactly like EasyPDM.FreeCad/EasyPDMDownload.FCMacro. Duplicated
' from EasyPDMUpload.bas (see file header for why this file does not import from it) --
' BuildBrowserDownloadUrl differs (mode=download, no "name" hint), WaitForTicket/NewGuid/
' UrlEncode/OpenUrlInBrowser are identical copies.
' ============================================================================

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

' Address that logs the browser in (token->cookie bridge) and deep-links straight to the
' "pending request from a CAD macro" popup for THIS ticket, in DOWNLOAD mode -- the popup
' shows a search box immediately (no "new/duplicate/attach" choice, that only applies to
' uploading) and completes the ticket via the SAME attach-existing endpoint used by
' EasyPDMUpload.bas's "Attach to existing" -- it does not create or change anything, only
' tells the macro WHICH item was picked.
Function BuildBrowserDownloadUrl(ByVal ticket As String) As String
    Dim redirectPath As String
    redirectPath = "/?ticket=" & UrlEncode(ticket) & "&mode=download"
    BuildBrowserDownloadUrl = GetBaseUrl() & "/auth/browser-login?token=" & UrlEncode(GetSessionToken()) & "&redirect=" & UrlEncode(redirectPath)
End Function

' Polls GET /create-tickets/{ticket} until the user picks an item in the browser, the wait
' times out (10 minutes), or the user presses Escape -- identical to EasyPDMUpload.bas's
' copy, see there for the full reasoning (no UserForm in this file, status bar text
' instead of a dialog, tick/poll counters instead of Timer() to avoid a midnight rollover).
Function WaitForTicket(ByVal ticket As String) As Object
    Const TICK_MS As Long = 400
    Const POLL_EVERY_MS As Long = 2000
    Const TIMEOUT_MS As Long = 600000 ' 10 minutes, same as EasyPDMDownload.FCMacro

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
' Login / session -- identical to EasyPDMUpload.bas (including the sessionToken-in-JSON-
' body fix -- MSXML2.XMLHTTP.6.0 does not reliably expose the Set-Cookie header).
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
            SetSessionToken ""
        Else
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
' Helpers -- revision label formatting, PDM naming convention regex, nested directory
' creation.
' ============================================================================

' Revisions as uppercase letters instead of digits: 1->A, 2->B, ..., 26->Z, 27->AA... --
' the same conversion as in EasyPDMUpload.bas / the web frontend / the FreeCAD macros.
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

' Escapes regex metacharacters in a string, for building a Pattern from arbitrary item
' names (VBScript.RegExp has no built-in escape function).
Private Function RegexEscape(ByVal s As String) As String
    Dim specials As String
    specials = "\^$.|?*+()[]{}"
    Dim result As String
    result = s
    Dim i As Long
    For i = 1 To Len(specials)
        result = Replace(result, Mid(specials, i, 1), "\" & Mid(specials, i, 1))
    Next i
    RegexEscape = result
End Function

' Builds a regex matching this macro family's naming convention: "number (name).REVISION."
' -- group 1 is the revision letter. Same name EasyPDMUpload.bas gives files, so downloads
' and uploads always agree.
Private Function NewRevisionRegex(ByVal itemNumber As Long, ByVal name As String) As Object
    Dim re As Object
    Set re = CreateObject("VBScript.RegExp")
    re.IgnoreCase = True
    re.Pattern = "^" & itemNumber & "\s*\(" & RegexEscape(name) & "\)\.([A-Za-z]+)\."
    Set NewRevisionRegex = re
End Function

' VBA's MkDir only creates ONE level at a time (unlike Python's os.makedirs) -- this walks
' the path and creates each missing segment in turn.
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
' Download logic -- picks the attachment matching the item's current revision, skips files
' already present at the same size, asks before replacing an older local revision,
' recurses into assembly children.
' ============================================================================

' Picks the attachment matching the item's CURRENT revision. If none match this macro
' family's naming convention (e.g. the item was only ever attached manually in the web
' app), falls back to the most recently uploaded attachment (the list is already sorted
' ascending by upload date) as the best approximation.
Private Function FindCurrentAttachment(ByVal item As Object, ByVal attachments As Object) As Object
    If attachments Is Nothing Or attachments.Count = 0 Then Exit Function

    Dim number As Long
    number = JsonGetLong(item, "itemNumber", 0)
    Dim name As String
    name = JsonGetString(item, "fileName", "")
    Dim wantedLabel As String
    wantedLabel = ""
    If JsonGetLong(item, "revisionNumber", 0) > 0 Then
        wantedLabel = UCase(RevisionLabel(JsonGetLong(item, "revisionNumber", 1)))
    End If

    Dim re As Object
    Set re = NewRevisionRegex(number, name)

    Dim a As Variant
    Dim lastMatch As Object
    Set lastMatch = Nothing
    For Each a In attachments
        Dim fname As String
        fname = JsonGetString(a, "fileName", "")
        If re.Test(fname) Then
            Dim m As Object
            Set m = re.Execute(fname)
            Dim label As String
            label = UCase(m(0).SubMatches(0))
            If wantedLabel <> "" And label = wantedLabel Then
                Set FindCurrentAttachment = a
                Exit Function
            End If
            Set lastMatch = a
        End If
    Next a

    If Not lastMatch Is Nothing Then
        Set FindCurrentAttachment = lastMatch
        Exit Function
    End If

    ' No attachment matches the naming convention -- fall back to the newest one.
    Dim count As Long
    count = attachments.Count
    Dim idx As Long
    idx = 0
    For Each a In attachments
        idx = idx + 1
        If idx = count Then Set FindCurrentAttachment = a
    Next a
End Function

' Local files in targetDir matching this item's naming convention (any revision) -- used
' to detect "I already have an older revision" (see DownloadItem). Returns a Collection of
' 2-element Variant arrays (revision label, full path).
Private Function LocalRevisionFiles(ByVal targetDir As String, ByVal itemNumber As Long, ByVal name As String) As Collection
    Dim result As New Collection
    Dim re As Object
    Set re = NewRevisionRegex(itemNumber, name)

    Dim fname As String
    fname = Dir(targetDir & "\*.*")
    Do While fname <> ""
        If re.Test(fname) Then
            Dim m As Object
            Set m = re.Execute(fname)
            result.Add Array(UCase(m(0).SubMatches(0)), targetDir & "\" & fname)
        End If
        fname = Dir()
    Loop
    Set LocalRevisionFiles = result
End Function

Sub WriteBytesToFile(ByRef bytes() As Byte, ByVal targetPath As String)
    Dim fileNum As Integer
    fileNum = FreeFile
    Open targetPath For Binary Access Write As #fileNum
    If UBound(bytes) >= LBound(bytes) Then
        Put #fileNum, , bytes
    End If
    Close #fileNum
End Sub

' Downloads the current CAD file of Part/Assembly "item" into targetDir (see rules 3 and 4
' in the module header). Returns the path to the file that ends up in the folder (freshly
' downloaded, already there, or -- if the user declined the newer revision -- the old one
' they already have), or "" if nothing could be resolved. Progress is reported via
' AppendLog (module-level, see Sub main()).
Function DownloadItem(ByVal item As Object, ByVal targetDir As String) As String
    Dim number As Long
    number = JsonGetLong(item, "itemNumber", 0)
    Dim name As String
    name = JsonGetString(item, "fileName", "")
    Dim label As String
    label = number & " (" & name & ")"

    Dim attachments As Object
    On Error Resume Next
    Err.Clear
    Set attachments = ApiGet("/items/" & JsonGetString(item, "id", "") & "/attachments")
    On Error GoTo 0
    If attachments Is Nothing Then
        AppendLog "  " & label & T("Dl_FetchAttachmentsFailed")
        Exit Function
    End If

    Dim current As Object
    Set current = FindCurrentAttachment(item, attachments)
    If current Is Nothing Then
        AppendLog "  " & label & T("Dl_NoAttachments")
        Exit Function
    End If

    Dim currentName As String
    currentName = JsonGetString(current, "fileName", "")
    Dim targetPath As String
    targetPath = targetDir & "\" & currentName

    If Dir(targetPath) <> "" Then
        Dim sameSize As Boolean
        On Error Resume Next
        sameSize = (FileLen(targetPath) = JsonGetLong(current, "fileSize", -1))
        On Error GoTo 0
        If sameSize Then
            AppendLog "  " & label & T("Dl_AlreadyInFolder")
            DownloadItem = targetPath
            Exit Function
        End If
    End If

    Dim older As Collection
    Set older = LocalRevisionFiles(targetDir, number, name)
    Dim olderOtherRevision As New Collection
    Dim entry As Variant
    For Each entry In older
        If entry(1) <> targetPath Then olderOtherRevision.Add entry
    Next entry

    If olderOtherRevision.Count > 0 Then
        Dim revLabels As String
        revLabels = ""
        For Each entry In olderOtherRevision
            If InStr(revLabels, CStr(entry(0))) = 0 Then
                If revLabels <> "" Then revLabels = revLabels & ", "
                revLabels = revLabels & entry(0)
            End If
        Next entry
        Dim wanted As String
        wanted = "?"
        If JsonGetLong(item, "revisionNumber", 0) > 0 Then wanted = RevisionLabel(JsonGetLong(item, "revisionNumber", 1))

        Dim answer As VbMsgBoxResult
        answer = MsgBox( _
            T("NewerRevisionPart1") & label & T("NewerRevisionPart2") & revLabels & _
            T("NewerRevisionPart3") & wanted & "." & vbCrLf & vbCrLf & _
            T("DownloadNewestQuestion"), vbYesNo, T("TitleNewerRevision"))
        If answer <> vbYes Then
            AppendLog "  " & label & T("Dl_KeepingExistingRevisionPrefix") & revLabels & ")."
            If olderOtherRevision.Count = 1 Then
                DownloadItem = olderOtherRevision(1)(1)
            End If
            Exit Function
        End If
    End If

    AppendLog T("Dl_DownloadingPrefix") & currentName & "..."
    Dim bytes() As Byte
    On Error Resume Next
    Err.Clear
    bytes = ApiGetBinary("/attachments/" & JsonGetString(current, "id", "") & "/file")
    Dim dlErrNum As Long, dlErrDesc As String
    dlErrNum = Err.Number
    dlErrDesc = Err.Description
    On Error GoTo 0
    If dlErrNum <> 0 Then
        AppendLog "  " & label & T("Dl_ErrorDownloadingMid") & currentName & " (" & dlErrDesc & ")."
        Exit Function
    End If

    EnsureDirectory targetDir
    WriteBytesToFile bytes, targetPath
    AppendLog T("Dl_Saved") & currentName
    DownloadItem = targetPath
End Function

' Recursively downloads the WHOLE component tree of an Assembly (direct children, then
' their children, and so on) into targetDir -- see point 2 in the module header. `seen`
' (a Scripting.Dictionary keyed by item id) protects against downloading the same shared
' component twice.
Sub DownloadChildrenRecursive(ByVal item As Object, ByVal targetDir As String, ByRef seen As Object)
    Dim rows As Object
    On Error Resume Next
    Err.Clear
    Set rows = ApiGet("/items/" & JsonGetString(item, "id", "") & "/children")
    On Error GoTo 0
    If rows Is Nothing Then
        AppendLog "  " & JsonGetLong(item, "itemNumber", 0) & " (" & JsonGetString(item, "fileName", "") & T("Dl_FetchChildrenFailed")
        Exit Sub
    End If

    ' NOTE: each row is {"item": {...}, "quantity": ..., "position": ...} -- the item
    ' itself is NESTED under the "item" key (same convention as childEntries in
    ' item-detail-panel.tsx), not a flat object like GET /api/items.
    Dim row As Variant
    For Each row In rows
        Dim child As Object
        Set child = JsonGetObject(row, "item")
        If Not child Is Nothing Then
            Dim childId As String
            childId = JsonGetString(child, "id", "")
            If childId <> "" And Not seen.Exists(childId) Then
                seen.Add childId, True
                DownloadItem child, targetDir
                If JsonGetString(child, "itemType", "") = "assembly" Then
                    DownloadChildrenRecursive child, targetDir, seen
                End If
            End If
        End If
    Next row
End Sub


' ============================================================================
' Entry point -- run via Tools -> Macro -> Run (or F5 in the VBA editor, with the cursor
' inside Sub "main").
' ============================================================================

Sub AppendLog(ByVal message As String)
    gLogText = gLogText & message & vbCrLf
    LogLine message
End Sub

Sub main()
    LogLine "=== EasyPDM download macro started ==="

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

    ' Which item to download is picked in the browser, not a native InputBox -- exactly
    ' like EasyPDM.FreeCad/EasyPDMDownload.FCMacro (see BuildBrowserDownloadUrl/
    ' WaitForTicket above).
    Dim ticket As String
    ticket = NewGuid()
    OpenUrlInBrowser BuildBrowserDownloadUrl(ticket)

    Dim ticketData As Object
    Set ticketData = WaitForTicket(ticket)
    If ticketData Is Nothing Then
        MsgBox T("CancelledNothingDownloaded"), vbInformation, T("AppTitle")
        LogLine "=== Finished: browser ticket cancelled/timed out ==="
        Exit Sub
    End If

    Dim topItem As Object
    Set topItem = ApiGet("/items/" & JsonGetString(ticketData, "itemId", ""))
    If topItem Is Nothing Then
        MsgBox T("SelectedItemLoadFailed"), vbExclamation, T("AppTitle")
        Exit Sub
    End If

    Dim defaultFolder As String
    defaultFolder = GetDownloadFolder()
    Dim targetDir As String
    targetDir = InputBox(T("PromptTargetFolder"), T("TitleDownloadFromPdm"), defaultFolder)
    targetDir = Trim(targetDir)
    If targetDir = "" Then Exit Sub
    If Right(targetDir, 1) = "\" Then targetDir = Left(targetDir, Len(targetDir) - 1)

    On Error GoTo Failed
    EnsureDirectory targetDir
    SetDownloadFolder targetDir

    gLogText = ""
    AppendLog T("Dl_DownloadingToPrefix") & JsonGetLong(topItem, "itemNumber", 0) & " (" & JsonGetString(topItem, "fileName", "") & T("Dl_DownloadingToMiddle") & targetDir & "..."

    Dim seen As Object
    Set seen = CreateObject("Scripting.Dictionary")
    seen.Add JsonGetString(topItem, "id", ""), True

    Dim topPath As String
    topPath = DownloadItem(topItem, targetDir)
    If JsonGetString(topItem, "itemType", "") = "assembly" Then
        DownloadChildrenRecursive topItem, targetDir, seen
    End If

    If topPath <> "" And Dir(topPath) <> "" Then
        Dim docType As Long
        Dim ext As String
        ext = UCase(Mid(topPath, InStrRev(topPath, ".") + 1))
        Select Case ext
            Case "SLDPRT": docType = SW_DOC_PART
            Case "SLDASM": docType = SW_DOC_ASSEMBLY
            Case "SLDDRW": docType = SW_DOC_DRAWING
            Case Else: docType = SW_DOC_PART
        End Select

        Dim openErrors As Long, openWarnings As Long
        Dim opened As Object
        Set opened = swApp.OpenDoc6(topPath, docType, 0, "", openErrors, openWarnings)
        If opened Is Nothing Then
            AppendLog T("Dl_FailedToOpenPrefix") & topPath & T("Dl_FailedToOpenSuffix") & openErrors & ")."
        Else
            AppendLog T("Dl_Opened") & topPath
        End If
    Else
        AppendLog T("Dl_CouldNotDetermineMainFile")
    End If

    MsgBox gLogText, vbInformation, T("AppTitle")
    LogLine "=== Finished ==="
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

' Separate Sub -- can be bound to your own toolbar button/shortcut to log out of EasyPDM.
Sub Logout()
    ApiLogout
    MsgBox T("LoggedOutMessage"), vbInformation, T("AppTitle")
End Sub
