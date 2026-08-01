#!/usr/bin/env bash
# Uruchamia PdmSystem.Api (serwuje też zbudowany frontend z wwwroot/) pod
# http://localhost:5000. Wymaga: .NET SDK, uruchomionego PostgreSQL (usługa
# "postgresql", connection string w PdmSystem.Api/appsettings.json).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if command -v systemctl >/dev/null 2>&1 && ! systemctl is-active --quiet postgresql 2>/dev/null; then
    echo "Uwaga: usługa PostgreSQL nie wygląda na uruchomioną." >&2
    echo "Spróbuj: sudo systemctl start postgresql" >&2
    echo
fi

echo "Startuję PdmSystem.Api pod http://localhost:5000 (Ctrl+C, żeby zatrzymać)..."
exec dotnet run --project "$REPO_ROOT/PdmSystem.Api" --urls http://localhost:5000
