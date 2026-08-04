# Obraz PdmSystem.Api, serwujący też zbudowany frontend (PdmSystem.Web) ze swojego
# wwwroot/ — dokładnie tak samo, jak przy uruchomieniu lokalnym (zob. run.sh). Budowany
# z KORZENIA repozytorium (kontekst budowy musi widzieć zarówno PdmSystem.Web/, jak
# i PdmSystem.Api/), zob. docker-compose.yml.

# ============================================================
# Etap 1: budowa frontendu (React + Vite)
# ============================================================
FROM node:24-alpine AS frontend-build
WORKDIR /src

COPY PdmSystem.Web/package.json PdmSystem.Web/package-lock.json PdmSystem.Web/
RUN cd PdmSystem.Web && npm ci

COPY PdmSystem.Web PdmSystem.Web/
# vite.config.ts ma outDir "../PdmSystem.Api/wwwroot" (ścieżka względna do PdmSystem.Web/)
# — katalog musi istnieć obok, żeby "npm run build" miał gdzie zapisać wynik.
RUN mkdir -p PdmSystem.Api/wwwroot && cd PdmSystem.Web && npm run build

# ============================================================
# Etap 2: publikacja backendu (.NET)
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src

COPY PdmSystem.Api/PdmSystem.Api.csproj PdmSystem.Api/
RUN dotnet restore PdmSystem.Api/PdmSystem.Api.csproj

COPY PdmSystem.Api PdmSystem.Api/
# Zbudowany frontend z etapu 1 trafia do wwwroot/ PRZED publikacją, żeby "dotnet publish"
# skopiował go razem z resztą (UseStaticFiles w Program.cs serwuje go z tego katalogu).
COPY --from=frontend-build /src/PdmSystem.Api/wwwroot PdmSystem.Api/wwwroot/

RUN dotnet publish PdmSystem.Api/PdmSystem.Api.csproj -c Release -o /app --no-restore

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

ENTRYPOINT ["dotnet", "PdmSystem.Api.dll"]
