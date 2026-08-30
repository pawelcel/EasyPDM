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
#   sudo ./install-linux.sh
#
# Obsługiwane menedżery pakietów (do instalacji samego PostgreSQL): pacman (Arch/CachyOS),
# apt (Debian/Ubuntu), dnf (Fedora/RHEL). Inna dystrybucja: zainstaluj PostgreSQL ręcznie
# i uruchom ponownie ten skrypt — reszta kroków jest niezależna od dystrybucji.
#
# Aktualizacja: uruchom ten sam skrypt ponownie (z zaktualizowanego checkoutu repo albo z
# nowszej paczki) — wykrywa istniejącą bazę/konto, przebudowuje/podmienia tylko aplikację,
# restartuje usługę. Nowe migracje bazy program stosuje sam automatycznie przy starcie.
#
# Odinstalowanie: sudo ./uninstall-linux.sh
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "Uruchom z sudo: sudo ./install-linux.sh" >&2
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
DB_NAME=pdm
DB_USER=pdm_user

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
        echo "Nie rozpoznano menedżera pakietów — zainstaluj PostgreSQL ręcznie i uruchom ponownie ten skrypt." >&2
        exit 1
    fi
    echo "Instaluję PostgreSQL ($PKG_INSTALL $PG_PACKAGE)..."
    $PKG_INSTALL $PG_PACKAGE
fi

# W odróżnieniu od Debiana/Fedory, pakiet PostgreSQL na Arch NIE inicjalizuje klastra
# automatycznie przy instalacji — trzeba to zrobić ręcznie, tylko raz.
if command -v pacman >/dev/null 2>&1 && [ ! -s /var/lib/postgres/data/PG_VERSION ]; then
    echo "Inicjalizuję klaster PostgreSQL (initdb)..."
    install -d -o postgres -g postgres /var/lib/postgres/data
    sudo -u postgres initdb -D /var/lib/postgres/data
fi
if command -v dnf >/dev/null 2>&1 && [ ! -s /var/lib/pgsql/data/PG_VERSION ]; then
    echo "Inicjalizuję klaster PostgreSQL (postgresql-setup --initdb)..."
    postgresql-setup --initdb
fi

systemctl enable --now postgresql
# Krótkie oczekiwanie, aż serwer faktycznie zacznie przyjmować połączenia po świeżym starcie.
for _ in $(seq 1 10); do
    sudo -u postgres pg_isready >/dev/null 2>&1 && break
    sleep 1
done

echo "== 2/6: Baza danych =="
DB_PASSWORD="${PDM_DB_PASSWORD:-}"
GENERATED_PASSWORD=0
if [ -z "$DB_PASSWORD" ]; then
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
    echo "Zakładam schemat bazy (db/schema.sql)..."
    PGPASSWORD="${DB_PASSWORD}" psql -h localhost -U "${DB_USER}" -d "${DB_NAME}" -f "${REPO_ROOT}/db/schema.sql"
else
    echo "Baza '${DB_NAME}' już istnieje — pomijam zakładanie schematu (aktualizacja istniejącej"
    echo "instalacji). Nowe migracje program zastosuje sam automatycznie przy starcie."
fi

echo "== 3/6: Budowa aplikacji =="
if [ "$PACKAGE_MODE" -eq 1 ]; then
    echo "Gotowa paczka wykryta w ${PUBLISH_DIR} — pomijam budowanie (ta maszyna nie musi"
    echo "mieć zainstalowanego .NET SDK ani Node.js)."
else
    if ! command -v dotnet >/dev/null 2>&1; then
        echo "Brak .NET SDK w PATH — zainstaluj .NET 10 SDK i uruchom ponownie ten skrypt." >&2
        exit 1
    fi
    if ! command -v npm >/dev/null 2>&1; then
        echo "Brak npm w PATH — zainstaluj Node.js i uruchom ponownie ten skrypt." >&2
        exit 1
    fi

    echo "Buduję frontend (npm run build)..."
    (cd "${REPO_ROOT}/EasyPDM.Web" && npm ci && npm run build)

    echo "Publikuję backend (self-contained, linux-x64)..."
    rm -rf "${PUBLISH_DIR}"
    dotnet publish "${REPO_ROOT}/EasyPDM.Api" -c Release -r linux-x64 --self-contained true \
        -p:PublishSingleFile=true -o "${PUBLISH_DIR}"
fi

echo "== 4/6: Konto systemowe i katalogi =="
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

echo "== 5/6: Konfiguracja i usługa systemd =="
# Sekrety (hasło do bazy) w osobnym pliku z ograniczonymi uprawnieniami — nie w samej
# jednostce systemd w /etc/systemd/system/, która bywa czytelna dla wszystkich.
cat > "${CONFIG_DIR}/easypdm.env" <<EOF
ConnectionString=Host=localhost;Port=5432;Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD}
StorageRoot=${DATA_DIR}/storage
BackupRoot=${DATA_DIR}/backups
LogRoot=${DATA_DIR}/logs
ASPNETCORE_URLS=http://0.0.0.0:5000
EOF
chmod 600 "${CONFIG_DIR}/easypdm.env"
chown root:root "${CONFIG_DIR}/easypdm.env"

cat > /etc/systemd/system/easypdm.service <<EOF
[Unit]
Description=EasyPDM — lokalny serwer PDM
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

echo "== 6/6: Gotowe =="
echo "EasyPDM działa pod http://localhost:5000"
echo "Migracje bazy (jeśli jakieś nowe) program stosuje sam przy starcie — nic dodatkowego"
echo "nie trzeba robić ręcznie."
echo "Pierwsze logowanie: admin / admin — zmień hasło od razu po zalogowaniu."
echo "Status usługi:   systemctl status easypdm"
echo "Logi na żywo:    journalctl -u easypdm -f"
if [ "$GENERATED_PASSWORD" -eq 1 ]; then
    echo "Wygenerowane hasło do bazy danych zapisane w ${CONFIG_DIR}/easypdm.env (tylko root)."
fi
