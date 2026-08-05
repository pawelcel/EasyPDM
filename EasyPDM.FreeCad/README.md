# EasyPDM — makro FreeCAD

Makro `EasyPDMUpload.FCMacro` wysyła aktywny dokument FreeCAD wprost do EasyPDM, bez
przechodzenia przez przeglądarkę.

## Instalacja

Nie trzeba niczego instalować jako workbench. Wystarczy w FreeCAD:

- **Macro → Macros… → Add path to macro path list** i wskazać ten folder (`EasyPDM.FreeCad/`),
  a potem uruchamiać makro `PdmUpload` z listy — **albo**
- **Macro → Macros… → Execute** i wskazać plik `EasyPDMUpload.FCMacro` bezpośrednio, za każdym
  razem z dowolnej lokalizacji na dysku.

Adres API (domyślnie `http://localhost:5000/api`) jest zapamiętywany w preferencjach FreeCAD
(`User parameter:BaseApp/EasyPDM`) po pierwszym uruchomieniu — można go zmienić w oknie
dialogowym makra, w polu "Adres API".

## Logowanie

EasyPDM.Api wymaga zalogowania dla każdego wywołania poza samym logowaniem — makro więc
też się loguje. Przy pierwszym uruchomieniu (albo gdy zapisana sesja wygasła lub została
unieważniona) pojawia się okno logowania (nazwa użytkownika + hasło, te same konta co
w aplikacji webowej). Token sesji trafia do tych samych preferencji FreeCAD co adres API,
więc **kolejne uruchomienia makra — także po restarcie FreeCAD — NIE proszą o ponowne
logowanie**, dopóki sesja jest ważna (30 dni, tak samo jak w aplikacji webowej). W głównym
oknie "Wyślij do PDM" widać, kto jest zalogowany, i można się stamtąd **wylogować**
(przycisk "Wyloguj") — unieważnia to sesję po stronie serwera i czyści lokalnie zapisany
token, więc kolejne uruchomienie makra od razu poprosi o ponowne zalogowanie.

## Co robi

1. **Zapisuje aktywny dokument** — jeśli dokument nie miał jeszcze nadanej ścieżki na dysku,
   makro poprosi o "Zapisz jako" (standardowe okno FreeCAD), zanim cokolwiek wyśle.
2. **Otwiera okno dialogowe**, w którym najpierw wybiera się **tryb**:
   - **Nowy element w PDM** — pełny formularz: projekt PDM (pobierany z serwera) i
     opcjonalnie element nadrzędny (Folder/Złożenie tego projektu — dokładnie te same
     reguły struktury co w aplikacji webowej: pod Częścią/Plikiem nie da się nic dodać),
     typ (Część/Złożenie), nazwa (domyślnie etykieta dokumentu), rodzaj i zależne od niego
     pola — dla Części rodzaj jest wymagany: **Wykonywana** → Materiał, **Zakupowa** →
     Producent/Numer zamówieniowy 1 i 2/Masa, **Normalia** → Materiał/Norma, **Klienta** →
     brak dodatkowych pól. Dla Złożenia rodzaj jest opcjonalny i ograniczony do
     Wykonywana/Zakupowa/Normalia (bez "Klienta"), a Masa jest zawsze widoczna niezależnie od
     wybranego rodzaju — Złożenie nigdy nie ma pola Materiał (tylko Część) — identycznie jak
     w aplikacji webowej.
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

## Automatyczne wykrywanie złożenia

Jeśli aktywny dokument linkuje (`App::Link` — standardowy sposób budowania złożeń
w workbenchu Assembly/Assembly4) do **innych, zapisanych plików `.FCStd`**, makro **przed**
otwarciem głównego okna pyta, czy wysłać całe drzewo automatycznie:

- **Wykrywanie**: przechodzi po obiektach dokumentu, znajduje `App::Link` wskazujące na
  inne dokumenty i schodzi rekurencyjnie w głąb (pod-złożenie też może linkować dalej).
  Ilość liczy z liczby odnośników do tego samego pliku — kilka osobnych linków i wzorce/
  tablice linków (`ElementCount`) liczą się razem (np. 2 osobne śruby + wzorzec 2 sztuk tej
  samej śruby = 4 w BOM-ie).
- **Kolejność wysyłki**: liście najpierw (części bez dalszych linków), potem pod-złożenia,
  na końcu główny dokument — żeby każdy komponent istniał w PDM, zanim zostanie podpięty
  jako podelement.
- **Już wysłane komponenty**: rozpoznawane po etykiecie w formacie `numer (nazwa).REWIZJA`
  (ten sam format, który makro samo nadaje po wysłaniu) — jeśli taki numer istnieje w PDM,
  komponent NIE jest tworzony ponownie, tylko podpinany do BOM-u z wyliczoną ilością.
- **Nowe komponenty**: dla każdego jeszcze nie wysłanego pliku pokazuje się osobne, krótkie
  okno (Projekt/Typ/Nazwa/Rodzaj i zależne od rodzaju pola — te same reguły co dla
  pojedynczego nowego elementu, patrz wyżej); typ (Część/Złożenie) jest podpowiadany na
  podstawie tego, czy dany plik sam ma dalsze linki.
- Wybranie **"Nie"** na pytanie o automatyczne wysłanie wysyła TYLKO bieżący dokument,
  dokładnie tak jak dotychczas (bez podelementów) — struktura BOM-u zostaje wtedy do
  ręcznego uzupełnienia w aplikacji webowej, jak wcześniej.

## Ograniczenia pierwszej wersji

- Automatyczne wykrywanie złożenia działa tylko dla odnośników do **zewnętrznych, zapisanych
  plików** (`App::Link`) — nie dla złożeń trzymanych w jednym pliku jako kontenery
  `App::Part` (te nie mają osobnych plików do wysłania osobno; trzeba je wtedy wysyłać
  ręcznie, część po części, tak jak dotychczas).
- Rozpoznanie "już wysłanego" komponentu opiera się na etykiecie dokumentu — makro samo ją
  nadaje po wysłaniu, ale **nie zapisuje tej zmiany na dysk** (żeby nie ruszać pliku
  użytkownika bez pytania), więc działa pewnie tylko w obrębie jednej sesji FreeCAD, chyba
  że dokument zostanie potem ręcznie zapisany. W nowej sesji, dla pliku nigdy ręcznie nie
  zapisanego po wysłaniu, makro zaproponuje utworzenie go w PDM jeszcze raz — trzeba to
  wtedy przerwać/skorygować ręcznie.
- **Kopiowanie/rejestrowanie pliku w `storage/` zakłada, że ten folder jest widoczny
  w systemie plików tej maszyny** — dziś klient (FreeCAD) i serwer (`EasyPDM.Api`)
  działają na tym samym dysku, więc to działa bez dodatkowej konfiguracji. Jeśli
  `GET /api/config` jest nieosiągalne albo ścieżka niedostępna do zapisu (np. FreeCAD na
  innej maszynie niż serwer), kopia trafia do PDM zwykłym uploadem HTTP — w tym trybie
  fallbackowym historia rewizji NIE jest zachowywana (każda wysyłka nadpisuje poprzednią
  kopię po stronie PDM). W obu trybach lokalny plik zawsze zostaje nienaruszony.
- Endpoint rejestracji akceptuje wyłącznie ścieżki leżące wewnątrz skonfigurowanego
  magazynu (`StorageRoot`) — nie da się nim "podpiąć" dowolnego pliku z dysku serwera.
- Zapisany dokument jest wysyłany w swoim aktualnym stanie — makro nie waliduje np. czy
  dokument ma otwarte niezapisane zmiany w innych powiązanych plikach.

## Weryfikacja

Logika (bez samego okna dialogowego) była testowana automatycznie przez `freecadcmd`
przeciwko żywemu `EasyPDM.Api`:
- **logowanie/sesja**: wywołanie API bez sesji odrzucone (401); złe hasło odrzucone zwykłym
  błędem (token lokalny zostaje pusty); poprawne logowanie zapisuje token i wyświetlaną
  nazwę użytkownika w preferencjach FreeCAD, kolejne wywołania API z tym tokenem działają;
  wylogowanie czyści token/nazwę lokalnie ORAZ unieważnia sesję po stronie serwera (kolejne
  wywołanie z tym samym, już unieważnionym tokenem znowu dostaje 401),
- **cztery rodzaje Części** (Wykonywana/Zakupowa/Normalia/Klienta) w combo, z widocznością pól
  zależną od wybranego rodzaju dokładnie jak w `PartPropertyForm` (sprawdzone na realnym oknie
  `PdmUploadDialog`, wyświetlonym przez Qt w trybie `offscreen`); rodzaj Złożenia ograniczony
  do trzech opcji bez "Klienta", z Masą zawsze widoczną niezależnie od rodzaju, ale bez pola
  Materiał (tylko Część je ma);
  utworzenie Części "Normalia" (z Materiałem i Normą) i "Klienta" (bez dodatkowych pól)
  potwierdzone odczytem zapisanych właściwości z serwera,
- **automatyczne wykrywanie złożenia** (trzypoziomowe drzewo: część linkowana 2× osobnymi
  linkami + 1× wzorcem po 2 sztuki w głównym złożeniu, plus ta sama część jeszcze raz
  wewnątrz osobnego pod-złożenia): wykryta kolejność wysyłki liście-najpierw, poprawnie
  zsumowana ilość (2+2=4) dla części użytej wielokrotnie, poprawne krawędzie rodzic-dziecko
  na wszystkich poziomach (w tym z pod-złożenia do jego własnej części) — potwierdzone
  bezpośrednio przez `GET /api/projects/{projectId}/relations` po pełnym przebiegu
  `process_assembly_tree` (z podmienionym oknem nowego komponentu, żeby dało się to
  uruchomić bez GUI). Ponowne sprawdzenie w tej samej sesji rozpoznaje już wysłane
  komponenty po etykiecie (bez tworzenia duplikatów).
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
