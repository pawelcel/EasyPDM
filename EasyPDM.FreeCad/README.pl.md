# EasyPDM — makra FreeCAD

[English](README.md) | **Polski** | [Deutsch](README.de.md)

Dwa niezależne makra, jedno na wysyłanie, jedno na pobieranie/otwieranie:

- **`EasyPDMUpload.FCMacro`** wysyła aktywny dokument FreeCAD do EasyPDM. Makro nie pyta
  lokalnie o NIC poza folderem zapisu — nawet wybór "nowy element czy dogranie do
  istniejącego" zapada w **przeglądarce systemowej** (opisane w tym pliku niżej), na tym
  samym formularzu/pasku co w aplikacji webowej — **z jednym wyjątkiem**: jeśli etykieta
  dokumentu już wygląda, jakby należała do istniejącego elementu PDM, makro pyta najpierw
  lokalnie, czy dograć ją tam jako nową rewizję, całkowicie omijając przeglądarkę (patrz
  "Lokalny skrót dla już rozpoznanego dokumentu" niżej).
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
1b. **Lokalny skrót dla już rozpoznanego dokumentu.** FreeCAD nie ma trwałego odpowiednika
    Właściwości niestandardowej `EasyPDM_ItemId` z makra SolidWorks (patrz README makra
    SolidWorks po opis tego mechanizmu) — zamiast tego, jeśli **etykieta** dokumentu
    wygląda jak `numer (nazwa).REWIZJA`, a ten numer faktycznie pasuje do istniejącego
    elementu PDM po numerze/nazwie pliku (`match_existing_item`), makro pyta **lokalnie,
    natywnie**: dograć bieżącą wersję do tego elementu jako nową rewizję? Potwierdzenie
    pokazuje jeszcze dwa natywne pytania Tak/Nie (Eksportuj STEP — domyślnie Tak;
    Eksportuj PDF — domyślnie Nie) i dogrywa wprost do tego elementu, **całkowicie
    omijając przeglądarkę** dla tego dokumentu. Odmowa spada do zwykłego przepływu przez
    przeglądarkę w kroku 2 niżej, z dopasowanym numerem elementu podpowiedzianym w
    wyszukiwarce "Dograj do istniejącego". Ponieważ to opiera się na dowolnej etykiecie,
    nie na trwałej właściwości, w rzadkich przypadkach może się mylić — np. własne "Zapisz
    jako" FreeCAD kopiuje etykietę dokumentu na naprawdę nowy, niepowiązany plik, co może
    sprawić, że fałszywie "rozpozna się" jako istniejący element; odmowa potwierdzenia jest
    zawsze bezpieczna (spada z powrotem do przeglądarki).
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
     nigdy nie ma pola Materiał (tylko Część). Popup ma też **checkboksy "Eksportuj STEP"
     i "Eksportuj PDF"** (STEP domyślnie zaznaczony, PDF nie — patrz krok 5 niżej, co
     dokładnie robi każdy z nich przy eksporcie). Bilet jest przypięty JAWNIE do tego
     jednego, konkretnego popupu — żadne
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
     podpowiedziami podczas pisania (po numerze albo nazwie) i tymi samymi checkboksami
     STEP/PDF. Jeśli etykieta lokalnego dokumentu wygląda jak `numer (nazwa).REWIZJA` (bo to samo
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
5. **Opcjonalnie eksportuje STEP i/lub PDF i wgrywa je automatycznie jako załączniki z
   rolami "step"/"pdf".** Skąd bierze się Tak/Nie, zależy od wybranej wcześniej ścieżki:
   dla **nowego elementu** albo **dogrania do istniejącego** zdecydowanego w przeglądarce
   (krok 2), bierze się z checkboksów tego samego biletu; dla każdego **komponentu
   złożenia** z własnym biletem (patrz "Automatyczne wykrywanie złożenia" niżej) — z
   checkboksów TEGO KONKRETNEGO komponentu; dla **lokalnego skrótu** (krok 1b) — z dwóch
   natywnych pytań Tak/Nie pokazanych tam zamiast tego. Nie ma już ścieżki, gdzie STEP
   byłby bezwarunkowo wymuszony bez żadnego wyboru.
   - **STEP**: działa tym samym, leżącym u podstaw mechanizmem co ręczny przycisk "STEP" w
     panelu Załączników w aplikacji webowej, więc od razu zasila stały podgląd 3D w panelu
     elementu. Dokument jest najpierw `recompute()`-owany (potrzebne, żeby kontenery/linki
     złożenia zgłosiły swoją faktyczną geometrię zamiast nieaktualnego, pustego compoundu),
     potem zbierana jest geometria z jawnym wykluczeniem obiektów konstrukcyjnych/
     odniesienia (origin, osie, płaszczyzny); jeśli obecny jest obiekt-kontener
     (`Assembly::AssemblyObject`, `App::Part`, `PartDesign::Body` — czyli to złożenie, nie
     goła część), eksportowana jest TYLKO gotowa, już poprawnie ułożona bryła tego
     kontenera (eksport też jego dzieci-linków zdublowałby geometrię każdej części), w
     przeciwnym razie każdy pozostały widoczny obiekt z bryłą łączony jest w jeden compound.
     Ta bryła eksportowana jest bezpośrednio do STEP (nie przez ogólną funkcję FreeCAD
     `Part.export`, która cicho odrzuca obiekty złożenia/linków komunikatem "is not a
     shape", mimo że mają zupełnie poprawną geometrię — to był realny, naprawiony błąd:
     złożenia potrafiły wyprodukować plik STEP zupełnie bez geometrii w środku).
   - **PDF**: korzysta z `Gui.export(...)` FreeCAD na tych samych widocznych obiektach —
     to nie jest typowa dla FreeCAD ścieżka PDF oparta o rysunki TechDraw, więc traktuj to
     jako rozwiązanie best-effort dla zwykłej Części/Bryły/złożenia, nie gwarantowany
     mechanizm; potwierdzone jako działające w praktyce, ale jeśli kiedyś cicho zawiedzie
     dla konkretnego dokumentu, to pierwsze miejsce do sprawdzenia.
   - Każdy z załączników **zastępuje** poprzedni z tą samą rolą (usuwany przed wysyłką
     nowego), żeby podgląd zawsze pokazywał aktualną rewizję. Jeśli dokument nie ma żadnej
     widocznej bryły (sam szkic, pusty dokument) albo eksport/wysyłka się nie powiedzie —
     ten jeden załącznik jest **cicho pomijany**, nie przerywa ani nie cofa reszty wysyłki
     (plik `.FCStd` w PDM jest już bezpiecznie zapisany w tym momencie).

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
  pasującej do numeru/nazwy pliku istniejącego elementu PDM (to samo sprawdzenie
  `match_existing_item` co lokalny skrót na górnym poziomie, krok 1b wyżej) — taki
  komponent jest czysto **referencyjny**: żadnej wysyłki, żadnego biletu przeglądarki,
  nic dla niego nie jest wysyłane — tylko podpinany do BOM-u z wyliczoną ilością, po
  swoim istniejącym ID.
- **Nowe komponenty też idą przez przeglądarkę, jeden po drugim** — każdy jeszcze
  nierozpoznany plik dostaje WŁASNY bilet przeglądarki (ten sam wybór Nowy element/
  Duplikuj/Dograj do istniejącego, wraz z własnymi checkboksami STEP/PDF, co dokument
  główny), otwierany sekwencyjnie, nigdy kilka kart naraz, liście najpierw. Ponieważ
  tylko jedna karta przeglądarki na uruchomienie makra może niezawodnie przejąć fokus
  Windows, tuż przed każdą kolejną kartą (po pierwszej) pojawia się natywny `MsgBox`
  "kliknij OK, aby kontynuować" — kliknięcie liczy się jako świeże działanie użytkownika,
  które pozwala kolejnemu oknu przeglądarki przejąć fokus zamiast otworzyć się cicho w
  tle (sprawdź pasek zadań, jeśli krok wygląda na zawieszony). Podpowiadany typ (Część/
  Złożenie) wstępnie wypełnia formularz w przeglądarce na podstawie tego, czy dany plik
  sam ma dalsze linki.
- **Nowo utworzone komponenty są ukrywane z korzenia drzewa projektu** po podpięciu do
  swojego właściwego rodzica (`PATCH /items/{id}/visibility {showInTree: false}`) —
  dotyczy to tylko komponentów faktycznie utworzonych W TYM przebiegu przez własny bilet;
  już istniejący, czysto referencyjny komponent (poprzedni punkt) zachowuje jakąkolwiek
  widoczność już miał, bo może być celowo niezależnym wpisem katalogu używanym też
  gdzie indziej.
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
  ręcznie, część po części, tak jak dotychczas). Obejmuje to też natywny workbench
  Assembly FreeCAD, o ile jego komponenty są osobnymi zapisanymi dokumentami (typowy i
  potwierdzony jako działający sposób budowania w nim złożenia) — jego własne kontenery
  (`Assembly::AssemblyObject`) i więzy nie mają osobnego pliku i są poprawnie pomijane
  przez przechodzenie wykrywające, liczą się tylko ich dzieci `App::Link`.
- **Eksport PDF jest rozwiązaniem best-effort** (patrz krok 5 wyżej, `Gui.export(...)`) —
  nie korzysta z typowej dla FreeCAD ścieżki PDF opartej o TechDraw, więc wyniki mogą się
  różnić między wersjami FreeCAD i typami dokumentu, mimo że potwierdzone jako działające
  w praktyce.
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

## Status

**Zweryfikowane na żywo od początku do końca na prawdziwym FreeCAD**, w kilku rundach
testów i poprawek (ostatnio 2026-08-27): logowanie, przepływ przez bilet w przeglądarce
(nowy element/duplikat/dograj do istniejącego) zarówno dla dokumentu głównego, jak i dla
każdego komponentu złożenia z osobna, natywny skrót "już rozpoznany dokument" (krok 1b),
Save As pod nazwą PDM z poprawnie rozwiązującymi się odnośnikami `App::Link` po
późniejszym pobraniu, eksport STEP (w tym naprawa dla dokumentów natywnego workbencha
Assembly, które wcześniej produkowały plik STEP bez żadnej geometrii) i eksport PDF —
wszystko potwierdzone na żywym serwerze przez użytkownika, nie tylko przeglądem
statycznym (w środowisku, w którym te pliki są edytowane, wciąż nie ma dostępnego
FreeCAD, więc każda poprawka tutaj wzięła się z tego, że użytkownik odtworzył problem na
żywo — w jednym przypadku wklejając wynik z Panelu raportu FreeCAD do diagnozy linia po
linii — nie z etapu budowania/testów).

Wcześniej w historii tego pliku, leżąca u podstaw logika logowania/biletów/rewizji była
też weryfikowana automatycznie przez `freecadcmd` przeciwko żywemu `EasyPDM.Api`
(obsługa sesji, numeracja `revision_label()`, pełny cykl rewizji A→B, walidacja ścieżek
w endpoincie rejestracji, kolejność liście-najpierw i sumowanie ilości przy
automatycznym wykrywaniu złożenia) — patrz historia gita w okolicach 2026-08-2x po pełną
listę, ponieważ dokładnie ten natywny dialog, który te testy sprawdzały
(`PdmUploadDialog`/`PartPropertyForm`), pochodzi sprzed przejścia na tworzenie elementów
przez przeglądarkę opisane wyżej i nie istnieje już w tym pliku w tamtej formie —
leżące u podstaw zachowanie warstwy danych, które te testy weryfikowały (rewizje,
krawędzie BOM, rejestracja w magazynie), pozostaje niezmienione.

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

⚠️ **Jeszcze niepotwierdzone na żywo na FreeCAD**, w odróżnieniu od `EasyPDMUpload.FCMacro`
wyżej (które jest, w kilku rundach testów). To makro dzieli ten sam, leżący u podstaw
mechanizm biletów przeglądarki (`GET /api/auth/browser-login`, `GET
/api/create-tickets/{ticket}` + `POST /create-tickets/{ticket}/attach-existing`, pasek
"oczekujące żądanie z makra" w aplikacji webowej) już przećwiczony na żywo przez
`EasyPDMUpload.FCMacro`, więc sama hydraulika biletu/okna oczekiwania nie jest niczym
nowym ani nietypowym — ale specyfika TEGO makra (rekurencyjne pobieranie komponentów,
wykrywanie już-pobrane/nieaktualna-rewizja po nazwie pliku, otwieranie wyniku przez
`App.openDocument`) była weryfikowana tylko: składniowo (`ast.parse`), pod kątem
poprawności polskich znaków (skrypt sprawdzający częstość znaków — zero zniekształceń)
i przez uważny przegląd logiki względem rzeczywistych endpointów API (`GET /api/items`,
`/items/{id}/attachments`, `/items/{id}/children`, `/attachments/{id}/file`, każdy
sprawdzony w kodzie `EasyPDM.Api`). Przy pierwszym uruchomieniu na żywym FreeCAD obserwuj
przebieg (log w oknie na końcu i w konsoli raportów FreeCAD) i zgłoś, co nie zagra —
najbardziej ryzykowne miejsca to: rozpoznawanie nazw załączników regexem (jeśli plik ma
nietypową nazwę) i to, czy odnośniki `App::Link` w pobranym złożeniu faktycznie rozwiążą
się automatycznie po umieszczeniu wszystkich plików w jednym, płaskim folderze (zależy od
tego, jak zapisane są ścieżki linków w oryginalnym pliku — patrz "Ograniczenia" wyżej).
