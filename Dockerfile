# Obraz EasyPDM.Api, serwujący też zbudowany frontend (EasyPDM.Web) ze swojego
# wwwroot/ — dokładnie tak samo, jak przy uruchomieniu lokalnym (zob. run.sh). Budowany
# z KORZENIA repozytorium (kontekst budowy musi widzieć zarówno EasyPDM.Web/, jak
# i EasyPDM.Api/), zob. docker-compose.yml.

# ============================================================
# Etap 1: budowa frontendu (React + Vite)
# ============================================================
FROM node:24-alpine AS frontend-build
WORKDIR /src

COPY EasyPDM.Web/package.json EasyPDM.Web/package-lock.json EasyPDM.Web/
RUN cd EasyPDM.Web && npm ci

COPY EasyPDM.Web EasyPDM.Web/
# vite.config.ts ma outDir "../EasyPDM.Api/wwwroot" (ścieżka względna do EasyPDM.Web/)
# — katalog musi istnieć obok, żeby "npm run build" miał gdzie zapisać wynik.
RUN mkdir -p EasyPDM.Api/wwwroot && cd EasyPDM.Web && npm run build

# ============================================================
# Etap 2: publikacja backendu (.NET)
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src

COPY EasyPDM.Api/EasyPDM.Api.csproj EasyPDM.Api/
RUN dotnet restore EasyPDM.Api/EasyPDM.Api.csproj

COPY EasyPDM.Api EasyPDM.Api/
# db/migrations/*.sql jest wbudowywane w EasyPDM.Api.dll jako EmbeddedResource przez
# "..\db\migrations\*.sql" w EasyPDM.Api.csproj (rozwiązuje się do /src/db/migrations/
# w tym kontekście budowy) — bez tego katalogu "dotnet publish" po cichu zbudowałby się
# BEZ ani jednej migracji (glob na nieistniejący katalog nie jest błędem), więc obraz
# Docker nigdy nie zastosowałby żadnej przyszłej migracji (zob. MigrationRunner.cs).
COPY db db/
# Zbudowany frontend z etapu 1 trafia do wwwroot/ PRZED publikacją, żeby "dotnet publish"
# skopiował go razem z resztą (UseStaticFiles w Program.cs serwuje go z tego katalogu).
COPY --from=frontend-build /src/EasyPDM.Api/wwwroot EasyPDM.Api/wwwroot/

RUN dotnet publish EasyPDM.Api/EasyPDM.Api.csproj -c Release -o /app --no-restore

# ============================================================
# Etap 3: obraz uruchomieniowy
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# pg_dump/pg_restore (backup/restore w Ustawienia -> Magazyn plików) muszą być w wersji
# zgodnej z serwerem PostgreSQL 18 — domyślne repozytoria Debiana mają starsze wersje,
# więc doinstalowujemy z oficjalnego repozytorium PGDG. Narzędzia budowy (curl/gnupg) są
# usuwane po instalacji, żeby nie zostawały w finalnym obrazie.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates gnupg \
    && install -d /usr/share/postgresql-common/pgdg \
    && curl -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc --fail \
         https://www.postgresql.org/media/keys/ACCC4CF8.asc \
    && . /etc/os-release \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" \
         > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-18 \
    && apt-get purge -y curl gnupg \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

COPY --from=backend-build /app .

# Magazyn plików/kopii zapasowych/logów trzyma się POZA katalogiem aplikacji, na
# zamontowanym wolumenie (/data w docker-compose.yml) — dzięki temu przetrwa odtworzenie
# kontenera (np. przy aktualizacji obrazu), w odróżnieniu od reszty /app.
ENV StorageRoot=/data/storage
ENV BackupRoot=/data/backups
ENV LogRoot=/data/logs
ENV ASPNETCORE_URLS=http://+:8080
VOLUME ["/data"]
EXPOSE 8080

ENTRYPOINT ["dotnet", "EasyPDM.Api.dll"]
