# EasyPDM — makra SolidWorks

[English](README.md) | **Polski** | [Deutsch](README.de.md)

Dwa makra `.bas`, każde w pełni samodzielne (osobny plik, osobny moduł VBA, bez zależności
między sobą poza współdzielonym miejscem w rejestrze Windows na sesję logowania):

- **`EasyPDMUpload.bas`** — wysyła aktywny dokument SolidWorks do EasyPDM. Wybór
  projektu/nowy-czy-istniejący/duplikuj/właściwości elementu odbywa się w przeglądarce
  (ten sam wzorzec co makra FreeCAD), poza jednym wyjątkiem — patrz "Różnice względem
  makr FreeCAD" niżej.
- **`EasyPDMDownload.bas`** — pobiera Część/Złożenie z EasyPDM (wraz ze wszystkimi
  składnikami złożenia) i otwiera je w SolidWorks; wybór KTÓREGO elementu też odbywa się
  w przeglądarce.

## Status

**Zweryfikowane na żywo od początku do końca na prawdziwym SolidWorks 2026**, w kilku
rundach testów i poprawek (od 2026-08-20, ostatnio 2026-08-27): logowanie, przepływ przez
bilet w przeglądarce (nowy element/duplikat/dograj do istniejącego), upload/rejestracja
pliku, eksport załączników STEP/PDF, automatyczne wykrywanie drzewa złożenia z biletami
w przeglądarce per komponent, oraz rekurencyjne pobieranie komponentów w
`EasyPDMDownload.bas` — wszystko potwierdzone na żywym serwerze, nie tylko przeglądem
statycznym (w środowisku, w którym te pliki są edytowane, wciąż nie ma kompilatora VBA,
więc każda poprawka tutaj wzięła się z tego, że użytkownik odtworzył problem na żywo i
wkleił log samego makra, nie z etapu budowania). Zob. "Historia naprawionych problemów"
niżej po konkretne błędy znalezione tym sposobem.

Kilka wąskich, rzadko używanych fragmentów kodu pozostaje faktycznie nieprzetestowanych
w praktyce (oznaczone `UNVERIFIED` bezpośrednio w kodzie) — zob. "Znane ryzyka" niżej.

## Różnice względem makr FreeCAD

To **odpowiedniki**, nie port 1:1 — VBA nie ma wbudowanego JSON ani okien dialogowych bez
osobnych plików binarnych (SolidWorks/VBA UserForm), więc kilka rzeczy jest rozwiązanych
inaczej:

- **JSON**: własny, minimalny parser/budowniczy w samym makrze (zduplikowany w obu plikach,
  nie współdzielony — VBA nie ma niezawodnego sposobu importowania jednego modułu z
  drugiego) — wystarczający do kształtów odpowiedzi tego konkretnego API, nie ogólnego
  przeznaczenia.
- **Brak `UserForm`** — zamiast Qt-owych formularzy FreeCAD, wszystkie natywne okna to
  zwykłe `InputBox`/`MsgBox` (`UserForm` to osobny plik binarny, nie da się go dołączyć do
  pojedynczego `.bas` importowanego przez "Plik → Importuj plik..."). Hasła przy logowaniu
  nie da się zamaskować gwiazdkami zwykłym `InputBox`. Czekanie na przeglądarkę (patrz
  niżej) pokazuje postęp w pasku stanu SolidWorks zamiast w oknie z przyciskiem Anuluj —
  Escape jest jedynym dostępnym gestem anulowania.
- **Wybór projektu/nowy-czy-istniejący/duplikuj/właściwości elementu w przeglądarce** —
  ten sam wzorzec bilet+`GET /api/auth/browser-login`+popup "oczekujące żądanie z makra
  CAD" co makra FreeCAD, **z jednym świadomym wyjątkiem**: jeśli dokument jest JUŻ
  podpięty do elementu PDM (patrz punkt niżej), makro NIE otwiera przeglądarki wcale —
  pyta lokalnie (natywny `MsgBox`) o zgodę na nową rewizję, plus czy eksportować STEP
  i/lub PDF (patrz niżej), dokładnie jak przed tą zmianą. SolidWorks zna wtedy element ze
  100% pewnością, więc przeglądarka nic by tu nie dodała; FreeCAD zawsze idzie do
  przeglądarki (albo, od niedawna, najpierw pyta natywnie, gdy znajdzie dopasowanie po
  etykiecie), bo nie ma równie niezawodnego mechanizmu lokalnego.
- **Rozpoznawanie "już wysłanego" dokumentu** (`EasyPDMUpload.bas`): NIE przez etykietę/
  nazwę pliku (SolidWorks nie ma odpowiednika swobodnej etykiety FreeCAD) — przez
  **Właściwości niestandardowe** dokumentu (`EasyPDM_ItemId`, `EasyPDM_ItemNumber`),
  zapisywane w samym pliku po udanej wysyłce. To w rzeczywistości **trwalsze** podejście
  niż w FreeCAD — działa też w NOWEJ sesji SolidWorks, i jest też wykorzystywane dla
  KAŻDEGO komponentu złożenia z osobna (patrz "wykrywanie drzewa złożenia" niżej), nie
  tylko dla dokumentu głównego.
- **Nowe komponenty złożenia też idą przez przeglądarkę, jeden po drugim** — każdy
  jeszcze niepodpięty komponent znaleziony podczas przechodzenia drzewa otwiera własny
  bilet + kartę przeglądarki (liście najpierw, ten sam wybór Nowy/Duplikuj/Dograj do
  istniejącego co dokument główny), sekwencyjnie, nigdy kilka kart naraz — to ten sam
  wzorzec, do którego później dostosowano makro FreeCAD. Ponieważ tylko jedna karta
  przeglądarki na uruchomienie makra może niezawodnie przejąć fokus Windows, tuż przed
  otwarciem każdej kolejnej karty pojawia się natywny `MsgBox` "kliknij OK, aby
  kontynuować" — kliknięcie liczy się jako świeże działanie użytkownika, które pozwala
  kolejnemu oknu przeglądarki przejąć fokus zamiast otworzyć się cicho w tle (sprawdź
  pasek zadań, jeśli krok wygląda na zawieszony).
- **Eksport STEP i PDF są opcjonalne wszędzie** — checkbox na każdy z nich na ścieżkach
  przez bilet w przeglądarce (nowy element/duplikat/dograj do istniejącego, zarówno dla
  dokumentu głównego, jak i każdego komponentu złożenia), albo dwa natywne pytania
  Tak/Nie (`ExportStepPrompt`/`ExportPdfPrompt`, STEP domyślnie Tak, PDF domyślnie Nie)
  na ścieżce bez przeglądarki "już podpięty". Eksport PDF korzysta z własnego "Zapisz
  jako PDF" SolidWorks, oznaczony rolą załącznika `"pdf"`, niezależną od załącznika STEP.

## Co robi `EasyPDMUpload.bas`

1. **Logowanie** — przy pierwszym uruchomieniu (albo gdy zapisana sesja wygasła/została
   unieważniona) pyta o adres API, nazwę użytkownika i hasło. Token sesji zapisywany jest
   w rejestrze Windows (`HKEY_CURRENT_USER\Software\VB and VBA Program Settings\EasyPDM`)
   przez wbudowane `SaveSetting`/`GetSetting` — kolejne uruchomienia (także po restarcie
   SolidWorks, także z `EasyPDMDownload.bas` — sesja jest współdzielona) nie proszą o
   ponowne logowanie, dopóki sesja jest ważna (30 dni).
2. **Zapisuje aktywny dokument**, jeśli jeszcze nie był zapisany (standardowe okno "Zapisz
   jako" SolidWorks).
3. **Jeśli aktywny dokument to Złożenie**: najpierw rozwiązuje wszystkie komponenty
   Lightweight (`ResolveAllLightWeightComponents`, wywołane bezpośrednio na modelu
   złożenia — wywołanie przez `.Extension` zamiast tego cicho nic nie robi i było
   prawdziwym błędem złapanym podczas testów: `GetModelDoc2()` zwraca `Nothing` dla
   wciąż-Lightweight komponentu, przez co nie da się go odróżnić od "komponentu nie
   znaleziono" dopóki nie zostanie rozwiązany), potem wykrywa drzewo komponentów
   (`IAssemblyDoc.GetComponents`, rekurencyjnie). Podsumowujący `MsgBox` przed
   przejściem drzewa wypisuje każdy znaleziony komponent, oznaczając już podpięte ich
   docelowym numerem elementu PDM i nazwą pliku — świadome zabezpieczenie, bo własne
   "Zapisz jako" SolidWorks cicho kopiuje Właściwości niestandardowe, co może sprawić,
   że naprawdę nowa część fałszywie "rozpozna się" jako istniejący element, jeśli nie
   zwróci się uwagi na to podsumowanie. Po potwierdzeniu przechodzi liśćmi najpierw (ten
   dokument na końcu); dla każdego JESZCZE niepodpiętego komponentu otwiera własny bilet
   w przeglądarce (ten sam wybór Nowy/Duplikuj/Dograj do istniejącego co dokument główny,
   patrz "Różnice względem makr FreeCAD" po opis natywnego `MsgBox` przed każdą kartą).
   Nowo utworzone komponenty dostają własny eksport STEP/PDF (wg wyboru checkboxa tego
   konkretnego komponentu w przeglądarce) i wpis `EasyPDM_ItemId`, i są automatycznie
   podpinane pod swojego rodzica w strukturze BOM. **Już podpięte komponenty są tylko
   referencjonowane**, nigdy nie wysyłane ponownie — niezależnie od statusu — tylko
   podpinane do BOM-u z wyliczoną ilością; jeśli któryś ma aktualnie status "Sprawdzany"
   albo "Wydany", jest to też wypisane w końcowym komunikacie sukcesu, jako przypomnienie
   że ewentualne lokalne zmiany w nim NIE zostały wysłane. **Komponenty usunięte ze
   złożenia od poprzedniej wysyłki są też oznaczane** — dla każdego rodzica (dokumentu
   głównego i każdego pod-złożenia), przed podpięciem jego aktualnych lokalnych dzieci,
   makro sprawdza czy PDM nadal ma relację BOM do dziecka, którego nie ma już lokalnie,
   i pyta natywnie o potwierdzenie przed usunięciem tej relacji (same elementy nigdy nie
   są kasowane, tylko ich podpięcie pod tego konkretnego rodzica).
4. Sprawdza **Właściwości niestandardowe** dokumentu głównego:
   - **Już podpięty** (ma zapisane `EasyPDM_ItemId`) — pyta lokalnie o zgodę na dogranie
     bieżącej wersji jako nowej rewizji, bez otwierania przeglądarki (patrz "Różnice
     względem makr FreeCAD"), a potem jeszcze dwa natywne pytania Tak/Nie o eksport STEP
     (domyślnie Tak) i PDF (domyślnie Nie).
   - **Jeszcze niepodpięty** — otwiera przeglądarkę systemową (już zalogowaną, most
     token→ciasteczko) na popupie "oczekujące żądanie z makra CAD", z trzema opcjami do
     wyboru TAM: **Nowy element** (projekt, opcjonalnie rodzic, typ, nazwa — dla Części
     dodatkowo rodzaj i zależne od niego pola: Wykonywana → Materiał; Zakupowa →
     Producent/Numery zamówieniowe/Masa; Normalia → Materiał/Norma; Klienta → brak
     dodatkowych pól; **Złożenie nie ma rodzaju w ogóle** — tylko opcjonalna Masa — plus
     checkboxy eksportu STEP i PDF), **Duplikuj** (wskazuje istniejący element, kopiuje
     jego właściwości do nowego, bez plików) albo **Dograj do istniejącego**
     (wyszukiwarka po całej bazie + te same checkboxy STEP/PDF). Makro czeka (odpytuje
     co ~2s, limit 10 minut, Escape anuluje, postęp w pasku stanu SolidWorks) i
     kontynuuje automatycznie, gdy wybór zostanie zatwierdzony w przeglądarce.
   - **Istniejący element ze statusem "Wydany"** (obie ścieżki wyżej): pyta o zgodę na
     nową rewizję i opcjonalny komentarz — dokładnie ten sam mechanizm co w aplikacji
     webowej, jedyna decyzja świadomie zostająca lokalna nawet na ścieżce przez
     przeglądarkę.
   - **Istniejący element ze statusem "Sprawdzany"**: dogranie jest zamiast tego **twardo
     blokowane** natywnym komunikatem błędu — makro NIE cofa cicho statusu do "W pracy" i
     nie wysyła mimo to (to był realny, naprawiony błąd: recenzowanie elementu i ciche
     zresetowanie go przez ponowną wysyłkę spod nóg recenzenta). Osoba recenzująca musi
     najpierw sama przenieść element poza status "Sprawdzany" w aplikacji webowej.
5. **Kopiuje** bieżący plik dokumentu do PDM pod nazwą `numer (nazwa).REWIZJA.rozszerzenie`
   (ta sama konwencja co w aplikacji webowej i makrach FreeCAD). **Lokalny plik NIE jest
   ruszany** — nie jest ani przenoszony, ani usuwany. Jeśli magazyn PDM jest widoczny
   z tej maszyny (`GET /api/config`), kopia trafia do wspólnego `storage/components/` i
   jest **rejestrowana** bez ponownego przesyłania przez HTTP (zachowuje historię
   rewizji); jeśli nie (typowe, gdy SolidWorks i usługa EasyPDM działają jako różni
   użytkownicy Windows — magazyn jest w `C:\ProgramData\...`, do którego zwykły
   użytkownik zwykle nie ma prawa zapisu), zwykły upload HTTP (fallback bez zachowania
   historii rewizji, korzystający specjalnie do tego wywołania z
   `WinHttp.WinHttpRequest.5.1` — zob. "Historia naprawionych problemów" niżej) — **to
   nie błąd**, tylko poprawnie zadziałane zabezpieczenie. Ta ścieżka zapasowa sprawdza
   też i kasuje istniejący załącznik z rolą `"cad"` o dokładnie tej samej nazwie pliku
   przed wgraniem nowego, więc ponowny zapis przy tej samej literze rewizji nie
   gromadzi zduplikowanych załączników.
6. Gdy eksport STEP/PDF jest włączony (checkbox w przeglądarce, albo natywne pytania na
   ścieżce bez przeglądarki — patrz p.4): eksportuje widoczną geometrię do tymczasowego
   pliku `.step`/`.pdf` (`IModelDocExtension.SaveAs`) i wgrywa jako załącznik z rolą
   `"step"`/`"pdf"`, zastępując poprzedni załącznik tej samej roli — zasila stały
   podgląd 3D (STEP) w aplikacji webowej. Błąd eksportu (np. brak widocznej geometrii)
   NIE przerywa reszty operacji.
7. Zapisuje `EasyPDM_ItemId`/`EasyPDM_ItemNumber` we Właściwościach niestandardowych
   dokumentu i pokazuje potwierdzenie.

## Co robi `EasyPDMDownload.bas`

1. **Logowanie** — jak wyżej (sesja współdzielona z `EasyPDMUpload.bas`).
2. **Wybór elementu do pobrania odbywa się w przeglądarce** — ten sam popup "oczekujące
   żądanie z makra CAD" co przy wysyłaniu, tyle że od razu z samą wyszukiwarką (bez
   wyboru Nowy/Duplikuj, nieistotnego przy pobieraniu). Makro czeka tak samo jak przy
   wysyłaniu (Escape anuluje, limit 10 minut). Jedyne, co zostaje lokalnym `InputBox`, to
   **folder docelowy** (domyślnie podpowiada ostatnio użyty — ta sama preferencja co
   folder docelowy w `EasyPDMUpload.bas`, więc wysłane i pobrane pliki mogą lądować w
   jednym miejscu).
3. Dla Złożenia: pobiera też **wszystkie jego składniki rekurencyjnie** (bezpośrednie
   dzieci, potem ich dzieci, i tak dalej — cały BOM), do TEGO SAMEGO folderu co plik
   główny. Bez tego złożenie oparte o zewnętrzne odnośniki do zapisanych plików (typowe w
   SolidWorks) nie miałoby czym się otworzyć.
4. Dla każdego pliku: jeśli w folderze jest już plik o dokładnie tej samej nazwie (czyli
   tej samej rewizji) i tym samym rozmiarze co na serwerze — pomija (nie pobiera drugi
   raz). Jeśli jest plik tego elementu w innej (starszej) rewizji, a na serwerze jest
   nowsza — pyta, czy pobrać nowszą.
5. Na końcu otwiera główny (wybrany) plik w SolidWorks (`swApp.OpenDoc6`) — pliki
   składników zostają tylko na dysku, SolidWorks sam rozwiąże odnośniki złożenia do nich.

Skąd bierze pliki do pobrania: EasyPDM przechowuje aktualny plik CAD jako załącznik (nie
ma osobnego mechanizmu "plik elementu"), a poprzednie rewizje zostają jako osobne
załączniki obok — `EasyPDMDownload.bas` rozpoznaje konwencję nazw nadawaną przez
`EasyPDMUpload.bas`, żeby trafić w załącznik odpowiadający AKTUALNEJ rewizji; jeśli
element nigdy nie przeszedł przez żadne makro CAD (dograny ręcznie w aplikacji webowej),
bierze po prostu najnowiej wgrany załącznik.

## Jak sprawdzić, czy zadziałało

Trzy niezależne sposoby, od najszybszego do najbardziej szczegółowego:

1. **Komunikat na końcu** — po udanej operacji makro pokazuje okno z podsumowaniem (np.
   "Wysłano do EasyPDM: element #67 (rewizja B)." albo log pobierania). Okno z "Błąd: ..."
   oznacza, że coś nie wyszło.
2. **Aplikacja webowa** — najpewniejszy dowód dla uploadu: wejdź w projekt (albo "Cała
   baza"), znajdź element po numerze z komunikatu i sprawdź, czy ma dołączony plik (panel
   właściwości → Załączniki) i poprawne właściwości.
3. **Log makra** — każde uruchomienie dopisuje (nie nadpisuje) szczegółowy, ostemplowany
   czasem przebieg do zwykłego pliku tekstowego (osobny plik dla każdego makra, żeby się
   nie mieszały):

   ```
   %TEMP%\EasyPDM_macro.log            <- EasyPDMUpload.bas
   %TEMP%\EasyPDM_download_macro.log   <- EasyPDMDownload.bas
   ```

   (wklej `%TEMP%` w pasek adresu Eksploratora Windows, żeby tam trafić). Zawiera m.in.
   każde wywołanie API z kodem odpowiedzi (`GET /items -> 200`), wynik logowania, czy
   magazyn plików był widoczny z tej maszyny, czy plik skopiował się/zarejestrował/pobrał
   poprawnie, i pełną treść błędu, jeśli coś zawiodło. To pierwsze miejsce do sprawdzenia,
   gdy coś nie działa — ścieżka do niego jest też dopisana w oknie błędu/sukcesu na końcu.

## Instalacja

SolidWorks nie ma formatu makra czysto tekstowego (jak `.FCMacro` we FreeCAD) — makra to
projekty VBA. `.bas` to standardowy format eksportu/importu **modułu** VBA (nie całego
projektu makra), więc dla KAŻDEGO z dwóch plików osobno:

1. SolidWorks → **Narzędzia → Makro → Nowy...** — utwórz nowy (pusty) projekt makra i
   zapisz go (np. `EasyPDMUpload.swp` / `EasyPDMDownload.swp`).
2. W otwartym edytorze VBA: **Plik → Importuj plik...** → wskaż `EasyPDMUpload.bas` albo
   `EasyPDMDownload.bas`. Jeśli w projekcie jest jeszcze pusty, autogenerowany moduł
   (typowo `Module1`/`Upload1`) — usuń go (prawy klik w drzewku projektu → Remove... →
   No, gdy zapyta o eksport), żeby nie zostawić dwóch modułów naraz.
3. Uruchamiaj przez **Narzędzia → Makro → Uruchom** (wskazując zapisany plik `.swp`) albo
   bezpośrednio z edytora VBA (F5, **z kursorem wewnątrz `Sub main()`** — jeśli w projekcie
   są inne procedury, F5 uruchamia tę, w której akurat stoi kursor, nie zawsze `main`
   automatycznie).
4. Osobny `Sub Logout` (w każdym z modułów) wylogowuje z EasyPDM — można go przypiąć do
   własnego przycisku/skrótu w SolidWorks.

Adres API (domyślnie `http://localhost:5000/api`) zapisuje się automatycznie po pierwszym
podaniu przy logowaniu — wspólnie dla obu makr.

## Historia naprawionych problemów

Znalezione i naprawione 2026-08-20, podczas pierwszego realnego testu `EasyPDMUpload.bas`
na żywym SolidWorks 2026 — zostawione tu jako udokumentowany przykład tego rodzaju
specyficznych dla VBA pułapek, na jakie ten kod już trafiał (oba makra korzystają teraz z
tej samej, już poprawionej infrastruktury):

1. **Brak tokenu sesji w treści logowania** — `MSXML2.XMLHTTP.6.0` nie dawał pewnego
   dostępu do nagłówka `Set-Cookie`. Naprawione po stronie serwera (`POST /auth/login`
   dokłada `sessionToken` wprost do treści JSON) — obowiązuje dla obu makr.
2. **Niezadeklarowane stałe SolidWorks** (`swCustomInfoText`, `swCustomPropertyReplaceValue`,
   `swDocPART`, `swDocASSEMBLY`) — cały moduł celowo używa late bindingu (typ `Object`
   zamiast `SldWorks.*`), więc gołe nazwy enumów SolidWorks nie miały skąd wziąć wartości.
   Naprawione przez jawne stałe `Long` z udokumentowanymi wartościami z SolidWorks API,
   zgrupowane na samym początku modułu.
3. **`swApp` niezadeklarowane** — WBREW wcześniejszemu założeniu, `swApp` NIE jest
   automatycznie widoczne w każdym module VBA projektu, tylko w tym, który SolidWorks sam
   wygenerował przy "Nowy...". Zaimportowany moduł potrzebuje własnej deklaracji i
   przypisania (`Set swApp = Application.SldWorks`) na starcie `main()`.
4. **Mojibake polskich znaków** — VBA nie importował pliku `.bas` jako UTF-8; polskie
   znaki (w komentarzach ORAZ w oknach widocznych dla użytkownika) wychodziły jako
   krzaki. Naprawione przez przetłumaczenie całego pliku na czysty ASCII (angielski).
5. **`MSXML2.XMLHTTP.send()` odrzucał gołą tablicę `Byte()`** ("Parametr jest
   niepoprawny") przy uploadzie przez fallback HTTP. Pierwsza próba opakowała bajty w
   binarny `ADODB.Stream` wysyłany przez ten sam obiekt `MSXML2.XMLHTTP` — to samo w
   sobie nadal zawodziło na prawdziwym uploadzie z ogólnym błędem "BRAK POŁĄCZENIA".
   Faktyczna naprawa: `ApiUploadFile` przełączono konkretnie na
   `WinHttp.WinHttpRequest.5.1`, który przyjmuje tablicę `Byte()` bezpośrednio przez
   `.Send()`; każde inne wywołanie (`ApiGet`, logowanie itd.) nadal używa
   `MSXML2.XMLHTTP.6.0`.

Kolejne rundy testów na żywo (do 2026-08-27) znalazły i naprawiły jeszcze kilka spraw,
podsumowanych tu zbiorczo zamiast punkt po punkcie — pełne szczegóły w historii gita:
niewykrywane komponenty złożenia Lightweight (`ResolveAllLightWeightComponents` trzeba
wywołać bezpośrednio na modelu, nie przez `.Extension`, które cicho nic nie robi);
żądania GET `MSXML2.XMLHTTP.6.0` serwowane z lokalnego cache HTTP Windows w
nieskończoność, naprawione dodaniem nagłówków `Cache-Control`/`Pragma` plus parametru
`_ts=` do każdego wywołania `ApiGet` w OBU plikach (każdy ma własną, niezależną kopię
`ApiGet`, a `EasyPDMDownload.bas` potrzebował dodatkowo tej samej poprawki w
`ApiGetBinary`, bo nieaktualna odpowiedź z cache tam po cichu zepsułaby faktyczną
zawartość pobranego pliku, nie tylko pole statusu); nowy komponent złożenia pojawiający
się jako niechciany duplikat w korzeniu projektu, naprawione przez jawne ukrywanie
nowo utworzonych elementów-liści z korzenia drzewa po podpięciu do właściwego rodzica;
oraz powtarzające się załączniki roli "cad" gromadzące się przy każdym zapisie na tej
samej rewizji, naprawione przez sprawdzenie i skasowanie istniejącego załącznika o tej
samej nazwie pliku przed fallbackowym uploadem HTTP.

## Znane ryzyka / miejsca do sprawdzenia w pierwszej kolejności

Główne przepływy są już zweryfikowane na żywo (patrz "Status" wyżej). To, co pozostaje
faktycznie nieprzetestowane, to kilka wąskich, rzadko używanych fragmentów, oznaczonych
`UNVERIFIED` bezpośrednio w kodzie:

1. **Dokładna sygnatura parametrów `IModelDocExtension.SaveAs`** dla eksportu
   `.step`/`.pdf` (`UploadStepAttachment`/`UploadPdfAttachment`) — napisana z dokumentacji
   API SolidWorks; jeśli SolidWorks zgłosi błąd argumentów, sprawdzić w edytorze VBA (F1
   na `SaveAs`) dokładną sygnaturę zainstalowanej wersji.
2. **Ścieżka "zapisz w tym samym formacie"** dla dokumentu już raz zapisanego w tej
   sesji (pomija pełne okno Zapisz jako) — typowy przypadek "po prostu zapisz ponownie i
   uruchom makro" jest dobrze przećwiczony, ta konkretna gałąź mniej.
3. **Dokładna wartość stałej `SW_SAVE_AS_SILENT`**, używanej do wyciszenia natywnych
   okien potwierdzenia SolidWorks podczas wywołań `SaveAs` eksportu STEP/PDF.

Żadne z tych miejsc nie spowodowało jak dotąd zgłoszonej awarii — są tu wypisane jako
pierwsze miejsce do sprawdzenia, gdyby coś specyficznego dla wersji SolidWorks kiedyś
zawiodło przy eksporcie albo zapisie.

## Ograniczenia (świadomie poza zakresem tej wersji)

- `EasyPDMDownload.bas`: nie próbuje pobierać KONKRETNEJ starszej rewizji — zawsze celuje
  w aktualną. Wszystkie pliki (główny + składniki) lądują płasko w jednym folderze, bez
  odtwarzania struktury BOM jako podfolderów.
- Brak odpowiednika FreeCAD-owego "folderu docelowego na lokalne kopie" —
  `RenameAndUpload` kopiuje/wysyła plik prosto do magazynu PDM, nie zostawia zmienionej
  nazwy kopii obok plików roboczych użytkownika.
- Hasło przy logowaniu nie jest maskowane (zwykły `InputBox`, bez własnego `UserForm`).
- Folder docelowy pobierania to zwykły `InputBox` z tekstem ścieżki, nie przeglądarka
  systemowa plików.
- Kopiowanie/rejestrowanie/pobieranie pliku przez `storage/` zakłada, że ten folder jest
  widoczny w systemie plików tej maszyny — tak samo jak w makrach FreeCAD.
