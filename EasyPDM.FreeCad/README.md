# EasyPDM — makra FreeCAD

Dwa niezależne makra, jedno na wysyłanie, jedno na pobieranie/otwieranie:

- **`EasyPDMUpload.FCMacro`** wysyła aktywny dokument FreeCAD do EasyPDM. Makro nie pyta
  lokalnie o NIC poza folderem zapisu — nawet wybór "nowy element czy dogranie do
  istniejącego" zapada w **przeglądarce systemowej** (opisane w tym pliku niżej), na tym
  samym formularzu/pasku co w aplikacji webowej.
- **`EasyPDMDownload.FCMacro`** pobiera Część/Złożenie z EasyPDM (razem ze WSZYSTKIMI jego
  składnikami, jeśli to Złożenie) i od razu je otwiera w FreeCAD (opisane w osobnej sekcji
  na końcu tego pliku).

Oba dzielą to samo logowanie i adres API (te same preferencje FreeCAD) — zalogowanie się
w jednym starcza dla drugiego.

## Instalacja

Nie trzeba niczego instalować jako workbench. Dla każdego z dwóch makr osobno, w FreeCAD:

- **Macro → Macros… → Add path to macro path list** i wskazać ten folder (`EasyPDM.FreeCad/`),
  a potem uruchamiać makro z listy — **albo**
- **Macro → Macros… → Execute** i wskazać plik (`EasyPDMUpload.FCMacro` albo
  `EasyPDMDownload.FCMacro`) bezpośrednio, za każdym razem z dowolnej lokalizacji na dysku.

Adres API (domyślnie `http://localhost:5000/api`) jest zapamiętywany w preferencjach FreeCAD
(`User parameter:BaseApp/EasyPDM`) po pierwszym uruchomieniu — można go zmienić w oknie
"Wyślij do PDM" (pierwsze okno w każdym przebiegu — folder docelowy + konto), w polu
"Adres API".

## Logowanie

EasyPDM.Api wymaga zalogowania dla każdego wywołania poza samym logowaniem — makro więc
też się loguje. Przy pierwszym uruchomieniu (albo gdy zapisana sesja wygasła lub została
unieważniona) pojawia się okno logowania (nazwa użytkownika + hasło, te same konta co
w aplikacji webowej). Token sesji trafia do tych samych preferencji FreeCAD co adres API,
więc **kolejne uruchomienia makra — także po restarcie FreeCAD — NIE proszą o ponowne
logowanie**, dopóki sesja jest ważna (30 dni, tak samo jak w aplikacji webowej). W oknie
"Wyślij do PDM" widać, kto jest zalogowany, i można się stamtąd **wylogować** (przycisk
"Wyloguj") — unieważnia to sesję po stronie serwera i czyści lokalnie zapisany token, więc
kolejne uruchomienie makra od razu poprosi o ponowne zalogowanie.

**Dlaczego makro w ogóle potrzebuje własnej sesji, skoro decyzja nowy/istniejący i cały
formularz są w przeglądarce?** Bo makro samo musi się cały czas potrafić uwierzytelnić,
żeby (a) odpytać, czy przeglądarka już skończyła (`GET /api/create-tickets/{ticket}`), i
(b) dograć sam plik CAD do wskazanego elementu — to wciąż robi makro, nie przeglądarka, bo
inaczej zniknęłaby cała automatyka zmiany nazwy pliku / eksportu STEP / BOM-u złożenia. Ta
sama sesja makra jest przy okazji tym, co loguje przeglądarkę AUTOMATYCZNIE —
`GET /api/auth/browser-login` zamienia token makra na ciasteczko przeglądarki — więc
logowanie w makrze i "darmowe" zalogowanie przeglądarki to **jedna, ta sama operacja**, nie
dwie osobne.

## Co robi

1. **Zapisuje aktywny dokument** — jeśli dokument nie miał jeszcze nadanej ścieżki na dysku,
   makro poprosi o "Zapisz jako" (standardowe okno FreeCAD), zanim cokolwiek wyśle.
1a. **Pyta o folder docelowy** dla lokalnych kopii (Save As) wszystkich dokumentów wysyłanych
    w tej sesji (patrz krok 3 niżej) — domyślnie podpowiada ostatnio użyty (**współdzielona
    preferencja z `EasyPDMDownload.FCMacro`** — ustaw ten sam folder w obu makrach, żeby
    wysłane i pobrane pliki lądowały razem w jednym miejscu). Pytane RAZ, na samym początku —
    obejmuje też automatycznie wykryte drzewo złożenia (część/pod-złożenia wysyłane wcześniej
    niż otwiera się główne okno).
2. **Żadne natywne okno się już nie pokazuje.** Makro od razu otwiera **przeglądarkę
   systemową**, już zalogowaną (most token→ciasteczko, zob. "Logowanie" wyżej), na pasku
   "oczekuje żądanie z makra CAD" (widoczny na KAŻDYM ekranie aplikacji webowej, dopóki
   bilet czeka). Pasek pokazuje **jawny wybór trzema przyciskami** — wybór "nowy element,
   duplikat czy dogranie do istniejącego" zapada tam, nie lokalnie i nie przez przypadkowe
   kliknięcie byle "Dodaj" gdziekolwiek w aplikacji (świadomie NIE dzieje się w ten sposób):
   - **"Nowy element"** — otwiera **samowystarczalny popup**, bez potrzeby wcześniejszej
     nawigacji po panelu projektów po lewej: dopiero W TYM POPUPIE wybiera się projekt,
     opcjonalnie element nadrzędny, typ (Część/Złożenie), nazwę (domyślnie podpowiedziana
     z etykiety dokumentu), rodzaj i zależne od niego pola: dla Części rodzaj jest
     wymagany — **Wykonywana** → Materiał, **Zakupowa** → Producent/Numer zamówieniowy
     1 i 2/Masa, **Normalia** → Materiał/Norma, **Klienta** → brak dodatkowych pól. Dla
     Złożenia rodzaj jest opcjonalny i ograniczony do Wykonywana/Zakupowa/Normalia (bez
     "Klienta"), a Masa jest zawsze widoczna niezależnie od wybranego rodzaju — Złożenie
     nigdy nie ma pola Materiał (tylko Część). Popup ma też **checkbox "Eksportuj i wyślij
     model STEP"**. Bilet jest przypięty JAWNIE do tego jednego, konkretnego popupu — żadne
     INNE "Dodaj" w aplikacji (w drzewie projektu, panelu szczegółów) nigdy przypadkiem go
     nie "połknie". **Anuluj** w popupie wraca do wyboru "Nowy element"/"Duplikuj"/"Dograj
     do istniejącego" bez tworzenia niczego.
   - **"Duplikuj"** — najpierw wyszukiwarka wskazuje **źródłowy** element (Część/Złożenie)
     z całej bazy, potem otwiera TEN SAM popup co "Nowy element", tylko wstępnie wypełniony
     jego właściwościami (rodzaj/materiał/producent/numery zamówieniowe/norma/masa) — **bez
     kopiowania żadnego pliku**. Wszystkie pola dalej można edytować przed zapisem — to
     zwykłe tworzenie nowego elementu, tylko podpowiedziane danymi ze źródła.
   - **"Dograj do istniejącego"** — rozwija wyszukiwarkę Części/Złożenia z
     **całej bazy** (nie tylko bieżącego projektu, bo komponent może być współdzielony), z
     podpowiedziami podczas pisania (po numerze albo nazwie) i tym samym checkboksem STEP.
     Jeśli etykieta lokalnego dokumentu wygląda jak `numer (nazwa).REWIZJA` (bo to samo
     makro już go tak nazwało po wcześniejszej wysyłce), wyszukiwarka od razu podpowiada
     dopasowany element — to tylko **podpowiedź**, wybór zawsze można zmienić. Wybrany
     element nie jest tworzony na nowo — bieżący dokument trafia do niego jako **aktualna
     wersja jego bieżącej rewizji** (to, co dzieje się dalej ze statusem "Wydany"/nową
     rewizją, opisuje krok 2a niżej — to JEDYNA decyzja, która świadomie zostaje lokalna,
     w FreeCAD, tuż przed samym dograniem pliku).

   Jeden wspólny formularz/pasek w przeglądarce dla WSZYSTKIEGO, więc te reguły nie mogą
   się już rozjechać między makrem a aplikacją webową. Po zdecydowaniu w przeglądarce
   (dowolną z trzech dróg wyżej), FreeCAD (okno "Czekam na przeglądarkę", odpytujące serwer
   co ~2 s, limit 10 minut) samo wykrywa zakończenie i kontynuuje od kroku 3 niżej — nie
   trzeba wracać do FreeCAD ręcznie. Anulowanie w przeglądarce albo w oknie oczekiwania
   kończy makro komunikatem bez tworzenia/dogrania pliku (element mógł już powstać w PDM,
   jeśli zdążono go zapisać w przeglądarce — wtedy trzeba uruchomić makro ponownie i
   dograć do niego przez pasek "oczekujące żądanie z makra").
2a. **Jedyna decyzja, która świadomie ZOSTAŁA lokalna** — dla dogrywanego elementu ze
    statusem **"Wydany"** (w tym statusie PDM nie pozwala dogrywać plików), makro otwiera
    okno "Nowa rewizja": pyta, czy stworzyć nową rewizję, i pozwala wpisać opcjonalny
    **komentarz do rewizji** (co się zmieniło) — dokładnie ten sam komentarz, który można
    dodać w aplikacji webowej przy tej samej zmianie statusu. To bezpośrednio potwierdzenie
    tego, co makro zaraz zrobi z plikiem na dysku, nie dane elementu w PDM — stąd zostało
    lokalne. Anulowanie nic nie zapisuje; zatwierdzenie zmienia status na "W pracy" (ten
    sam mechanizm co w aplikacji webowej — podnosi numer rewizji i zapisuje komentarz,
    jeśli podano) i dopiero potem wysyła plik.
3. Element w tym momencie już istnieje w PDM (nowy — stworzony przez formularz w
   przeglądarce; już istniejący — wskazany tam samym paskiem, zob. krok 2). W obu
   przypadkach makro
   **KOPIUJE** bieżący plik dokumentu do PDM pod nazwą `numer (nazwa).REWIZJA.rozszerzenie`
   — ten sam format numer/nazwa, w jakim PDM wyświetla Części/Złożenia wszędzie indziej, plus
   rewizja jako **wielka litera** (A, B, C... — ta sama konwencja `revisionLabel()` co
   w aplikacji webowej; liczba w bazie się nie zmienia, to czysto kwestia formatowania).
   **Lokalny dokument jest też zapisywany (Save As) pod TĄ SAMĄ nazwą**, w folderze
   wybranym w kroku 1a (albo w folderze oryginału, jeśli akurat nic tam nie trzeba było
   przenosić — plik już tam był pod właściwą nazwą) — stary plik zostaje na dysku
   nietknięty, ale `doc.FileName` od teraz wskazuje na nowy. Dzięki temu złożenie linkujące
   (`App::Link`) do tego dokumentu,
   zapisywane PO nim w tej samej sesji (automatycznie wykryte drzewo złożenia zawsze
   zapisuje złożenie jako ostatnie — zob. niżej), zapisze swój odnośnik już pod nową nazwą —
   dokładnie tą, pod jaką `EasyPDMDownload.FCMacro` później zapisuje pobrane pliki, więc
   odnośniki po pobraniu od razu się zgadzają. **Bez tego złożenie po pobraniu szukałoby
   oryginalnej, sprzed-wysyłkowej nazwy pliku** (potwierdzone w praktyce komunikatem FreeCAD:
   `Link broken! ... File: <oryginalna_nazwa>.FCStd`).
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
   nie jest zachowywana.
5. **Opcjonalnie eksportuje STEP i wgrywa go automatycznie jako załącznik z rolą "step"** —
   makro nie ma tu już żadnego lokalnego wyboru. Dla **nowego** elementu decyduje o tym
   WYŁĄCZNIE checkbox w formularzu w przeglądarce (krok 2 wyżej). Dla dogrywania do
   **istniejącego** elementu i dla automatycznie wykrytych komponentów złożenia (oba nigdy
   nie przechodzą przez przeglądarkę) STEP eksportuje się **zawsze**, bez pytania. Gdy
   eksportuje, działa dokładnie tym samym mechanizmem co ręczny przycisk "STEP" w panelu
   Załączników w aplikacji webowej, więc od
   razu zasila stały podgląd 3D w panelu elementu. Eksportowana jest cała **widoczna**
   geometria dokumentu (wszystkie obiekty z bryłą, których widoczność jest włączona — dla
   Części to zwykle jedna bryła/Body, dla automatycznie wykrytego złożenia to rozwiązane
   `App::Link`i, więc STEP
   odzwierciedla całe złożenie). Poprzedni załącznik z rolą "step" jest **zastępowany**
   (usuwany przed wysyłką nowego), żeby podgląd zawsze pokazywał aktualną rewizję. Jeśli
   dokument nie ma żadnej widocznej bryły (sam szkic, pusty dokument) albo eksport/wysyłka
   się nie powiedzie — krok jest **cicho pomijany**, nie przerywa ani nie cofa reszty
   wysyłki (plik `.FCStd` w PDM jest już bezpiecznie zapisany w tym momencie).

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
  **natywne** okno (Projekt/Typ/Nazwa/Rodzaj i zależne od rodzaju pola — te same reguły co
  formularz w przeglądarce dla pojedynczego nowego elementu, patrz wyżej) — **celowo bez
  przeglądarki**, żeby wysłanie złożenia z wieloma nowymi komponentami nie wymagało tylu
  samo okien przeglądarki co komponentów; typ (Część/Złożenie) jest podpowiadany na
  podstawie tego, czy dany plik sam ma dalsze linki. Sam GŁÓWNY dokument złożenia (ten, na
  którym uruchomiono makro) i tak przechodzi przez formularz w przeglądarce jak każdy inny
  nowy element — tylko jego automatycznie wykryte SKŁADNIKI omijają przeglądarkę.
- Wybranie **"Nie"** na pytanie o automatyczne wysłanie wysyła TYLKO bieżący dokument,
  dokładnie tak jak dotychczas (bez podelementów) — struktura BOM-u zostaje wtedy do
  ręcznego uzupełnienia w aplikacji webowej, jak wcześniej.

## Ograniczenia pierwszej wersji

- **Krok 2** (nowy element/dogranie do istniejącego, oba przez przeglądarkę) wymaga, żeby
  domyślna przeglądarka systemowa umiała otworzyć adres serwera PDM (ten sam, co adres API
  w preferencjach makra) — na typowej instalacji (klient i serwer w tej samej sieci)
  działa to bez dodatkowej konfiguracji. Okno oczekiwania w FreeCAD ma limit **10 minut**
  — po przekroczeniu (albo Anuluj) makro kończy się komunikatem, bez tworzenia/dogrania
  pliku.
- Automatyczne wykrywanie złożenia działa tylko dla odnośników do **zewnętrznych, zapisanych
  plików** (`App::Link`) — nie dla złożeń trzymanych w jednym pliku jako kontenery
  `App::Part` (te nie mają osobnych plików do wysłania osobno; trzeba je wtedy wysyłać
  ręcznie, część po części, tak jak dotychczas).
- Rozpoznanie "już wysłanego" komponentu opiera się na etykiecie dokumentu — makro samo ją
  nadaje po wysłaniu i od razu zapisuje na dysk (Save As pod nazwą PDM), więc działa też
  w NOWEJ sesji FreeCAD, o ile otwarty zostanie przemianowany plik (ten pod nazwą PDM) —
  stara kopia sprzed wysyłki (zostawiona nietknięta na dysku) nadal ma oryginalną etykietę
  i nie zostanie rozpoznana.
- **Lokalny plik dokumentu jest PRZENOSZONY na nową nazwę (Save As) przy każdej wysyłce** —
  stary plik (pod oryginalną nazwą) zostaje na dysku, ale nie jest już aktywnie
  edytowany/otwarty; ręczna zmiana starego pliku NIE trafi automatycznie do PDM (trzeba
  wysłać ją z powrotem jako kolejną rewizję).
- **Kopiowanie/rejestrowanie pliku w `storage/` zakłada, że ten folder jest widoczny
  w systemie plików tej maszyny** — dziś klient (FreeCAD) i serwer (`EasyPDM.Api`)
  działają na tym samym dysku, więc to działa bez dodatkowej konfiguracji. Jeśli
  `GET /api/config` jest nieosiągalne albo ścieżka niedostępna do zapisu (np. FreeCAD na
  innej maszynie niż serwer), kopia trafia do PDM zwykłym uploadem HTTP — w tym trybie
  fallbackowym historia rewizji NIE jest zachowywana (każda wysyłka nadpisuje poprzednią
  kopię po stronie PDM).
- Endpoint rejestracji akceptuje wyłącznie ścieżki leżące wewnątrz skonfigurowanego
  magazynu (`StorageRoot`) — nie da się nim "podpiąć" dowolnego pliku z dysku serwera.
- Zapisany dokument jest wysyłany w swoim aktualnym stanie — makro nie waliduje np. czy
  dokument ma otwarte niezapisane zmiany w innych powiązanych plikach.
- **Gwiazdka "niezapisane zmiany" przy nazwie dokumentu może zostać widoczna nawet PO
  udanej wysyłce (i po jawnym `recompute()`+`save()` na końcu, i na każdym dokumencie z
  osobna)** — potwierdzone jako niezależne od makra: ten sam dokument dostaje gwiazdkę
  nawet po zwykłym, ręcznym `doc.save()` wpisanym wprost w konsoli Pythona FreeCAD (a
  właściwie już od samego korzystania z konsoli, jeszcze przed `save()`), bez udziału
  jakiegokolwiek kodu z tego pliku. To zachowanie samego FreeCAD (prawdopodobnie workbencha
  Assembly) — makro nie ma jak temu zapobiec, bo problem nie leży w tym, co ono robi.
  Nie zweryfikowano, czy sam PLIK na dysku jest mimo to poprawnie zapisany z aktualną
  zawartością (prawdopodobne, skoro `save()` faktycznie się wykonuje — tylko sam wskaźnik
  "zmieniony" w GUI nie znika).

## Weryfikacja

⚠️ **Poniższe testy dotyczą wersji SPRZED zmiany "formularz nowego elementu w
przeglądarce"** (opisanej w kroku 2 wyżej) — ta zmiana jest **nieprzetestowana na żywym
FreeCAD**. Zweryfikowane do tej pory: składnia (`py_compile`), spójność struktury (brak
osieroconych odwołań do usuniętych pól natywnego dialogu), i end-to-end na poziomie samych
endpointów backendu (`GET /api/auth/browser-login` — ustawienie ciasteczka + przekierowanie
+ ochrona przed open-redirect, `POST /projects/{id}/nodes` z ticketem + `GET
/create-tickets/{ticket}` — zwrot poprawnych danych elementu) w izolowanym środowisku
testowym, patrz `EasyPDM.Api.Tests/CreateTicketEndpointsTests.cs` i
`AuthEndpointsTests.cs`. NIE zweryfikowane na żywo: samo `webbrowser.open()` z poziomu
FreeCAD, okno "Czekam na przeglądarkę" (`WaitForTicketDialog`) w realnym GUI, oraz cały
przepływ od kliknięcia "Dodaj" w przeglądarce do automatycznego kontynuowania w FreeCAD.
Przy pierwszym uruchomieniu obserwuj przebieg uważnie i zgłoś, co nie zagra.

⚠️ **Poniższe testy dotyczą wersji SPRZED zmiany "Save As lokalnego pliku pod nazwą PDM"**
(opisanej w kroku 3 wyżej) — w szczególności punkty mówiące, że lokalny plik/`doc.FileName`
"nie zmienia się"/"zostaje nienaruszony" opisują POPRZEDNIE zachowanie, nie obecne.

Sam mechanizm Save As **został potwierdzony na żywo przez użytkownika** — wysłanie
złożenia z Częścią, a potem pobranie go przez `EasyPDMDownload.FCMacro`, otworzyło się BEZ
błędu "Link broken" (wcześniej, przed tą zmianą, złożenie zgłaszało dokładnie taki błąd,
szukając oryginalnej nazwy pliku sprzed wysyłki). Dwie NOWSZE, dobudowane od razu potem
rzeczy nie były jeszcze testowane na żywo: **okno wyboru folderu docelowego** (krok 1a —
współdzielone z folderem pobierania w `EasyPDMDownload.FCMacro`) i **poprawiony komunikat
końcowy** (rozróżniający, czy lokalny plik faktycznie został przeniesiony, czy już tam był).

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

---

# EasyPDMDownload.FCMacro — pobieranie i otwieranie

Drugie makro: zamiast wysyłać, **pobiera** Część/Złożenie z EasyPDM i od razu **otwiera** je
w FreeCAD. Logowanie i adres API są dokładnie tak samo skonfigurowane jak w
`EasyPDMUpload.FCMacro` (te same preferencje FreeCAD) — osobna instalacja/uruchomienie
(patrz "Instalacja" wyżej), ale wspólna sesja. Tak jak w `EasyPDMUpload.FCMacro`, wybór
elementu zapada w **przeglądarce systemowej**, nie w natywnym oknie — makro pyta lokalnie
TYLKO o folder docelowy.

## Co robi

1. Natywne okno pyta TYLKO o folder docelowy (domyślnie podpowiada ostatnio użyty, osobna
   preferencja FreeCAD `DownloadFolder`, z przyciskiem **"..."** do zmiany) i pokazuje
   konto/**Wyloguj**. Po zatwierdzeniu makro od razu otwiera **przeglądarkę systemową**, już
   zalogowaną (most token→ciasteczko), na pasku "oczekuje żądanie pobrania z makra CAD"
   (widoczny na każdym ekranie aplikacji webowej) — tam dopiero wyszukuje się Część/Złożenie
   z **całej bazy** (po numerze albo nazwie) i zatwierdza wybór. FreeCAD (okno "Czekam na
   przeglądarkę", odpytujące co ~2 s, limit **10 minut**) samo wykrywa zatwierdzenie i
   pobieranie zaczyna się automatycznie — nie trzeba wracać do FreeCAD ręcznie. Anulowanie
   w przeglądarce/oknie oczekiwania kończy makro bez pobierania niczego.
2. Dla **Złożenia**: pobiera też WSZYSTKIE jego składniki rekurencyjnie (bezpośrednie dzieci,
   potem ich dzieci, i tak dalej — cały BOM), do TEGO SAMEGO folderu co plik główny. Bez tego
   złożenie zbudowane na odnośnikach `App::Link` do zewnętrznych, zapisanych plików (standard
   w workbenchu Assembly/Assembly4) nie miałoby czym się otworzyć — FreeCAD rozwiązuje te
   odnośniki dopiero przy otwieraniu dokumentu, więc pliki składników muszą już leżeć na dysku
   PRZED otwarciem pliku głównego.
3. Jeśli w folderze docelowym jest już plik o **dokładnie tej samej nazwie** (czyli tej samej
   rewizji) i tym samym rozmiarze co na serwerze — pomija go, nie pobiera drugi raz.
4. Jeśli w folderze jest już plik TEGO SAMEGO elementu, ale w **innej (starszej) rewizji**,
   a na serwerze jest nowsza — pyta, czy pobrać nowszą, zamiast cicho nadpisywać albo cicho
   zostawiać nieaktualny plik.
5. Na końcu **otwiera** główny (wybrany) plik w FreeCAD (`App.openDocument`) — pliki
   składników zostają tylko na dysku, nie są automatycznie otwierane jako osobne dokumenty
   (dokładnie tak, jak FreeCAD sam otwiera złożenie: linkowane pliki wczytuje w tle).

## Skąd bierze pliki

EasyPDM nie ma osobnego "pliku elementu" dla Części/Złożenia — aktualny plik CAD jest
załącznikiem (`item_attachments`), a przy KAŻDEJ wysyłce nowej rewizji przez
`EasyPDMUpload.FCMacro` poprzednia kopia ZOSTAJE (nowy załącznik obok starego, różne nazwy:
`numer (nazwa).REWIZJA.rozszerzenie`) — więc historia rewizji jest w praktyce odtwarzalna
z samej listy załączników, bez potrzeby osobnego API do "starych wersji pliku". Makro
rozpoznaje tę konwencję nazw, żeby trafić w załącznik odpowiadający AKTUALNEJ rewizji
elementu; jeśli element nigdy nie przeszedł przez żadne makro CAD (np. dograny ręcznie
w aplikacji webowej, załączniki mają dowolne, oryginalne nazwy), bierze po prostu najnowiej
wgrany załącznik jako najlepsze przybliżenie.

## Ograniczenia pierwszej wersji

- **Wybór elementu** (krok 1 wyżej) wymaga, żeby domyślna przeglądarka systemowa umiała
  otworzyć adres serwera PDM (ten sam, co adres API w preferencjach makra) — na typowej
  instalacji (klient i serwer w tej samej sieci) działa to bez dodatkowej konfiguracji.
  Okno oczekiwania w FreeCAD ma limit **10 minut** — po przekroczeniu (albo Anuluj) makro
  kończy się bez pobierania niczego.
- Zawsze celuje w AKTUALNĄ rewizję — nie da się nim pobrać konkretnej, wybranej starszej
  rewizji (starsze lokalne kopie służą wyłącznie do wykrycia "masz nieaktualną wersję",
  punkt 4 wyżej).
- Wszystkie pliki (główny + składniki) lądują płasko w JEDNYM folderze, bez odtwarzania
  struktury BOM jako podfolderów. To najbezpieczniejszy domyślny wybór dla odnośników
  `App::Link` zapisanych jako ścieżki WZGLĘDEM folderu dokumentu (typowe dla Assembly4), ale
  jeśli oryginalny model był budowany z plikami w osobnych podfolderach albo z odnośnikami
  zapisanymi jako ścieżki BEZWZGLĘDNE z innej maszyny, odnośniki mogą mimo to nie rozwiązać
  się automatycznie — wtedy trzeba je poprawić ręcznie w FreeCAD (Assembly4 ma do tego
  narzędzie "Make link relative"/zmianę ścieżki linku).
- Współdzielony komponent (użyty w kilku miejscach drzewa) pobierany jest tylko raz
  (rozpoznawany po ID elementu) — tak samo jak przy wysyłaniu w `EasyPDMUpload.FCMacro`.
- Rozpoznanie "to jest plik tego elementu" opiera się na tej samej konwencji nazw co
  wysyłanie (`numer (nazwa).REWIZJA.rozszerzenie`) — element, którego JEDYNY załącznik ma
  zupełnie inną nazwę (nigdy nie przeszedł przez żadne makro CAD), i tak zostanie pobrany
  (bierze najnowszy załącznik), ale wykrycie "masz już starszą rewizję" (punkt 4) wtedy nie
  zadziała, bo nie ma z czego rozpoznać litery rewizji w nazwie lokalnego pliku.

## Status weryfikacji

⚠️ **Nieprzetestowane na żywym FreeCAD** — w odróżnieniu od `EasyPDMUpload.FCMacro` (które
przeszło pełny cykl testów przez `freecadcmd` przeciwko żywemu serwerowi, plus ręczną
weryfikację w GUI), to makro było możliwe do zweryfikować tylko: składniowo (`ast.parse`),
pod kątem poprawności polskich znaków (skrypt sprawdzający częstość znaków — zero
zniekształceń) i przez uważny przegląd logiki względem rzeczywistych endpointów API
(`GET /api/items`, `/items/{id}/attachments`, `/items/{id}/children`,
`/attachments/{id}/file`, każdy sprawdzony w kodzie `EasyPDM.Api`). Przy pierwszym
uruchomieniu na żywym FreeCAD obserwuj przebieg (log w oknie na końcu i w konsoli raportów
FreeCAD) i zgłoś, co nie zagra — najbardziej ryzykowne miejsca to: rozpoznawanie nazw
załączników regexem (jeśli plik ma nietypową nazwę) i to, czy odnośniki `App::Link` w
pobranym złożeniu faktycznie rozwiążą się automatycznie po umieszczeniu wszystkich plików
w jednym, płaskim folderze (zależy od tego, jak zapisane są ścieżki linków w oryginalnym
pliku — patrz "Ograniczenia" wyżej).

Wybór elementu przez przeglądarkę (krok 1 wyżej) to NAJNOWSZA zmiana w tym pliku, zbudowana
wprost na tym samym, już zweryfikowanym mechanizmie z `EasyPDMUpload.FCMacro`
(`GET /api/auth/browser-login`, bilet `GET /api/create-tickets/{ticket}` +
`POST /create-tickets/{ticket}/attach-existing`, pasek "oczekujące żądanie z makra" w
aplikacji webowej) — backend/frontend tej części były testowane end-to-end (`curl` w
izolowanym środowisku), ale samo `EasyPDMDownload.FCMacro` (`webbrowser.open`,
`WaitForTicketDialog` w realnym GUI) nie było jeszcze uruchomione na żywym FreeCAD.
