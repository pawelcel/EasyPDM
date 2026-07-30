# PdmSystem — system PDM dla plików CAD

## Status (aktualny kierunek)

Zmiana podejścia względem pierwszej wersji: **żadnego automatycznego skanowania dysku na
razie**. Zamiast tego — ręczne tworzenie projektów i elementów przez API/przeglądarkę,
z uploadem pliku wprost do magazynu API. Skanowanie (`PdmSystem.Core`, `PdmSystem.Indexer`)
zostaje w repo nietknięte, ale **wstrzymane** — wróci jako jedna z metod zasilania danych
(razem z przyszłymi wtyczkami do SolidWorks/FreeCAD), nie jedyna.

Przetestowane na żywo: CachyOS, .NET 10, PostgreSQL 18. `PdmSystem.Api` z projektami
jeszcze nie przechodził realnego testu — to następny krok.

## Co tu jest

- **`db/schema.sql`** — pełny schemat od zera (z tabelą `projects`).
- **`db/migrations/002_add_projects.sql`** — migracja dla już istniejącej bazy (masz taką,
  jeśli testowałeś wcześniejszą wersję ze skanerem) — dodaje `projects` i `items.project_id`
  bez utraty danych.
- **`PdmSystem.Core/`** — wspólna biblioteka: adaptery CAD (`FreeCadAdapter`, zaparkowany
  `SolidWorksAdapter`), `FileScanner`, `ScanRunner`. **Obecnie nieużywana przez `Api`** —
  patrz "Znane ograniczenia" niżej.
- **`PdmSystem.Indexer/`** — konsolowy skaner dysku. Działa, ale **po migracji `002` przestanie
  działać** dopóki nie doda się mu project_id — patrz niżej.
- **`PdmSystem.Api/`** — ASP.NET Core + frontend w `wwwroot/`. Teraz obsługuje:
  - **Projekty** — kontener grupujący elementy.
  - **Ręczne dodawanie elementów** — upload pliku przez przeglądarkę, plik trafia do
    katalogu `storage/` obok aplikacji (konfigurowalne przez `StorageRoot` w `appsettings.json`).
  - Tagowanie, filtrowanie, edycję właściwości (bez zmian względem poprzedniej wersji).

### Endpointy API

| Metoda | Ścieżka | Co robi |
|---|---|---|
| GET | `/api/projects` | lista projektów z liczbą elementów |
| POST | `/api/projects` | tworzy projekt `{ "name": "...", "description": "..." }` |
| POST | `/api/projects/{projectId}/items` | **multipart/form-data**: `file` (wymagany), `properties` (opcjonalny JSON) |
| GET | `/api/items?search=&tag=&projectId=` | lista elementów z filtrami |
| GET | `/api/items/{id}` | szczegóły elementu |
| GET | `/api/items/{id}/file` | pobranie wgranego pliku |
| GET | `/api/tags` | lista tagów |
| POST/DELETE | `/api/items/{id}/tags[/{tagName}]` | zarządzanie tagami |
| PATCH/DELETE | `/api/items/{id}/properties[/{key}]` | zarządzanie właściwościami |

## Jak uruchomić

```bash
# Jeśli masz już bazę z poprzedniej wersji:
psql -h localhost -U pdm_user -d pdm -f db/migrations/002_add_projects.sql

# Jeśli stawiasz od zera:
psql -h localhost -U pdm_user -d pdm -f db/schema.sql

cd PdmSystem.Api
dotnet restore && dotnet build && dotnet run
```

Otwórz adres z konsoli (`http://localhost:5000`). Workflow w przeglądarce: **"+ Projekt"**
→ nazwij projekt → wybierz go z listy → **"+ Element"** → wybierz plik → **Wgraj**.

## Znane ograniczenia

1. **`PdmSystem.Indexer` jest teraz niezgodny ze schematem po migracji `002`** —
   `items.project_id` jest `NOT NULL`, a `DatabaseWriter` w `PdmSystem.Core` nic o projektach
   nie wie, więc próba `INSERT` się wysypie. To świadomie zostawione bez naprawy, dopóki nie
   zdecydujemy, jak skanowanie dysku ma mapować się na projekty (jeden projekt na cały skan?
   osobny projekt na folder? coś innego?) — do ustalenia, zanim wrócimy do tego kodu.
2. **`PdmSystem.Api` z obsługą projektów nie był jeszcze uruchamiany na żywo** — to,
   co dotąd testowaliśmy razem krok po kroku (skaner, pierwsza wersja API), działało.
   Ta rozbudowa — nie. Traktuj pierwsze uruchomienie jako test, nie pewnik.
3. **Brak walidacji rozmiaru/typu wgrywanego pliku** — każdy plik przejdzie, niezależnie
   od rozszerzenia czy wielkości. Do dodania, jeśli ma być używane przez więcej niż Ciebie.
4. **Magazyn plików (`storage/`) to zwykły folder na dysku serwera** — brak kopii
   zapasowych, brak wersjonowania fizycznego pliku (rewizje w bazie są, ale nie ma jeszcze
   endpointu, który faktycznie tworzyłby nową rewizję przy podmianie pliku).
5. **Brak logiki checkout/checkin/rewizji/statusu w API** — schemat bazy to wspiera,
   endpointy jeszcze nie.

## Następne kroki (proponowana kolejność)

1. Zbuduj i przetestuj `PdmSystem.Api` z nowym kodem — utwórz projekt, wgraj plik, potaguj.
2. Zdecyduj: endpoint do podmiany pliku (tworzy nową rewizję) czy najpierw checkout/status.
3. Docelowo: wtyczka do FreeCAD/SolidWorks, która woła `POST /api/projects/{id}/items`
   bezpośrednio przy zapisie pliku — naturalne rozszerzenie tego samego mechanizmu ręcznego
   uploadu, tylko zainicjowane programowo zamiast przez człowieka w przeglądarce.
