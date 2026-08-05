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
; Aktualizacja: uruchom ten sam instalator ponownie (nowy build z packaging\windows\build.ps1)
; — wykrywa istniejącą rolę/bazę (pomija zakładanie schematu), zatrzymuje usługę PRZED
; podmianą plików (PrepareToInstall — inaczej Windows zablokowałby nadpisanie działającego
; .exe), i uruchamia ją z powrotem zamiast rejestrować od nowa. Nowe migracje bazy program
; stosuje sam automatycznie przy starcie (nic nie trzeba robić ręcznie).
;
; Kompilacja: automatyczna, przy każdym pushu dotykającym tych plików —
; .github/workflows/build-windows-installer.yml buduje EasyPDMSetup.exe na windowsowym
; runnerze GitHuba (ma Inno Setup Compiler fabrycznie) i wystawia go jako pobieralny
; artefakt przebiegu — nie trzeba mieć Windows/Inno Setup lokalnie, żeby dostać gotowy
; instalator. Skompilowane i zweryfikowane realnym kompilatorem (kilka błędów Pascal
; Script — brak lokalnych "const" w funkcjach, LoadStringFromFile wymaga AnsiString,
; brak Randomize/RandSeed/GetTickCount w tym dialekcie — zostało po drodze wyłapanych
; i poprawionych). Instalacja end-to-end na żywej maszynie z PostgreSQL wciąż nie była
; ręcznie przetestowana — przy pierwszym uruchomieniu obserwuj przebieg i zgłoś, co nie zagra.

#define MyAppName "EasyPDM"
#define MyAppVersion "0.1"
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
OutputBaseFilename=EasyPDMSetup
OutputDir=Output
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern

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

[Code]
const
  // Inno Setup Pascal Script nie pozwala na sekcję "const" zadeklarowaną LOKALNIE wewnątrz
  // funkcji (błąd kompilacji "'BEGIN' expected") — dlatego to stała globalna, nie lokalna
  // wewnątrz GenerateRandomPassword, gdzie jest jedynym użyciem.
  RandomPasswordChars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';

var
  PostgresPasswordPage: TInputQueryWizardPage;
  PsqlPath: String;
  DebugLogPath: String;

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
            { Nazwy katalogów wersji sortują się leksykograficznie tak samo jak numerycznie
              dla jednocyfrowych/dwucyfrowych głównych wersji PostgreSQL (9..99) — wystarczające
              dla wyboru "najnowszej" bez pełnego parsowania wersji. }
            if (Best = '') or (FindRec.Name > Best) then
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
    'Połączenie z PostgreSQL',
    'Hasło superużytkownika "postgres"',
    'EasyPDM potrzebuje go JEDNORAZOWO, żeby założyć własną rolę (pdm_user) i bazę danych ' +
    '(pdm) — nie jest nigdzie zapisywane.');
  PostgresPasswordPage.Add('Hasło "postgres":', True);
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
  Result := (PageID = PostgresPasswordPage.ID) and (PgPasswordFromCmdLine() <> '');
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  PsqlPath := FindPsqlPath();
  if PsqlPath = '' then
  begin
    if MsgBox('Nie znaleziono zainstalowanego PostgreSQL (wymagany, wersja 18 zalecana). ' +
              'Otworzyć stronę pobierania? Po instalacji uruchom ten instalator ponownie.',
              mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://www.postgresql.org/download/windows/', '', '', SW_SHOW, ewNoWait, ResultCode);
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
      MsgBox('Podaj hasło superużytkownika "postgres".', mbError, MB_OK);
      Result := False;
    end
    else if not RunPsql(PostgresPasswordPage.Values[0], '-U postgres -c "SELECT 1;" postgres', 'test polaczenia') then
    begin
      MsgBox('Nie udało się połączyć z PostgreSQL tym hasłem. Spróbuj ponownie.', mbError, MB_OK);
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

  PgSuperPassword := PgPasswordFromCmdLine();
  if PgSuperPassword = '' then
    PgSuperPassword := PostgresPasswordPage.Values[0];
  PdmPassword := GenerateRandomPassword(32);

  { Rola — pomijana (tylko ALTER hasła), jeśli instalator jest uruchamiany ponownie
    (aktualizacja) i rola już istnieje; hasło pdm_user jest wtedy i tak nadpisywane, żeby
    appsettings zawsze się zgadzało z tym, co faktycznie jest w bazie. }
  if RoleExists(PgSuperPassword) then
    RoleOk := RunPsql(PgSuperPassword, '-U postgres -c "ALTER ROLE pdm_user PASSWORD ''' + PdmPassword + ''';" postgres', 'ALTER ROLE pdm_user')
  else
    RoleOk := RunPsql(PgSuperPassword, '-U postgres -c "CREATE ROLE pdm_user LOGIN PASSWORD ''' + PdmPassword + ''';" postgres', 'CREATE ROLE pdm_user');

  { Wcześniej wynik powyższego wcale nie był sprawdzany — przy błędzie (np. złe hasło
    superużytkownika albo połączenie z niewłaściwym serwerem PostgreSQL, gdy na maszynie
    działa więcej niż jedna instancja na porcie 5432) appsettings.json i tak zapisywało się
    z hasłem, które NIGDY nie trafiło do żadnej realnej roli — usługa startowała, ale
    EasyPDM.Api.exe od razu padał na "password authentication failed". Teraz przerywamy
    głośno zamiast zostawiać użytkownika z cichą, niedziałającą instalacją. }
  if not RoleOk then
  begin
    LogInstall('BLAD KRYTYCZNY: nie udalo sie zalozyc/zaktualizowac roli pdm_user - przerywam konfiguracje bazy.');
    MsgBox('Nie udało się skonfigurować roli bazy danych PostgreSQL (pdm_user). ' +
      'Sprawdź hasło superużytkownika "postgres" oraz czy na tym porcie (5432) nie działa ' +
      'inna instancja PostgreSQL. Szczegóły w logu: ' + DebugLogPath, mbError, MB_OK);
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
      MsgBox('Nie udało się utworzyć bazy danych PostgreSQL "pdm". Szczegóły w logu: ' + DebugLogPath, mbError, MB_OK);
      exit;
    end;

    SchemaOk := RunPsql(PdmPassword, '-U pdm_user -f "' + ExpandConstant('{app}\db\schema.sql') + '" pdm', 'zaladuj schema.sql');
    if not SchemaOk then
    begin
      LogInstall('BLAD KRYTYCZNY: nie udalo sie zaladowac schema.sql do bazy pdm.');
      MsgBox('Nie udało się załadować schematu bazy danych. Szczegóły w logu: ' + DebugLogPath, mbError, MB_OK);
      exit;
    end;
  end;

  LogInstall('Rola i baza danych OK. Zapisuje appsettings.Production.json i rejestruje usluge.');

  { Katalogi na magazyn plików/kopie zapasowe/logi — %ProgramData%, bo to trwałe dane
    aplikacji współdzielone przez wszystkich użytkowników maszyny, nie profil pojedynczej
    osoby. Tworzone jawnie, bo appsettings.json samo w sobie nie zakłada katalogów. }
  ForceDirectories(ExpandConstant('{#MyDataDir}\storage'));
  ForceDirectories(ExpandConstant('{#MyDataDir}\backups'));
  ForceDirectories(ExpandConstant('{#MyDataDir}\logs'));

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
    Exec(ExpandConstant('{sys}\sc.exe'), 'description {#MyServiceName} "Lokalny serwer PDM"',
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
