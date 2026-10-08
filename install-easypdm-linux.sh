#!/usr/bin/env bash
# Instaluje EasyPDM jako usługę systemd na TEJ maszynie (natywnie, bez Dockera):
# PostgreSQL (jeśli jeszcze nie ma), baza danych, self-contained publish backendu razem
# ze zbudowanym frontendem, dedykowane konto systemowe, usługa systemd z autostartem.
#
# Dwa tryby uruchomienia:
#   1. Z katalogu repo (klon z gita) -- skrypt SAM buduje frontend i backend na tej
#      maszynie, wymaga .NET SDK + Node.js/npm zainstalowanych tu tylko na czas budowy.
#   2. Z rozpakowanej gotowej paczki (patrz .github/workflows/build-linux-package.yml,
#      artefakt "EasyPDM-Linux-x64_v<wersja>") -- obok tego skryptu leży już zbudowany katalog
#      publish/ (self-contained exe + wwwroot), więc budowanie jest pomijane i ta
#      maszyna NIE musi mieć .NET SDK/npm w ogóle.
#
# Uruchom z sudo:
#   sudo ./install-easypdm-linux.sh
#
# Port: pierwszy wolny od 5000 przy świeżej instalacji; przy aktualizacji zostaje ten, na którym
# usługa już działa; PDM_PORT=<port> wymusza konkretny (sudo PDM_PORT=8080 ./install-easypdm-linux.sh).
# Język komunikatów: z ustawień systemu albo sudo EASYPDM_LANG=en ./install-easypdm-linux.sh
# Baza: świeża instalacja zakłada własną "easypdm"; aktualizacja zostaje przy swojej;
# PDM_DB_NAME / PDM_DB_USER / PDM_DB_PASSWORD wskazują inną.
#
# Obsługiwane menedżery pakietów (do instalacji samego PostgreSQL): pacman (Arch/CachyOS),
# apt (Debian/Ubuntu), dnf (Fedora/RHEL). Inna dystrybucja: zainstaluj PostgreSQL ręcznie
# i uruchom ponownie ten skrypt — reszta kroków jest niezależna od dystrybucji.
#
# Aktualizacja: uruchom ten sam skrypt ponownie (z zaktualizowanego checkoutu repo albo z
# nowszej paczki) — wykrywa istniejącą bazę/konto, przebudowuje/podmienia tylko aplikację,
# restartuje usługę. Nowe migracje bazy program stosuje sam automatycznie przy starcie.
#
# Odinstalowanie: sudo ./uninstall-easypdm-linux.sh
set -euo pipefail

# Komunikaty po polsku, niemiecku albo angielsku — ten sam wybór i te same reguły co w
# install-easypdm-docker.sh (gettext: LC_ALL, potem LC_MESSAGES, potem LANG; przy C/POSIX
# angielski; inaczej pierwszeństwo ma LANGUAGE). EASYPDM_LANG=pl|de|en wymusza język.
#
# Uwaga na sudo: domyślnie przepuszcza LANG, LANGUAGE i LC_*, ale NIE dowolne inne zmienne —
# EASYPDM_LANG (tak samo PDM_PORT) trzeba więc podać PO sudo:
#   sudo EASYPDM_LANG=en ./install-easypdm-linux.sh
detect_lang() {
    local v="${EASYPDM_LANG:-}"
    if [ -z "$v" ]; then
        local locale="${LC_ALL:-${LC_MESSAGES:-${LANG:-}}}"
        case "$locale" in
            ""|C|C.*|POSIX) v="" ;;
            *) v="${LANGUAGE:-$locale}"; v="${v%%:*}" ;;
        esac
    fi
    case "$(printf '%s' "${v:0:2}" | tr 'A-Z' 'a-z')" in
        pl) echo pl ;; de) echo de ;; *) echo en ;;
    esac
}
UI_LANG="$(detect_lang)"

# msg KLUCZ [argumenty] — komunikat w wybranym języku (argumenty jak w printf).
msg() {
    local key="$1"; shift
    local text
    case "$UI_LANG:$key" in
        pl:root)            text="Uruchom z sudo: sudo ./install-easypdm-linux.sh" ;;
        de:root)            text="Mit sudo ausführen: sudo ./install-easypdm-linux.sh" ;;
        *:root)             text="Run with sudo: sudo ./install-easypdm-linux.sh" ;;
        pl:no_pkg_mgr)      text="Nie rozpoznano menedżera pakietów — zainstaluj PostgreSQL ręcznie i uruchom ponownie ten skrypt." ;;
        de:no_pkg_mgr)      text="Kein unterstützter Paketmanager gefunden — installieren Sie PostgreSQL selbst und führen Sie dieses Skript erneut aus." ;;
        *:no_pkg_mgr)       text="No supported package manager found — install PostgreSQL yourself and run this script again." ;;
        pl:installing_pg)   text="Instaluję PostgreSQL (%s)..." ;;
        de:installing_pg)   text="PostgreSQL wird installiert (%s)..." ;;
        *:installing_pg)    text="Installing PostgreSQL (%s)..." ;;
        pl:initdb)          text="Inicjalizuję klaster PostgreSQL (%s)..." ;;
        de:initdb)          text="PostgreSQL-Cluster wird initialisiert (%s)..." ;;
        *:initdb)           text="Initialising the PostgreSQL cluster (%s)..." ;;
        pl:step_db)         text="== 2/6: Baza danych ==" ;;
        de:step_db)         text="== 2/6: Datenbank ==" ;;
        *:step_db)          text="== 2/6: Database ==" ;;
        pl:schema)          text="Zakładam schemat bazy (db/schema.sql)..." ;;
        de:schema)          text="Datenbankschema wird angelegt (db/schema.sql)..." ;;
        *:schema)           text="Creating the database schema (db/schema.sql)..." ;;
        pl:db_exists)       text="Baza '%s' już istnieje — pomijam zakładanie schematu (aktualizacja istniejącej\ninstalacji). Nowe migracje program zastosuje sam automatycznie przy starcie." ;;
        de:db_exists)       text="Datenbank '%s' existiert bereits — Schema wird übersprungen (Aktualisierung einer\nbestehenden Installation). Neue Migrationen wendet das Programm beim Start selbst an." ;;
        *:db_exists)        text="Database '%s' already exists — skipping the schema (updating an existing\ninstallation). New migrations are applied by the program itself on start." ;;
        pl:step_build)      text="== 3/6: Budowa aplikacji ==" ;;
        de:step_build)      text="== 3/6: Anwendung bauen ==" ;;
        *:step_build)       text="== 3/6: Building the application ==" ;;
        pl:package_found)   text="Gotowa paczka wykryta w %s — pomijam budowanie (ta maszyna nie musi\nmieć zainstalowanego .NET SDK ani Node.js)." ;;
        de:package_found)   text="Fertiges Paket in %s gefunden — Bauen wird übersprungen (dieser Rechner braucht\nweder das .NET SDK noch Node.js)." ;;
        *:package_found)    text="Prebuilt package found in %s — skipping the build (this machine needs\nneither the .NET SDK nor Node.js)." ;;
        pl:no_dotnet)       text="Brak .NET SDK w PATH — zainstaluj .NET 10 SDK i uruchom ponownie ten skrypt." ;;
        de:no_dotnet)       text="Kein .NET SDK im PATH — installieren Sie das .NET 10 SDK und führen Sie dieses Skript erneut aus." ;;
        *:no_dotnet)        text="No .NET SDK on PATH — install the .NET 10 SDK and run this script again." ;;
        pl:no_npm)          text="Brak npm w PATH — zainstaluj Node.js i uruchom ponownie ten skrypt." ;;
        de:no_npm)          text="Kein npm im PATH — installieren Sie Node.js und führen Sie dieses Skript erneut aus." ;;
        *:no_npm)           text="No npm on PATH — install Node.js and run this script again." ;;
        pl:building_web)    text="Buduję frontend (npm run build)..." ;;
        de:building_web)    text="Frontend wird gebaut (npm run build)..." ;;
        *:building_web)     text="Building the frontend (npm run build)..." ;;
        pl:publishing)      text="Publikuję backend (self-contained, linux-x64)..." ;;
        de:publishing)      text="Backend wird veröffentlicht (self-contained, linux-x64)..." ;;
        *:publishing)       text="Publishing the backend (self-contained, linux-x64)..." ;;
        pl:step_account)    text="== 4/6: Konto systemowe i katalogi ==" ;;
        de:step_account)    text="== 4/6: Systemkonto und Verzeichnisse ==" ;;
        *:step_account)     text="== 4/6: System account and folders ==" ;;
        pl:step_service)    text="== 5/6: Konfiguracja i usługa systemd ==" ;;
        de:step_service)    text="== 5/6: Konfiguration und systemd-Dienst ==" ;;
        *:step_service)     text="== 5/6: Configuration and systemd service ==" ;;
        pl:bad_db_name)     text="'%s' nie nadaje się na nazwę bazy ani roli — dozwolone małe litery, cyfry i podkreślenie." ;;
        de:bad_db_name)     text="'%s' taugt nicht als Datenbank- oder Rollenname — erlaubt sind Kleinbuchstaben, Ziffern und Unterstrich." ;;
        *:bad_db_name)      text="'%s' cannot be used as a database or role name — use lowercase letters, digits and underscores." ;;
        pl:bad_port)        text="PDM_PORT=%s nie jest poprawnym numerem portu (1-65535)." ;;
        de:bad_port)        text="PDM_PORT=%s ist keine gültige Portnummer (1-65535)." ;;
        *:bad_port)         text="PDM_PORT=%s is not a valid port number (1-65535)." ;;
        pl:no_port)         text="Nie udało się znaleźć wolnego portu w zakresie 5000-5200 — podaj go jawnie:\n  sudo PDM_PORT=<port> ./install-easypdm-linux.sh" ;;
        de:no_port)         text="Kein freier Port im Bereich 5000-5200 gefunden — geben Sie ihn ausdrücklich an:\n  sudo PDM_PORT=<Port> ./install-easypdm-linux.sh" ;;
        *:no_port)          text="Could not find a free port in the range 5000-5200 — give one explicitly:\n  sudo PDM_PORT=<port> ./install-easypdm-linux.sh" ;;
        pl:waiting)         text="Czekam, aż EasyPDM zacznie odpowiadać na porcie %s..." ;;
        de:waiting)         text="Warte, bis EasyPDM auf Port %s antwortet..." ;;
        *:waiting)          text="Waiting for EasyPDM to answer on port %s..." ;;
        pl:not_responding)  text="EasyPDM nie odpowiada na porcie %s — sprawdź: journalctl -u easypdm -n 50" ;;
        de:not_responding)  text="EasyPDM antwortet nicht auf Port %s — prüfen Sie: journalctl -u easypdm -n 50" ;;
        *:not_responding)   text="EasyPDM is not answering on port %s — check: journalctl -u easypdm -n 50" ;;
        pl:step_done)       text="== 6/6: Gotowe ==" ;;
        de:step_done)       text="== 6/6: Fertig ==" ;;
        *:step_done)        text="== 6/6: Done ==" ;;
        pl:running)         text="EasyPDM działa pod %s" ;;
        de:running)         text="EasyPDM läuft unter %s" ;;
        *:running)          text="EasyPDM is running at %s" ;;
        pl:migrations)      text="Migracje bazy (jeśli jakieś nowe) program stosuje sam przy starcie — nic dodatkowego\nnie trzeba robić ręcznie." ;;
        de:migrations)      text="Datenbankmigrationen, falls vorhanden, wendet das Programm beim Start selbst an —\nvon Hand ist nichts zu tun." ;;
        *:migrations)       text="Database migrations, if any, are applied by the program itself on start — nothing\nto do by hand." ;;
        pl:first_login)     text="Pierwsze logowanie: admin / admin — zmień hasło od razu po zalogowaniu." ;;
        de:first_login)     text="Erste Anmeldung: admin / admin — ändern Sie das Passwort gleich nach der Anmeldung." ;;
        *:first_login)      text="First login: admin / admin — change the password right after signing in." ;;
        pl:status)          text="Status usługi:   systemctl status easypdm\nLogi na żywo:    journalctl -u easypdm -f" ;;
        de:status)          text="Dienststatus:     systemctl status easypdm\nLive-Protokolle:  journalctl -u easypdm -f" ;;
        *:status)           text="Service status:  systemctl status easypdm\nLive logs:       journalctl -u easypdm -f" ;;
        pl:gen_password)    text="Wygenerowane hasło do bazy danych zapisane w %s (tylko root)." ;;
        de:gen_password)    text="Das erzeugte Datenbankpasswort steht in %s (nur root)." ;;
        *:gen_password)     text="The generated database password is saved in %s (root only)." ;;
        pl:gen_port)        text="Port 5000 był zajęty, więc wybrano %s — żeby zmienić, uruchom instalator ponownie:\n  sudo PDM_PORT=<port> ./install-easypdm-linux.sh" ;;
        de:gen_port)        text="Port 5000 war belegt, daher wurde %s gewählt — zum Ändern das Installationsprogramm erneut ausführen:\n  sudo PDM_PORT=<Port> ./install-easypdm-linux.sh" ;;
        *:gen_port)         text="Port 5000 was taken, so %s was chosen — to change it, run the installer again:\n  sudo PDM_PORT=<port> ./install-easypdm-linux.sh" ;;
        *)                  text="$key" ;;
    esac
    # shellcheck disable=SC2059  # format celowo z tablicy komunikatów
    printf "${text}\n" "$@"
}

if [ "$(id -u)" -ne 0 ]; then
    msg root >&2
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR=/opt/easypdm
# Paczka z build-linux-package.yml niesie już gotowy katalog publish/ obok tego skryptu --
# jeśli jest, pomijamy budowanie i instalujemy bezpośrednio z niego (tryb 2 powyżej).
if [ -x "${REPO_ROOT}/publish/EasyPDM.Api" ]; then
    PACKAGE_MODE=1
    PUBLISH_DIR="${REPO_ROOT}/publish"
else
    PACKAGE_MODE=0
    PUBLISH_DIR="${REPO_ROOT}/EasyPDM.Api/bin/publish-linux"
fi
DATA_DIR=/var/lib/easypdm
CONFIG_DIR=/etc/easypdm
SERVICE_USER=easypdm

echo "== 1/6: PostgreSQL =="
if command -v pacman >/dev/null 2>&1; then
    PKG_INSTALL="pacman -S --needed --noconfirm"
    PG_PACKAGE="postgresql"
elif command -v apt-get >/dev/null 2>&1; then
    PKG_INSTALL="apt-get install -y"
    PG_PACKAGE="postgresql"
elif command -v dnf >/dev/null 2>&1; then
    PKG_INSTALL="dnf install -y"
    # Na Fedorze/RHEL pakiet "postgresql" to WYŁĄCZNIE narzędzia klienckie (psql) — serwer
    # (postmaster, jednostka systemd, postgresql-setup) jest w osobnym "postgresql-server",
    # który i tak ciągnie "postgresql" jako zależność.
    PG_PACKAGE="postgresql-server"
else
    PKG_INSTALL=""
    PG_PACKAGE=""
fi

if ! command -v psql >/dev/null 2>&1; then
    if [ -z "$PKG_INSTALL" ]; then
        msg no_pkg_mgr >&2
        exit 1
    fi
    msg installing_pg "$PKG_INSTALL $PG_PACKAGE"
    $PKG_INSTALL $PG_PACKAGE
fi

# W odróżnieniu od Debiana/Fedory, pakiet PostgreSQL na Arch NIE inicjalizuje klastra
# automatycznie przy instalacji — trzeba to zrobić ręcznie, tylko raz.
if command -v pacman >/dev/null 2>&1 && [ ! -s /var/lib/postgres/data/PG_VERSION ]; then
    msg initdb "initdb"
    install -d -o postgres -g postgres /var/lib/postgres/data
    # Uwierzytelnianie podane JAWNIE. Gołe initdb ustawia "trust" — każdy użytkownik tej maszyny
    # łączyłby się wtedy z bazą jako dowolna rola, łącznie z superużytkownikiem postgres, bez
    # hasła. peer dla gniazda lokalnego wystarcza poleceniom "sudo -u postgres ..." w tym
    # skrypcie, a scram-sha-256 przez TCP — aplikacji, która loguje się hasłem swojej roli. Tak
    # samo, jak klaster domyślnie konfigurują Debian i Ubuntu.
    sudo -u postgres initdb -D /var/lib/postgres/data --auth-local=peer --auth-host=scram-sha-256
fi
if command -v dnf >/dev/null 2>&1 && [ ! -s /var/lib/pgsql/data/PG_VERSION ]; then
    msg initdb "postgresql-setup --initdb"
    postgresql-setup --initdb
fi

systemctl enable --now postgresql
# Krótkie oczekiwanie, aż serwer faktycznie zacznie przyjmować połączenia po świeżym starcie.
for _ in $(seq 1 10); do
    sudo -u postgres pg_isready >/dev/null 2>&1 && break
    sleep 1
done

msg step_db
# Baza, rola i hasło. Kolejność:
#   1. PDM_DB_NAME / PDM_DB_USER / PDM_DB_PASSWORD podane jawnie.
#   2. To, czego używa istniejąca instalacja (ConnectionString w easypdm.env) — aktualizacja
#      zostaje przy swojej bazie i swoim haśle. Dotąd hasło roli było generowane od nowa przy
#      każdym uruchomieniu.
#   3. Świeża instalacja: baza i rola "easypdm", jak konto systemowe usługi.
# Dotąd było na sztywno "pdm" / "pdm_user" — te same nazwy, których używa środowisko
# deweloperskie i przykłady w dokumentacji. Na maszynie programisty instalator przejmował więc
# jego bazę i po cichu zmieniał hasło jego roli (wyłapane w praktyce na CachyOS). Instalacje
# z 0.6 i starszych mają "pdm" zapisane w easypdm.env, więc punkt 2 zostawia je przy niej.
CONFIG_FILE="${CONFIG_DIR}/easypdm.env"
CURRENT_DB_NAME=""; CURRENT_DB_USER=""; CURRENT_DB_PASSWORD=""
if [ -f "${CONFIG_FILE}" ]; then
    CURRENT_CS="$(sed -n 's/^ConnectionString=//p' "${CONFIG_FILE}" | head -n 1)"
    cs_part() { printf '%s' "${CURRENT_CS}" | tr ';' '\n' | sed -n "s/^$1=//p" | head -n 1; }
    CURRENT_DB_NAME="$(cs_part Database)"
    CURRENT_DB_USER="$(cs_part Username)"
    CURRENT_DB_PASSWORD="$(cs_part Password)"
fi
DB_NAME="${PDM_DB_NAME:-${CURRENT_DB_NAME:-easypdm}}"
DB_USER="${PDM_DB_USER:-${CURRENT_DB_USER:-easypdm}}"
# Nazwy trafiają wprost do poleceń SQL, więc tylko bezpieczne identyfikatory.
for name in "${DB_NAME}" "${DB_USER}"; do
    if ! [[ "${name}" =~ ^[a-z_][a-z0-9_]*$ ]]; then
        msg bad_db_name "${name}" >&2
        exit 1
    fi
done
GENERATED_PASSWORD=0
if [ -n "${PDM_DB_PASSWORD:-}" ]; then
    DB_PASSWORD="${PDM_DB_PASSWORD}"
elif [ -n "${CURRENT_DB_PASSWORD}" ] && [ "${DB_USER}" = "${CURRENT_DB_USER}" ]; then
    DB_PASSWORD="${CURRENT_DB_PASSWORD}"
else
    DB_PASSWORD="$(head -c 32 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 32)"
    GENERATED_PASSWORD=1
fi

if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='${DB_USER}'" | grep -q 1; then
    sudo -u postgres psql -c "CREATE ROLE ${DB_USER} LOGIN PASSWORD '${DB_PASSWORD}';"
else
    sudo -u postgres psql -c "ALTER ROLE ${DB_USER} PASSWORD '${DB_PASSWORD}';"
fi

if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='${DB_NAME}'" | grep -q 1; then
    sudo -u postgres createdb -O "${DB_USER}" "${DB_NAME}"
    msg schema
    PGPASSWORD="${DB_PASSWORD}" psql -h localhost -U "${DB_USER}" -d "${DB_NAME}" -f "${REPO_ROOT}/db/schema.sql"
else
    msg db_exists "${DB_NAME}"
fi

msg step_build
if [ "$PACKAGE_MODE" -eq 1 ]; then
    msg package_found "${PUBLISH_DIR}"
else
    if ! command -v dotnet >/dev/null 2>&1; then
        msg no_dotnet >&2
        exit 1
    fi
    if ! command -v npm >/dev/null 2>&1; then
        msg no_npm >&2
        exit 1
    fi

    msg building_web
    (cd "${REPO_ROOT}/EasyPDM.Web" && npm ci && npm run build)

    msg publishing
    rm -rf "${PUBLISH_DIR}"
    dotnet publish "${REPO_ROOT}/EasyPDM.Api" -c Release -r linux-x64 --self-contained true \
        -p:PublishSingleFile=true -o "${PUBLISH_DIR}"
fi

msg step_account
getent group "${SERVICE_USER}" >/dev/null || groupadd --system "${SERVICE_USER}"
getent passwd "${SERVICE_USER}" >/dev/null || useradd --system --gid "${SERVICE_USER}" \
    --home-dir "${DATA_DIR}" --shell /usr/sbin/nologin "${SERVICE_USER}"

install -d -o "${SERVICE_USER}" -g "${SERVICE_USER}" \
    "${DATA_DIR}" "${DATA_DIR}/storage" "${DATA_DIR}/backups" "${DATA_DIR}/logs"
install -d "${CONFIG_DIR}"

rm -rf "${APP_DIR}"
install -d "${APP_DIR}"
cp -r "${PUBLISH_DIR}/." "${APP_DIR}/"
chown -R root:root "${APP_DIR}"
chmod +x "${APP_DIR}/EasyPDM.Api"

msg step_service

# Port. Kolejność ma znaczenie:
#   1. PDM_PORT podany jawnie — wygrywa zawsze, także przy aktualizacji (to świadoma zmiana).
#   2. Port z istniejącej konfiguracji — aktualizacja NIE może przenieść działającej usługi pod
#      inny adres; zakładki, makra CAD i inne komputery w sieci znają stary.
#   3. Pierwszy wolny od 5000 — przy świeżej instalacji. Dotąd port był na sztywno 5000, a gdy
#      ktoś go już trzymał (np. EasyPDM w Dockerze na tej samej maszynie), usługa nie wstawała,
#      a skrypt i tak pisał, że działa.
# Wolny = nikt nie przyjmuje połączenia na 127.0.0.1:<port> (bash /dev/tcp, bez ss/netstat).
EXISTING_PORT=""
if [ -f "${CONFIG_DIR}/easypdm.env" ]; then
    EXISTING_PORT="$(sed -n 's#^ASPNETCORE_URLS=http://[^:]*:\([0-9][0-9]*\).*#\1#p' "${CONFIG_DIR}/easypdm.env" | head -n 1)"
fi
GENERATED_PORT=0
if [ -n "${PDM_PORT:-}" ]; then
    if ! [[ "${PDM_PORT}" =~ ^[0-9]+$ ]] || [ "${PDM_PORT}" -lt 1 ] || [ "${PDM_PORT}" -gt 65535 ]; then
        msg bad_port "${PDM_PORT}" >&2
        exit 1
    fi
    PORT="${PDM_PORT}"
elif [ -n "${EXISTING_PORT}" ]; then
    PORT="${EXISTING_PORT}"
else
    PORT=5000
    ATTEMPTS=0
    while (echo > "/dev/tcp/127.0.0.1/${PORT}") 2>/dev/null; do
        PORT=$((PORT + 1))
        ATTEMPTS=$((ATTEMPTS + 1))
        if [ "$ATTEMPTS" -ge 200 ]; then
            msg no_port >&2
            exit 1
        fi
    done
    GENERATED_PORT=1
fi
# Sekrety (hasło do bazy) w osobnym pliku z ograniczonymi uprawnieniami — nie w samej
# jednostce systemd w /etc/systemd/system/, która bywa czytelna dla wszystkich.
cat > "${CONFIG_FILE}" <<EOF
ConnectionString=Host=localhost;Port=5432;Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD}
StorageRoot=${DATA_DIR}/storage
BackupRoot=${DATA_DIR}/backups
LogRoot=${DATA_DIR}/logs
ASPNETCORE_URLS=http://0.0.0.0:${PORT}
EOF
chmod 600 "${CONFIG_FILE}"
chown root:root "${CONFIG_FILE}"

cat > /etc/systemd/system/easypdm.service <<EOF
[Unit]
Description=EasyPDM — local PDM server
After=network.target postgresql.service
Wants=postgresql.service

[Service]
Type=simple
User=${SERVICE_USER}
Group=${SERVICE_USER}
# WorkingDirectory MUSI wskazywać katalog aplikacji — self-contained publish wyznacza
# katalog główny (content root, więc i wwwroot/) z BIEŻĄCEGO katalogu roboczego procesu,
# nie z lokalizacji samego pliku wykonywalnego.
WorkingDirectory=${APP_DIR}
EnvironmentFile=${CONFIG_DIR}/easypdm.env
ExecStart=${APP_DIR}/EasyPDM.Api
Restart=on-failure
RestartSec=5
ReadWritePaths=${DATA_DIR}
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable easypdm
# "restart", nie "start" — przy AKTUALIZACJI usługa zwykle już działa (poprzednia wersja),
# a "systemctl start" na już uruchomionej usłudze nic by nie zrobił, więc stary proces
# zostałby ze starym plikiem wykonywalnym mimo podmienionych plików w /opt/easypdm.
systemctl restart easypdm

# Sukces ogłaszamy dopiero, gdy na porcie odpowiada naprawdę EasyPDM. Dotąd skrypt pisał
# "działa pod :5000" bez sprawdzania — także wtedy, gdy usługa padła, bo port był zajęty. Strona
# ma być TĄ aplikacją (tytuł z EasyPDM.Web/index.html), a nie czymkolwiek, co akurat słucha na
# porcie. Bez curla zostaje samo sprawdzenie, czy port przyjmuje połączenia.
msg waiting "${PORT}"
READY=0
for _ in $(seq 1 60); do
    if command -v curl >/dev/null 2>&1; then
        curl -s --max-time 2 "http://127.0.0.1:${PORT}/" 2>/dev/null | grep -q '<title>EasyPDM' && { READY=1; break; }
    elif (echo > "/dev/tcp/127.0.0.1/${PORT}") 2>/dev/null; then
        READY=1; break
    fi
    sleep 1
done
if [ "$READY" -ne 1 ]; then
    msg not_responding "${PORT}" >&2
    exit 1
fi

msg step_done
msg running "http://localhost:${PORT}"
msg migrations
msg first_login
msg status
if [ "$GENERATED_PASSWORD" -eq 1 ]; then
    msg gen_password "${CONFIG_FILE}"
fi
# Tylko gdy port RÓŻNI się od domyślnego — jak w install-easypdm-docker.sh.
if [ "$GENERATED_PORT" -eq 1 ] && [ "$PORT" != "5000" ]; then
    msg gen_port "${PORT}"
fi
