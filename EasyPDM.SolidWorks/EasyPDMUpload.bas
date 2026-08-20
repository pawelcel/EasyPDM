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
'     cannot mask a typed password with asterisks.
'   - DELIBERATELY OUT OF SCOPE for this version: automatic detection of a whole assembly
'     tree (App::Link -> IComponent2, recursively uploading every component at once) -- the
'     most complex part of the original macro. Here you upload ONE active document at a
'     time (Part/Assembly/Drawing); the BOM structure is built in the web app.
'   - Recognizing an "already uploaded" document: NOT via label/filename (SolidWorks has
'     no equivalent of FreeCAD's free-form label) -- via document Custom Properties
'     (EasyPDM_ItemId/EasyPDM_ItemNumber), written into the file itself after a successful
'     upload. More durable than the FreeCAD approach (also works in a brand NEW session,
'     no need to manually save after a label change).
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

' SolidWorks application object -- CONTRARY to this module's earlier (wrong) assumption,
' "swApp" is NOT automatically visible in EVERY VBA module of the project, only in the one
' SolidWorks itself generates via "Tools -> Macro -> New" (that one gets its own "Dim
' swApp"). This file is imported as a SEPARATE module, so it needs its own declaration --
' and its own assignment at the start of main() via "Application.SldWorks" (the standard
' way to obtain the application object from VBA hosted inside SolidWorks itself).
Private swApp As Object


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
    Err.Raise ERR_API, "EasyPDM", "No connection to " & GetBaseUrl() & ": " & Err.Description
End Function

Function ApiRegisterAttachment(ByVal itemId As String, ByVal filePath As String) As Object
    Dim bodyJson As String
    bodyJson = "{""filePath"":" & JsonStr(filePath) & "}"
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
Function ApiUploadFile(ByVal path As String, ByVal filePath As String, Optional ByVal overrideFilename As String = "") As Object
    Dim boundary As String
    boundary = "----EasyPDMBoundary" & Format(Now, "yyyymmddhhnnss") & CStr(Int(Rnd * 100000))

    Dim fileName As String
    If overrideFilename <> "" Then
        fileName = overrideFilename
    Else
        fileName = Mid(filePath, InStrRev(filePath, "\") + 1)
    End If

    Dim head As String
    head = "--" & boundary & vbCrLf & _
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

    Dim http As Object
    Set http = NewHttpRequest()
    http.Open "POST", GetBaseUrl() & path, False
    http.setRequestHeader "Content-Type", "multipart/form-data; boundary=" & boundary
    Dim cookie As String
    cookie = AuthCookieHeader()
    If cookie <> "" Then http.setRequestHeader "Cookie", cookie

    On Error GoTo NetErr
    http.send body
    On Error GoTo 0

    LogLine "POST (upload, " & totalLen & " B) " & path & " -> " & http.Status
    RaiseForStatus http.Status, http.responseText
    If Trim(http.responseText) = "" Then
        Set ApiUploadFile = Nothing
    Else
        Set ApiUploadFile = JsonParse(http.responseText)
    End If
    Exit Function
NetErr:
    LogLine "POST (upload) " & path & " -> NO CONNECTION: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", "No connection to " & GetBaseUrl() & ": " & Err.Description
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
        Err.Raise ERR_API, "EasyPDM", "Login failed: " & http.responseText
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
        ' A plain InputBox does not mask typed text with asterisks -- a limitation of this
        ' simplified macro (no custom UserForm with a password field).
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

' Collects Part/Assembly properties from the user using the SAME rules as
' kind_field_visibility() in the FreeCAD macro / PartPropertyForm+add-node-dialog in the
' web app: Manufactured -> Material; Purchased -> Manufacturer/Order numbers/Mass;
' Standard -> Material/Norm; Client -> no extra fields. Assembly: Material/Mass always
' shown together, kind is optional and without "Client".
Function PromptPartProperties(ByVal isPart As Boolean) As String
    Dim kindsText As String
    If isPart Then
        kindsText = "1 - Manufactured" & vbCrLf & "2 - Purchased" & vbCrLf & "3 - Standard" & vbCrLf & "4 - Client"
    Else
        kindsText = "1 - Manufactured" & vbCrLf & "2 - Purchased" & vbCrLf & "3 - Standard" & vbCrLf & "(leave empty - no kind)"
    End If

    Dim choice As String
    Dim rodzaj As String
    choice = InputBox("Kind:" & vbCrLf & kindsText, "Properties")
    Select Case Trim(choice)
        Case "1": rodzaj = "Wykonywana"
        Case "2": rodzaj = "Zakupowa"
        Case "3": rodzaj = "Normalia"
        Case "4": If isPart Then rodzaj = "Klienta" Else rodzaj = ""
        Case Else: rodzaj = ""
    End Select

    If isPart And rodzaj = "" Then
        MsgBox "Kind is required for a Part.", vbExclamation, "EasyPDM"
        PromptPartProperties = PromptPartProperties(isPart)
        Exit Function
    End If

    Dim props As String
    Dim firstProp As Boolean
    props = "{"
    firstProp = True

    If rodzaj <> "" Then
        props = props & """rodzaj"":" & JsonStr(rodzaj)
        firstProp = False
    End If

    Dim showMaterial As Boolean, showMass As Boolean, showPurchase As Boolean, showNorm As Boolean
    If isPart Then
        showMaterial = (rodzaj = "Wykonywana" Or rodzaj = "Normalia")
        showMass = (rodzaj = "Zakupowa")
        showPurchase = (rodzaj = "Zakupowa")
        showNorm = (rodzaj = "Normalia")
    Else
        showMaterial = True
        showMass = True
        showPurchase = False
        showNorm = False
    End If

    Dim fieldValue As String

    If showMaterial Then
        fieldValue = InputBox("Material (optional):", "Properties")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """material"":" & JsonStr(fieldValue)
            firstProp = False
        End If
    End If

    If showMass Then
        fieldValue = InputBox("Mass (optional):", "Properties")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """mass"":" & JsonStr(fieldValue)
            firstProp = False
        End If
    End If

    If showPurchase Then
        fieldValue = InputBox("Manufacturer (optional):", "Properties")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """manufacturer"":" & JsonStr(fieldValue)
            firstProp = False
        End If

        fieldValue = InputBox("Order number 1 (optional):", "Properties")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """orderNumber"":" & JsonStr(fieldValue)
            firstProp = False
        End If

        fieldValue = InputBox("Order number 2 (optional):", "Properties")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """orderNumber2"":" & JsonStr(fieldValue)
            firstProp = False
        End If
    End If

    If showNorm Then
        fieldValue = InputBox("Norm (optional):", "Properties")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """norm"":" & JsonStr(fieldValue)
            firstProp = False
        End If
    End If

    props = props & "}"
    PromptPartProperties = props
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

' Shared ending of both modes: COPIES the current document file into PDM under the name
' "number (name).REVISION.extension" -- the same convention as itemDisplayLabel in the
' frontend and _rename_and_upload in the FreeCAD macro. The LOCAL file is left untouched --
' neither moved nor deleted.
Function RenameAndUpload(ByVal filePath As String, ByVal itemId As String, ByVal itemNumber As Long, ByVal name As String, ByVal revision As Long) As Boolean
    Dim ext As String
    Dim dotPos As Long
    dotPos = InStrRev(filePath, ".")
    If dotPos > 0 Then ext = Mid(filePath, dotPos) Else ext = ""

    Dim newFilename As String
    newFilename = itemNumber & " (" & SanitizeFilename(name) & ")." & RevisionLabel(revision) & ext
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
            ApiRegisterAttachment itemId, targetPath
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
                Err.Raise ERR_API, "EasyPDM", "Attachment registration failed: " & registerErrDesc
            End If
            LogLine "Registered attachment: " & targetPath
        End If

        RenameAndUpload = True
        Exit Function
    End If

    LogLine "Plain HTTP upload: /items/" & itemId & "/attachments as """ & newFilename & """"
    ApiUploadFile "/items/" & itemId & "/attachments", filePath, newFilename
    RenameAndUpload = True
End Function


' ============================================================================
' PDM core: new item / attach to an existing item (with revision handling).
' ============================================================================

' Creates a NEW item in PDM, then attaches the current document as its file. A new item
' always starts at revision 1.
Function PushNewItemToPdm(ByVal projectId As String, ByVal itemType As String, ByVal name As String, ByVal propertiesJson As String, ByVal filePath As String) As Object
    Dim bodyJson As String
    bodyJson = "{""name"":" & JsonStr(name) & ",""itemType"":" & JsonStr(itemType) & ",""properties"":" & propertiesJson & "}"

    Dim created As Object
    Set created = ApiPostJson("/projects/" & projectId & "/nodes", bodyJson)

    Dim itemId As String, itemNumber As Long
    itemId = JsonGetString(created, "id", "")
    itemNumber = JsonGetLong(created, "itemNumber", 0)
    LogLine "Created new PDM item: #" & itemNumber & " (id " & itemId & "), project " & projectId & ", type " & itemType

    RenameAndUpload filePath, itemId, itemNumber, name, 1

    Dim result As Object
    Set result = CreateObject("Scripting.Dictionary")
    result.Add "itemId", itemId
    result.Add "itemNumber", itemNumber
    result.Add "revision", 1
    Set PushNewItemToPdm = result
End Function

' Attaches the current document as the current file of an ALREADY EXISTING Part/Assembly --
' without creating a new record. If the item's status is "wydany" (released -- PDM does not
' allow attaching files in that status), asks for consent to create a new revision plus an
' optional comment -- the exact same mechanism as in the web app (PATCH /items/{id}/status
' bumps the revision number). Returns Nothing if the user declined the new revision.
Function PushToExistingItem(ByVal itemId As String, ByVal filePath As String) As Object
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
        proceed = MsgBox("Item #" & itemNumber & " is in status ""Released"" -- attaching a file requires a new revision. Create a new revision?", vbYesNo + vbQuestion, "New revision")
        If proceed <> vbYes Then
            Set PushToExistingItem = Nothing
            Exit Function
        End If

        Dim comment As String
        comment = InputBox("New revision comment (optional):", "New revision")

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
    RenameAndUpload filePath, itemId, itemNumber, fileName, revision
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
                "The status/revision of item #" & itemNumber & " have already been changed on the server " & _
                "(revision " & revision & ", status 'w_pracy'), but attaching the file failed: " & _
                uploadErrDesc & vbCrLf & vbCrLf & _
                "The item in PDM now has a new revision, but still the file of the previous version -- fix " & _
                "this manually in the web app (attach the file again) or retry from this macro."
        Else
            Err.Raise ERR_API, "EasyPDM", uploadErrDesc
        End If
    End If

    Dim result As Object
    Set result = CreateObject("Scripting.Dictionary")
    result.Add "itemId", itemId
    result.Add "itemNumber", itemNumber
    result.Add "revision", revision
    Set PushToExistingItem = result
End Function

Function PromptForProject() As String
    Dim projects As Object
    Set projects = ApiGet("/projects")
    If projects Is Nothing Or projects.Count = 0 Then
        MsgBox "No projects available in PDM.", vbExclamation, "EasyPDM"
        PromptForProject = ""
        Exit Function
    End If

    Dim ids() As String
    ReDim ids(1 To projects.Count)
    Dim listText As String
    Dim i As Long
    i = 0
    Dim p As Variant
    For Each p In projects
        i = i + 1
        ids(i) = JsonGetString(p, "id", "")
        listText = listText & i & " - " & JsonGetString(p, "name", "") & vbCrLf
    Next p

    Dim choice As String
    choice = InputBox("Pick a project (number):" & vbCrLf & listText, "New item in PDM")
    Dim idx As Long
    idx = Val(choice)
    If idx < 1 Or idx > projects.Count Then
        PromptForProject = ""
    Else
        PromptForProject = ids(idx)
    End If
End Function

' Looks up a Part/Assembly by the number visible to the user (e.g. in the name
' "67 (Name)") -- NOT by GUID, which a regular user never sees anywhere. Searches the
' WHOLE database, not just the current project -- a component can be used across multiple
' projects.
Function PromptForExistingItem() As Object
    Dim numberText As String
    numberText = InputBox("Item number in PDM (visible e.g. in the name ""67 (Name)""):", "Existing item")
    If Trim(numberText) = "" Then
        Set PromptForExistingItem = Nothing
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
                Set PromptForExistingItem = it
                Exit Function
            End If
        End If
    Next it

    MsgBox "No Part/Assembly found with number " & targetNumber & ".", vbExclamation, "EasyPDM"
    Set PromptForExistingItem = Nothing
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
        MsgBox "Failed to save the document -- save it manually (Ctrl+S) and run the macro again.", vbExclamation, "EasyPDM"
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

    Dim baseName As String
    baseName = Mid(filePath, InStrRev(filePath, "\") + 1)
    Dim dotPos As Long
    dotPos = InStrRev(baseName, ".")
    If dotPos > 0 Then baseName = Left(baseName, dotPos - 1)
    defaultName = baseName

    GetActiveDocInfo = True
End Function

Private Function GetCustPropMgr() As Object
    Set GetCustPropMgr = swApp.ActiveDoc.Extension.CustomPropertyManager("")
End Function

' Reads the PDM item id saved in the document's Custom Properties (if this document was
' already uploaded via this macro before) -- empty string if the document is not yet linked
' to any PDM item.
Function GetLinkedItemId() As String
    Dim mgr As Object
    Set mgr = GetCustPropMgr()
    Dim valOut As String, resolvedOut As String
    On Error Resume Next
    mgr.Get4 CUSTPROP_ITEM_ID, False, valOut, resolvedOut
    On Error GoTo 0
    GetLinkedItemId = valOut
End Function

' Saves the document-to-PDM-item link as Custom Properties -- unlike the FreeCAD approach
' (changing the label, NOT saved to disk), this works reliably in a brand NEW SolidWorks
' session too, since Properties are part of the file itself.
Sub SetLinkedItem(ByVal itemId As String, ByVal itemNumberText As String)
    Dim mgr As Object
    Set mgr = GetCustPropMgr()
    mgr.Add3 CUSTPROP_ITEM_ID, SW_CUSTOM_INFO_TEXT, itemId, SW_CUSTOM_PROPERTY_REPLACE
    mgr.Add3 CUSTPROP_ITEM_NUMBER, SW_CUSTOM_INFO_TEXT, itemNumberText, SW_CUSTOM_PROPERTY_REPLACE
End Sub


' ============================================================================
' Entry point -- run via Tools -> Macro -> Run (or F5 in the VBA editor).
' ============================================================================

Sub main()
    LogLine "=== EasyPDM macro started ==="

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

    Dim filePath As String, itemTypeGuess As String, defaultName As String
    If Not GetActiveDocInfo(filePath, itemTypeGuess, defaultName) Then
        MsgBox "No active, saved document.", vbExclamation, "EasyPDM"
        LogLine "No active/saved document -- done."
        Exit Sub
    End If
    LogLine "Active document: """ & filePath & """, detected type: " & itemTypeGuess

    On Error GoTo Failed

    Dim linkedItemId As String
    linkedItemId = GetLinkedItemId()

    Dim resultInfo As Object

    If linkedItemId <> "" Then
        Dim confirmUpdate As VbMsgBoxResult
        confirmUpdate = MsgBox("This document is already linked to a PDM item. Attach the current version as a new revision/update?", vbYesNo + vbQuestion, "EasyPDM")
        If confirmUpdate <> vbYes Then Exit Sub
        Set resultInfo = PushToExistingItem(linkedItemId, filePath)
    Else
        Dim mode As VbMsgBoxResult
        mode = MsgBox("Does this document already exist in PDM (attach a new version to an existing item)?" & vbCrLf & _
                       "Yes = I will pick an existing item by number." & vbCrLf & _
                       "No = I will create a new item in PDM.", vbYesNoCancel + vbQuestion, "EasyPDM")
        If mode = vbCancel Then Exit Sub

        If mode = vbYes Then
            Dim existingItem As Object
            Set existingItem = PromptForExistingItem()
            If existingItem Is Nothing Then Exit Sub
            linkedItemId = JsonGetString(existingItem, "id", "")
            Set resultInfo = PushToExistingItem(linkedItemId, filePath)
        Else
            Dim projectId As String
            projectId = PromptForProject()
            If projectId = "" Then Exit Sub

            Dim name As String
            name = InputBox("Item name:", "New item in PDM", defaultName)
            If Trim(name) = "" Then Exit Sub

            Dim itemType As String
            If itemTypeGuess = "part" Or itemTypeGuess = "assembly" Then
                itemType = itemTypeGuess
            Else
                itemType = "file"
            End If

            Dim propertiesJson As String
            If itemType = "part" Or itemType = "assembly" Then
                propertiesJson = PromptPartProperties(itemType = "part")
            Else
                propertiesJson = "{}"
            End If

            Set resultInfo = PushNewItemToPdm(projectId, itemType, name, propertiesJson, filePath)
            linkedItemId = JsonGetString(resultInfo, "itemId", "")
        End If
    End If

    If Not resultInfo Is Nothing Then
        SetLinkedItem linkedItemId, CStr(JsonGetLong(resultInfo, "itemNumber", 0))
        LogLine "=== Finished successfully: item #" & JsonGetLong(resultInfo, "itemNumber", 0) & _
                ", revision " & RevisionLabel(JsonGetLong(resultInfo, "revision", 1)) & " ==="
        MsgBox "Uploaded to EasyPDM: item #" & JsonGetLong(resultInfo, "itemNumber", 0) & _
               " (revision " & RevisionLabel(JsonGetLong(resultInfo, "revision", 1)) & ")." & vbCrLf & vbCrLf & _
               "Run log: " & LogFilePath(), vbInformation, "EasyPDM"
    Else
        LogLine "=== Finished without uploading (cancelled or no new revision) ==="
    End If
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

' Separate Sub -- can be bound to your own toolbar button/shortcut to log out of EasyPDM
' without running the whole upload flow (the next run of "main" will ask to log in again).
Sub Logout()
    ApiLogout
    MsgBox "Logged out of EasyPDM.", vbInformation, "EasyPDM"
End Sub
