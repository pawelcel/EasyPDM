#!/usr/bin/env bash
# Odinstalowuje to, co zainstalował install-easypdm-linux.sh: usługę systemd, aplikację, konto
# systemowe — a bazę, rolę i dane tylko na wyraźne życzenie (pytanie albo PDM_REMOVE_DATA=yes).
# Samego PostgreSQL nie rusza nigdy: może służyć czemuś innemu. Uruchom z sudo: sudo ./uninstall-easypdm-linux.sh
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
        pl:bad_db_name) text="'%s' nie nadaje się na nazwę bazy ani roli — dozwolone małe litery, cyfry i podkreślenie." ;;
        de:bad_db_name) text="'%s' taugt nicht als Datenbank- oder Rollenname — erlaubt sind Kleinbuchstaben, Ziffern und Unterstrich." ;;
        *:bad_db_name)  text="'%s' cannot be used as a database or role name — use lowercase letters, digits and underscores." ;;
        pl:foreign_db)  text="UWAGA: baza '%s' nie została założona pod nazwą, którą nadaje instalator EasyPDM.\nMoże jej używać coś innego, np. środowisko deweloperskie. Usuwaj ją tylko, jeśli masz pewność." ;;
        de:foreign_db)  text="ACHTUNG: Die Datenbank '%s' trägt nicht den Namen, den das EasyPDM-Installationsprogramm vergibt.\nSie kann von etwas anderem genutzt werden, z. B. einer Entwicklungsumgebung. Nur entfernen, wenn Sie sicher sind." ;;
        *:foreign_db)   text="WARNING: the database '%s' does not carry the name the EasyPDM installer gives.\nSomething else may use it, e.g. a development setup. Remove it only if you are sure." ;;
        pl:ask_purge)   text="Usunąć także bazę danych '%s', rolę '%s' i wszystkie dane w /var/lib/easypdm\n(pliki, kopie zapasowe, logi)? Tego nie da się cofnąć. [t/N]" ;;
        de:ask_purge)   text="Auch die Datenbank '%s', die Rolle '%s' und alle Daten in /var/lib/easypdm\n(Dateien, Sicherungen, Protokolle) entfernen? Das lässt sich nicht rückgängig machen. [j/N]" ;;
        *:ask_purge)    text="Also remove the database '%s', the role '%s' and all data in /var/lib/easypdm\n(files, backups, logs)? This cannot be undone. [y/N]" ;;
        pl:purging)     text="Usuwam bazę danych, rolę i dane..." ;;
        de:purging)     text="Datenbank, Rolle und Daten werden entfernt..." ;;
        *:purging)      text="Removing the database, the role and the data..." ;;
        pl:purged)      text="Gotowe. Usunięto bazę '%s', rolę '%s' i katalog %s.\nPostgreSQL zostaje — jeśli był zainstalowany tylko dla EasyPDM, odinstaluj go ręcznie." ;;
        de:purged)      text="Fertig. Datenbank '%s', Rolle '%s' und Verzeichnis %s wurden entfernt.\nPostgreSQL bleibt — falls es nur für EasyPDM installiert wurde, entfernen Sie es von Hand." ;;
        *:purged)       text="Done. Removed the database '%s', the role '%s' and %s.\nPostgreSQL stays — if it was installed only for EasyPDM, remove it yourself." ;;
        pl:purge_failed) text="Nie udało się usunąć bazy '%s' (czy ktoś jest z nią połączony?). Dane zostały nietknięte." ;;
        de:purge_failed) text="Die Datenbank '%s' ließ sich nicht entfernen (ist jemand verbunden?). Die Daten sind unverändert." ;;
        *:purge_failed) text="Could not remove the database '%s' (is something connected to it?). The data is untouched." ;;
        pl:done)        text="Gotowe. Dane zostały zachowane:\n  - baza '%s' i rola '%s'\n  - pliki, kopie zapasowe i logi w /var/lib/easypdm/\nPonowna instalacja podejmie je z powrotem. Usunięcie: sudo PDM_REMOVE_DATA=yes ./uninstall-easypdm-linux.sh" ;;
        de:done)        text="Fertig. Die Daten wurden behalten:\n  - Datenbank '%s' und Rolle '%s'\n  - Dateien, Sicherungen und Protokolle in /var/lib/easypdm/\nEine erneute Installation übernimmt sie wieder. Entfernen: sudo PDM_REMOVE_DATA=yes ./uninstall-easypdm-linux.sh" ;;
        *:done)         text="Done. The data was kept:\n  - the database '%s' and the role '%s'\n  - files, backups and logs in /var/lib/easypdm/\nA new install picks them up again. To remove them: sudo PDM_REMOVE_DATA=yes ./uninstall-easypdm-linux.sh" ;;
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
DATA_DIR=/var/lib/easypdm
SERVICE_USER=easypdm

# Nazwy bazy i roli TEJ instalacji — z jej konfiguracji, odczytane, zanim konfiguracja zniknie
# niżej. Bez konfiguracji (np. drugie uruchomienie po odinstalowaniu z zachowaniem danych)
# zostaje to, co zakłada świeża instalacja; PDM_DB_NAME / PDM_DB_USER wskazują inne.
DB_NAME=""; DB_USER=""
if [ -f "${CONFIG_DIR}/easypdm.env" ]; then
    CS="$(sed -n 's/^ConnectionString=//p' "${CONFIG_DIR}/easypdm.env" | head -n 1)"
    DB_NAME="$(printf '%s' "${CS}" | tr ';' '\n' | sed -n 's/^Database=//p' | head -n 1)"
    DB_USER="$(printf '%s' "${CS}" | tr ';' '\n' | sed -n 's/^Username=//p' | head -n 1)"
fi
DB_NAME="${PDM_DB_NAME:-${DB_NAME:-easypdm}}"
DB_USER="${PDM_DB_USER:-${DB_USER:-easypdm}}"
for name in "${DB_NAME}" "${DB_USER}"; do
    if ! [[ "${name}" =~ ^[a-z_][a-z0-9_]*$ ]]; then
        msg bad_db_name "${name}" >&2
        exit 1
    fi
done

# Czy usunąć też bazę, rolę i dane. Pytamy NA POCZĄTKU, zanim cokolwiek zniknie — Ctrl+C przy
# pytaniu niczego nie psuje. Domyślnie NIE: to jedyne miejsce, z którego nie ma powrotu (w
# /var/lib/easypdm leżą też automatyczne kopie zapasowe).
#   PDM_REMOVE_DATA=yes   usuń bez pytania (np. w skrypcie)
#   PDM_REMOVE_DATA=no    zachowaj bez pytania
#   bez zmiennej          zapytaj — ale tylko przy terminalu; bez niego (CI, potok) zachowaj
# Baza o innej nazwie niż "easypdm" nie została założona przez instalator 0.7+ — najpewniej
# instalacja z 0.6, która przejęła bazę "pdm" środowiska deweloperskiego. Wtedy pytanie
# dostaje wyraźne ostrzeżenie, bo ta sama baza może służyć czemuś innemu.
PURGE=0
case "$(printf '%s' "${PDM_REMOVE_DATA:-}" | tr 'A-Z' 'a-z')" in
    yes|y|1|true) PURGE=1 ;;
    "")
        if [ -t 0 ]; then
            if [ "${DB_NAME}" != "easypdm" ]; then
                msg foreign_db "${DB_NAME}"
            fi
            ANSWER=""
            read -r -p "$(msg ask_purge "${DB_NAME}" "${DB_USER}") " ANSWER || ANSWER=""
            case "$(printf '%s' "${ANSWER}" | tr 'A-Z' 'a-z')" in
                y|yes|t|tak|j|ja) PURGE=1 ;;
            esac
        fi
        ;;
esac

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
if [ "${PURGE}" -eq 1 ]; then
    msg purging
    # Pliki znikają DOPIERO po udanym usunięciu bazy — inaczej zostałaby baza z odnośnikami do
    # plików, których już nie ma. Usunięcie bazy nie uda się, gdy ktoś jest z nią połączony
    # (np. środowisko deweloperskie na tej samej bazie) — i dobrze.
    if sudo -u postgres dropdb --if-exists "${DB_NAME}" \
        && sudo -u postgres psql -q -c "DROP ROLE IF EXISTS ${DB_USER};"; then
        rm -rf "${DATA_DIR}"
        msg purged "${DB_NAME}" "${DB_USER}" "${DATA_DIR}"
    else
        msg purge_failed "${DB_NAME}" >&2
        exit 1
    fi
else
    msg done "${DB_NAME}" "${DB_USER}"
fi
