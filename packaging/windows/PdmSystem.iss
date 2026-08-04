; Instalator Windows dla PdmSystem — Inno Setup (https://jrsoftware.org/isinfo.php).
;
; PRZED kompilacją: uruchom packaging\windows\build.ps1 (buduje frontend + publikuje
; self-contained backend win-x64 do packaging\windows\publish\), potem skompiluj ten plik
; w Inno Setup Compiler (iscc.exe / GUI).
;
; Co robi zainstalowany PdmSystem:
;   1. Sprawdza, czy PostgreSQL jest już zainstalowany (szuka psql.exe w typowej lokalizacji
;      instalatora EDB) — jeśli nie, kieruje na stronę pobierania i przerywa instalację
;      (świadomie NIE próbujemy cicho pobierać/instalować 300+ MB instalatora PostgreSQL —
;      za duże ryzyko niewidocznej awarii bez możliwości zdiagnozowania).
;   2. Pyta o hasło superużytkownika "postgres" (potrzebne, żeby założyć rolę/bazę PdmSystem).
;   3. Zakłada rolę "pdm_user" (z wygenerowanym losowo hasłem) i bazę "pdm", ładuje schemat
;      (db\schema.sql, dołączony do instalatora).
;   4. Zapisuje appsettings.Production.json z prawdziwym connection stringiem i ścieżkami
;      magazynu/kopii/logów w %ProgramData%\PdmSystem.
;   5. Rejestruje PdmSystem.Api.exe jako usługę Windows (autostart, działa w tle bez okna
;      konsoli) i ją uruchamia.
;   6. Skrót na pulpicie/w Menu Start otwierający http://localhost:5000.
;
; NIEZWERYFIKOWANE: ten plik został napisany bez dostępu do Windows/Inno Setup Compiler w tym
; środowisku (Linux) — oparty o udokumentowane, standardowe wzorce Inno Setup, ale nie
; skompilowany ani nie przetestowany end-to-end. Przy pierwszym uruchomieniu obserwuj
; przebieg instalacji i zgłoś, co nie zagra.

#define MyAppName "PdmSystem"
#define MyAppVersion "1.0.0"
#define MyAppExeName "PdmSystem.Api.exe"
#define MyServiceName "PdmSystem"
#define MyDataDir "{commonappdata}\PdmSystem"

[Setup]
; Stały GUID — nie zmieniaj między wersjami, Inno Setup używa go do wykrywania aktualizacji
; istniejącej instalacji zamiast instalowania obok niej.
AppId={{B6E2B6B0-2B0A-4C1E-9C1B-5F6A6F6C7D8E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=PdmSystemSetup
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
var
  PostgresPasswordPage: TInputQueryWizardPage;
  PsqlPath: String;

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
const
  Chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Len do
    Result := Result + Chars[Random(Length(Chars)) + 1];
end;

{ psql nie ma parametru na hasło podane wprost (poza .pgpass) — najprostszy niezawodny sposób
  na Windows to tymczasowy plik .bat, który najpierw ustawia PGPASSWORD, a potem woła psql.
  Zwraca True, jeśli psql zakończył się kodem 0. UWAGA: Args MUSI zawierać własne "-U <rola>"
  — ta funkcja nie narzuca żadnej roli domyślnej (wywołania łączą się raz jako "postgres",
  raz jako "pdm_user"). }
function RunPsql(PgPassword, Args: String): Boolean;
var
  BatchFile: String;
  ResultCode: Integer;
begin
  BatchFile := ExpandConstant('{tmp}\pdm_psql_' + IntToStr(Random(1000000)) + '.bat');
  SaveStringToFile(BatchFile,
    '@echo off' + #13#10 +
    'set PGPASSWORD=' + PgPassword + #13#10 +
    '"' + PsqlPath + '" -h localhost ' + Args + #13#10,
    False);
  try
    Result := Exec(BatchFile, '', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  finally
    DeleteFile(BatchFile);
  end;
end;

function RoleExists(PgPassword: String): Boolean;
var
  OutFile, Output: String;
  ResultCode: Integer;
  BatchFile: String;
begin
  { -tAc zwraca surowy wynik zapytania bez nagłówków — łapiemy go do pliku, bo Exec() samo
    w sobie nie oddaje stdout w Inno Setup. }
  OutFile := ExpandConstant('{tmp}\pdm_role_check.txt');
  BatchFile := ExpandConstant('{tmp}\pdm_psql_check_' + IntToStr(Random(1000000)) + '.bat');
  SaveStringToFile(BatchFile,
    '@echo off' + #13#10 +
    'set PGPASSWORD=' + PgPassword + #13#10 +
    '"' + PsqlPath + '" -h localhost -U postgres -tAc ' +
    '"SELECT 1 FROM pg_roles WHERE rolname=''pdm_user''" > "' + OutFile + '"' + #13#10,
    False);
  try
    Exec(BatchFile, '', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if LoadStringFromFile(OutFile, Output) then
      Result := Pos('1', Output) > 0
    else
      Result := False;
  finally
    DeleteFile(BatchFile);
    DeleteFile(OutFile);
  end;
end;

procedure InitializeWizard();
begin
  PostgresPasswordPage := CreateInputQueryPage(wpSelectDir,
    'Połączenie z PostgreSQL',
    'Hasło superużytkownika "postgres"',
    'PdmSystem potrzebuje go JEDNORAZOWO, żeby założyć własną rolę (pdm_user) i bazę danych ' +
    '(pdm) — nie jest nigdzie zapisywane.');
  PostgresPasswordPage.Add('Hasło "postgres":', True);
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
    else if not RunPsql(PostgresPasswordPage.Values[0], '-U postgres -c "SELECT 1;" postgres') then
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
begin
  if CurStep <> ssPostInstall then
    exit;

  PgSuperPassword := PostgresPasswordPage.Values[0];
  PdmPassword := GenerateRandomPassword(32);

  { Rola + baza — pomijane, jeśli instalator jest uruchamiany ponownie (aktualizacja) i już
    istnieją; hasło pdm_user jest wtedy i tak nadpisywane, żeby appsettings zawsze się zgadzało
    z tym, co faktycznie jest w bazie. }
  if RoleExists(PgSuperPassword) then
    RunPsql(PgSuperPassword, '-U postgres -c "ALTER ROLE pdm_user PASSWORD ''' + PdmPassword + ''';" postgres')
  else
  begin
    RunPsql(PgSuperPassword, '-U postgres -c "CREATE ROLE pdm_user LOGIN PASSWORD ''' + PdmPassword + ''';" postgres');
    RunPsql(PgSuperPassword, '-U postgres -c "CREATE DATABASE pdm OWNER pdm_user;" postgres');
    RunPsql(PdmPassword, '-U pdm_user -f "' + ExpandConstant('{app}\db\schema.sql') + '" pdm');
  end;

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

  { Rejestracja jako usługa Windows — PdmSystem.Api.exe wywołuje builder.Host.UseWindowsService(),
    więc poprawnie integruje się z Menedżerem Sterowania Usługami (start/stop/restart). }
  Exec(ExpandConstant('{sys}\sc.exe'),
    'create {#MyServiceName} binPath= "' + ExpandConstant('{app}\{#MyAppExeName}') +
    '" start= auto DisplayName= "PdmSystem"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'description {#MyServiceName} "Lokalny serwer PDM"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start {#MyServiceName}',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
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
