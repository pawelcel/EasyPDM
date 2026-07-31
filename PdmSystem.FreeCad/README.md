# PdmSystem — makro FreeCAD

Makro `PdmUpload.FCMacro` wysyła aktywny dokument FreeCAD wprost do PdmSystem, bez
przechodzenia przez przeglądarkę.

## Instalacja

Nie trzeba niczego instalować jako workbench. Wystarczy w FreeCAD:

- **Macro → Macros… → Add path to macro path list** i wskazać ten folder (`PdmSystem.FreeCad/`),
  a potem uruchamiać makro `PdmUpload` z listy — **albo**
- **Macro → Macros… → Execute** i wskazać plik `PdmUpload.FCMacro` bezpośrednio, za każdym
  razem z dowolnej lokalizacji na dysku.

Adres API (domyślnie `http://localhost:5000/api`) jest zapamiętywany w preferencjach FreeCAD
(`User parameter:BaseApp/PdmSystem`) po pierwszym uruchomieniu — można go zmienić w oknie
dialogowym makra, w polu "Adres API".

## Co robi

1. **Zapisuje aktywny dokument** — jeśli dokument nie miał jeszcze nadanej ścieżki na dysku,
   makro poprosi o "Zapisz jako" (standardowe okno FreeCAD), zanim cokolwiek wyśle.
2. **Otwiera okno dialogowe**, w którym najpierw wybiera się **tryb**:
   - **Nowy element w PDM** — pełny formularz: projekt PDM (pobierany z serwera) i
     opcjonalnie element nadrzędny (Folder/Złożenie tego projektu — dokładnie te same
     reguły struktury co w aplikacji webowej: pod Częścią/Plikiem nie da się nic dodać),
     typ (Część/Złożenie), nazwa (domyślnie etykieta dokumentu), rodzaj i zależne od niego
     pola — dla Części rodzaj jest wymagany (Wykonywana → Materiał; Zakupowa →
     Producent/Numer zamówieniowy 1 i 2/Masa), dla Złożenia Materiał/Masa/Rodzaj są razem
     opcjonalne — identycznie jak w aplikacji webowej.
     **Automatyczne wykrycie istniejącego elementu**: makro sprawdza nazwę (przy otwarciu
     okna — na podstawie etykiety dokumentu — i na bieżąco przy wpisywaniu w polu "Nazwa")
     i jeśli w PDM istnieje już Część/Złożenie o dokładnie takiej nazwie, samo przełącza
     tryb na "Istniejący element" i zaznacza dopasowany element (jeśli nazwa jest
     jednoznaczna — przy kilku elementach o tej samej nazwie w różnych projektach tylko
     zawęża wyszukiwanie, wybór zostaje po stronie użytkownika). To tylko **podpowiedź** —
     niczego nie wysyła automatycznie; zawsze można ręcznie wrócić do "Nowy element", jeśli
     rzeczywiście chodziło o nowy rekord o tej samej nazwie.
   - **Istniejący element w PDM** — jedno pole: Część albo Złożenie do wyboru/wyszukania
     z **całej bazy** (nie tylko bieżącego projektu, bo komponent może być używany w wielu
     projektach), z podpowiedziami podczas pisania (po numerze albo nazwie). Wybrany
     element nie jest tworzony na nowo — bieżący dokument trafia do niego jako **aktualna
     wersja jego bieżącej rewizji**. Jeśli element ma status **"Wydany"** (w tym statusie
     PDM nie pozwala dogrywać plików), makro otwiera okno "Nowa rewizja": pyta, czy
     stworzyć nową rewizję, i pozwala wpisać opcjonalny **komentarz do rewizji** (co się
     zmieniło) — dokładnie ten sam komentarz, który można dodać w aplikacji webowej przy
     tej samej zmianie statusu. Anulowanie nic nie zapisuje; zatwierdzenie zmienia status
     na "W pracy" (ten sam mechanizm co w aplikacji webowej — podnosi numer rewizji
     i zapisuje komentarz, jeśli podano) i dopiero potem wysyła plik.
3. **Nowy element**: tworzy go w PDM (`POST /api/projects/{id}/nodes`). W obu trybach:
   **KOPIUJE** bieżący plik dokumentu do PDM pod nazwą `numer (nazwa).REWIZJA.rozszerzenie`
   — ten sam format numer/nazwa, w jakim PDM wyświetla Części/Złożenia wszędzie indziej, plus
   rewizja jako **wielka litera** (A, B, C... — ta sama konwencja `revisionLabel()` co
   w aplikacji webowej; liczba w bazie się nie zmienia, to czysto kwestia formatowania).
   **Lokalny plik, w miejscu gdzie został otwarty, NIE jest ruszany** — nie jest ani
   przenoszony, ani usuwany (tylko etykieta dokumentu w FreeCAD się zmienia, co nie ma
   wpływu na dysk); przyda się to, gdy serwer PDM przestanie współdzielić dysk z klientem,
   np. po przeniesieniu do kontenera Docker.
4. Jeśli magazyn PDM jest widoczny z tej maszyny (zob. `GET /api/config`), kopia trafia do
   **WSPÓLNEGO folderu** `storage/components/` (jeden dla wszystkich projektów — Część/
   Złożenie bywa współdzielone jako komponent BOM między projektami) i jest **REJESTROWANA
   BEZ ponownego przesyłania przez HTTP** (`POST /api/items/{id}/attachments/register`) —
   kopia już fizycznie leży w magazynie serwera. **PDM ZACHOWUJE kopie poprzednich rewizji**
   — np. `67 (nazwa).A.FCStd` zostaje, gdy powstaje rewizja B; nadpisywana jest tylko kopia
   TEJ SAMEJ rewizji (czyli powtórna wysyłka bez zmiany statusu). Jeśli magazyn jest
   nieosiągalny z tej maszyny (np. FreeCAD na innym komputerze niż serwer), kopia trafia
   przez zwykły upload HTTP (`POST /api/items/{id}/attachments`, ten sam mechanizm co
   dogrywanie plików CAD z panelu właściwości w aplikacji webowej) — wtedy historia rewizji
   nie jest zachowywana. W obu przypadkach lokalny plik zostaje nienaruszony.

## Ograniczenia pierwszej wersji

- Obsługuje tylko **bieżący, pojedynczy dokument** — nie chodzi rekurencyjnie po
  `App::Link`/złączach złożenia, żeby automatycznie zbudować cały BOM z podzespołów.
  Każdą część złożenia trzeba na razie wysłać osobno, a powiązania w BOM-ie ustawić
  w aplikacji webowej.
- **Kopiowanie/rejestrowanie pliku w `storage/` zakłada, że ten folder jest widoczny
  w systemie plików tej maszyny** — dziś klient (FreeCAD) i serwer (`PdmSystem.Api`)
  działają na tym samym dysku, więc to działa bez dodatkowej konfiguracji. Jeśli
  `GET /api/config` jest nieosiągalne albo ścieżka niedostępna do zapisu (np. FreeCAD na
  innej maszynie niż serwer), kopia trafia do PDM zwykłym uploadem HTTP — w tym trybie
  fallbackowym historia rewizji NIE jest zachowywana (każda wysyłka nadpisuje poprzednią
  kopię po stronie PDM). W obu trybach lokalny plik zawsze zostaje nienaruszony.
- Brak logowania/autoryzacji — zakłada, że `PdmSystem.Api` jest dostępne bez
  uwierzytelniania, tak jak reszta systemu dziś. Endpoint rejestracji akceptuje wyłącznie
  ścieżki leżące wewnątrz skonfigurowanego magazynu (`StorageRoot`) — nie da się nim
  "podpiąć" dowolnego pliku z dysku serwera.
- Zapisany dokument jest wysyłany w swoim aktualnym stanie — makro nie waliduje np. czy
  dokument ma otwarte niezapisane zmiany w innych powiązanych plikach.

## Weryfikacja

Logika (bez samego okna dialogowego) była testowana automatycznie przez `freecadcmd`
przeciwko żywemu `PdmSystem.Api`:
- utworzenie Części z materiałem, utworzenie Złożenia pod Folderem z powiązaniem w BOM-ie,
- **plik zapisany poza magazynem (np. symulowany Pulpit) zostaje tam, gdzie był** — po
  wysyłce lokalny plik nadal istnieje pod tą samą ścieżką i nazwą, `doc.FileName` się nie
  zmienia, a do `storage/components/` trafia tylko jego KOPIA (bajty identyczne, sprawdzone
  porównaniem lokalnego pliku i kopii na serwerze),
- elementy z DWÓCH różnych projektów lądują w tym samym, wspólnym `storage/components/` —
  bez osobnych podfolderów per projekt,
- endpoint rejestracji: odrzucenie ścieżki spoza magazynu (400) i nieistniejącego pliku (404),
  poprawna rejestracja (potwierdzone przez `file_path` w bazie),
- `revision_label()`: 1→A, 2→B, 26→Z, 27→AA,
- **pełny cykl rewizji**: pierwsza wysyłka tworzy kopię `N (nazwa).A.ext` na serwerze
  z jednym załącznikiem (lokalny plik cały czas bez zmian); powtórna wysyłka BEZ zmiany
  statusu nadpisuje tylko tę samą kopię `.A.` (nie mnoży kopii, lokalny plik nietknięty);
  wysyłka do elementu o statusie "Wydany" z odmową → nic się nie zmienia (status zostaje
  "Wydany", kopia i załącznik A nienaruszone); z potwierdzeniem → status wraca do "W pracy",
  numer rewizji rośnie, na serwerze powstaje NOWA kopia `.B.ext` **obok** `.A.ext` (stara NIE
  jest usuwana, lokalny plik przez cały czas pod tą samą, niezmienioną nazwą/ścieżką), a w PDM
  są teraz dokładnie DWA załączniki — po jednym na rewizję,
- **automatyczne wykrycie istniejącego elementu po nazwie** (jedyny test budujący realne
  okno `PdmUploadDialog`, bez `exec()`): dokładnie jedno dopasowanie nazwy → tryb
  przełącza się na "Istniejący element" i element jest od razu zaznaczony; brak
  dopasowania → zostaje "Nowy element"; kilka elementów o tej samej nazwie (różne
  projekty) → tryb się przełącza i wyszukiwanie zawęża się do tej nazwy, ale nic nie jest
  zaznaczane automatycznie; wpisywanie nazwy na żywo w polu "Nazwa" wywołuje to samo
  sprawdzenie,
- **komentarz do rewizji**: przy potwierdzeniu nowej rewizji z komentarzem —
  `GET /api/items/{id}/revisions` zwraca dokładnie jeden wpis z tym komentarzem i właściwym
  numerem rewizji; odmowa rewizji albo potwierdzenie z PUSTYM komentarzem niczego tam nie
  dopisują (rewizje bez komentarza po prostu nie mają wpisu).

Wszystko przeszło poprawnie — w tym raz na żywo przez samego użytkownika w GUI FreeCAD
(utworzenie elementu i rewizji A→B), co potwierdziło zachowanie plików i załączników w realnym
środowisku, nie tylko w testach automatycznych. Samo okno dialogowe (PySide6, w tym
wyszukiwarka istniejących elementów i okno "Nowa rewizja" z komentarzem) było też sprawdzane
ręcznie — testowane na FreeCAD 1.1.3 z PySide6.
