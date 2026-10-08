#!/usr/bin/env bash
# Odinstalowuje to, co zainstalował install-linux.sh: usługę systemd, aplikację, konto
# systemowe. NIE dotyka PostgreSQL ani samej bazy danych "pdm" — te trzeba usunąć ręcznie,
# jeśli naprawdę mają zniknąć (żeby przypadkiem nie skasować danych, których ktoś jeszcze
# potrzebuje). Uruchom z sudo: sudo ./uninstall-easypdm-linux.sh
set -euo pipefail

# Komunikaty po polsku, niemiecku albo angielsku — ten sam wybór i te same reguły co w
# install-easypdm-docker.sh (gettext: LC_ALL, potem LC_MESSAGES, potem LANG; przy C/POSIX
# angielski; inaczej pierwszeństwo ma LANGUAGE). EASYPDM_LANG=pl|de|en wymusza język.
#
# Uwaga na sudo: domyślnie przepuszcza LANG, LANGUAGE i LC_*, ale NIE dowolne inne zmienne —
# EASYPDM_LANG trzeba więc podać PO sudo:
#   sudo EASYPDM_LANG=en ./uninstall-easypdm-linux.sh
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

msg() {
    local key="$1"; shift
    local text
    case "$UI_LANG:$key" in
        pl:root)     text="Uruchom z sudo: sudo ./uninstall-easypdm-linux.sh" ;;
        de:root)     text="Mit sudo ausführen: sudo ./uninstall-easypdm-linux.sh" ;;
        *:root)      text="Run with sudo: sudo ./uninstall-easypdm-linux.sh" ;;
        pl:stopping) text="Zatrzymuję i usuwam usługę systemd..." ;;
        de:stopping) text="systemd-Dienst wird gestoppt und entfernt..." ;;
        *:stopping)  text="Stopping and removing the systemd service..." ;;
        pl:removing) text="Usuwam aplikację i konfigurację (%s, %s)..." ;;
        de:removing) text="Anwendung und Konfiguration werden entfernt (%s, %s)..." ;;
        *:removing)  text="Removing the application and configuration (%s, %s)..." ;;
        pl:done)     text="Gotowe. NIE usunięto (zrób to ręcznie, jeśli naprawdę chcesz):\n  - magazynu plików/kopii zapasowych/logów: /var/lib/easypdm/\n  - bazy danych: sudo -u postgres dropdb pdm\n  - roli bazy danych: sudo -u postgres psql -c \"DROP ROLE pdm_user;\"\n  - samego PostgreSQL (jeśli był zainstalowany tylko dla EasyPDM)" ;;
        de:done)     text="Fertig. NICHT entfernt (bei Bedarf von Hand):\n  - Dateispeicher, Sicherungen und Protokolle: /var/lib/easypdm/\n  - die Datenbank: sudo -u postgres dropdb pdm\n  - die Datenbankrolle: sudo -u postgres psql -c \"DROP ROLE pdm_user;\"\n  - PostgreSQL selbst (falls es nur für EasyPDM installiert wurde)" ;;
        *:done)      text="Done. NOT removed (do it yourself if you really want to):\n  - file storage, backups and logs: /var/lib/easypdm/\n  - the database: sudo -u postgres dropdb pdm\n  - the database role: sudo -u postgres psql -c \"DROP ROLE pdm_user;\"\n  - PostgreSQL itself (if it was installed only for EasyPDM)" ;;
        *)           text="$key" ;;
    esac
    # shellcheck disable=SC2059
    printf "${text}\n" "$@"
}

if [ "$(id -u)" -ne 0 ]; then
    msg root >&2
    exit 1
fi

APP_DIR=/opt/easypdm
CONFIG_DIR=/etc/easypdm
SERVICE_USER=easypdm

msg stopping
systemctl disable --now easypdm 2>/dev/null || true
rm -f /etc/systemd/system/easypdm.service
systemctl daemon-reload

msg removing "${APP_DIR}" "${CONFIG_DIR}"
rm -rf "${APP_DIR}" "${CONFIG_DIR}"

# "|| true" na obu liniach -- bez tego, pod `set -e`, uruchomienie skryptu DRUGI raz (np.
# odinstalowanie już odinstalowanego) przerywałoby się w milczeniu na pierwszej linii (konto
# już nie istnieje -> getent zwraca niezerowy kod -> cała lista `&&` kończy się niezerowo),
# nigdy nie docierając do komunikatu "Gotowe" poniżej.
getent passwd "${SERVICE_USER}" >/dev/null && userdel "${SERVICE_USER}" || true
getent group "${SERVICE_USER}" >/dev/null && groupdel "${SERVICE_USER}" 2>/dev/null || true

echo
msg done
