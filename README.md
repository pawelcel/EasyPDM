# PdmSystem — system PDM dla plików CAD

## Status (aktualny kierunek)

Zmiana podejścia względem pierwszej wersji: **żadnego automatycznego skanowania dysku na
razie**. Zamiast tego — ręczne tworzenie projektów i elementów przez aplikację webową,
z uploadem pliku wprost do magazynu API. Skanowanie (`PdmSystem.Core`, `PdmSystem.Indexer`)
zostaje w repo nietknięte, ale **wstrzymane** — wróci jako jedna z metod zasilania danych
(razem z przyszłymi wtyczkami do SolidWorks/FreeCAD), nie jedyna.

Frontend to teraz osobna aplikacja **React + Vite + TypeScript** (`PdmSystem.Web/`) budowana
wprost do `PdmSystem.Api/wwwroot/` — patrz "Frontend" niżej. Przetestowane na żywo: CachyOS,
.NET 10, PostgreSQL 18.

## Co tu jest

- **`db/schema.sql`** — pełny schemat od zera.
- **`db/migrations/`** — migracje `002`–`012` dla już istniejącej bazy: projekty, typy
  elementów (folder/część/złożenie/plik), widoczność w drzewku, status/rewizje, materiały
  (+ grupy), załączniki, kolejność BOM (`position`), komentarze do rewizji, logowanie i role.
- **`PdmSystem.Core/`** — wspólna biblioteka: adaptery CAD (`FreeCadAdapter`, zaparkowany
  `SolidWorksAdapter`), `FileScanner`, `ScanRunner`. **Obecnie nieużywana przez `Api`** —
  patrz "Znane ograniczenia" niżej.
- **`PdmSystem.Indexer/`** — konsolowy skaner dysku. Niezgodny ze schematem po migracji
  `002` (patrz "Znane ograniczenia").
- **`PdmSystem.Api/`** — ASP.NET Core (minimal API, Npgsql bez ORM), endpointy podzielone
  po funkcjach w `Endpoints/` (`ProjectEndpoints`, `ItemEndpoints`, `StructureEndpoints`,
  `PropertyEndpoints`, `TagEndpoints`, `MaterialEndpoints`, `AttachmentEndpoints`,
  `AuthEndpoints`, `UserEndpoints`, `ConfigEndpoints`). Serwuje też zbudowany frontend ze
  swojego `wwwroot/`.
- **`PdmSystem.Web/`** — frontend: React 19 + Vite + TypeScript + Tailwind v4 + shadcn/ui
  (komponenty na bazie Base UI, styl „base-nova”).
- **`PdmSystem.FreeCad/`** — makro `PdmUpload.FCMacro`: uruchamiane z poziomu FreeCAD,
  zapisuje aktywny dokument, pyta o dane (projekt, typ, rodzaj, materiał/producent/numery
  zamówieniowe...), tworzy Część/Złożenie w PDM, dogrywa plik jako załącznik i zmienia
  nazwę lokalnego pliku na `numer (nazwa)`. Szczegóły w `PdmSystem.FreeCad/README.md`.

### Model danych — elementy i struktura

Cztery typy elementów (`item_type`): **Folder** (czysty kontener), **Część**/**Złożenie**
(mają numer z globalnej sekwencji, status i rewizję), **Inny plik** (dowolny plik bez
struktury pod sobą). Struktura drzewa/BOM-u to osobna tabela `item_relations`
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
komponentów — element z rodzicem poza usuwanym poddrzewem nie znika).

Część/Złożenie mają maszynę stanów: `w_pracy → sprawdzany → (w_pracy | wydany) → w_pracy`
(powrót z `wydany` podnosi numer rewizji). Poza statusem `w_pracy` edycja
nazwy/właściwości jest zablokowana — wyjątek: cena/waluta/typ ceny zawsze edytowalne.

BOM złożenia pokazuje: L.p. (edytowalne wpisaniem liczby całkowitej — musi być unikalna
w tym BOM-ie — albo przeciągnięciem wiersza), Nazwa, Ilość, Materiał, Producent, Numer
zamówieniowy 1/2 (brakujące pola jako „-”).

Załączniki (`item_attachments`) to osobny mechanizm od struktury — dowolny plik (np. CAD)
można dopiąć do Części/Złożenia/Pliku z panelu właściwości; nie da się ich dodać ani
usunąć przez drzewko po lewej.

### Logowanie i role

Każde żądanie do `/api/*` (poza `/api/auth/login`) wymaga zalogowania — sesja to losowy
token w ciasteczku httpOnly (`pdm_session`, 30 dni ważności), zapisany w tabeli `sessions`.
Hasła trzymane jako PBKDF2 (własna, zależna wyłącznie od `System.Security.Cryptography`
implementacja w `PasswordHasher.cs` — bez dodatkowych pakietów NuGet).

Dwie role (`users.role`): **administrator** (pełny dostęp) i **użytkownik** (wszystko poza
zarządzaniem kontami i endpointem `DELETE /api/items/{id}` — może odpinać elementy ze
struktury, ale nie usuwać ich całkowicie z bazy). System pilnuje, żeby zawsze zostawał co
najmniej jeden administrator (nie da się usunąć ani zdegradować ostatniego).

Jeśli tabela `users` jest pusta przy starcie API, samo zakłada domyślne konto
**`admin` / `admin`** (patrz konsola przy pierwszym uruchomieniu) — zmień to hasło od razu
po zalogowaniu (`PATCH /api/auth/password`, albo z poziomu aplikacji webowej).

### Endpointy API

| Metoda | Ścieżka | Co robi |
|---|---|---|
| GET/POST | `/api/projects` | lista projektów / utworzenie projektu |
| GET | `/api/items?search=&tag=&projectId=` | lista elementów z filtrami |
| GET | `/api/items/{id}` | szczegóły elementu |
| POST | `/api/projects/{projectId}/nodes` | tworzy Folder/Część/Złożenie/Plik bez uploadu (kontener albo plik „na razie bez zawartości”) |
| POST | `/api/projects/{projectId}/items` | **multipart/form-data**: upload pliku (opcjonalnie `parentId`) |
| GET | `/api/items/{id}/file` | pobranie wgranego pliku |
| PATCH | `/api/items/{id}/name` \| `/visibility` \| `/status` \| `/project` | zmiana nazwy / widoczności w drzewku / statusu / przeniesienie do innego projektu |
| DELETE | `/api/items/{id}` | usunięcie całkowite (rekurencyjne, bezpieczne dla współdzielonych elementów) |
| GET | `/api/projects/{projectId}/relations` | relacje rodzic-dziecko (struktura/BOM) danego projektu |
| POST/DELETE | `/api/items/{parentId}/children[/{childId}]` | dodanie/odpięcie podelementu |
| PATCH | `/api/items/{parentId}/children/{childId}/position` \| `/reorder` | zmiana L.p. w BOM-ie (pojedyncza pozycja albo cała nowa kolejność) |
| GET | `/api/tags` | lista tagów |
| POST/DELETE | `/api/items/{id}/tags[/{tagName}]` | zarządzanie tagami |
| PATCH/DELETE | `/api/items/{id}/properties[/{key}]` | zarządzanie właściwościami (zablokowane poza statusem `w_pracy`, poza polami ceny) |
| GET/POST/DELETE | `/api/materials[/{name}]` | katalog materiałów do wyboru w Części (`name` + opcjonalna `group`) |
| GET/POST/DELETE | `/api/items/{itemId}/attachments[/{id}]`, `/api/attachments/{id}/file` | załączniki (upload/lista/pobranie/usunięcie) |
| GET | `/api/items/{id}/revisions` | historia komentarzy rewizji (tylko rewizje z komentarzem) |
| POST | `/api/auth/login` | logowanie (`{username, password}`) — jedyny endpoint bez wymaganej sesji |
| POST | `/api/auth/logout` | wylogowanie (kasuje sesję) |
| GET | `/api/auth/me` | dane aktualnie zalogowanego użytkownika |
| PATCH | `/api/auth/password` | zmiana WŁASNEGO hasła (wymaga podania obecnego) |
| GET/POST/PATCH/DELETE | `/api/users[/{id}]` | zarządzanie kontami — **tylko administrator** |

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
`users` jest pusta — zob. "Logowanie i role" wyżej). Zmień hasło od razu po zalogowaniu.

Workflow w przeglądarce: ekran logowania → strona startowa **„Witaj”** → **Projekty**
(wybierz/utwórz projekt → buduj strukturę drzewa) albo **Cała baza** (przeszukaj wszystkie
elementy niezależnie od projektu) albo **Lista materiałów** albo (tylko administrator)
**Użytkownicy**.

## Znane ograniczenia

1. **`PdmSystem.Indexer` jest niezgodny z obecnym schematem** — `items.project_id` jest
   `NOT NULL`, a `DatabaseWriter` w `PdmSystem.Core` nic o projektach (ani o typach
   elementów/BOM-ie) nie wie. To świadomie zostawione bez naprawy, dopóki nie zdecydujemy,
   jak skanowanie dysku ma mapować się na projekty i strukturę.
2. **Brak walidacji rozmiaru/typu wgrywanego pliku i załącznika** — każdy plik przejdzie,
   niezależnie od rozszerzenia czy wielkości.
3. **Magazyn plików (`storage/`) to zwykły folder na dysku serwera** — brak kopii
   zapasowych. Wersjonowanie pliku przy zmianie rewizji działa dziś tylko w przepływie
   makra FreeCAD (`storage/components/`, jeden plik na rewizję, zob.
   `PdmSystem.FreeCad/README.md`) — zwykłe załączniki dodawane z aplikacji webowej nie mają
   automatycznego powiązania z numerem rewizji.
4. **Brak logiki checkout/checkin** — schemat bazy to wspiera (`checked_out_by`,
   `checkout_history`), endpointy jeszcze nie.
5. **Logowanie/role są, ale nic jeszcze nie zapisuje "kto to zrobił"** — endpointy wiedzą,
   kto jest zalogowany (`HttpContext.Items["CurrentUser"]`), ale żadna operacja (dodanie
   elementu, zmiana statusu/właściwości, komentarz do rewizji...) nie zapisuje jeszcze
   autora do bazy.

## Następne kroki (proponowana kolejność)

1. Walidacja uploadu (typ/rozmiar) dla elementów i załączników.
2. Checkout/checkin i realne wersjonowanie pliku przy podmianie.
3. Docelowo: wtyczka do FreeCAD/SolidWorks, która woła API bezpośrednio przy zapisie
   pliku — naturalne rozszerzenie tego samego mechanizmu ręcznego uploadu, tylko
   zainicjowane programowo zamiast przez człowieka w przeglądarce.
