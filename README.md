# PdmSystem — system PDM dla plików CAD

## Status

Ręczne tworzenie projektów i elementów przez aplikację webową (upload pliku wprost do
magazynu API) albo przez makro FreeCAD (`PdmSystem.FreeCad/`), które woła to samo API.
Wcześniejsze podejście ze skanowaniem dysku (`PdmSystem.Core`, `PdmSystem.Indexer`) zostało
usunięte z repo — było niezgodne ze schematem od migracji `002` i nigdy nieużywane przez
`Api`.

Frontend to osobna aplikacja **React 19 + Vite + TypeScript** (`PdmSystem.Web/`) budowana
wprost do `PdmSystem.Api/wwwroot/`. Interfejs jest w pełni przetłumaczony (polski/angielski/
niemiecki) i ma tryb jasny/ciemny. Przetestowane na żywo: CachyOS, .NET 10, PostgreSQL 18.

## Co tu jest

- **`db/schema.sql`** — pełny schemat od zera (aktualny stan po wszystkich migracjach).
- **`db/migrations/`** — migracje `002`–`026` dla już istniejącej bazy: projekty, typy
  elementów, widoczność w drzewku, status/rewizje, materiały (+ grupy/podgrupy), załączniki,
  kolejność BOM, komentarze do rewizji, logowanie i role, właściwości projektu, kaskadowe
  usuwanie, kolejność korzeni drzewka, producenci, zapisane filtry, dostęp do projektów per
  użytkownik, właściciel/blokada elementu, usunięcie martwego schematu rewizji/checkout,
  historia (status/rewizje/załączniki/blokada), harmonogram automatycznej kopii zapasowej.
- **`PdmSystem.Api/`** — ASP.NET Core (minimal API, Npgsql bez ORM), endpointy podzielone
  po funkcjach w `Endpoints/` — pełna lista niżej w "Endpointy API". Serwuje też zbudowany
  frontend ze swojego `wwwroot/`. Własny `FileLoggerProvider` (bez dodatkowego pakietu NuGet)
  zapisuje logi programu do `logs/` (rotacja dzienna, 30 dni retencji), widoczne w
  Ustawienia → Logi.
- **`PdmSystem.Web/`** — frontend: React 19 + Vite + TypeScript + Tailwind v4 + shadcn/ui
  (komponenty na bazie Base UI, styl „base-nova”), i18n (pl/en/de), motyw jasny/ciemny.
- **`PdmSystem.FreeCad/`** — makro `PdmUpload.FCMacro`: uruchamiane z poziomu FreeCAD,
  zapisuje aktywny dokument, pyta o dane (projekt, typ, rodzaj, materiał/producent/numery
  zamówieniowe...), tworzy Część/Złożenie w PDM, dogrywa plik jako załącznik i zmienia
  nazwę lokalnego pliku na `numer (nazwa)`. Szczegóły w `PdmSystem.FreeCad/README.md`.

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
blokada, tylko właściciel może go edytować (właściwości, nazwa, status, widoczność,
przeniesienie do innego projektu, załączniki, struktura BOM pod nim) — **nawet
administrator jej nie omija**. Każdy może zablokować zwolniony element, stając się jego
nowym właścicielem; zwolnienie może wykonać tylko aktualny właściciel. Element w statusie
`wydany` zawsze jest zwolniony i bez właściciela — nie da się go zablokować. W drzewku
pokazuje to ikona kłódki: zielona (zablokowane przez Ciebie), żółta (przez kogoś innego),
otwarta (zwolnione).

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
| GET/POST/PATCH/DELETE | `/api/users[/{id}]` | zarządzanie kontami — **tylko administrator** |
| GET/POST/PATCH/DELETE | `/api/projects[/{id}]` | lista/tworzenie/edycja/usunięcie projektu (zapis — tylko administrator; lista filtrowana wg dostępu) |
| GET/POST/DELETE | `/api/project-users`, `/api/projects/{projectId}/users/{userId}` | zarządzanie przypisaniami użytkowników do projektów — **tylko administrator** |
| GET | `/api/items?search=&tag=&projectId=` | lista elementów z filtrami (filtrowana wg dostępu do projektu) |
| GET | `/api/items/{id}` | szczegóły elementu |
| POST | `/api/projects/{projectId}/nodes` | tworzy Folder/Część/Złożenie/Plik bez uploadu |
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
| GET | `/api/config` | lokalizacja magazynu plików (do użytku np. przez makro FreeCAD) |
| GET/POST | `/api/settings/storage`, `/storage/move`, `/backup`, `/restore` | lokalizacja/statystyki magazynu, przeniesienie, backup (pg_dump + pliki w ZIP), przywrócenie z backupu — **tylko administrator** |
| GET/PATCH | `/api/settings/backup-schedule` | harmonogram automatycznej kopii zapasowej (włącz/wyłącz, częstotliwość, dzień, godzina, liczba przechowywanych kopii) — **tylko administrator** |
| GET | `/api/settings/logs`, `/logs/{date}`, `/logs/{date}/download` | lista dni z zapisanym logiem, ostatnie N wierszy z danego dnia, pobranie pełnego pliku — **tylko administrator** |

## Jak uruchomić

```bash
# Jeśli masz już bazę z poprzedniej wersji, dogoń wszystkie migracje po kolei:
for f in db/migrations/*.sql; do psql -h localhost -U pdm_user -d pdm -f "$f"; done

# Jeśli stawiasz od zera:
psql -h localhost -U pdm_user -d pdm -f db/schema.sql

# Backend (serwuje też zbudowany frontend z wwwroot/)
cd PdmSystem.Api
dotnet restore && dotnet build && dotnet run
```

Frontend — do pracy nad UI z podglądem na żywo (proxy `/api` → `http://localhost:5000`):

```bash
cd PdmSystem.Web
npm install
npm run dev      # http://localhost:5173
```

Do wdrożenia: `npm run build` w `PdmSystem.Web/` nadpisuje `PdmSystem.Api/wwwroot/` —
`dotnet run` serwuje wynik pod `http://localhost:5000` bez dodatkowej konfiguracji.

Pierwsze logowanie: **`admin` / `admin`** (konto zakładane automatycznie, jeśli tabela
`users` jest pusta — zob. "Logowanie, role i dostęp do projektów" wyżej). Zmień hasło od
razu po zalogowaniu.

Workflow w przeglądarce: ekran logowania → strona startowa **„Witaj”** → **Projekty**
(wybierz/utwórz projekt → buduj strukturę drzewa) albo **Cała baza** (przeszukaj wszystkie
elementy niezależnie od projektu, z zapisywalnymi filtrami) albo **Materiały**/
**Producenci** (katalogi) albo **Ustawienia** (Język i Wygląd dla każdego; Użytkownicy i
Magazyn plików tylko dla administratora).

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
   rewizję, zob. `PdmSystem.FreeCad/README.md`) — zwykłe załączniki dodawane z aplikacji
   webowej nie mają automatycznego powiązania z numerem rewizji.
3. **Nie każda operacja zapisuje “kto to zrobił”** — utworzenie elementu (`created_by`),
   zmiana statusu, komentarz do rewizji, dodanie/usunięcie załącznika i blokada/zwolnienie
   właściciela już to robią (widać w „Historii"), ale np. zmiana właściwości/nazwy/tagów
   nie zapisuje autora.

## Następne kroki (proponowana kolejność)

1. Walidacja uploadu (typ/rozmiar) dla elementów i załączników.
2. Zapisywanie autora zmiany właściwości/nazwy/tagów (punkt 3 wyżej).
