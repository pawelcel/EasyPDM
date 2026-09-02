# EasyPDM — dokumentacja techniczna

[English](TECHNICAL.md) | **Polski** | [Deutsch](TECHNICAL.de.md)

Ten dokument jest dla administratora, który instaluje/utrzymuje EasyPDM, oraz dla
programistów. Opis samego narzędzia (do czego służy i jak z niego korzystać przy
projektowaniu) jest w [README.pl.md](README.pl.md).

## Status

Ręczne tworzenie projektów i elementów przez aplikację webową (upload pliku wprost do
magazynu API) albo przez makro FreeCAD (`EasyPDM.FreeCad/`) lub SolidWorks
(`EasyPDM.SolidWorks/`), które wołają to samo API.
Wcześniejsze podejście ze skanowaniem dysku (`EasyPDM.Core`, `EasyPDM.Indexer`) zostało
usunięte z repo — było niezgodne ze schematem od migracji `002` i nigdy nieużywane przez
`Api`.

Frontend to osobna aplikacja **React 19 + Vite + TypeScript** (`EasyPDM.Web/`) budowana
wprost do `EasyPDM.Api/wwwroot/`. Interfejs jest w pełni przetłumaczony (polski/angielski/
niemiecki) i ma tryb jasny/ciemny. Przetestowane na żywo: CachyOS, .NET 10, PostgreSQL 18.

## Co tu jest

- **`db/schema.sql`** — pełny schemat od zera (aktualny stan po wszystkich migracjach).
- **`db/migrations/`** — migracje `002`–`039` dla już istniejącej bazy: projekty, typy
  elementów, widoczność w drzewku, status/rewizje, materiały (+ grupy/podgrupy), załączniki,
  kolejność BOM, komentarze do rewizji, logowanie i role, właściwości projektu, kaskadowe
  usuwanie, kolejność korzeni drzewka, producenci, zapisane filtry, dostęp do projektów per
  użytkownik, właściciel/blokada elementu, usunięcie martwego schematu rewizji/checkout,
  historia (status/rewizje/załączniki/blokada), harmonogram automatycznej kopii zapasowej,
  śledzenie zastosowanych migracji, rola podglądu/CAD załącznika, literowy prefiks numeru
  elementu per rodzaj, Klienci (katalog + własne drzewko plików), elementy bez projektu
  (element może istnieć bez żadnego projektu, dostępny wyłącznie przez "Cała baza"), adres
  kontaktu producenta/klienta, domyślna wartość/unikalność pozycji BOM, powiadomienia + ich
  preferencje per typ, znacznik przykładowego projektu oraz mała wewnętrzna tabela flag
  `system_state`. Od migracji 027 pliki z tego folderu są wbudowane
  w program (embedded resources) i stosowane **automatycznie przy każdym starcie** — zob.
  `MigrationRunner.cs` i "Jak uruchomić" niżej — nie trzeba ich już odpalać ręcznie przez psql.
- **`EasyPDM.Api/`** — ASP.NET Core (minimal API, Npgsql bez ORM), endpointy podzielone
  po funkcjach w `Endpoints/` — pełna lista niżej w "Endpointy API". Serwuje też zbudowany
  frontend ze swojego `wwwroot/`. Własny `FileLoggerProvider` (bez dodatkowego pakietu NuGet)
  zapisuje logi programu do `logs/` (rotacja dzienna, 30 dni retencji), widoczne w
  Ustawienia → Logi.
- **`EasyPDM.Web/`** — frontend: React 19 + Vite + TypeScript + Tailwind v4 + shadcn/ui
  (komponenty na bazie Base UI, styl „base-nova”), i18n (pl/en/de), motyw jasny/ciemny.
- **`EasyPDM.Api.Tests/`** — testy integracyjne (xUnit + `WebApplicationFactory`),
  uruchamiają CAŁĄ aplikację przeciwko prawdziwemu PostgreSQL (osobny schemat `pdm_test` w
  tej samej bazie, zerowany przed każdą klasą testową). Lokalnie: `dotnet test
  EasyPDM.Api.Tests` (connection string domyślnie wskazuje na lokalny `pdm`/`pdm_user` —
  nadpisywalny zmienną `EASYPDM_TEST_CONNECTION_STRING`, tak jak w CI).
- **`EasyPDM.FreeCad/`** — dwa makra: `EasyPDMUpload.FCMacro` (uruchamiane z poziomu
  FreeCAD, zapisuje aktywny dokument, deleguje wybór projektu/nowy-czy-istniejący/
  właściwości do przeglądarki, tworzy Część/Złożenie w PDM, dogrywa plik jako załącznik,
  eksportuje STEP i zmienia nazwę lokalnego pliku na `numer (nazwa)`) i
  `EasyPDMDownload.FCMacro` (odwrotny kierunek: wybór Części/Złożenia w przeglądarce,
  pobiera je razem z CAŁYM drzewem składników Złożenia — żeby odnośniki `App::Link` się
  rozwiązały — i od razu otwiera w FreeCAD; pomija już pobrane pliki, pyta przed
  nadpisaniem starszej rewizji nowszą). **Oba makra nieprzetestowane na żywym FreeCAD w
  obecnej wersji** (przepływ przez przeglądarkę) — zob. `EasyPDM.FreeCad/README.md`.
- **`EasyPDM.SolidWorks/`** — odpowiednik powyższego dla SolidWorks (makra VBA
  `EasyPDMUpload.bas`/`EasyPDMDownload.bas`), z tym samym przepływem przez przeglądarkę,
  eksportem STEP i automatycznym wykrywaniem drzewa złożenia. **Niezweryfikowane na żywym
  SolidWorks** — zob. `EasyPDM.SolidWorks/README.md` po szczegóły i znane ryzyka.
- **`Dockerfile`/`Dockerfile.postgres`/`docker-compose.yml`/`install-easypdm-docker.sh`**,
  **`install-easypdm-linux.sh`/`uninstall-easypdm-linux.sh`** i **`packaging/windows/`**
  (instalator `.exe`, Inno Setup) — trzy ścieżki wdrożenia bez ręcznego składania z osobna
  backendu/frontendu/bazy, zob. "Jak uruchomić" niżej.
- **`.github/workflows/`** — siedem workflowów CI, wszystkie uruchamialne też ręcznie
  (`workflow_dispatch`) albo przez `gh workflow run <plik>`:
  - `build.yml` — przy każdym pushu/PR: build backendu + testy integracyjne
    (`EasyPDM.Api.Tests`, przeciwko usłudze `postgres` w CI) i typy/lint/build frontendu.
  - `build-windows-installer.yml` — buduje `EasyPDM_Windows_v<wersja>.exe` (zob. wyżej) i dodatkowo
    **realnie go instaluje** na windowsowym runnerze (PostgreSQL przez Chocolatey,
    `/VERYSILENT`), sprawdzając dwukrotnie (świeża instalacja + symulacja aktualizacji), że
    usługa startuje i serwer odpowiada — jedyny sposób, żeby to sprawdzić bez posiadania
    fizycznego/wirtualnego Windows. Zadeklarowany też jako reużywalny `workflow_call` (zob.
    `create-release-draft.yml` niżej).
  - `build-linux-package.yml` — buduje `EasyPDM-Linux-x64_v<wersja>.tar.gz` (self-contained
    backend + zbudowany frontend + skrypty instalacyjne + `db/schema.sql`) i realnie instaluje
    go na czystym runnerze Ubuntu, żeby sprawdzić, że usługa startuje. Też zadeklarowany jako
    reużywalny `workflow_call`.
  - `test-linux-installer.yml` — uruchamia `install-easypdm-linux.sh` naprawdę na czystym
    Ubuntu (świeża instalacja, "aktualizacja", `uninstall-easypdm-linux.sh`), czego lokalne
    środowisko deweloperskie (bez hasła do `sudo` w tej sesji) nie pozwalało zrobić.
  - `publish-docker-image.yml` — buduje i publikuje obrazy `api` i `postgres` (ten drugi
    z wbudowanym `db/schema.sql`) do GitHub Container Registry (`ghcr.io/pawelcel/easypdm-api`,
    `ghcr.io/pawelcel/easypdm-postgres`) z tagiem `:edge` (+ SHA commita) przy każdym pushu
    dotykającym kodu serwera — do sprawdzenia najnowszego stanu `main` przed wydaniem, zob.
    "Docker" niżej.
  - `publish-docker-release.yml` — te same dwa obrazy, ale tylko przy wypchnięciu taga
    wersji (`v*`); to jedyny workflow aktualizujący `:latest` (to, co realnie ściąga
    `docker-compose.yml`), plus pasujący tag `:vX.Y.Z`. Zob. "Docker" niżej.
  - `create-release-draft.yml` — też przy wypchnięciu taga wersji (`v*`), niezależnie od
    `publish-docker-release.yml`: najpierw sprawdza, czy `MyAppVersion` (`EasyPDM.iss`) i
    `APP_VERSION` (`version.ts`) faktycznie zgadzają się z tagiem (inaczej od razu przerywa),
    potem woła `build-windows-installer.yml`/`build-linux-package.yml` jako reużywalne
    workflowy i tworzy **szkic** (draft) Release'a na GitHubie z dołączonymi obydwoma
    artefaktami i notatkami wyciągniętymi z pasującej sekcji `## [X.Y]` w `CHANGELOG.md`.
    Świadomie nigdy nie publikuje go automatycznie — ktoś musi przejrzeć szkic i kliknąć
    "Publish release".

### Model danych — elementy i struktura

Cztery typy elementów (`item_type`): **Folder** (czysty kontener), **Część**/**Złożenie**
(mają numer z globalnej sekwencji, status, rewizję i właściciela), **Inny plik** (dowolny
plik bez struktury pod sobą). Struktura drzewa/BOM-u to osobna tabela `item_relations`
(`parent_id`, `child_id`, `quantity`, `position`) — pozwala tej samej Części/Złożeniu być
współdzielonym komponentem w wielu złożeniach/projektach jednocześnie.

Co wolno dodać pod czym (wymuszane i backendowo, i we froncie):

| Rodzic | Dozwolone dzieci |
|---|---|
| Projekt / Folder | wszystko (Folder, Część, Złożenie, Plik) |
| Złożenie | tylko Część i Złożenie (BOM) |
| Część / Plik | nic — to liście struktury |

Usuwanie elementu ma dwa tryby: **„Usuń ze struktury”** (odpina relację / chowa korzeń,
rekord zostaje) i **„Usuń całkowicie”** (rekurencyjne, ale bezpieczne dla współdzielonych
komponentów — element z rodzicem poza usuwanym poddrzewem nie znika; tylko administrator).
Część/Złożenie da się też **zduplikować** (kopia dostaje nowy numer, świeży status i
właściciela) — z poziomu drzewka kopia ląduje zaraz pod oryginałem.

Część ma cztery **rodzaje** (`properties.rodzaj`), każdy z innym zestawem pól i inną ikoną
w drzewku: **Wykonywana** (Materiał, Cena, Dodatkowe informacje), **Zakupowa** (Producent,
Numer zamówieniowy 1/2, Masa, Cena, Dodatkowe informacje), **Normalia** (Materiał, Norma,
Dodatkowe informacje), **Klienta** (bez dodatkowych pól poza Dodatkowymi informacjami).
Złożenie nie ma rodzaju w ogóle — tylko opcjonalną Masę.

Część/Złożenie mają maszynę stanów: `w_pracy → sprawdzany → (w_pracy | wydany) → w_pracy`
(powrót z `wydany` podnosi numer rewizji, z opcjonalnym komentarzem do rewizji). Poza
statusem `w_pracy` edycja nazwy/właściwości jest zablokowana — wyjątek: cena/waluta/typ
ceny zawsze edytowalne. Na dole panelu właściwości Części/Złożenia pokazuje się
**Historia**: kiedy i kto utworzył element, każda zmiana statusu (kiedy/kto/z-na), każda
rewizja z komentarzem (kiedy/kto/opis), każde dodanie/usunięcie załącznika
(kiedy/kto/nazwa pliku) i każde zablokowanie/zwolnienie właściciela (kiedy/kto), połączone
w jedną chronologiczną listę.

**Właściciel i blokada** (`owner_id`/`owner_locked`) — niezależne od statusu. Twórca
Części/Złożenia staje się od razu jej właścicielem i element jest zablokowany: dopóki trwa
blokada, tylko właściciel może go edytować (właściwości, nazwa, widoczność, przeniesienie
do innego projektu, załączniki, struktura BOM pod nim) — **nawet administrator jej nie
omija**. Każdy może zablokować zwolniony element, stając się jego nowym właścicielem;
zwolnienie może wykonać tylko aktualny właściciel — **z wyjątkiem administratora, który
może też przejąć (`POST /lock`) albo wymusić zwolnienie (`POST /release`) cudzej blokady,
oraz zmienić status zablokowanego elementu (`PATCH /status`) niezależnie od tego, kto jest
właścicielem** — na wypadek np. nieobecności pracownika. Element w statusie `wydany`
zawsze jest zwolniony i bez właściciela — nie da się go zablokować. W drzewku pokazuje to
ikona kłódki: zielona (zablokowane przez Ciebie), żółta (przez kogoś innego), otwarta
(zwolnione).

BOM złożenia pokazuje: L.p. (edytowalne wpisaniem liczby całkowitej — musi być unikalna
w tym BOM-ie — albo przeciągnięciem wiersza), Nazwa, Ilość, Materiał, Producent, Numer
zamówieniowy 1/2 (brakujące pola jako „-”), razem z zagłębionymi elementami (części
zagnieżdżonych złożeń, L.p. w formie `2.1`). Eksport do CSV w dwóch wariantach: pełny
(każde wystąpienie osobno) i zsumowany (ten sam komponent użyty kilka razy w różnych
miejscach — jeden wiersz z łączną, rozwiniętą przez cały łańcuch ilością).

Załączniki (`item_attachments`) to osobny mechanizm od struktury — dowolny plik (np. CAD)
można dopiąć do Części/Złożenia/Pliku z panelu właściwości; nie da się ich dodać ani usunąć
przez drzewko po lewej. Z poziomu Projektu/Złożenia/Części da się pobrać **dokumentację** —
ZIP zebrany ze wszystkich załączników w danym zakresie (cały projekt albo dane
Złożenie/Część razem z poddrzewem), z wyborem, które rozszerzenia plików uwzględnić.

Numer elementu (`item_number`) pochodzi z jednej, globalnej sekwencji PostgreSQL —
usunięcie elementu NIE zwalnia jego numeru automatycznie (standardowe zachowanie
sekwencji). Administrator może ręcznie cofnąć sekwencję do wskazanego numeru (Ustawienia
→ Numeracja) — działa tylko, gdy żaden istniejący element nie ma już takiego numeru lub
wyższego, więc pozwala odzyskać "ogon" numeracji po usuniętych elementach testowych bez
ryzyka kolizji.

### Logowanie, role i dostęp do projektów

Każde żądanie do `/api/*` (poza `/api/auth/login`) wymaga zalogowania — sesja to losowy
token w ciasteczku httpOnly (`pdm_session`, 30 dni ważności), zapisany w tabeli `sessions`.
Hasła trzymane jako PBKDF2 (własna implementacja w `PasswordHasher.cs`, tylko
`System.Security.Cryptography` — bez dodatkowych pakietów NuGet).

Dwie role (`users.role`): **administrator** (pełny dostęp, widzi wszystkie projekty) i
**użytkownik** (dostęp tylko do przypisanych mu projektów — `project_users`, zarządzane w
Ustawienia → Użytkownicy; nieprzypisany projekt jest dla niego niewidoczny na liście i bez
struktury). Zwykły użytkownik może odpinać elementy ze struktury, ale nie usuwać ich
całkowicie z bazy ani zarządzać kontami. System pilnuje, żeby zawsze zostawał co najmniej
jeden administrator (nie da się usunąć ani zdegradować ostatniego). Ustawienia Języka i
Wyglądu są dostępne dla każdego; Użytkownicy, Magazyn plików i Logi tylko dla administratora.

Jeśli tabela `users` jest pusta przy starcie API, samo zakłada domyślne konto
**`admin` / `admin`** (patrz konsola przy pierwszym uruchomieniu) — zmień to hasło od razu
po zalogowaniu (`PATCH /api/auth/password`, albo z poziomu aplikacji webowej).

### Endpointy API

| Metoda | Ścieżka | Co robi |
|---|---|---|
| POST | `/api/auth/login` \| `/logout` | logowanie / wylogowanie — login to jedyny endpoint bez wymaganej sesji |
| GET/PATCH | `/api/auth/me` \| `/password` | dane zalogowanego użytkownika / zmiana WŁASNEGO hasła |
| GET | `/api/auth/browser-login` | most token→ciasteczko dla makr CAD (otwiera przeglądarkę już zalogowaną) |
| GET/POST/PATCH/DELETE | `/api/users[/{id}]` | zarządzanie kontami — **tylko administrator** |
| GET/POST/PATCH/DELETE | `/api/projects[/{id}]` | lista/tworzenie/edycja/usunięcie projektu (zapis — tylko administrator; lista filtrowana wg dostępu) |
| GET/POST/DELETE | `/api/project-users`, `/api/projects/{projectId}/users/{userId}` | zarządzanie przypisaniami użytkowników do projektów — **tylko administrator** |
| GET | `/api/items?search=&tag=&projectId=` | lista elementów z filtrami (filtrowana wg dostępu do projektu) |
| GET | `/api/items/{id}` | szczegóły elementu |
| POST | `/api/projects/{projectId}/nodes` | tworzy Folder/Część/Złożenie/Plik bez uploadu (opcjonalnie z ticketem dla makra CAD) |
| POST | `/api/projects/{projectId}/items` | **multipart/form-data**: upload pliku (opcjonalnie `parentId`) |
| GET | `/api/items/{id}/file` | pobranie wgranego pliku |
| POST | `/api/items/{id}/duplicate` | duplikuje Część/Złożenie (nowy numer, status, właściciel) |
| PATCH | `/api/items/{id}/name` \| `/visibility` \| `/status` \| `/project` | zmiana nazwy / widoczności w drzewku / statusu / przeniesienie do innego projektu |
| POST | `/api/items/{id}/lock` \| `/release` | zablokowanie (przejęcie na własność) / zwolnienie elementu |
| DELETE | `/api/items/{id}` | usunięcie całkowite (rekurencyjne, bezpieczne dla współdzielonych elementów) — **tylko administrator** |
| GET | `/api/projects/{projectId}/relations` | relacje rodzic-dziecko (struktura/BOM) danego projektu |
| POST/DELETE | `/api/items/{parentId}/children[/{childId}]` | dodanie/odpięcie podelementu |
| PATCH | `/api/items/{parentId}/children/{childId}/position` \| `/reorder` | zmiana L.p. w BOM-ie (pojedyncza pozycja albo cała nowa kolejność) |
| PATCH | `/api/projects/{projectId}/roots/reorder` | zmiana kolejności korzeni drzewka projektu |
| GET | `/api/items/{id}/bom` \| `/bom/csv` \| `/bom/aggregated-csv` | zagłębiony BOM (JSON) / eksport CSV (pełny / zsumowany) |
| GET | `/api/items/{id}/documentation/extensions`, `/documentation` | rozszerzenia plików dostępne do pobrania / ZIP z załącznikami (element + poddrzewo) |
| GET | `/api/projects/{projectId}/documentation/extensions`, `/documentation` | to samo, dla całego projektu |
| GET | `/api/tags` | lista tagów |
| POST/DELETE | `/api/items/{id}/tags[/{tagName}]` | zarządzanie tagami |
| PATCH/DELETE | `/api/items/{id}/properties[/{key}]` | zarządzanie właściwościami (zablokowane poza statusem `w_pracy` i poza blokadą właściciela — wyjątek: pola ceny) |
| GET | `/api/items/{id}/revisions` | historia komentarzy rewizji (tylko rewizje z komentarzem) |
| GET | `/api/items/{id}/history` | pełna historia: utworzenie, zmiany statusu, rewizje, dodanie/usunięcie załącznika, blokada/zwolnienie właściciela (kiedy/kto/opis), chronologicznie |
| GET/POST/PATCH/DELETE | `/api/materials[/{id}]` | katalog materiałów (nazwa + grupa/podgrupa) |
| GET/POST/PATCH/DELETE | `/api/manufacturers[/{id}]`, `/api/manufacturers/{id}/contacts[/{contactId}]` | katalog producentów + osoby kontaktowe |
| GET/POST/DELETE | `/api/items/{itemId}/attachments[/{id}]`, `/register`, `/api/attachments/{id}/file` | załączniki (upload/rejestracja istniejącego pliku/lista/pobranie/usunięcie) |
| GET/POST/DELETE | `/api/saved-filters[/{id}]` | zapisane zestawy filtrów widoku „Cała baza” (prywatne per użytkownik) |
| GET/POST | `/api/create-tickets/{ticket}`, `/attach-existing` | korelacja makro CAD ↔ przeglądarka (zob. `EasyPDM.FreeCad/README.md`) |
| GET | `/api/config` | lokalizacja magazynu plików (do użytku np. przez makro FreeCAD) |
| GET/POST | `/api/settings/storage`, `/storage/move`, `/backup`, `/restore` | lokalizacja/statystyki magazynu, przeniesienie, backup (pg_dump + pliki w ZIP), przywrócenie z backupu — **tylko administrator** |
| GET/PATCH | `/api/settings/backup-schedule` | harmonogram automatycznej kopii zapasowej (włącz/wyłącz, częstotliwość, dzień, godzina, liczba przechowywanych kopii) — **tylko administrator** |
| GET/PATCH | `/api/settings/item-number-prefixes[/{rodzaj}]` | prefiksy-litery numeru elementu per rodzaj — **tylko administrator** |
| GET/POST | `/api/settings/item-number-sequence`, `/reset` | podgląd/cofnięcie sekwencji numerów elementów — **tylko administrator** |
| GET | `/api/settings/logs`, `/logs/{date}`, `/logs/{date}/download` | lista dni z zapisanym logiem, ostatnie N wierszy z danego dnia, pobranie pełnego pliku — **tylko administrator** |

## Jak uruchomić

Backend czyta prawdziwe dane dostępowe (hasło do bazy, ścieżka magazynu) z
`EasyPDM.Api/appsettings.Local.json` — **plik NIE jest w repozytorium** (gitignored, bo
zawiera hasło), więc na nowym klonie trzeba go założyć z wzoru:

```bash
cp EasyPDM.Api/appsettings.Local.json.example EasyPDM.Api/appsettings.Local.json
# ...i wpisać tam prawdziwe ConnectionString/StorageRoot dla tej maszyny.
```

Program **sam stosuje nowe migracje bazy przy każdym starcie** (wbudowane w plik
wykonywalny jako embedded resources, śledzone w tabeli `schema_migrations` — zob.
`MigrationRunner.cs`) — więc na już istniejącej, znanej bazie wystarczy zwyczajnie ją
uruchomić, bez ręcznego dogania `db/migrations/`. Jedyny przypadek, kiedy trzeba coś zrobić
ręcznie, to zupełnie **świeży, pusty** PostgreSQL — wtedy najpierw:

```bash
# Jeśli nie istnieje jeszcze rola/baza (świeży PostgreSQL):
sudo -u postgres psql -c "CREATE ROLE pdm_user LOGIN PASSWORD 'twoje-haslo';"
sudo -u postgres createdb -O pdm_user pdm

# ...i podstawowy schemat (od tego miejsca program dogania resztę sam):
psql -h localhost -U pdm_user -d pdm -f db/schema.sql

# Backend (serwuje też zbudowany frontend z wwwroot/)
cd EasyPDM.Api
dotnet restore && dotnet build && dotnet run
```

Frontend — do pracy nad UI z podglądem na żywo (proxy `/api` → `http://localhost:5000`):

```bash
cd EasyPDM.Web
npm install
npm run dev      # http://localhost:5173
```

Do wdrożenia: `npm run build` w `EasyPDM.Web/` nadpisuje `EasyPDM.Api/wwwroot/` —
`dotnet run` serwuje wynik pod `http://localhost:5000` bez dodatkowej konfiguracji.

### Docker (zalecane do wdrożenia serwerowego)

**Najprościej**: `./install-easypdm-docker.sh` — zakłada `.env` (generuje losowe hasło do
bazy, jeśli nie podasz własnego), sam wybiera WOLNY port hosta (próbuje od 5000 wzwyż —
przydatne na serwerze, gdzie inne usługi mogą już coś tam trzymać, co w praktyce jest częstym
przypadkiem), buduje i uruchamia kontenery. Uruchom ten sam skrypt ponownie po `git pull`,
żeby zaktualizować — wykrywa istniejący `.env` i niczego w nim nie nadpisuje.

Albo ręcznie:

```bash
cp .env.example .env      # ustaw prawdziwe PDM_DB_PASSWORD
docker compose up -d --build
```

Uruchamia dwa kontenery: `postgres` (obraz `postgres:18`, dane na wolumenie `pgdata`, schemat
z `db/schema.sql` zakładany automatycznie przy pustym wolumenie) i `api` (budowany z
`Dockerfile` w korzeniu repo — buduje frontend, publikuje backend, doinstalowuje
`postgresql-client-18` dla funkcji backup/restore w Ustawieniach). Magazyn plików,
automatyczne kopie zapasowe i logi trzymane są na wolumenie `pdm-data` (`/data` w
kontenerze) — przetrwają przebudowanie obrazu przy aktualizacji. Po starcie:
`http://localhost:5000`. Jeśli port 5000 jest już zajęty na tej maszynie, ustaw
`PDM_HOST_PORT=inny_port` w `.env` (NIE przez `docker-compose.override.yml` — Compose
DOKLEJA listy jak `ports` między plikami zamiast je zastępować, więc override z innym
portem i tak próbowałby zbindować oba naraz i padłby na tym zajętym).

**Aktualizacja**: `git pull && docker compose up -d --build` — nowy obraz `api` dostaje nowy
kod, kontener się odtwarza, a program **sam stosuje nowe migracje bazy przy starcie**
(wbudowane w plik wykonywalny, śledzone w tabeli `schema_migrations` — zob.
`MigrationRunner.cs`) — nic więcej nie trzeba robić ręcznie. `docker-entrypoint-initdb.d`
z `schema.sql` odpala się TYLKO przy pierwszym, zupełnie pustym starcie wolumenu `pgdata`
(świeża instalacja); przy aktualizacji nie jest w ogóle dotykany, bo wolumen już istnieje.

#### Wdrożenie BEZ klonowania repo (tylko gotowy obraz)

Dwa workflowy publikują gotowe obrazy do GitHub Container Registry —
`ghcr.io/pawelcel/easypdm-api` i `ghcr.io/pawelcel/easypdm-postgres` (ten drugi to zwykły
`postgres:18` z wbudowanym `db/schema.sql` — bez tego świeża baza zostałaby pusta, bo
`MigrationRunner.cs` świadomie nie tworzy sam podstawowego schematu):

- `publish-docker-image.yml` — przy każdym pushu na `main` dotykającym kodu serwera,
  taguje oba obrazy jako `:edge` (+ SHA commita). Do sprawdzenia najnowszego stanu `main`
  przed wydaniem (`docker pull ghcr.io/pawelcel/easypdm-api:edge`) — nigdy nie rusza
  `:latest`.
- `publish-docker-release.yml` — tylko przy wypchnięciu taga wersji (`v0.1.2`, zgodnego
  z `EasyPDM.Web/src/version.ts` i `MyAppVersion` w `packaging/windows/EasyPDM.iss`),
  taguje oba obrazy jako `:latest` ORAZ `:v0.1.2`. To JEDYNY workflow ruszający
  `:latest` — więc `docker-compose.yml` (który ściąga `:latest`) zawsze dostaje
  świadomie wydaną wersję, nigdy przypadkowy commit z `main`. Żeby wydać nową wersję:
  ```bash
  git tag v0.1.2
  git push origin v0.1.2
  ```

Więc do samego wdrożenia NIE trzeba klonować całego repo (ze wszystkimi makrami CAD/
instalatorami/testami, których serwer w ogóle nie potrzebuje). Wystarczą dwa pliki:

```bash
mkdir easypdm-deploy && cd easypdm-deploy
curl -O https://raw.githubusercontent.com/pawelcel/EasyPDM/main/docker-compose.yml
curl -O https://raw.githubusercontent.com/pawelcel/EasyPDM/main/.env.example
cp .env.example .env      # ustaw prawdziwe PDM_DB_PASSWORD
docker compose pull
docker compose up -d
```

> Dopóki repo (i pakiet w GHCR) jest prywatne, `curl` powyżej i `docker compose pull`
> wymagają uwierzytelnienia — `curl` z nagłówkiem `Authorization: Bearer <token>`, a przed
> `docker compose pull` dodatkowo `docker login ghcr.io -u <login> -p <token>` (token z
> uprawnieniem `read:packages`). Po upublicznieniu repo/obrazu żadne logowanie nie będzie
> już potrzebne.
>
> **Jednorazowo, po pierwszej publikacji**: KAŻDY pakiet w GHCR domyślnie jest PRYWATNY
> niezależnie od widoczności samego repo — trzeba je raz ręcznie przełączyć na publiczne,
> OBA (GitHub → zakładka **Packages** przy repo → `easypdm-api` / `easypdm-postgres` →
> **Package settings** → **Change visibility**), inaczej `docker compose pull` bez
> wcześniejszego `docker login` dostanie 403/404 nawet na publicznym repo.

**Aktualizacja** tą ścieżką: `docker compose pull && docker compose up -d` — bez `git pull`
(nie ma czego pullować, nie masz tu repo), po prostu ściąga to, na co aktualnie wskazuje
`:latest` — czyli najnowsze WYDANE wydanie, niekoniecznie najnowszy commit na `main`.

### Linux — instalacja natywna jako usługa systemd (bez Dockera)

```bash
sudo ./install-easypdm-linux.sh
```

Jeden skrypt: instaluje PostgreSQL, jeśli go jeszcze nie ma (rozpoznaje `pacman`/`apt`/`dnf`
— na Arch/CachyOS dodatkowo sam inicjalizuje klaster, bo tamtejszy pakiet, w odróżnieniu od
Debiana/Fedory, nie robi tego automatycznie), zakłada rolę i bazę `pdm` (generuje losowe
hasło, jeśli nie podasz własnego przez `PDM_DB_PASSWORD=... sudo -E ./install-easypdm-linux.sh`),
buduje frontend i publikuje backend jako **self-contained pojedynczy plik wykonywalny**
(`dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true` — gotowa usługa
NIE wymaga już zainstalowanego .NET-a, tylko sam czas budowy), zakłada dedykowane,
nieuprzywilejowane konto systemowe `easypdm`, i instaluje usługę systemd
(`easypdm.service`, autostart, `ProtectSystem=strict` + `ReadWritePaths` ograniczone do
`/var/lib/easypdm` — usługa nie może pisać nigdzie indziej w systemie). Po instalacji:
`http://localhost:5000`, status przez `systemctl status easypdm`, logi na żywo przez
`journalctl -u easypdm -f` (niezależnie od własnego dziennika aplikacji w Ustawienia ->
Logi). Odinstalowanie: `sudo ./uninstall-easypdm-linux.sh` (celowo NIE rusza samej bazy danych ani
PostgreSQL — o tym decyduje się ręcznie, żeby nie skasować danych przez pomyłkę).

**Aktualizacja**: `git pull`, potem `sudo ./install-easypdm-linux.sh` ponownie — wykrywa istniejącą
bazę/konto (pomija ich zakładanie), przebudowuje i podmienia tylko aplikację, jawnie
**restartuje usługę** (`systemctl restart`, nie tylko `enable --now`, które na już
uruchomionej usłudze nic by nie zrobiło). Nowe migracje bazy program stosuje sam
automatycznie przy starcie — nic dodatkowego nie trzeba robić ręcznie.

> Skrypt buduje ze źródeł tego repozytorium (jak `run.sh`, tylko jako trwała usługa
> zamiast procesu na pierwszym planie) — nie ma (jeszcze) osobnego, gotowego wydania
> binarnego do pobrania. Sam self-contained publikowany plik wykonywalny był realnie
> uruchomiony i sprawdzony (serwuje frontend, loguje), a treść jednostki systemd
> zweryfikowana przez `systemd-analyze verify`; pełny przebieg skryptu (tworzenie
> roli/bazy/konta systemowego przez `sudo`) nie był jeszcze wykonany end-to-end — przy
> pierwszym uruchomieniu obserwuj wyjście i zgłoś, jeśli coś nie zagra.

### Windows — instalator (`.exe`, Inno Setup)

**Najprościej: `.github/workflows/build-windows-installer.yml`** buduje gotowy
`EasyPDM_Windows_v<wersja>.exe` (numer wersji z `MyAppVersion`/`OutputBaseFilename` w
`packaging/windows/EasyPDM.iss`) automatycznie na windowsowym runnerze GitHuba (ma Inno
Setup Compiler fabrycznie) przy każdym pushu dotykającym backendu/frontendu/instalatora —
nie trzeba mieć Windows ani Inno Setup lokalnie. Uruchom ręcznie przez `gh workflow run
build-windows-installer.yml`, poczekaj (`gh run watch`), pobierz artefakt (`gh run download
<id> -n EasyPDM_Windows_v<wersja>`).

Alternatywnie, do zbudowania lokalnie na maszynie z Windows (.NET 10 SDK + Node.js +
[Inno Setup Compiler](https://jrsoftware.org/isinfo.php)):

```powershell
powershell -ExecutionPolicy Bypass -File packaging\windows\build.ps1
iscc packaging\windows\EasyPDM.iss
```

Powstaje `packaging\windows\Output\EasyPDM_Windows_v<wersja>.exe`. Instalator: sprawdza, czy
PostgreSQL jest już zainstalowany (jeśli nie — kieruje na stronę pobierania i przerywa,
świadomie NIE próbuje cicho doinstalować kilkusetmegabajtowego instalatora PostgreSQL w
tle), pyta o hasło superużytkownika `postgres` (jednorazowo, do założenia własnej roli
`pdm_user` i bazy `pdm` — samo hasło nigdzie nie jest zapisywane), zakłada schemat, zapisuje
`appsettings.Production.json` z resztą ustawień (magazyn/kopie/logi w
`%ProgramData%\EasyPDM`), rejestruje `EasyPDM.Api.exe` jako **usługę Windows**
(autostart, działa w tle bez okna konsoli) i tworzy skrót otwierający
`http://localhost:5000`. Odinstalowanie zatrzymuje i usuwa usługę (standardowy deinstalator
Inno Setup) — tak samo jak na Linuksie, celowo nie rusza samej bazy danych.

**Aktualizacja**: zbuduj nowy `EasyPDM_Windows_v<wersja>.exe` (jak wyżej) i uruchom go ponownie —
`PrepareToInstall` w skrypcie `.iss` zatrzymuje usługę PRZED podmianą plików (inaczej
Windows zablokowałby nadpisanie działającego `.exe`), instalator wykrywa istniejącą
rolę/bazę (pomija zakładanie schematu) i istniejącą usługę (uruchamia ją z powrotem zamiast
rejestrować od nowa). Nowe migracje bazy program stosuje sam automatycznie przy starcie.

> Skrypt `.iss` faktycznie się kompiluje (zweryfikowane prawdziwym Inno Setup Compilerem w
> CI, nie tylko przeglądem kodu) — po drodze złapane i poprawione 5 realnych błędów
> specyficznych dla dialektu Pascal Script Inno Setup (m.in. brak lokalnych sekcji `const`
> w funkcjach, `LoadStringFromFile` wymagające `AnsiString`, brak `Randomize`/`RandSeed`/
> `GetTickCount` — nie ma żadnego udokumentowanego sposobu na ręczne zasianie wbudowanego
> `Random`, więc korzysta z niego wprost). Sama instalacja end-to-end na żywej maszynie z
> PostgreSQL nie była jeszcze ręcznie przetestowana — przy pierwszym uruchomieniu obserwuj
> przebieg i zgłoś, co nie zagra.

Pierwsze logowanie: **`admin` / `admin`** (konto zakładane automatycznie, jeśli tabela
`users` jest pusta — zob. "Logowanie, role i dostęp do projektów" wyżej). Zmień hasło od
razu po zalogowaniu.

## Znane ograniczenia

1. **Brak walidacji rozmiaru/typu wgrywanego pliku i załącznika** — każdy plik przejdzie,
   niezależnie od rozszerzenia czy wielkości.
2. **Magazyn plików (`storage/`) to zwykły folder na dysku serwera.** Backup/restore z
   poziomu Ustawień pakuje `pg_dump` bazy razem z magazynem plików w jeden ZIP; można go
   pobrać ręcznie albo włączyć automatyczną kopię (Ustawienia -> Magazyn plików ->
   Automatyczna kopia zapasowa) z wyborem częstotliwości (codziennie/co tydzień/co miesiąc)
   oraz dnia i godziny — sprawdzane co minutę przez `ScheduledBackupService` w tle, zapisywane
   do osobnego katalogu `backups/` (niezależnego od `storage/`, żeby kopia nie pakowała samej
   siebie), z konfigurowalną liczbą przechowywanych ostatnich kopii (domyślnie 14 — starsze
   są automatycznie kasowane). Wersjonowanie pliku przy zmianie
   rewizji działa dziś tylko w przepływie makra FreeCAD (`storage/components/`, jeden plik na
   rewizję, zob. `EasyPDM.FreeCad/README.md`) — zwykłe załączniki dodawane z aplikacji
   webowej nie mają automatycznego powiązania z numerem rewizji.
3. **Nie każda operacja zapisuje "kto to zrobił"** — utworzenie elementu (`created_by`),
   zmiana statusu, komentarz do rewizji, dodanie/usunięcie załącznika i blokada/zwolnienie
   właściciela już to robią (widać w „Historii"), ale np. zmiana właściwości/nazwy/tagów
   nie zapisuje autora.
4. **W Dockerze „Zmień lokalizację” magazynu plików (Ustawienia -> Magazyn plików) nie
   przetrwa przebudowania obrazu** — ta operacja zapisuje nową ścieżkę do
   `appsettings.json` wewnątrz kontenera `api` (poza wolumenem `pdm-data`), więc po
   `docker compose up --build` wraca do wartości ze zmiennej środowiskowej `StorageRoot`
   ustawionej w `Dockerfile`. Sama zmiana lokalizacji API działa poprawnie w trakcie życia
   kontenera — problem dotyczy tylko trwałości tego ustawienia między przebudowaniami.

## Następne kroki (proponowana kolejność)

1. Walidacja uploadu (typ/rozmiar) dla elementów i załączników.
2. Zapisywanie autora zmiany właściwości/nazwy/tagów (punkt 3 wyżej).
