Attribute VB_Name = "EasyPDMDownload"
Option Explicit

' ============================================================================
' EasyPDMDownload -- SolidWorks macro that downloads a Part/Assembly from EasyPDM and
' opens it.
'
' This is the COUNTERPART of EasyPDM.FreeCad/EasyPDMDownload.FCMacro, not a 1:1 port -- see
' EasyPDMUpload.bas's header for the general reasons (own minimal JSON, plain InputBox/
' MsgBox instead of a real search dialog -- VBA UserForms are separate binary resources,
' not embeddable in a plain .bas text file, so this uses the same InputBox-based picker
' pattern as "existing item" lookup in EasyPDMUpload.bas).
'
' This file is fully SELF-CONTAINED (does not depend on EasyPDMUpload.bas being present in
' the same VBA project) -- it duplicates the login/JSON/HTTP helpers rather than importing
' them, the same way EasyPDM.FreeCad/EasyPDMDownload.FCMacro duplicates rather than imports
' from EasyPDMUpload.FCMacro. The API address, session token and download folder are
' shared with EasyPDMUpload.bas through the SAME Windows registry keys (identical constants
' below) -- logging in from one macro is enough for both.
'
' All comments/strings are plain English (ASCII only) -- VBA's file import does not
' reliably handle UTF-8 (confirmed in practice while building EasyPDMUpload.bas).
'
' What it does:
'   1. Asks for the PDM item number (Part/Assembly) to download, and the target folder
'      (defaults to the last one used -- shared with EasyPDMUpload.bas's target folder).
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

' SolidWorks application object -- declared and assigned at the start of Sub main(), same
' reasoning as EasyPDMUpload.bas ("swApp" is only auto-provided in the module SolidWorks
' itself generates via "Tools -> Macro -> New", not in an imported module like this one).
Private swApp As Object


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
        Err.Raise ERR_API, "EasyPDM", "Server error (" & status & "): " & responseText
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
    Err.Raise ERR_API, "EasyPDM", "No connection to " & GetBaseUrl() & ": " & Err.Description
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
        Err.Raise ERR_AUTH, "EasyPDM", "Session expired."
    ElseIf http.Status < 200 Or http.Status >= 300 Then
        Err.Raise ERR_API, "EasyPDM", "Server error (" & http.Status & ")."
    End If

    ApiGetBinary = http.responseBody
    Exit Function
NetErr:
    LogLine "GET (binary) " & path & " -> NO CONNECTION: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", "No connection to " & GetBaseUrl() & ": " & Err.Description
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
    Err.Raise ERR_API, "EasyPDM", "No connection to " & GetBaseUrl() & ": " & Err.Description
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
        Err.Raise ERR_API, "EasyPDM", "Login failed: " & http.responseText
    End If

    Dim user As Object
    Set user = JsonParse(http.responseText)

    Dim token As String
    token = JsonGetString(user, "sessionToken", "")
    If token = "" Then token = ExtractSessionCookie(http)
    If token = "" Then
        LogLine "Login: server responded 2xx, but no session token was found either in the response body or in the headers."
        Err.Raise ERR_API, "EasyPDM", "Login failed -- the server did not return a session."
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
    Err.Raise ERR_API, "EasyPDM", "No connection to " & GetBaseUrl() & ": " & Err.Description
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
    serverUrl = InputBox("EasyPDM API address:", "Log in to PDM", GetBaseUrl())
    If Trim(serverUrl) = "" Then
        PromptLogin = False
        Exit Function
    End If
    SetBaseUrl serverUrl

    Dim attempt As Integer
    For attempt = 1 To 3
        Dim username As String, password As String
        username = InputBox("Username:", "Log in to PDM")
        If Trim(username) = "" Then
            PromptLogin = False
            Exit Function
        End If
        password = InputBox("Password (NOTE: this field does not mask typed characters):", "Log in to PDM")
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
            MsgBox "Logged in as " & JsonGetString(user, "displayName", username) & ".", vbInformation, "EasyPDM"
            PromptLogin = True
            Exit Function
        Else
            MsgBox "Login failed: " & loginErrDesc, vbExclamation, "EasyPDM"
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
        AppendLog "  " & label & ": failed to fetch the attachment list."
        Exit Function
    End If

    Dim current As Object
    Set current = FindCurrentAttachment(item, attachments)
    If current Is Nothing Then
        AppendLog "  " & label & ": no CAD files to download (the item has no attachments)."
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
            AppendLog "  " & label & ": already in the folder (current revision), skipping."
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
            "The folder already has " & label & " at revision " & revLabels & _
            ", but the current one on the server is revision " & wanted & "." & vbCrLf & vbCrLf & _
            "Download the newest version?", vbYesNo, "Newer revision")
        If answer <> vbYes Then
            AppendLog "  " & label & ": keeping the existing local revision (" & revLabels & ")."
            If olderOtherRevision.Count = 1 Then
                DownloadItem = olderOtherRevision(1)(1)
            End If
            Exit Function
        End If
    End If

    AppendLog "  Downloading " & currentName & "..."
    Dim bytes() As Byte
    On Error Resume Next
    Err.Clear
    bytes = ApiGetBinary("/attachments/" & JsonGetString(current, "id", "") & "/file")
    Dim dlErrNum As Long, dlErrDesc As String
    dlErrNum = Err.Number
    dlErrDesc = Err.Description
    On Error GoTo 0
    If dlErrNum <> 0 Then
        AppendLog "  " & label & ": ERROR downloading " & currentName & " (" & dlErrDesc & ")."
        Exit Function
    End If

    EnsureDirectory targetDir
    WriteBytesToFile bytes, targetPath
    AppendLog "  Saved: " & currentName
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
        AppendLog "  " & JsonGetLong(item, "itemNumber", 0) & " (" & JsonGetString(item, "fileName", "") & "): failed to fetch the component list."
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
' Item lookup -- same InputBox-based pattern as PromptForExistingItem in EasyPDMUpload.bas
' (a real searchable dialog would need a VBA UserForm, a separate binary resource not
' embeddable in a plain .bas file).
' ============================================================================

Function PromptForItem() As Object
    Dim numberText As String
    numberText = InputBox("PDM item number to download (visible e.g. in the name ""67 (Name)""):", "Download from PDM")
    If Trim(numberText) = "" Then
        Set PromptForItem = Nothing
        Exit Function
    End If
    Dim targetNumber As Long
    targetNumber = Val(numberText)

    Dim items As Object
    Set items = ApiGet("/items")
    Dim it As Variant
    For Each it In items
        If JsonGetLong(it, "itemNumber", -1) = targetNumber Then
            Dim itemType As String
            itemType = JsonGetString(it, "itemType", "")
            If itemType = "part" Or itemType = "assembly" Then
                Set PromptForItem = it
                Exit Function
            End If
        End If
    Next it

    MsgBox "No Part/Assembly found with number " & targetNumber & ".", vbExclamation, "EasyPDM"
    Set PromptForItem = Nothing
End Function


' ============================================================================
' Entry point -- run via Tools -> Macro -> Run (or F5 in the VBA editor, with the cursor
' inside Sub "main").
' ============================================================================

' A module-level log used only within one run of main(), so DownloadItem/
' DownloadChildrenRecursive can append to it -- they call it directly since they live in
' the same module.
Private gLogLines As Collection

Sub AppendLog(ByVal message As String)
    gLogLines.Add message
    LogLine message
End Sub

Sub main()
    LogLine "=== EasyPDM download macro started ==="

    On Error Resume Next
    Set swApp = Application.SldWorks
    On Error GoTo 0

    If swApp Is Nothing Then
        MsgBox "This macro must be run from inside SolidWorks.", vbCritical, "EasyPDM"
        Exit Sub
    End If

    If Not EnsureLoggedIn() Then
        LogLine "Login cancelled -- done."
        Exit Sub
    End If

    Dim topItem As Object
    Set topItem = PromptForItem()
    If topItem Is Nothing Then Exit Sub

    Dim defaultFolder As String
    defaultFolder = GetDownloadFolder()
    Dim targetDir As String
    targetDir = InputBox("Target folder:", "Download from PDM", defaultFolder)
    targetDir = Trim(targetDir)
    If targetDir = "" Then Exit Sub
    If Right(targetDir, 1) = "\" Then targetDir = Left(targetDir, Len(targetDir) - 1)

    On Error GoTo Failed
    EnsureDirectory targetDir
    SetDownloadFolder targetDir

    Set gLogLines = New Collection
    AppendLog "Downloading " & JsonGetLong(topItem, "itemNumber", 0) & " (" & JsonGetString(topItem, "fileName", "") & ") to " & targetDir & "..."

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
            AppendLog "Failed to open " & topPath & " in SolidWorks (error code " & openErrors & ")."
        Else
            AppendLog "Opened: " & topPath
        End If
    Else
        AppendLog "Could not determine the main file to open."
    End If

    Dim summary As String
    Dim i As Long
    For i = 1 To gLogLines.Count
        summary = summary & gLogLines(i) & vbCrLf
    Next i
    MsgBox summary, vbInformation, "EasyPDM"
    LogLine "=== Finished ==="
    Exit Sub

Failed:
    LogLine "=== ERROR (" & Err.Number & "): " & Err.Description & " ==="
    If Err.Number = ERR_AUTH Then
        MsgBox "Session expired -- run the macro again to log in." & vbCrLf & vbCrLf & _
               "Run log: " & LogFilePath(), vbExclamation, "EasyPDM"
        SetSessionToken ""
    Else
        MsgBox "Error: " & Err.Description & vbCrLf & vbCrLf & "Run log: " & LogFilePath(), vbCritical, "EasyPDM"
    End If
End Sub

' Separate Sub -- can be bound to your own toolbar button/shortcut to log out of EasyPDM.
Sub Logout()
    ApiLogout
    MsgBox "Logged out of EasyPDM.", vbInformation, "EasyPDM"
End Sub
