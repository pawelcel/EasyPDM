#!/usr/bin/env bash
# Odinstalowuje to, co zainstalował install-linux.sh: usługę systemd, aplikację, konto
# systemowe. NIE dotyka PostgreSQL ani samej bazy danych "pdm" — te trzeba usunąć ręcznie,
# jeśli naprawdę mają zniknąć (żeby przypadkiem nie skasować danych, których ktoś jeszcze
# potrzebuje). Uruchom z sudo: sudo ./uninstall-linux.sh
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "Uruchom z sudo: sudo ./uninstall-linux.sh" >&2
    exit 1
fi

APP_DIR=/opt/easypdm
CONFIG_DIR=/etc/easypdm
SERVICE_USER=easypdm

echo "Zatrzymuję i usuwam usługę systemd..."
systemctl disable --now easypdm 2>/dev/null || true
rm -f /etc/systemd/system/easypdm.service
systemctl daemon-reload

echo "Usuwam aplikację i konfigurację (${APP_DIR}, ${CONFIG_DIR})..."
rm -rf "${APP_DIR}" "${CONFIG_DIR}"

# "|| true" na obu liniach -- bez tego, pod `set -e`, uruchomienie skryptu DRUGI raz (np.
# odinstalowanie już odinstalowanego) przerywałoby się w milczeniu na pierwszej linii (konto
# już nie istnieje -> getent zwraca niezerowy kod -> cała lista `&&` kończy się niezerowo),
# nigdy nie docierając do komunikatu "Gotowe" poniżej.
getent passwd "${SERVICE_USER}" >/dev/null && userdel "${SERVICE_USER}" || true
getent group "${SERVICE_USER}" >/dev/null && groupdel "${SERVICE_USER}" 2>/dev/null || true

echo
echo "Gotowe. NIE usunięto (zrób to ręcznie, jeśli naprawdę chcesz):"
echo "  - magazynu plików/kopii zapasowych/logów: /var/lib/easypdm/"
echo "  - bazy danych: sudo -u postgres dropdb pdm"
echo "  - roli bazy danych: sudo -u postgres psql -c \"DROP ROLE pdm_user;\""
echo "  - samego PostgreSQL (jeśli był zainstalowany tylko dla EasyPDM)"
