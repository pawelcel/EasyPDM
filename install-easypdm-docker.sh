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
# Aktualizacja: uruchom ten sam skrypt ponownie po "git pull" — wykrywa istniejący .env
# (NIE nadpisuje już ustawionego hasła/portu), przebudowuje i podmienia tylko obraz "api".
# Nowe migracje bazy program stosuje sam automatycznie przy starcie.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

echo "== 1/3: Docker =="
if ! command -v docker >/dev/null 2>&1; then
    echo "Docker nie jest zainstalowany. Zainstaluj go i uruchom ten skrypt ponownie:" >&2
    echo "  curl -fsSL https://get.docker.com | sh" >&2
    echo "  sudo usermod -aG docker \$USER   # potem wyloguj się i zaloguj ponownie" >&2
    exit 1
fi
if ! docker compose version >/dev/null 2>&1; then
    echo "Znaleziono 'docker', ale brak wtyczki 'docker compose' (Compose v2) — dokończ" >&2
    echo "instalację Dockera (zob. https://docs.docker.com/compose/install/) i spróbuj ponownie." >&2
    exit 1
fi

echo "== 2/3: Konfiguracja (.env) =="
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
            echo "Nie udało się znaleźć wolnego portu w zakresie 5000-5200 — ustaw" >&2
            echo "PDM_HOST_PORT w .env ręcznie i uruchom ten skrypt ponownie." >&2
            exit 1
        fi
    done
    echo "PDM_HOST_PORT=${PORT}" >> .env
    GENERATED_PORT=1
fi

echo "== 3/3: Budowanie i uruchamianie kontenerów =="
docker compose up -d --build

PORT="$(grep '^PDM_HOST_PORT=' .env | cut -d= -f2)"
echo
echo "EasyPDM działa pod http://localhost:${PORT}"
echo "Pierwsze logowanie: admin / admin — zmień hasło od razu po zalogowaniu."
echo "Status kontenerów: docker compose ps"
echo "Logi na żywo:       docker compose logs -f api"
if [ "$GENERATED_PASSWORD" -eq 1 ]; then
    echo "Wygenerowane hasło do bazy danych zapisane w .env (plik NIE jest śledzony w git)."
fi
if [ "$GENERATED_PORT" -eq 1 ]; then
    echo "Port ${PORT} wybrany automatycznie (domyślny 5000 był zajęty albo nie ustawiono go"
    echo "jawnie) — żeby zmienić, edytuj PDM_HOST_PORT w .env i uruchom \"docker compose up -d\" ponownie."
fi
