#!/usr/bin/env bash
# Instaluje EasyPDM przez Docker Compose na TEJ maszynie: zakłada .env (z wygenerowanym
# hasłem do bazy, jeśli nie podano własnego), automatycznie wybiera WOLNY port hosta, jeśli
# nie ustawiono go jawnie (domyślny 5000 bywa już zajęty na serwerze z innymi usługami —
# dokładnie to spotkaliśmy w praktyce przy pierwszym wdrożeniu), buduje i uruchamia
# kontenery.
#
# Wymaga zainstalowanego Dockera (z wtyczką "compose") — jeśli go nie ma, skrypt podaje
# komendę instalacyjną i przerywa (samo zainstalowanie Dockera to zbyt duża, systemowa
# zmiana, żeby robić ją bez pytania).
#
# Uruchom z katalogu repo:
#   ./install-easypdm-docker.sh
#
# Język komunikatów: z ustawień systemu albo wymuszony, np. EASYPDM_LANG=en ./install-easypdm-docker.sh
#
# Aktualizacja: uruchom ten sam skrypt ponownie po "git pull" — wykrywa istniejący .env
# (NIE nadpisuje już ustawionego hasła/portu), przebudowuje i podmienia tylko obraz "api".
# Nowe migracje bazy program stosuje sam automatycznie przy starcie.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

# Komunikaty po polsku, niemiecku albo angielsku — ten sam wybór co w makrach CAD.
#
# Język jak w gettext: locale komunikatów to LC_ALL, a gdy go nie ma, LC_MESSAGES, a gdy i tego
# nie ma, LANG. Jeśli wychodzi C/POSIX, komunikaty są angielskie — jawne LC_ALL=C znaczy "bez
# tłumaczeń" i NIE przepuszcza dalej do LANG. W przeciwnym razie pierwszeństwo ma LANGUAGE (bywa
# listą, np. "pl:en"), a dopiero po nim sama locale.
#
# EASYPDM_LANG=pl|de|en wygrywa ze wszystkim. Jest po to, żeby dało się dostać angielski przebieg
# na komputerze z polskim systemem (np. do nagrania instrukcji) bez przestawiania języka całego
# systemu — samo LANG=en nie wystarczy, gdy ustawione jest LANGUAGE albo LC_ALL.
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

# msg KLUCZ [argumenty] — wypisuje komunikat w wybranym języku (argumenty jak w printf).
msg() {
    local key="$1"; shift
    local text
    case "$UI_LANG:$key" in
        pl:no_docker)    text="Docker nie jest zainstalowany. Zainstaluj go i uruchom ten skrypt ponownie:" ;;
        de:no_docker)    text="Docker ist nicht installiert. Installieren Sie ihn und führen Sie dieses Skript erneut aus:" ;;
        *:no_docker)     text="Docker is not installed. Install it and run this script again:" ;;
        pl:relogin)      text="potem wyloguj się i zaloguj ponownie" ;;
        de:relogin)      text="danach ab- und wieder anmelden" ;;
        *:relogin)       text="then log out and back in" ;;
        pl:no_compose)   text="Znaleziono 'docker', ale brak wtyczki 'docker compose' (Compose v2) — dokończ\ninstalację Dockera (zob. https://docs.docker.com/compose/install/) i spróbuj ponownie." ;;
        de:no_compose)   text="'docker' gefunden, aber das Plugin 'docker compose' (Compose v2) fehlt — schließen Sie\ndie Docker-Installation ab (siehe https://docs.docker.com/compose/install/) und versuchen Sie es erneut." ;;
        *:no_compose)    text="Found 'docker', but the 'docker compose' plugin (Compose v2) is missing — finish\ninstalling Docker (see https://docs.docker.com/compose/install/) and try again." ;;
        pl:step_config)  text="== 2/3: Konfiguracja (.env) ==" ;;
        de:step_config)  text="== 2/3: Konfiguration (.env) ==" ;;
        *:step_config)   text="== 2/3: Configuration (.env) ==" ;;
        pl:no_port)      text="Nie udało się znaleźć wolnego portu w zakresie 5000-5200 — ustaw\nPDM_HOST_PORT w .env ręcznie i uruchom ten skrypt ponownie." ;;
        de:no_port)      text="Kein freier Port im Bereich 5000-5200 gefunden — setzen Sie\nPDM_HOST_PORT in der .env von Hand und führen Sie dieses Skript erneut aus." ;;
        *:no_port)       text="Could not find a free port in the range 5000-5200 — set\nPDM_HOST_PORT in .env by hand and run this script again." ;;
        pl:step_build)   text="== 3/3: Budowanie i uruchamianie kontenerów ==" ;;
        de:step_build)   text="== 3/3: Container bauen und starten ==" ;;
        *:step_build)    text="== 3/3: Building and starting the containers ==" ;;
        pl:running)      text="EasyPDM działa pod %s" ;;
        de:running)      text="EasyPDM läuft unter %s" ;;
        *:running)       text="EasyPDM is running at %s" ;;
        pl:first_login)  text="Pierwsze logowanie: admin / admin — zmień hasło od razu po zalogowaniu." ;;
        de:first_login)  text="Erste Anmeldung: admin / admin — ändern Sie das Passwort gleich nach der Anmeldung." ;;
        *:first_login)   text="First login: admin / admin — change the password right after signing in." ;;
        pl:status)       text="Status kontenerów: docker compose ps\nLogi na żywo:       docker compose logs -f api" ;;
        de:status)       text="Container-Status:  docker compose ps\nLive-Protokolle:   docker compose logs -f api" ;;
        *:status)        text="Container status: docker compose ps\nLive logs:        docker compose logs -f api" ;;
        pl:gen_password) text="Wygenerowane hasło do bazy danych zapisane w .env (plik NIE jest śledzony w git)." ;;
        de:gen_password) text="Das erzeugte Datenbankpasswort steht in der .env (die Datei wird NICHT von git verfolgt)." ;;
        *:gen_password)  text="The generated database password is saved in .env (the file is NOT tracked by git)." ;;
        pl:gen_port)     text="Port 5000 był zajęty, więc wybrano %s — żeby zmienić, edytuj PDM_HOST_PORT w .env\ni uruchom \"docker compose up -d\" ponownie." ;;
        de:gen_port)     text="Port 5000 war belegt, daher wurde %s gewählt — zum Ändern PDM_HOST_PORT in der .env anpassen\nund \"docker compose up -d\" erneut ausführen." ;;
        *:gen_port)      text="Port 5000 was taken, so %s was chosen — to change it, edit PDM_HOST_PORT in .env\nand run \"docker compose up -d\" again." ;;
        *)               text="$key" ;;
    esac
    # shellcheck disable=SC2059  # format celowo z tablicy komunikatów
    printf "${text}\n" "$@"
}

echo "== 1/3: Docker =="
if ! command -v docker >/dev/null 2>&1; then
    msg no_docker >&2
    echo "  curl -fsSL https://get.docker.com | sh" >&2
    echo "  sudo usermod -aG docker \$USER   # $(msg relogin)" >&2
    exit 1
fi
if ! docker compose version >/dev/null 2>&1; then
    msg no_compose >&2
    exit 1
fi

msg step_config
if [ ! -f .env ]; then
    cp .env.example .env
fi

# Hasło do bazy — generujemy, jeśli .env wciąż ma placeholder z .env.example (albo brakuje
# wpisu w ogóle) — ta sama konwencja co install-easypdm-linux.sh dla instalacji natywnej.
GENERATED_PASSWORD=0
if ! grep -q '^PDM_DB_PASSWORD=' .env || grep -q '^PDM_DB_PASSWORD=zmien-to-haslo$' .env; then
    DB_PASSWORD="$(head -c 32 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 32)"
    if grep -q '^PDM_DB_PASSWORD=' .env; then
        sed -i "s/^PDM_DB_PASSWORD=.*/PDM_DB_PASSWORD=${DB_PASSWORD}/" .env
    else
        echo "PDM_DB_PASSWORD=${DB_PASSWORD}" >> .env
    fi
    GENERATED_PASSWORD=1
fi

# Port hosta — jeśli nie ustawiony jawnie, szukamy pierwszego wolnego od 5000 wzwyż, próbując
# nawiązać połączenie TCP (bash /dev/tcp, bez zależności od ss/netstat/lsof — port "zajęty",
# jeśli połączenie się uda, "wolny", jeśli zostanie odrzucone). Ograniczone do 200 prób, żeby
# nie zapętlić się bez końca w skrajnym przypadku.
GENERATED_PORT=0
if ! grep -q '^PDM_HOST_PORT=' .env; then
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
    echo "PDM_HOST_PORT=${PORT}" >> .env
    GENERATED_PORT=1
fi

msg step_build
docker compose up -d --build

PORT="$(grep '^PDM_HOST_PORT=' .env | cut -d= -f2)"
echo
msg running "http://localhost:${PORT}"
msg first_login
msg status
if [ "$GENERATED_PASSWORD" -eq 1 ]; then
    msg gen_password
fi
# Tylko gdy port RÓŻNI się od domyślnego. Na świeżej instalacji port jest wybierany zawsze, a
# dotychczasowe "wybrano 5000, bo domyślny 5000 był zajęty" przeczyło samo sobie przy każdej
# zwykłej instalacji, w której nic nie było zajęte.
if [ "$GENERATED_PORT" -eq 1 ] && [ "$PORT" != "5000" ]; then
    msg gen_port "${PORT}"
fi
