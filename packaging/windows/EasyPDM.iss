; Instalator Windows dla EasyPDM — Inno Setup (https://jrsoftware.org/isinfo.php).
;
; PRZED kompilacją: uruchom packaging\windows\build.ps1 (buduje frontend + publikuje
; self-contained backend win-x64 do packaging\windows\publish\), potem skompiluj ten plik
; w Inno Setup Compiler (iscc.exe / GUI).
;
; Co robi zainstalowany EasyPDM:
;   1. Sprawdza, czy PostgreSQL jest już zainstalowany (szuka psql.exe w typowej lokalizacji
;      instalatora EDB) — jeśli nie, kieruje na stronę pobierania i przerywa instalację
;      (świadomie NIE próbujemy cicho pobierać/instalować 300+ MB instalatora PostgreSQL —
;      za duże ryzyko niewidocznej awarii bez możliwości zdiagnozowania).
;   2. Pyta o hasło superużytkownika "postgres" (potrzebne, żeby założyć rolę/bazę EasyPDM).
;   3. Zakłada rolę "pdm_user" (z wygenerowanym losowo hasłem) i bazę "pdm", ładuje schemat
;      (db\schema.sql, dołączony do instalatora).
;   4. Zapisuje appsettings.Production.json z prawdziwym connection stringiem i ścieżkami
;      magazynu/kopii/logów w %ProgramData%\EasyPDM.
;   5. Rejestruje EasyPDM.Api.exe jako usługę Windows (autostart, działa w tle bez okna
;      konsoli) i ją uruchamia.
;   6. Skrót na pulpicie/w Menu Start otwierający http://localhost:5000.
;
; Aktualizacja: uruchom ten sam instalator ponownie (nowy build z packaging\windows\build.ps1).
; Istniejąca instalacja jest wykrywana po stałym AppId (rejestr, klucz Uninstall — zob.
; UninstallRegKey w [Code]), więc Inno podmienia ją W MIEJSCU zamiast instalować obok.
; Przebieg aktualizacji:
;   - NIE pyta o hasło superużytkownika "postgres" — odczytuje hasło roli pdm_user z
;     appsettings.Production.json poprzedniej instalacji (ReadExistingPdmPassword) i w ogóle
;     nie dotyka roli ani bazy. Hasło roli ZOSTAJE bez zmian, więc nic, co łączy się do tej
;     bazy poza EasyPDM (skrypty kopii, pgAdmin), nie przestaje działać.
;   - Zatrzymuje usługę PRZED podmianą plików (PrepareToInstall — inaczej Windows
;     zablokowałby nadpisanie działającego .exe) i uruchamia ją z powrotem zamiast
;     rejestrować od nowa.
;   - Nowe migracje bazy program stosuje sam przy starcie (nic ręcznie).
;   - Ustawienia zmienione w aplikacji (np. lokalizacja magazynu plików) przeżywają
;     aktualizację: siedzą w appsettings.Local.json, a instalator pisze tylko Production.json.
; Instalacja STARSZEJ wersji na nowszej jest odrzucana (InitializeSetup) — migracje bazy idą
; wyłącznie w przód, więc starszy program nie umiałby odczytać już zmigrowanego schematu.
;
; Kompilacja: automatyczna, przy każdym pushu dotykającym tych plików —
; .github/workflows/build-windows-installer.yml buduje EasyPDM_Windows_v{#MyAppVersion}.exe na windowsowym
; runnerze GitHuba (ma Inno Setup Compiler fabrycznie) i wystawia go jako pobieralny
; artefakt przebiegu — nie trzeba mieć Windows/Inno Setup lokalnie, żeby dostać gotowy
; instalator. Skompilowane i zweryfikowane realnym kompilatorem (kilka błędów Pascal
; Script — brak lokalnych "const" w funkcjach, LoadStringFromFile wymaga AnsiString,
; brak Randomize/RandSeed/GetTickCount w tym dialekcie — zostało po drodze wyłapanych
; i poprawionych). Instalacja end-to-end na żywej maszynie z PostgreSQL wciąż nie była
; ręcznie przetestowana — przy pierwszym uruchomieniu obserwuj przebieg i zgłoś, co nie zagra.

#define MyAppName "EasyPDM"
#define MyAppVersion "0.6"
#define MyAppExeName "EasyPDM.Api.exe"
#define MyServiceName "EasyPDM"
#define MyDataDir "{commonappdata}\EasyPDM"

[Setup]
; Stały GUID — nie zmieniaj między wersjami, Inno Setup używa go do wykrywania aktualizacji
; istniejącej instalacji zamiast instalowania obok niej.
AppId={{B6E2B6B0-2B0A-4C1E-9C1B-5F6A6F6C7D8E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=EasyPDM_Windows_v{#MyAppVersion}
OutputDir=Output
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
SetupIconFile=..\..\EasyPDM.Api\app.ico

[Languages]
; Te same trzy języki co aplikacja webowa (pl/en/de) — Inno Setup POKAZUJE OKNO WYBORU
; JĘZYKA na samym początku instalacji automatycznie, gdy zdefiniowany jest więcej niż
; jeden język (domyślne ShowLanguageDialog=auto, nieustawione tutaj celowo — nie trzeba
; nic dodatkowo konfigurować). Tłumaczy to WBUDOWANE strony kreatora Inno Setup (Witaj,
; Wybór katalogu, Gotowy do instalacji...) — własne strony/komunikaty z [Code] (hasło
; PostgreSQL, błędy) są tłumaczone OSOBNO przez [CustomMessages] niżej i CustomMessage()
; w Pascal Script, bo Inno Setup nie robi tego automatycznie za nas.
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "pl"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "..\..\db\schema.sql"; DestDir: "{app}\db"; Flags: ignoreversion

[Icons]
; Skrót "otwiera przeglądarkę" — [Icons] tworzy zwykłe skróty .lnk, które muszą wskazywać na
; prawdziwy plik, więc zamiast bezpośrednio na URL (nieprawidłowy cel dla .lnk) celujemy
; w explorer.exe z adresem jako parametrem — standardowa sztuczka na otwarcie domyślnej
; przeglądarki bez zakładania, jaka to przeglądarka.
Name: "{group}\{#MyAppName}"; Filename: "{win}\explorer.exe"; Parameters: "http://localhost:5000"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{win}\explorer.exe"; Parameters: "http://localhost:5000"; IconFilename: "{app}\{#MyAppExeName}"

[CustomMessages]
; Tłumaczenia własnych stron/komunikatów z [Code] (strona hasła PostgreSQL, MsgBoxy) —
; [Languages] wyżej tłumaczy tylko WBUDOWANE strony kreatora Inno Setup, tych własnych
; nie dotyka, więc trzeba je zdefiniować osobno i odwoływać się do nich przez
; CustomMessage('Klucz') w Pascal Script zamiast wpisywać literały na stałe.
en.PgPageCaption=PostgreSQL Connection
en.PgPageSubCaption=Superuser password for "postgres"
en.PgPageDescription=EasyPDM needs it ONCE, to create its own role (pdm_user) and database (pdm) — it is not stored anywhere.
en.PgFieldLabel=Password for "postgres":
en.PgNotFoundConfirm=No installed PostgreSQL found (required, version 18 recommended). Open the download page? Run this installer again after installing it.
en.PgPasswordRequired=Enter the "postgres" superuser password.
en.PgConnectionFailed=Could not connect to PostgreSQL with this password. Please try again.
en.PgRoleConfigFailed=Could not configure the PostgreSQL database role (pdm_user). Check the "postgres" superuser password and whether another PostgreSQL instance is running on this port (5432). Details in the log:
en.PgDatabaseCreateFailed=Could not create the PostgreSQL database "pdm". Details in the log:
en.PgSchemaLoadFailed=Could not load the database schema. Details in the log:
en.ServiceDescription=Local PDM server
en.DowngradeBlocked=A newer version of EasyPDM (%1) is already installed — this installer carries version %2. Installing an older version over a newer one is not supported: the database has already been migrated to the newer schema and older versions cannot read it. Uninstall the current version first if you really want to go back.

pl.PgPageCaption=Połączenie z PostgreSQL
pl.PgPageSubCaption=Hasło superużytkownika "postgres"
pl.PgPageDescription=EasyPDM potrzebuje go JEDNORAZOWO, żeby założyć własną rolę (pdm_user) i bazę danych (pdm) — nie jest nigdzie zapisywane.
pl.PgFieldLabel=Hasło "postgres":
pl.PgNotFoundConfirm=Nie znaleziono zainstalowanego PostgreSQL (wymagany, wersja 18 zalecana). Otworzyć stronę pobierania? Po instalacji uruchom ten instalator ponownie.
pl.PgPasswordRequired=Podaj hasło superużytkownika "postgres".
pl.PgConnectionFailed=Nie udało się połączyć z PostgreSQL tym hasłem. Spróbuj ponownie.
pl.PgRoleConfigFailed=Nie udało się skonfigurować roli bazy danych PostgreSQL (pdm_user). Sprawdź hasło superużytkownika "postgres" oraz czy na tym porcie (5432) nie działa inna instancja PostgreSQL. Szczegóły w logu:
pl.PgDatabaseCreateFailed=Nie udało się utworzyć bazy danych PostgreSQL "pdm". Szczegóły w logu:
pl.PgSchemaLoadFailed=Nie udało się załadować schematu bazy danych. Szczegóły w logu:
pl.ServiceDescription=Lokalny serwer PDM
pl.DowngradeBlocked=Zainstalowana jest już nowsza wersja EasyPDM (%1) — ten instalator zawiera wersję %2. Instalacja starszej wersji na nowszej nie jest wspierana: baza danych została już zmigrowana do nowszego schematu, którego starsze wersje nie potrafią odczytać. Jeśli naprawdę chcesz się cofnąć, najpierw odinstaluj obecną wersję.

de.PgPageCaption=PostgreSQL-Verbindung
de.PgPageSubCaption=Passwort des Superusers "postgres"
de.PgPageDescription=EasyPDM benötigt es EINMALIG, um seine eigene Rolle (pdm_user) und Datenbank (pdm) anzulegen — es wird nirgendwo gespeichert.
de.PgFieldLabel=Passwort für "postgres":
de.PgNotFoundConfirm=Keine PostgreSQL-Installation gefunden (erforderlich, Version 18 empfohlen). Download-Seite öffnen? Starten Sie dieses Installationsprogramm nach der Installation erneut.
de.PgPasswordRequired=Geben Sie das Passwort des Superusers "postgres" ein.
de.PgConnectionFailed=Verbindung zu PostgreSQL mit diesem Passwort fehlgeschlagen. Bitte versuchen Sie es erneut.
de.PgRoleConfigFailed=Die PostgreSQL-Datenbankrolle (pdm_user) konnte nicht konfiguriert werden. Überprüfen Sie das Passwort des Superusers "postgres" und ob auf diesem Port (5432) eine andere PostgreSQL-Instanz läuft. Details im Protokoll:
de.PgDatabaseCreateFailed=Die PostgreSQL-Datenbank "pdm" konnte nicht erstellt werden. Details im Protokoll:
de.PgSchemaLoadFailed=Das Datenbankschema konnte nicht geladen werden. Details im Protokoll:
de.ServiceDescription=Lokaler PDM-Server
de.DowngradeBlocked=Es ist bereits eine neuere Version von EasyPDM (%1) installiert — dieses Installationsprogramm enthält Version %2. Eine ältere Version über eine neuere zu installieren wird nicht unterstützt: Die Datenbank wurde bereits auf das neuere Schema migriert, das ältere Versionen nicht lesen können. Deinstallieren Sie zuerst die aktuelle Version, wenn Sie wirklich zurückgehen möchten.

[Code]
const
  // Inno Setup Pascal Script nie pozwala na sekcję "const" zadeklarowaną LOKALNIE wewnątrz
  // funkcji (błąd kompilacji "'BEGIN' expected") — dlatego to stała globalna, nie lokalna
  // wewnątrz GenerateRandomPassword, gdzie jest jedynym użyciem.
  RandomPasswordChars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  // Klucz, pod którym Inno Setup trzyma dane odinstalowania (w tym DisplayVersion i
  // InstallLocation) — GUID MUSI być identyczny z AppId w [Setup] wyżej, inaczej wykrywanie
  // istniejącej instalacji po cichu przestanie działać.
  UninstallRegKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B6E2B6B0-2B0A-4C1E-9C1B-5F6A6F6C7D8E}_is1';

var
  PostgresPasswordPage: TInputQueryWizardPage;
  PsqlPath: String;
  DebugLogPath: String;
  // Hasło roli pdm_user odczytane z appsettings.Production.json POPRZEDNIEJ instalacji.
  // Niepuste = to aktualizacja, przy której rola i baza już istnieją i nie trzeba ich w
  // ogóle dotykać (a więc i pytać o hasło superużytkownika "postgres").
  ExistingPdmPassword: String;

{ Log instalacji zapisywany do %ProgramData%\EasyPDM (przetrwa poza katalogiem tymczasowym
  instalatora, więc da się go obejrzeć już PO zakończeniu) — RunPsql/RoleExists/DatabaseExists
  nic wcześniej nie logowały, więc cichy błąd (np. zła rola/baza/hasło) był kompletnie
  niewidoczny. }
procedure LogInstall(Msg: String);
begin
  if DebugLogPath = '' then
    exit;
  SaveStringToFile(DebugLogPath,
    GetDateTimeString('yyyy/mm/dd hh:nn:ss', '-', ':') + '  ' + Msg + #13#10, True);
end;

{ Wyciąga wiodącą liczbę całkowitą z nazwy katalogu wersji ("18" -> 18, "9.6" -> 9,
  cokolwiek bez cyfr na początku -> -1). Same tylko wiodące cyfry — PostgreSQL 10+ nazywa
  katalogi samym numerem głównej wersji ("10".."18"...), a starsze 9.x dodatkowo mają
  numer podwersji po kropce ("9.6", "9.5") — obie formy mają sensowną wiodącą liczbę
  całkowitą do porównania. }
function LeadingMajorVersion(const S: String): Integer;
var
  I: Integer;
  Digits: String;
begin
  Digits := '';
  I := 1;
  while (I <= Length(S)) and (S[I] >= '0') and (S[I] <= '9') do
  begin
    Digits := Digits + S[I];
    I := I + 1;
  end;
  if Digits = '' then
    Result := -1
  else
    Result := StrToIntDef(Digits, -1);
end;

{ Szuka psql.exe w typowej lokalizacji instalatora EDB: C:\Program Files\PostgreSQL\<wersja>\bin\ .
  Zwraca pełną ścieżkę do najnowszej znalezionej wersji, albo pusty string, jeśli nic nie ma. }
function FindPsqlPath(): String;
var
  FindRec: TFindRec;
  BaseDir, Candidate, Best: String;
begin
  Result := '';
  Best := '';
  BaseDir := ExpandConstant('{pf}\PostgreSQL');
  if not DirExists(BaseDir) then
    exit;
  if FindFirst(BaseDir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0)
           and (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Candidate := BaseDir + '\' + FindRec.Name + '\bin\psql.exe';
          if FileExists(Candidate) then
            { Porównanie po WIODĄCEJ LICZBIE głównej wersji (LeadingMajorVersion), nie po
              samym stringu — samo porównanie leksykograficzne błędnie wybrałoby stary
              "9.6" ponad nowym "18" (bo '9' > '1' jako znaki), gdyby na tej samej maszynie
              współistniały stara instalacja PostgreSQL 9.x obok nowszej 10+. }
            if (Best = '') or (LeadingMajorVersion(FindRec.Name) > LeadingMajorVersion(Best)) then
            begin
              Best := FindRec.Name;
              Result := Candidate;
            end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

{ Porównuje numery wersji ("0.4" vs "0.3", "1.0" vs "0.12") człon po członie, numerycznie.
  Zwraca 1 gdy A > B, -1 gdy A < B, 0 gdy równe. Porównanie leksykograficzne byłoby tu
  błędne — "0.12" < "0.3" jako zwykły tekst, a jako wersja jest odwrotnie. }
function CompareVersionStrings(const A, B: String): Integer;
var
  PartA, PartB, RestA, RestB: String;
  P: Integer;
begin
  Result := 0;
  RestA := A;
  RestB := B;
  while (Result = 0) and ((RestA <> '') or (RestB <> '')) do
  begin
    P := Pos('.', RestA);
    if P > 0 then
    begin
      PartA := Copy(RestA, 1, P - 1);
      RestA := Copy(RestA, P + 1, Length(RestA));
    end
    else
    begin
      PartA := RestA;
      RestA := '';
    end;
    P := Pos('.', RestB);
    if P > 0 then
    begin
      PartB := Copy(RestB, 1, P - 1);
      RestB := Copy(RestB, P + 1, Length(RestB));
    end
    else
    begin
      PartB := RestB;
      RestB := '';
    end;
    if StrToIntDef(PartA, 0) > StrToIntDef(PartB, 0) then
      Result := 1
    else if StrToIntDef(PartA, 0) < StrToIntDef(PartB, 0) then
      Result := -1;
  end;
end;

{ Wersja EasyPDM już zainstalowana na tej maszynie (pusty string, jeśli żadnej nie ma). }
function InstalledVersion(): String;
begin
  if not RegQueryStringValue(HKLM, UninstallRegKey, 'DisplayVersion', Result) then
    Result := '';
end;

{ Katalog poprzedniej instalacji — potrzebny, żeby sięgnąć po jej appsettings.Production.json
  JESZCZE ZANIM kreator ustali katalog docelowy (strona wyboru katalogu jest później).
  UWAGA przy edycji komentarzy w tej sekcji: komentarz w nawiasach klamrowych kończy się na
  PIERWSZYM nawiasie zamykającym, więc nie wolno w nim użyć stałej w klamrach (np. tej
  oznaczającej katalog aplikacji) — reszta zdania stałaby się wtedy kodem i kompilacja padłaby
  na "'BEGIN' expected" (dokładnie to się tu zdarzyło). }
function InstalledLocation(): String;
begin
  if not RegQueryStringValue(HKLM, UninstallRegKey, 'InstallLocation', Result) then
    Result := '';
end;

{ Wyciąga hasło roli pdm_user z ConnectionString w appsettings.Production.json poprzedniej
  instalacji. Dzięki temu aktualizacja NIE MUSI ani pytać o hasło superużytkownika
  "postgres", ani przestawiać hasła roli — a to drugie było realnie uciążliwe: zmiana hasła
  przy każdej aktualizacji wywracała wszystko, co łączy się do bazy poza samym EasyPDM
  (skrypty kopii zapasowych, pgAdmin z zapamiętanym hasłem). Pusty string = nie udało się
  odczytać (brak pliku, inny format) i wtedy przebieg wraca do pełnej konfiguracji z
  pytaniem o hasło, dokładnie jak przy świeżej instalacji. }
function ReadExistingPdmPassword(const AppDir: String): String;
var
  Content: AnsiString;
  S: String;
  P, E: Integer;
begin
  Result := '';
  if AppDir = '' then
    exit;
  { LoadStringFromFile wymaga AnsiString w tym dialekcie Pascal Script. }
  if not LoadStringFromFile(AddBackslash(AppDir) + 'appsettings.Production.json', Content) then
    exit;
  S := String(Content);
  P := Pos('Password=', S);
  if P = 0 then
    exit;
  S := Copy(S, P + Length('Password='), Length(S));
  { Wartość kończy się cudzysłowem zamykającym string JSON albo średnikiem kolejnego
    parametru connection stringa — bierzemy wszystko do pierwszego z nich. }
  E := 1;
  while (E <= Length(S)) and (S[E] <> '"') and (S[E] <> ';') do
    E := E + 1;
  Result := Copy(S, 1, E - 1);
end;

function GenerateRandomPassword(Len: Integer): String;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Len do
    Result := Result + RandomPasswordChars[Random(Length(RandomPasswordChars)) + 1];
end;

{ Escapuje wartość do bezpiecznego użycia w "set "VAR=wartość"" wewnątrz pliku .bat. Forma
  z cudzysłowem chroni przed większością metaznaków cmd.exe (&, |, <, >, ^, spacje), ale NIE
  przed % — cmd.exe rozwija %coś% jako odwołanie do zmiennej środowiskowej NIEZALEŻNIE od
  cudzysłowów, więc hasło zawierające np. "%PATH%" zostałoby po cichu podmienione realną
  wartością PATH zamiast zostać dosłownym tekstem — podwojenie % temu zapobiega. }
function EscapeForBatch(Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '%', '%%', True);
end;

{ psql nie ma parametru na hasło podane wprost (poza .pgpass) — najprostszy niezawodny sposób
  na Windows to tymczasowy plik .bat, który najpierw ustawia PGPASSWORD, a potem woła psql.
  Zwraca True, jeśli psql zakończył się kodem 0. UWAGA: Args MUSI zawierać własne "-U <rola>"
  — ta funkcja nie narzuca żadnej roli domyślnej (wywołania łączą się raz jako "postgres",
  raz jako "pdm_user"). }
function RunPsql(PgPassword, Args, StepLabel: String): Boolean;
var
  BatchFile: String;
  ResultCode: Integer;
  ExecOk: Boolean;
  StatusText: String;
begin
  BatchFile := ExpandConstant('{tmp}\pdm_psql_' + IntToStr(Random(1000000)) + '.bat');
  SaveStringToFile(BatchFile,
    '@echo off' + #13#10 +
    'set "PGPASSWORD=' + EscapeForBatch(PgPassword) + '"' + #13#10 +
    '"' + PsqlPath + '" -h localhost ' + Args + #13#10,
    False);
  try
    ExecOk := Exec(BatchFile, '', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Result := ExecOk and (ResultCode = 0);
    if not ExecOk then
      StatusText := 'nie udalo sie uruchomic psql.exe'
    else if ResultCode <> 0 then
      StatusText := 'psql zakonczyl sie kodem ' + IntToStr(ResultCode)
    else
      StatusText := 'OK';
    LogInstall('psql [' + StepLabel + ']: ' + StatusText);
  finally
    DeleteFile(BatchFile);
  end;
end;

function RoleExists(PgPassword: String): Boolean;
var
  OutFile: String;
  Output: AnsiString;
  ResultCode: Integer;
  BatchFile: String;
begin
  { -tAc zwraca surowy wynik zapytania bez nagłówków — łapiemy go do pliku, bo Exec() samo
    w sobie nie oddaje stdout w Inno Setup. }
  OutFile := ExpandConstant('{tmp}\pdm_role_check.txt');
  BatchFile := ExpandConstant('{tmp}\pdm_psql_check_' + IntToStr(Random(1000000)) + '.bat');
  SaveStringToFile(BatchFile,
    '@echo off' + #13#10 +
    'set "PGPASSWORD=' + EscapeForBatch(PgPassword) + '"' + #13#10 +
    '"' + PsqlPath + '" -h localhost -U postgres -tAc ' +
    '"SELECT 1 FROM pg_roles WHERE rolname=''pdm_user''" > "' + OutFile + '"' + #13#10,
    False);
  try
    Exec(BatchFile, '', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if LoadStringFromFile(OutFile, Output) then
      Result := Pos('1', Output) > 0
    else
      Result := False;
    LogInstall('RoleExists: surowy wynik = "' + Output + '" -> ' + IntToStr(ResultCode));
  finally
    DeleteFile(BatchFile);
    DeleteFile(OutFile);
  end;
end;

function DatabaseExists(PgPassword: String): Boolean;
var
  OutFile: String;
  Output: AnsiString;
  ResultCode: Integer;
  BatchFile: String;
begin
  OutFile := ExpandConstant('{tmp}\pdm_db_check.txt');
  BatchFile := ExpandConstant('{tmp}\pdm_psql_dbcheck_' + IntToStr(Random(1000000)) + '.bat');
  SaveStringToFile(BatchFile,
    '@echo off' + #13#10 +
    'set "PGPASSWORD=' + EscapeForBatch(PgPassword) + '"' + #13#10 +
    '"' + PsqlPath + '" -h localhost -U postgres -tAc ' +
    '"SELECT 1 FROM pg_database WHERE datname=''pdm''" > "' + OutFile + '"' + #13#10,
    False);
  try
    Exec(BatchFile, '', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if LoadStringFromFile(OutFile, Output) then
      Result := Pos('1', Output) > 0
    else
      Result := False;
    LogInstall('DatabaseExists: surowy wynik = "' + Output + '" -> ' + IntToStr(ResultCode));
  finally
    DeleteFile(BatchFile);
    DeleteFile(OutFile);
  end;
end;

{ "sc query" zwraca 0, jeśli usługa istnieje (niezależnie od tego, czy działa), a 1060
  ("nie istnieje taka usługa"), jeśli nie ma jej wcale. }
function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'query {#MyServiceName}', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  Result := ResultCode = 0;
end;

{ Wywoływane przez Inno Setup TUŻ PRZED skopiowaniem plików (po kliknięciu "Instaluj", ale
  przed [Files]) — kluczowe przy AKTUALIZACJI: Windows blokuje nadpisanie pliku .exe
  uruchomionego procesu usługi, więc trzeba ją najpierw zatrzymać, inaczej kopiowanie plików
  zawiedzie z niejasnym błędem "plik używany przez inny program". }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if ServiceExists() then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#MyServiceName}', '', SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
    { "sc stop" wraca od razu po WYSŁANIU sygnału zatrzymania, nie po faktycznym zakończeniu
      procesu — bez tej chwili zapasu kopiowanie plików mogłoby trafić na jeszcze zamknięty
      plik .exe. }
    Sleep(3000);
  end;
end;

procedure InitializeWizard();
begin
  PostgresPasswordPage := CreateInputQueryPage(wpSelectDir,
    CustomMessage('PgPageCaption'),
    CustomMessage('PgPageSubCaption'),
    CustomMessage('PgPageDescription'));
  PostgresPasswordPage.Add(CustomMessage('PgFieldLabel'), True);
end;

{ Hasło superużytkownika "postgres" podane z góry przez /PGPASSWORD=... w wierszu poleceń —
  do automatyzacji (np. testu instalatora w CI, gdzie i tak nie ma z kim wejść w interakcję
  przez /VERYSILENT). Zwraca pusty string, jeśli parametr nie został podany — wtedy
  normalny, interaktywny przebieg z PostgresPasswordPage działa jak dotychczas. }
function PgPasswordFromCmdLine(): String;
begin
  Result := ExpandConstant('{param:PGPASSWORD|}');
end;

{ Pomija stronę z hasłem, jeśli podano je już przez /PGPASSWORD — inaczej w trybie
  /VERYSILENT strona i tak się nie pokazuje, ale NextButtonClick nigdy by się nie wywołał,
  więc PostgresPasswordPage.Values[0] zostałoby puste; jawne pominięcie jest tu tylko dla
  spójności interaktywnego przebiegu (np. /LoadInf), nie zmienia zachowania w /VERYSILENT. }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { Hasło superużytkownika jest potrzebne WYŁĄCZNIE do założenia/przestawienia roli i bazy.
    Przy aktualizacji, gdy znamy już hasło roli z poprzedniej instalacji, nie robimy ani
    jednego, ani drugiego — więc pytanie o hasło "postgres" byłoby wtedy pytaniem o coś,
    czego instalator i tak nie użyje. }
  Result := (PageID = PostgresPasswordPage.ID)
    and ((PgPasswordFromCmdLine() <> '') or (ExistingPdmPassword <> ''));
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
  Installed: String;
begin
  { Blokada instalacji STARSZEJ wersji na nowszej. Migracje bazy idą wyłącznie w przód
    (MigrationRunner stosuje brakujące przy każdym starcie, nie umie ich cofać), więc po
    takim "downgradzie" program zastałby schemat z przyszłości, którego nie zna — cicha,
    trudna do zdiagnozowania awaria już po instalacji. Lepiej odmówić od razu, z
    wyjaśnieniem. }
  Installed := InstalledVersion();
  if (Installed <> '') and (CompareVersionStrings(Installed, '{#MyAppVersion}') > 0) then
  begin
    if not WizardSilent then
      MsgBox(FmtMessage(CustomMessage('DowngradeBlocked'), [Installed, '{#MyAppVersion}']), mbError, MB_OK);
    Result := False;
    exit;
  end;

  { Aktualizacja: sięgamy po hasło roli z poprzedniej instalacji, żeby w ogóle nie ruszać
    bazy (ani nie pytać o hasło superużytkownika) -- zob. ReadExistingPdmPassword. }
  if Installed <> '' then
    ExistingPdmPassword := ReadExistingPdmPassword(InstalledLocation());

  PsqlPath := FindPsqlPath();
  if PsqlPath = '' then
  begin
    { WizardSilent — /SUPPRESSMSGBOXES wycisza tylko WBUDOWANE okna Inno Setup, NIE własne
      MsgBox() z [Code]; bez tej straży instalacja /VERYSILENT bez PostgreSQL zawiesiłaby się
      w nieskończoność czekając na kliknięcie, którego nikt nigdy nie wykona (dokładnie to
      przydarzyło się w CI po dodaniu analogicznych MsgBox w CurStepChanged poniżej). }
    if not WizardSilent then
    begin
      if MsgBox(CustomMessage('PgNotFoundConfirm'), mbConfirmation, MB_YESNO) = IDYES then
        ShellExec('open', 'https://www.postgresql.org/download/windows/', '', '', SW_SHOW, ewNoWait, ResultCode);
    end;
    Result := False;
  end
  else
    Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = PostgresPasswordPage.ID then
  begin
    if PostgresPasswordPage.Values[0] = '' then
    begin
      MsgBox(CustomMessage('PgPasswordRequired'), mbError, MB_OK);
      Result := False;
    end
    else if not RunPsql(PostgresPasswordPage.Values[0], '-U postgres -c "SELECT 1;" postgres', 'test polaczenia') then
    begin
      MsgBox(CustomMessage('PgConnectionFailed'), mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  PgSuperPassword, PdmPassword, AppSettings: String;
  ResultCode: Integer;
  RoleOk, DbOk, SchemaOk: Boolean;
begin
  if CurStep <> ssPostInstall then
    exit;

  ForceDirectories(ExpandConstant('{#MyDataDir}'));
  DebugLogPath := ExpandConstant('{#MyDataDir}\install-debug.log');
  LogInstall('=== Instalacja/aktualizacja EasyPDM rozpoczeta ===');

  if ExistingPdmPassword <> '' then
  begin
    { AKTUALIZACJA ze znanym hasłem roli: rola i baza już istnieją i są sprawne (program
      właśnie z nich korzystał), więc nie ruszamy ich w ogóle — ani CREATE, ani ALTER.
      Wcześniej hasło pdm_user było przestawiane przy KAŻDEJ aktualizacji, co wywracało
      wszystko inne, co łączy się do tej bazy (skrypty kopii, pgAdmin). Nowe migracje
      schematu i tak stosuje sam program przy starcie, nie instalator. }
    PdmPassword := ExistingPdmPassword;
    LogInstall('Aktualizacja: rola i baza juz istnieja, uzywam zapisanego hasla pdm_user - pomijam konfiguracje bazy.');
  end
  else
  begin
    PgSuperPassword := PgPasswordFromCmdLine();
    if PgSuperPassword = '' then
      PgSuperPassword := PostgresPasswordPage.Values[0];
    PdmPassword := GenerateRandomPassword(32);

    { Rola — pomijana (tylko ALTER hasła), jeśli rola już istnieje, a mimo to nie udało się
      odczytać hasła z poprzedniej instalacji (np. skasowany/uszkodzony
      appsettings.Production.json): wtedy trzeba je ustawić na nowo, żeby appsettings
      zgadzało się z tym, co faktycznie jest w bazie. }
    if RoleExists(PgSuperPassword) then
      RoleOk := RunPsql(PgSuperPassword, '-U postgres -c "ALTER ROLE pdm_user PASSWORD ''' + PdmPassword + ''';" postgres', 'ALTER ROLE pdm_user')
    else
      RoleOk := RunPsql(PgSuperPassword, '-U postgres -c "CREATE ROLE pdm_user LOGIN PASSWORD ''' + PdmPassword + ''';" postgres', 'CREATE ROLE pdm_user');

    { Wcześniej wynik powyższego wcale nie był sprawdzany — przy błędzie (np. złe hasło
      superużytkownika albo połączenie z niewłaściwym serwerem PostgreSQL, gdy na maszynie
      działa więcej niż jedna instancja na porcie 5432) appsettings.json i tak zapisywało się
      z hasłem, które NIGDY nie trafiło do żadnej realnej roli — usługa startowała, ale
      EasyPDM.Api.exe od razu padał na "password authentication failed". Teraz przerywamy
      głośno zamiast zostawiać użytkownika z cichą, niedziałającą instalacją. MsgBox tylko
      poza trybem cichym — /SUPPRESSMSGBOXES NIE wycisza własnych MsgBox z [Code], więc pod
      /VERYSILENT taki MsgBox zawiesiłby instalator w nieskończoność (dokładnie to się stało
      w CI, zanim doszła straż WizardSilent poniżej). }
    if not RoleOk then
    begin
      LogInstall('BLAD KRYTYCZNY: nie udalo sie zalozyc/zaktualizowac roli pdm_user - przerywam konfiguracje bazy.');
      if not WizardSilent then
        MsgBox(CustomMessage('PgRoleConfigFailed') + ' ' + DebugLogPath, mbError, MB_OK);
      exit;
    end;

    { Baza sprawdzana NIEZALEŻNIE od roli (nie w tej samej gałęzi if/else) — instalacja
      przerwana wcześniej dokładnie między CREATE ROLE a CREATE DATABASE zostawiłaby rolę
      bez bazy; gdyby to sprawdzenie było zagnieżdżone pod "if not RoleExists", taki stan
      zostałby już NA ZAWSZE bez bazy/schematu przy każdym kolejnym uruchomieniu instalatora. }
    if not DatabaseExists(PgSuperPassword) then
    begin
      DbOk := RunPsql(PgSuperPassword, '-U postgres -c "CREATE DATABASE pdm OWNER pdm_user;" postgres', 'CREATE DATABASE pdm');
      if not DbOk then
      begin
        LogInstall('BLAD KRYTYCZNY: nie udalo sie utworzyc bazy danych pdm.');
        if not WizardSilent then
          MsgBox(CustomMessage('PgDatabaseCreateFailed') + ' ' + DebugLogPath, mbError, MB_OK);
        exit;
      end;

      SchemaOk := RunPsql(PdmPassword, '-U pdm_user -f "' + ExpandConstant('{app}\db\schema.sql') + '" pdm', 'zaladuj schema.sql');
      if not SchemaOk then
      begin
        LogInstall('BLAD KRYTYCZNY: nie udalo sie zaladowac schema.sql do bazy pdm.');
        if not WizardSilent then
          MsgBox(CustomMessage('PgSchemaLoadFailed') + ' ' + DebugLogPath, mbError, MB_OK);
        exit;
      end;
    end;
  end;

  LogInstall('Rola i baza danych OK. Zapisuje appsettings.Production.json i rejestruje usluge.');

  { Katalogi na magazyn plików/kopie zapasowe/logi — %ProgramData%, bo to trwałe dane
    aplikacji współdzielone przez wszystkich użytkowników maszyny, nie profil pojedynczej
    osoby. Tworzone jawnie, bo appsettings.json samo w sobie nie zakłada katalogów. }
  ForceDirectories(ExpandConstant('{#MyDataDir}\storage'));
  ForceDirectories(ExpandConstant('{#MyDataDir}\backups'));
  ForceDirectories(ExpandConstant('{#MyDataDir}\logs'));

  { %ProgramData% domyślnie NIE daje zwykłym użytkownikom (grupa "Users") prawa zapisu do
    nowo utworzonych podfolderów — tylko odczyt. Usługa Windows działa jako LocalSystem
    (pełny dostęp), więc normalnie tego nie widać, ALE każdy proces uruchomiony jako zwykły
    użytkownik (ręczne odpalenie EasyPDM.Api.exe do debugowania, albo makro FreeCAD/
    SolidWorks próbujące zapisać bezpośrednio do współdzielonego magazynu) dostanie
    UnauthorizedAccessException — potwierdzone w praktyce. "S-1-5-32-545" to
    lokalizacyjnie-niezależny SID wbudowanej grupy "Users" (nazwa "Users" bywa inna na
    nie-angielskich Windows, SID zawsze ten sam) — (OI)(CI)M nadaje Modify (odczyt/zapis/
    usuwanie) rekurencyjnie na cały katalog i wszystko, co w nim później powstanie. Bezpieczne
    do uruchomienia zawsze (też przy aktualizacji) — icacls /grant jest idempotentne. }
  Exec(ExpandConstant('{sys}\icacls.exe'),
    '"' + ExpandConstant('{#MyDataDir}') + '" /grant *S-1-5-32-545:(OI)(CI)M',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  LogInstall('icacls {#MyDataDir} /grant Users:M -> kod wyjscia ' + IntToStr(ResultCode));

  { appsettings.Production.json nadpisuje appsettings.json (domyślne środowisko to
    Production) — ten sam mechanizm warstwowej konfiguracji ASP.NET Core co appsettings.json
    + zmienne środowiskowe w Dockerze/na Linuksie, tylko przez plik zamiast zmiennych — na
    Windows Service ustawienie zmiennych środowiskowych per-usługa jest dużo mniej
    niezawodne (Menedżer Usług nie zawsze odświeża środowisko bez restartu). }
  AppSettings :=
    '{' + #13#10 +
    '  "ConnectionString": "Host=localhost;Port=5432;Database=pdm;Username=pdm_user;Password=' + PdmPassword + '",' + #13#10 +
    '  "StorageRoot": "' + ExpandConstant('{#MyDataDir}\storage') + '",' + #13#10 +
    '  "BackupRoot": "' + ExpandConstant('{#MyDataDir}\backups') + '",' + #13#10 +
    '  "LogRoot": "' + ExpandConstant('{#MyDataDir}\logs') + '"' + #13#10 +
    '}' + #13#10;
  { Ukośniki w ścieżkach Windows trzeba podwoić w JSON-ie (escape \). }
  StringChangeEx(AppSettings, '\', '\\', True);
  SaveStringToFile(ExpandConstant('{app}\appsettings.Production.json'), AppSettings, False);

  { Rejestracja jako usługa Windows — EasyPDM.Api.exe wywołuje builder.Host.UseWindowsService(),
    więc poprawnie integruje się z Menedżerem Sterowania Usługami (start/stop/restart).
    Przy AKTUALIZACJI usługa już istnieje (PrepareToInstall tylko ją zatrzymał, nie usunął) —
    "sc create" na istniejącej usłudze kończy się błędem, więc po prostu ją wtedy startujemy
    z powrotem zamiast rejestrować od nowa. }
  if not ServiceExists() then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'),
      'create {#MyServiceName} binPath= "' + ExpandConstant('{app}\{#MyAppExeName}') +
      '" start= auto DisplayName= "EasyPDM"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    LogInstall('sc create {#MyServiceName} -> kod wyjscia ' + IntToStr(ResultCode));
    Exec(ExpandConstant('{sys}\sc.exe'), 'description {#MyServiceName} "' + CustomMessage('ServiceDescription') + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#MyServiceName}',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  LogInstall('sc start {#MyServiceName} -> kod wyjscia ' + IntToStr(ResultCode));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
