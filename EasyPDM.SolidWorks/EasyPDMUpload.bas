Attribute VB_Name = "EasyPDMUpload"
Option Explicit

' ============================================================================
' EasyPDMUpload — makro SolidWorks do wysyłania aktywnego dokumentu do EasyPDM.
'
' NIEZWERYFIKOWANE: napisane bez dostępu do SolidWorks/VBA w tym środowisku (Linux) — nie
' było możliwości sprawdzić nawet składni, nie mówiąc o rzeczywistym uruchomieniu. Oparte
' wyłącznie o dobrze znane, standardowe wzorce VBA i SolidWorks API. Przed pierwszym
' realnym użyciem przejrzyj kod w edytorze VBA (pokaże błędy składniowe od razu po
' otwarciu/imporcie) i przetestuj na nieistotnym dokumencie.
'
' To ODPOWIEDNIK makra EasyPDM.FreeCad/EasyPDMUpload.FCMacro, nie 1:1 port — VBA nie ma
' wbudowanego JSON ani okien dialogowych bez osobnych plików binarnych (UserForm), więc:
'   - JSON: własny, minimalny parser/budowniczy (poniżej), wystarczający do kształtów
'     odpowiedzi tego konkretnego API — NIE ogólnego przeznaczenia.
'   - Okna: zwykłe InputBox/MsgBox zamiast rozwijanych list/formularzy Qt — haseł nie da
'     się zamaskować gwiazdkami zwykłym InputBoxem.
'   - ŚWIADOMIE POMINIĘTE (poza zakresem tej wersji): automatyczne wykrywanie całego
'     drzewa złożenia (App::Link -> IComponent2, rekurencyjne wysyłanie wszystkich
'     komponentów naraz) — najbardziej złożona część oryginalnego makra. Tu wysyłasz
'     JEDEN aktywny dokument na raz (Część/Złożenie/Rysunek); strukturę BOM buduje się
'     w aplikacji webowej.
'   - Rozpoznawanie "już wysłanego" dokumentu: NIE przez etykietę/nazwę pliku (SolidWorks
'     nie ma odpowiednika swobodnej etykiety FreeCAD) — przez Właściwości niestandardowe
'     dokumentu (EasyPDM_ItemId/EasyPDM_ItemNumber), zapisywane w samym pliku po udanej
'     wysyłce. Trwalsze niż podejście FreeCAD (działa też w NOWEJ sesji, bez potrzeby
'     ręcznego zapisu po zmianie etykiety).
'
' Instalacja:
'   SolidWorks -> Narzędzia -> Makro -> Nowy... (utwórz dowolny, pusty projekt makra),
'   w edytorze VBA: Plik -> Importuj plik... -> wskaż EasyPDMUpload.bas. Uruchamiaj przez
'   Narzędzia -> Makro -> Uruchom (albo F5 w samym edytorze VBA), Sub "main".
'   Osobny Sub "Logout" wylogowuje i można go przypiąć do własnego przycisku/skrótu.
'
' Adres API (domyślnie http://localhost:5000/api) i token sesji zapisywane są w rejestrze
' Windows przez SaveSetting/GetSetting (gałąź "HKEY_CURRENT_USER\Software\VB and VBA
' Program Settings\EasyPDM\Connection") — dokładnie ten sam cel co User parameter FreeCAD,
' tylko inny mechanizm przechowywania właściwy dla VBA.
' ============================================================================

Private Const APP_SETTINGS_NAME As String = "EasyPDM"
Private Const SETTINGS_SECTION As String = "Connection"
Private Const DEFAULT_BASE_URL As String = "http://localhost:5000/api"
Private Const SESSION_COOKIE_NAME As String = "pdm_session"

' Nazwy Właściwości niestandardowych dokumentu, w których zapisujemy powiązanie z PDM —
' zob. nagłówek modułu.
Private Const CUSTPROP_ITEM_ID As String = "EasyPDM_ItemId"
Private Const CUSTPROP_ITEM_NUMBER As String = "EasyPDM_ItemNumber"

' Własne numery błędów (Err.Raise) — odróżniają "brak/wygasła sesja" (ERR_AUTH, ma
' spowodować ponowne logowanie) od zwykłego błędu API (ERR_API, tylko komunikat).
Private Const ERR_AUTH As Long = vbObjectError + 1001
Private Const ERR_API As Long = vbObjectError + 1002


' ============================================================================
' Log — jedyny sposób, żeby zobaczyć krok po kroku co makro faktycznie zrobiło (i gdzie
' konkretnie się wywaliło), skoro nie ma tu żadnej konsoli jak w FreeCAD/przeglądarce.
' Zwykły plik tekstowy w %TEMP%, dopisywany (nie nadpisywany) przy każdym uruchomieniu —
' otwórz go zwykłym Notatnikiem. Ścieżkę widać też w oknie podsumowania na końcu "main".
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
' Ustawienia (adres API / token sesji / zapamiętana nazwa użytkownika) — rejestr Windows
' przez SaveSetting/GetSetting, standardowy wbudowany mechanizm VBA do tego celu.
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
' Minimalny JSON — własny parser/budowniczy, wystarczający do kształtów odpowiedzi TEGO
' API (proste obiekty/tablice/stringi/liczby/bool/null), NIE ogólnego przeznaczenia.
' Obiekt JSON -> Scripting.Dictionary, tablica JSON -> Collection.
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
    If Mid(s, pos, 1) = """" Then pos = pos + 1 ' pomiń otwierający "
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
    ' Val() (nie CDbl!) — CDbl zależy od separatora dziesiętnego bieżącego locale systemu
    ' (np. przecinek na polskim Windows), co źle zinterpretowałoby "0.1" z JSON-a; Val()
    ' zawsze oczekuje kropki, niezależnie od locale.
    JsonParseNumber = Val(Mid(s, startPos, pos - startPos))
End Function

' Bezpieczny odczyt pola tekstowego ze sparsowanego obiektu (Dictionary) — zwraca
' defaultValue, jeśli obiekt jest Nothing, pole nie istnieje albo jest JSON-owym null.
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
' HTTP — MSXML2.XMLHTTP (synchronicznie), z ciasteczkiem sesji doklejanym RĘCZNIE do
' każdego żądania (nie polegamy na automatycznym zarządzaniu ciasteczkami przez COM).
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
        Err.Raise ERR_API, "EasyPDM", "Błąd serwera (" & status & "): " & responseText
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
    LogLine "GET " & path & " -> BRAK POŁĄCZENIA: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", "Brak połączenia z " & GetBaseUrl() & ": " & Err.Description
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
    LogLine "POST " & path & " -> BRAK POŁĄCZENIA: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", "Brak połączenia z " & GetBaseUrl() & ": " & Err.Description
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
    LogLine "PATCH " & path & " -> BRAK POŁĄCZENIA: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", "Brak połączenia z " & GetBaseUrl() & ": " & Err.Description
End Function

Function ApiRegisterAttachment(ByVal itemId As String, ByVal filePath As String) As Object
    Dim bodyJson As String
    bodyJson = "{""filePath"":" & JsonStr(filePath) & "}"
    Set ApiRegisterAttachment = ApiPostJson("/items/" & itemId & "/attachments/register", bodyJson)
End Function

' Czyta cały plik jako tablicę bajtów — używane przy zwykłym uploadzie HTTP (gdy magazyn
' PDM nie jest widoczny w systemie plików tej maszyny).
Private Function ReadFileBytes(ByVal filePath As String) As Byte()
    Dim fileNum As Integer
    Dim buffer() As Byte
    fileNum = FreeFile
    Open filePath For Binary Access Read As #fileNum
    If LOF(fileNum) > 0 Then
        ReDim buffer(1 To LOF(fileNum))
        Get #fileNum, , buffer
    Else
        ReDim buffer(0 To -1) ' pusta tablica
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

' Zwykły upload przez HTTP (multipart/form-data), używany jako fallback, gdy magazyn PDM
' nie jest widoczny w systemie plików tej maszyny — ten sam mechanizm co dogrywanie
' plików CAD z panelu właściwości w aplikacji webowej.
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
    LogLine "POST (upload) " & path & " -> BRAK POŁĄCZENIA: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", "Brak połączenia z " & GetBaseUrl() & ": " & Err.Description
End Function


' ============================================================================
' Logowanie / sesja
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

    LogLine "POST /auth/login -> " & http.Status & " (użytkownik: " & username & ")"
    If http.Status = 401 Or http.Status < 200 Or http.Status >= 300 Then
        LogLine "Logowanie nieudane: " & http.responseText
        Err.Raise ERR_API, "EasyPDM", "Logowanie nie powiodło się: " & http.responseText
    End If

    Dim user As Object
    Set user = JsonParse(http.responseText)

    ' Token czytany PRZEDE WSZYSTKIM z treści odpowiedzi JSON ("sessionToken") — MSXML2.
    ' XMLHTTP.6.0 nie ma pewnego dostępu do nagłówka Set-Cookie (znany problem komponentów
    ' COM/WinHTTP), więc serwer specjalnie dokłada token też tam. Ciasteczko zostaje jako
    ' zapasowe źródło (starsze wersje serwera, gdyby ktoś nie zaktualizował API).
    Dim token As String
    token = JsonGetString(user, "sessionToken", "")
    If token = "" Then token = ExtractSessionCookie(http)
    If token = "" Then
        LogLine "Logowanie: serwer odpowiedział 2xx, ale nie znaleziono tokenu sesji ani w treści odpowiedzi, ani w nagłówkach."
        Err.Raise ERR_API, "EasyPDM", "Logowanie nie powiodło się — serwer nie zwrócił sesji."
    End If

    SetSessionToken token
    Dim displayName As String
    displayName = JsonGetString(user, "displayName", "")
    If displayName = "" Then displayName = JsonGetString(user, "username", username)
    SetSavedDisplayName displayName
    LogLine "Zalogowano jako " & displayName & "."

    Set ApiLogin = user
    Exit Function
NetErr:
    LogLine "POST /auth/login -> BRAK POŁĄCZENIA: " & Err.Description
    Err.Raise ERR_API, "EasyPDM", "Brak połączenia z " & GetBaseUrl() & ": " & Err.Description
End Function

' getResponseHeader("Set-Cookie") bywa filtrowane przez zabezpieczenia MSXML/WinINet
' (NIEZWERYFIKOWANE w tym środowisku, czy dotyczy to także MSXML2.XMLHTTP.6.0) — zapasowo
' szukamy tego samego nagłówka w PEŁNEJ liście nagłówków, jeśli pierwsza próba nic nie da.
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

' Sprawdza, czy zapisana sesja jest jeszcze ważna (GET /auth/me); jeśli jej nie ma albo
' wygasła/została unieważniona, pokazuje okno logowania. Zwraca True, jeśli po wywołaniu
' jest aktywna sesja (można kontynuować), False jeśli użytkownik anulował logowanie.
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
            SetSessionToken "" ' token wygasł/nieważny — nie próbuj go już więcej
        Else
            ' Np. brak połączenia — nie blokujemy logowaniem na etapie samego sprawdzenia;
            ' błąd i tak wypłynie czytelnie przy pierwszym realnym wywołaniu API.
            EnsureLoggedIn = True
            Exit Function
        End If
    End If

    EnsureLoggedIn = PromptLogin()
End Function

Private Function PromptLogin() As Boolean
    Dim serverUrl As String
    serverUrl = InputBox("Adres API EasyPDM:", "Logowanie do PDM", GetBaseUrl())
    If Trim(serverUrl) = "" Then
        PromptLogin = False
        Exit Function
    End If
    SetBaseUrl serverUrl

    Dim attempt As Integer
    For attempt = 1 To 3
        Dim username As String, password As String
        username = InputBox("Nazwa użytkownika:", "Logowanie do PDM")
        If Trim(username) = "" Then
            PromptLogin = False
            Exit Function
        End If
        ' Zwykły InputBox nie maskuje wpisywanego tekstu gwiazdkami — ograniczenie tej
        ' uproszczonej wersji makra (bez własnego UserForm z polem hasła).
        password = InputBox("Hasło (UWAGA: pole nie maskuje wpisywanych znaków):", "Logowanie do PDM")
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
            MsgBox "Zalogowano jako " & JsonGetString(user, "displayName", username) & ".", vbInformation, "EasyPDM"
            PromptLogin = True
            Exit Function
        Else
            MsgBox "Logowanie nie powiodło się: " & loginErrDesc, vbExclamation, "EasyPDM"
        End If
    Next attempt
    PromptLogin = False
End Function


' ============================================================================
' Pomocnicze — sanityzacja nazwy pliku, format rewizji, właściwości Części/Złożenia.
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
    If result = "" Then result = "bez_nazwy"
    SanitizeFilename = result
End Function

' Rewizje jako wielkie litery zamiast cyfr: 1->A, 2->B, ..., 26->Z, 27->AA... (jak
' numeracja kolumn arkusza) — ta sama konwersja co revisionLabel() we froncie i w makrze
' FreeCAD; sam numer w bazie (revisionNumber) się nie zmienia.
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

' Zbiera właściwości Części/Złożenia od użytkownika wg TYCH SAMYCH reguł co
' kind_field_visibility() w makrze FreeCAD / PartPropertyForm+add-node-dialog w aplikacji
' webowej: Wykonywana -> Materiał; Zakupowa -> Producent/Numery zamówieniowe/Masa;
' Normalia -> Materiał/Norma; Klienta -> brak dodatkowych pól. Złożenie: Materiał/Masa
' zawsze widoczne razem, rodzaj opcjonalny i bez "Klienta".
Function PromptPartProperties(ByVal isPart As Boolean) As String
    Dim kindsText As String
    If isPart Then
        kindsText = "1 - Wykonywana" & vbCrLf & "2 - Zakupowa" & vbCrLf & "3 - Normalia" & vbCrLf & "4 - Klienta"
    Else
        kindsText = "1 - Wykonywana" & vbCrLf & "2 - Zakupowa" & vbCrLf & "3 - Normalia" & vbCrLf & "(zostaw puste - bez rodzaju)"
    End If

    Dim choice As String
    Dim rodzaj As String
    choice = InputBox("Rodzaj:" & vbCrLf & kindsText, "Właściwości")
    Select Case Trim(choice)
        Case "1": rodzaj = "Wykonywana"
        Case "2": rodzaj = "Zakupowa"
        Case "3": rodzaj = "Normalia"
        Case "4": If isPart Then rodzaj = "Klienta" Else rodzaj = ""
        Case Else: rodzaj = ""
    End Select

    If isPart And rodzaj = "" Then
        MsgBox "Dla Części rodzaj jest wymagany.", vbExclamation, "EasyPDM"
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
        fieldValue = InputBox("Materiał (opcjonalnie):", "Właściwości")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """material"":" & JsonStr(fieldValue)
            firstProp = False
        End If
    End If

    If showMass Then
        fieldValue = InputBox("Masa (opcjonalnie):", "Właściwości")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """mass"":" & JsonStr(fieldValue)
            firstProp = False
        End If
    End If

    If showPurchase Then
        fieldValue = InputBox("Producent (opcjonalnie):", "Właściwości")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """manufacturer"":" & JsonStr(fieldValue)
            firstProp = False
        End If

        fieldValue = InputBox("Numer zamówieniowy 1 (opcjonalnie):", "Właściwości")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """orderNumber"":" & JsonStr(fieldValue)
            firstProp = False
        End If

        fieldValue = InputBox("Numer zamówieniowy 2 (opcjonalnie):", "Właściwości")
        If Trim(fieldValue) <> "" Then
            If Not firstProp Then props = props & ","
            props = props & """orderNumber2"":" & JsonStr(fieldValue)
            firstProp = False
        End If
    End If

    If showNorm Then
        fieldValue = InputBox("Norma (opcjonalnie):", "Właściwości")
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
' Magazyn plików PDM — jeśli widoczny w systemie plików tej maszyny (klient i serwer na
' tym samym dysku), kopia trafia bezpośrednio tam i jest REJESTROWANA bez ponownego
' przesyłania przez HTTP; w przeciwnym razie zwykły upload (zob. ApiUploadFile).
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

' Wspólna końcówka obu trybów: KOPIUJE bieżący plik dokumentu do PDM pod nazwą
' "numer (nazwa).REWIZJA.rozszerzenie" — ta sama konwencja co itemDisplayLabel we froncie
' i _rename_and_upload w makrze FreeCAD. LOKALNY plik NIE jest ruszany — nie jest ani
' przenoszony, ani usuwany.
Function RenameAndUpload(ByVal filePath As String, ByVal itemId As String, ByVal itemNumber As Long, ByVal name As String, ByVal revision As Long) As Boolean
    Dim ext As String
    Dim dotPos As Long
    dotPos = InStrRev(filePath, ".")
    If dotPos > 0 Then ext = Mid(filePath, dotPos) Else ext = ""

    Dim newFilename As String
    newFilename = itemNumber & " (" & SanitizeFilename(name) & ")." & RevisionLabel(revision) & ext
    LogLine "RenameAndUpload: item #" & itemNumber & ", plik lokalny """ & filePath & """, nowa nazwa """ & newFilename & """"

    Dim storageRoot As String
    storageRoot = GetStorageRoot()
    If storageRoot <> "" Then
        LogLine "Magazyn PDM widoczny lokalnie: " & storageRoot
    Else
        LogLine "Magazyn PDM niewidoczny z tej maszyny (GET /config nieudane albo pusta odpowiedź) — użyję zwykłego uploadu HTTP."
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
                LogLine "Skopiowano plik do: " & candidate
            Else
                LogLine "Kopiowanie do magazynu nie powiodło się (błąd " & copyErrNum & ") — przechodzę na zwykły upload HTTP."
            End If
        End If
    End If

    If targetPath <> "" Then
        ' Jeśli kopia TEJ SAMEJ rewizji (dokładnie ta sama nazwa) jest już zarejestrowana,
        ' nie trzeba nic robić — świeże bajty już tam leżą (FileCopy nadpisał w miejscu),
        ' a rejestrowanie drugi raz tej samej ścieżki i tak by się nie udało (unikalny
        ' file_path w bazie). Załączniki innych rewizji (inna litera) zostają nietknięte.
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
            LogLine "Załącznik """ & newFilename & """ już zarejestrowany — kopia w magazynie nadpisana świeżymi bajtami, bez ponownej rejestracji."
        Else
            Dim registerErrNum As Long, registerErrDesc As String
            On Error Resume Next
            Err.Clear
            ApiRegisterAttachment itemId, targetPath
            registerErrNum = Err.Number
            registerErrDesc = Err.Description
            On Error GoTo 0

            If registerErrNum <> 0 Then
                ' Rejestracja zawiodła — bez sprzątania kopia w storage/components/
                ' zostałaby osierocona: nigdy niezarejestrowana jako załącznik, zajmuje
                ' miejsce na dysku serwera bez żadnego odpowiadającego jej wpisu w bazie.
                LogLine "Rejestracja załącznika nie powiodła się: " & registerErrDesc & " — usuwam osieroconą kopię " & targetPath
                On Error Resume Next
                Kill targetPath
                On Error GoTo 0
                Err.Raise ERR_API, "EasyPDM", "Rejestracja załącznika nie powiodła się: " & registerErrDesc
            End If
            LogLine "Zarejestrowano załącznik: " & targetPath
        End If

        RenameAndUpload = True
        Exit Function
    End If

    LogLine "Zwykły upload HTTP: /items/" & itemId & "/attachments jako """ & newFilename & """"
    ApiUploadFile "/items/" & itemId & "/attachments", filePath, newFilename
    RenameAndUpload = True
End Function


' ============================================================================
' Rdzeń PDM: nowy element / dogranie do istniejącego (z obsługą rewizji).
' ============================================================================

' Tworzy NOWY element w PDM, a potem dogrywa bieżący dokument jako jego załącznik. Nowy
' element zawsze zaczyna od rewizji 1.
Function PushNewItemToPdm(ByVal projectId As String, ByVal itemType As String, ByVal name As String, ByVal propertiesJson As String, ByVal filePath As String) As Object
    Dim bodyJson As String
    bodyJson = "{""name"":" & JsonStr(name) & ",""itemType"":" & JsonStr(itemType) & ",""properties"":" & propertiesJson & "}"

    Dim created As Object
    Set created = ApiPostJson("/projects/" & projectId & "/nodes", bodyJson)

    Dim itemId As String, itemNumber As Long
    itemId = JsonGetString(created, "id", "")
    itemNumber = JsonGetLong(created, "itemNumber", 0)
    LogLine "Utworzono nowy element PDM: #" & itemNumber & " (id " & itemId & "), projekt " & projectId & ", typ " & itemType

    RenameAndUpload filePath, itemId, itemNumber, name, 1

    Dim result As Object
    Set result = CreateObject("Scripting.Dictionary")
    result.Add "itemId", itemId
    result.Add "itemNumber", itemNumber
    result.Add "revision", 1
    Set PushNewItemToPdm = result
End Function

' Dogrywa bieżący dokument jako aktualny plik JUŻ ISTNIEJĄCEJ Części/Złożenia — bez
' tworzenia nowego rekordu. Jeśli element ma status "wydany" (w tym statusie PDM nie
' pozwala dogrywać plików), pyta o zgodę na nową rewizję i opcjonalny komentarz —
' dokładnie ten sam mechanizm co w aplikacji webowej (PATCH /items/{id}/status podnosi
' numer rewizji). Zwraca Nothing, jeśli użytkownik odmówił nowej rewizji.
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
    LogLine "Dogrywam do istniejącego elementu #" & itemNumber & " (id " & itemId & "), status """ & currentStatus & """, obecna rewizja " & revision

    Dim statusChanged As Boolean
    statusChanged = False

    If currentStatus = "wydany" Then
        Dim proceed As VbMsgBoxResult
        proceed = MsgBox("Element #" & itemNumber & " jest w statusie ""Wydany"" — dogranie pliku wymaga nowej rewizji. Utworzyć nową rewizję?", vbYesNo + vbQuestion, "Nowa rewizja")
        If proceed <> vbYes Then
            Set PushToExistingItem = Nothing
            Exit Function
        End If

        Dim comment As String
        comment = InputBox("Komentarz do nowej rewizji (opcjonalnie):", "Nowa rewizja")

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
        ' Każdy status poza "w_pracy" blokuje dogrywanie plików po stronie backendu, nie
        ' tylko "wydany". Dla "sprawdzany" wystarczy wrócić do "w_pracy" bez pytania o nową
        ' rewizję: rewizję podnosi WYŁĄCZNIE przejście wydany -> w_pracy.
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
            ' Status/rewizja zostały już podniesione na serwerze (musiały być, żeby dogranie
            ' pliku było w ogóle dozwolone) — skoro sama kopia/rejestracja pliku zawiodła,
            ' element PDM jest teraz w rozjechanym stanie: nowa rewizja/status w bazie, ale
            ' wciąż plik ze starej rewizji. Cofnięcie NIE jest tu bezpiecznie możliwe —
            ' backend nie zezwala na przejście "w_pracy" -> "wydany" wprost — więc zamiast
            ' cichej/mylącej próby cofnięcia, głośno mówimy dokładnie co się stało.
            Err.Raise ERR_API, "EasyPDM", _
                "Status/rewizja elementu #" & itemNumber & " zostały już zmienione na serwerze " & _
                "(rewizja " & revision & ", status 'w_pracy'), ale dogranie pliku się nie powiodło: " & _
                uploadErrDesc & vbCrLf & vbCrLf & _
                "Element w PDM ma teraz nową rewizję, lecz wciąż plik poprzedniej wersji — popraw to " & _
                "ręcznie w aplikacji webowej (dograj plik ponownie) albo spróbuj ponownie z tego makra."
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
        MsgBox "Brak dostępnych projektów w PDM.", vbExclamation, "EasyPDM"
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
    choice = InputBox("Wybierz projekt (numer):" & vbCrLf & listText, "Nowy element w PDM")
    Dim idx As Long
    idx = Val(choice)
    If idx < 1 Or idx > projects.Count Then
        PromptForProject = ""
    Else
        PromptForProject = ids(idx)
    End If
End Function

' Szuka Części/Złożenia po numerze widocznym dla użytkownika (np. w nazwie "67 (Nazwa)") —
' NIE po GUID, którego zwykły użytkownik nigdzie nie widzi. Przeszukuje CAŁĄ bazę, nie
' tylko bieżący projekt — komponent może być używany w wielu projektach.
Function PromptForExistingItem() As Object
    Dim numberText As String
    numberText = InputBox("Numer elementu w PDM (widoczny np. w nazwie ""67 (Nazwa)""):", "Istniejący element")
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

    MsgBox "Nie znaleziono Części/Złożenia o numerze " & targetNumber & ".", vbExclamation, "EasyPDM"
    Set PromptForExistingItem = Nothing
End Function


' ============================================================================
' SolidWorks — aktywny dokument, zapis, Właściwości niestandardowe (powiązanie z PDM).
' "swApp" to zmienna globalna, którą SolidWorks automatycznie udostępnia w KAŻDYM makrze
' VBA utworzonym przez Narzędzia -> Makro -> Nowy — nie trzeba jej samemu tworzyć.
' ============================================================================

' Zwraca ścieżkę pliku, typ elementu PDM ("part"/"assembly"/"file") wynikający z typu
' dokumentu SolidWorks, i domyślną nazwę (nazwa pliku bez rozszerzenia). Zapisuje dokument,
' jeśli jeszcze nie był zapisany na dysku, użytkownik musi wtedy wskazać ścieżkę przez
' standardowe okno "Zapisz jako" SolidWorks (Save3 z pustą nazwą pliku otwiera je samo).
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
        MsgBox "Nie udało się zapisać dokumentu — zapisz go ręcznie (Ctrl+S) i uruchom makro ponownie.", vbExclamation, "EasyPDM"
        GetActiveDocInfo = False
        Exit Function
    End If
    filePath = swModel.GetPathName()
    If filePath = "" Then
        GetActiveDocInfo = False
        Exit Function
    End If

    Select Case swModel.GetType()
        Case swDocPART
            itemTypeGuess = "part"
        Case swDocASSEMBLY
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

' Odczytuje ID elementu PDM zapisane we Właściwościach niestandardowych dokumentu (jeśli
' dokument był już kiedyś wysłany tym makrem) — pusty string, jeśli dokument jeszcze nie
' jest powiązany z żadnym elementem PDM.
Function GetLinkedItemId() As String
    Dim mgr As Object
    Set mgr = GetCustPropMgr()
    Dim valOut As String, resolvedOut As String
    On Error Resume Next
    mgr.Get4 CUSTPROP_ITEM_ID, False, valOut, resolvedOut
    On Error GoTo 0
    GetLinkedItemId = valOut
End Function

' Zapisuje powiązanie dokumentu z elementem PDM jako Właściwości niestandardowe — w
' odróżnieniu od podejścia FreeCAD (zmiana etykiety, NIEZAPISYWANA na dysk) działa
' niezawodnie także w NOWEJ sesji SolidWorks, bo Właściwości są częścią samego pliku.
' UWAGA: nazwy stałych swCustomInfoText / swCustomPropertyReplaceValue NIE były
' weryfikowane względem realnej biblioteki typów SolidWorks w tym środowisku — jeśli
' edytor VBA zgłosi "zmienna niezdefiniowana" przy którejś z nich, sprawdź dokładną nazwę
' w przeglądarce obiektów (F2) pod swCustomInfoType_e / swCustomPropertyAddOption_e.
Sub SetLinkedItem(ByVal itemId As String, ByVal itemNumberText As String)
    Dim mgr As Object
    Set mgr = GetCustPropMgr()
    mgr.Add3 CUSTPROP_ITEM_ID, swCustomInfoText, itemId, swCustomPropertyReplaceValue
    mgr.Add3 CUSTPROP_ITEM_NUMBER, swCustomInfoText, itemNumberText, swCustomPropertyReplaceValue
End Sub


' ============================================================================
' Punkt wejścia — uruchamiany przez Narzędzia -> Makro -> Uruchom (albo F5 w edytorze VBA).
' ============================================================================

Sub main()
    LogLine "=== Uruchomiono makro EasyPDM ==="

    If swApp Is Nothing Then
        MsgBox "To makro trzeba uruchomić z poziomu SolidWorks.", vbCritical, "EasyPDM"
        Exit Sub
    End If

    If Not EnsureLoggedIn() Then
        LogLine "Anulowano logowanie — koniec."
        Exit Sub
    End If

    Dim filePath As String, itemTypeGuess As String, defaultName As String
    If Not GetActiveDocInfo(filePath, itemTypeGuess, defaultName) Then
        MsgBox "Brak aktywnego, zapisanego dokumentu.", vbExclamation, "EasyPDM"
        LogLine "Brak aktywnego/zapisanego dokumentu — koniec."
        Exit Sub
    End If
    LogLine "Aktywny dokument: """ & filePath & """, wykryty typ: " & itemTypeGuess

    On Error GoTo Failed

    Dim linkedItemId As String
    linkedItemId = GetLinkedItemId()

    Dim resultInfo As Object

    If linkedItemId <> "" Then
        Dim confirmUpdate As VbMsgBoxResult
        confirmUpdate = MsgBox("Ten dokument jest już powiązany z elementem PDM. Dograć bieżącą wersję jako nową rewizję/aktualizację?", vbYesNo + vbQuestion, "EasyPDM")
        If confirmUpdate <> vbYes Then Exit Sub
        Set resultInfo = PushToExistingItem(linkedItemId, filePath)
    Else
        Dim mode As VbMsgBoxResult
        mode = MsgBox("Czy ten dokument już istnieje w PDM (dograć nową wersję do istniejącego elementu)?" & vbCrLf & _
                       "Tak = wybiorę istniejący element po numerze." & vbCrLf & _
                       "Nie = utworzę nowy element w PDM.", vbYesNoCancel + vbQuestion, "EasyPDM")
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
            name = InputBox("Nazwa elementu:", "Nowy element w PDM", defaultName)
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
        LogLine "=== Zakończono pomyślnie: element #" & JsonGetLong(resultInfo, "itemNumber", 0) & _
                ", rewizja " & RevisionLabel(JsonGetLong(resultInfo, "revision", 1)) & " ==="
        MsgBox "Wysłano do EasyPDM: element #" & JsonGetLong(resultInfo, "itemNumber", 0) & _
               " (rewizja " & RevisionLabel(JsonGetLong(resultInfo, "revision", 1)) & ")." & vbCrLf & vbCrLf & _
               "Log przebiegu: " & LogFilePath(), vbInformation, "EasyPDM"
    Else
        LogLine "=== Zakończono bez wysyłki (anulowano albo brak nowej rewizji) ==="
    End If
    Exit Sub

Failed:
    LogLine "=== BŁĄD (" & Err.Number & "): " & Err.Description & " ==="
    If Err.Number = ERR_AUTH Then
        MsgBox "Sesja wygasła — uruchom makro ponownie, żeby się zalogować." & vbCrLf & vbCrLf & _
               "Log przebiegu: " & LogFilePath(), vbExclamation, "EasyPDM"
        SetSessionToken ""
    Else
        MsgBox "Błąd: " & Err.Description & vbCrLf & vbCrLf & "Log przebiegu: " & LogFilePath(), vbCritical, "EasyPDM"
    End If
End Sub

' Osobny Sub — można przypiąć do własnego przycisku/skrótu, żeby wylogować się z EasyPDM
' bez uruchamiania całej wysyłki (następne uruchomienie "main" poprosi o ponowne logowanie).
Sub Logout()
    ApiLogout
    MsgBox "Wylogowano z EasyPDM.", vbInformation, "EasyPDM"
End Sub
