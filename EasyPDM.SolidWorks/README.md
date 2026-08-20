# EasyPDM — makra SolidWorks

Dwa makra `.bas`, każde w pełni samodzielne (osobny plik, osobny moduł VBA, bez zależności
między sobą poza współdzielonym miejscem w rejestrze Windows na sesję logowania):

- **`EasyPDMUpload.bas`** — wysyła aktywny dokument SolidWorks wprost do EasyPDM, bez
  przechodzenia przez przeglądarkę.
- **`EasyPDMDownload.bas`** — pobiera Część/Złożenie z EasyPDM (wraz ze wszystkimi
  składnikami złożenia) i otwiera je w SolidWorks.

## Status

**`EasyPDMUpload.bas`: zweryfikowane na żywo** (SolidWorks 2026, 2026-08-20) — pełny
przebieg: logowanie, utworzenie nowego elementu w PDM, wysłanie pliku. Po drodze
znalezione i naprawione (w kodzie w repo) realne błędy, których nie dało się wykryć bez
prawdziwego SolidWorks — zob. "Historia naprawionych problemów" niżej.

**`EasyPDMDownload.bas`: niezweryfikowane** — napisane analogicznie do już
zweryfikowanego `EasyPDMUpload.bas` (ta sama, sprawdzona infrastruktura logowania/JSON/
HTTP, ten sam styl kodu), ale sama logika pobierania (w tym wywołanie
`swApp.OpenDoc6` otwierające pobrany plik) nie przeszła jeszcze żadnego realnego testu.
Przed pierwszym użyciem: zaimportuj do edytora VBA (pokaże błędy składniowe od razu) i
przetestuj na nieistotnym elemencie/folderze testowym.

## Różnice względem makr FreeCAD

To **odpowiedniki**, nie port 1:1 — VBA nie ma wbudowanego JSON ani okien dialogowych bez
osobnych plików binarnych (SolidWorks/VBA UserForm), więc kilka rzeczy jest rozwiązanych
inaczej:

- **JSON**: własny, minimalny parser/budowniczy w samym makrze (zduplikowany w obu plikach,
  nie współdzielony — VBA nie ma niezawodnego sposobu importowania jednego modułu z
  drugiego) — wystarczający do kształtów odpowiedzi tego konkretnego API, nie ogólnego
  przeznaczenia.
- **Okna**: zwykłe `InputBox`/`MsgBox` zamiast rozwijanych list i formularzy Qt z makr
  FreeCAD. Hasła nie da się zamaskować gwiazdkami zwykłym `InputBox` — wpisywane jest
  jawnym tekstem (widocznym na ekranie, niezapisywanym nigdzie poza samym oknem). Wybór
  elementu w PDM to zwykłe pytanie o numer (`InputBox`), nie wyszukiwarka z podpowiedziami.
- **Rozpoznawanie "już wysłanego" dokumentu** (`EasyPDMUpload.bas`): NIE przez etykietę/
  nazwę pliku (SolidWorks nie ma odpowiednika swobodnej etykiety FreeCAD) — przez
  **Właściwości niestandardowe** dokumentu (`EasyPDM_ItemId`, `EasyPDM_ItemNumber`),
  zapisywane w samym pliku po udanej wysyłce. To w rzeczywistości **trwalsze** podejście
  niż w FreeCAD — działa też w NOWEJ sesji SolidWorks.
- **Świadomie pominięte**: automatyczne wykrywanie i wysyłanie całego drzewa złożenia
  naraz przy uploadzie (odpowiednik `App::Link`/`discover_component_tree` z FreeCAD, oparty
  o `IAssemblyDoc`/`IComponent2` po stronie SolidWorks) — `EasyPDMUpload.bas` wysyła
  **jeden aktywny dokument na raz**; strukturę BOM (podpięcie komponentów pod złożenie)
  buduje się w aplikacji webowej. `EasyPDMDownload.bas` NIE ma tego ograniczenia w drugą
  stronę — pobieranie złożenia ściąga cały jego BOM rekurencyjnie (patrz niżej), bo do
  tego wystarczy tylko czytanie API, bez integracji z `IAssemblyDoc`.

## Co robi `EasyPDMUpload.bas`

1. **Logowanie** — przy pierwszym uruchomieniu (albo gdy zapisana sesja wygasła/została
   unieważniona) pyta o adres API, nazwę użytkownika i hasło. Token sesji zapisywany jest
   w rejestrze Windows (`HKEY_CURRENT_USER\Software\VB and VBA Program Settings\EasyPDM`)
   przez wbudowane `SaveSetting`/`GetSetting` — kolejne uruchomienia (także po restarcie
   SolidWorks, także z `EasyPDMDownload.bas` — sesja jest współdzielona) nie proszą o
   ponowne logowanie, dopóki sesja jest ważna (30 dni).
2. **Zapisuje aktywny dokument**, jeśli jeszcze nie był zapisany (standardowe okno "Zapisz
   jako" SolidWorks).
3. Sprawdza **Właściwości niestandardowe** dokumentu — jeśli dokument był już kiedyś
   wysłany tym makrem (ma zapisane `EasyPDM_ItemId`), od razu proponuje dogranie nowej
   wersji do tego elementu.
4. W przeciwnym razie pyta: **nowy element w PDM**, czy **już istniejący** (wyszukiwany po
   numerze widocznym w PDM, np. "67" z nazwy "67 (Nazwa)").
   - **Nowy**: projekt PDM (lista numerowana), nazwa (domyślnie nazwa pliku), typ
     (Część/Złożenie/Plik — zgadywany z typu dokumentu SolidWorks), rodzaj i zależne od
     niego pola — te same reguły co `PartPropertyForm`/`add-node-dialog` w aplikacji
     webowej (Wykonywana → Materiał; Zakupowa → Producent/Numery zamówieniowe/Masa;
     Normalia → Materiał/Norma; Klienta → brak dodatkowych pól; dla Złożenia rodzaj jest
     opcjonalny, bez "Klienta", Materiał/Masa zawsze widoczne).
   - **Istniejący ze statusem "Wydany"**: pyta o zgodę na nową rewizję i opcjonalny
     komentarz — dokładnie ten sam mechanizm co w aplikacji webowej.
5. **Kopiuje** bieżący plik dokumentu do PDM pod nazwą `numer (nazwa).REWIZJA.rozszerzenie`
   (ta sama konwencja co w aplikacji webowej i makrach FreeCAD). **Lokalny plik NIE jest
   ruszany** — nie jest ani przenoszony, ani usuwany. Jeśli magazyn PDM jest widoczny
   z tej maszyny (`GET /api/config`), kopia trafia do wspólnego `storage/components/` i
   jest **rejestrowana** bez ponownego przesyłania przez HTTP (zachowuje historię
   rewizji); jeśli nie (typowe, gdy SolidWorks i usługa EasyPDM działają jako różni
   użytkownicy Windows — magazyn jest w `C:\ProgramData\...`, do którego zwykły
   użytkownik zwykle nie ma prawa zapisu), zwykły upload HTTP (fallback bez zachowania
   historii rewizji) — **to nie błąd**, tylko poprawnie zadziałane zabezpieczenie.
6. Zapisuje `EasyPDM_ItemId`/`EasyPDM_ItemNumber` we Właściwościach niestandardowych
   dokumentu i pokazuje potwierdzenie.

## Co robi `EasyPDMDownload.bas`

1. **Logowanie** — jak wyżej (sesja współdzielona z `EasyPDMUpload.bas`).
2. Pyta o **numer elementu w PDM** (Część/Złożenie) i **folder docelowy** (domyślnie
   podpowiada ostatnio użyty — ta sama preferencja co folder docelowy w
   `EasyPDMUpload.bas`, więc wysłane i pobrane pliki mogą lądować w jednym miejscu).
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

## Historia naprawionych problemów (z pierwszego realnego testu `EasyPDMUpload.bas`)

Znalezione i naprawione 2026-08-20 na żywym SolidWorks 2026 — zostawione tu jako
udokumentowany przykład tego, na co uważać przy weryfikacji `EasyPDMDownload.bas` (który
korzysta z tej samej, już poprawionej infrastruktury, ale ma też WŁASNY, jeszcze
niesprawdzony kod — regex, rekurencyjne pobieranie, `OpenDoc6`):

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
   niepoprawny") przy uploadzie przez fallback HTTP. Naprawione przez opakowanie bajtów
   w binarny `ADODB.Stream` i wysłanie strumienia zamiast tablicy.

## Znane ryzyka / miejsca do sprawdzenia w pierwszej kolejności (`EasyPDMDownload.bas`)

Nieprzetestowane jeszcze fragmenty, specyficzne dla tego pliku (reszta infrastruktury —
logowanie/JSON/HTTP — jest tą samą, już zweryfikowaną bazą co w `EasyPDMUpload.bas`):

1. **`swApp.OpenDoc6`** — sygnatura (`FileName, Type, Options, Configuration, Errors,
   Warnings`) jest dobrze udokumentowanym, standardowym API, ale nie została przetestowana
   na żywo. `Options = 0` (brak specjalnych flag) powinno być bezpieczną wartością
   domyślną.
2. **`VBScript.RegExp`** (`NewRevisionRegex`) — używane do rozpoznawania konwencji nazw
   plików (`numer (nazwa).REWIZJA.rozszerzenie`) przy wykrywaniu aktualnej rewizji i
   starszych lokalnych kopii. Standardowy, stabilny mechanizm, ale nieprzetestowany w
   tym konkretnym zastosowaniu.
3. **Rekurencyjne pobieranie składników złożenia** (`DownloadChildrenRecursive`) —
   analogiczne do `_download_children_recursive` w `EasyPDMDownload.FCMacro` (w tym ten sam
   kształt odpowiedzi `GET /items/{id}/children`: element zagnieżdżony pod kluczem
   `"item"`), ale nieprzetestowane po stronie SolidWorks.
4. **`EnsureDirectory`** — ręczna implementacja tworzenia zagnieżdżonych folderów (VBA
   `MkDir` tworzy tylko jeden poziom naraz, w odróżnieniu od `os.makedirs`) — prosta
   logika, ale warto sprawdzić na ścieżce z kilkoma nieistniejącymi poziomami naraz.

## Ograniczenia (świadomie poza zakresem tej wersji)

- `EasyPDMUpload.bas`: brak automatycznego wykrywania i wysyłania całego drzewa złożenia
  naraz — strukturę BOM buduje się ręcznie w aplikacji webowej.
- `EasyPDMDownload.bas`: nie próbuje pobierać KONKRETNEJ starszej rewizji — zawsze celuje
  w aktualną. Wszystkie pliki (główny + składniki) lądują płasko w jednym folderze, bez
  odtwarzania struktury BOM jako podfolderów.
- Hasło przy logowaniu nie jest maskowane (zwykły `InputBox`, bez własnego `UserForm`).
- Wybór projektu/elementu przez numer w liście tekstowej, nie przez wyszukiwarkę z
  podpowiedziami jak w FreeCAD.
- Kopiowanie/rejestrowanie/pobieranie pliku przez `storage/` zakłada, że ten folder jest
  widoczny w systemie plików tej maszyny — tak samo jak w makrach FreeCAD.
