# EasyPDM — makra SolidWorks

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

**Poprzednia wersja (bez przeglądarki, natywne `InputBox`/`MsgBox` do wyboru
projektu/elementu/rodzaju) była zweryfikowana na żywo** (SolidWorks 2026, 2026-08-20) —
zob. "Historia naprawionych problemów" niżej, wciąż aktualna dla współdzielonej
infrastruktury logowania/JSON/HTTP, którą obecna wersja w całości zachowuje.

**Obecna wersja (wzorzec przeglądarki + eksport STEP + automatyczne wykrywanie drzewa
złożenia) jest NIEZWERYFIKOWANA** — napisana bez dostępu do SolidWorks/kompilatora VBA w
środowisku, w którym powstała (w przeciwieństwie do makr FreeCAD, gdzie `py_compile` dawał
realną weryfikację składni, tu jedyna weryfikacja to ręczny przegląd kodu). Wymaga
pełnego testu na żywym SolidWorks przed użyciem produkcyjnym — zob. "Znane ryzyka" niżej,
gdzie są dokładnie wskazane najbardziej niepewne fragmenty.

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
  pyta lokalnie tylko o zgodę na nową rewizję, dokładnie jak przed tą zmianą. SolidWorks
  zna wtedy element ze 100% pewnością, więc przeglądarka nic by tu nie dodała; FreeCAD
  zawsze idzie do przeglądarki, bo nie ma tak niezawodnego mechanizmu lokalnego.
- **Rozpoznawanie "już wysłanego" dokumentu** (`EasyPDMUpload.bas`): NIE przez etykietę/
  nazwę pliku (SolidWorks nie ma odpowiednika swobodnej etykiety FreeCAD) — przez
  **Właściwości niestandardowe** dokumentu (`EasyPDM_ItemId`, `EasyPDM_ItemNumber`),
  zapisywane w samym pliku po udanej wysyłce. To w rzeczywistości **trwalsze** podejście
  niż w FreeCAD — działa też w NOWEJ sesji SolidWorks, i (nowość) jest teraz też
  wykorzystywane dla KAŻDEGO komponentu złożenia z osobna (patrz "wykrywanie drzewa
  złożenia" niżej), nie tylko dla dokumentu głównego.
- **Nowe komponenty złożenia zbierane sekwencją `InputBox`, nie przez przeglądarkę** —
  ten sam powód co natywny dialog komponentu w FreeCAD (N kart przeglądarki dla N nowych
  komponentów złożenia byłoby gorszym UX niż jeden natywny prompt na komponent), tym
  bardziej uzasadniony tutaj przez brak `UserForm` w ogóle w tym pliku.
- **Eksport STEP dla ścieżek bez przeglądarki eksportuje się zawsze** (dokument już
  podpięty; automatycznie wykryte komponenty złożenia) — nie ma tam formularza w
  przeglądarce, w którym mógłby siedzieć checkbox. Tylko ścieżka przez bilet (nowy
  element/duplikat/dograj do istniejącego) ma checkbox STEP w przeglądarce, tak jak w
  FreeCAD.

## Co robi `EasyPDMUpload.bas`

1. **Logowanie** — przy pierwszym uruchomieniu (albo gdy zapisana sesja wygasła/została
   unieważniona) pyta o adres API, nazwę użytkownika i hasło. Token sesji zapisywany jest
   w rejestrze Windows (`HKEY_CURRENT_USER\Software\VB and VBA Program Settings\EasyPDM`)
   przez wbudowane `SaveSetting`/`GetSetting` — kolejne uruchomienia (także po restarcie
   SolidWorks, także z `EasyPDMDownload.bas` — sesja jest współdzielona) nie proszą o
   ponowne logowanie, dopóki sesja jest ważna (30 dni).
2. **Zapisuje aktywny dokument**, jeśli jeszcze nie był zapisany (standardowe okno "Zapisz
   jako" SolidWorks).
3. **Jeśli aktywny dokument to Złożenie**: wykrywa jego drzewo komponentów
   (`IAssemblyDoc.GetComponents`, rekurencyjnie) i pyta, czy wysłać automatycznie razem z
   nim wszystkie komponenty, które NIE są jeszcze podpięte do PDM (rozpoznawane przez
   Właściwości niestandardowe na KAŻDYM komponencie z osobna, patrz niżej) — liście
   najpierw, ten dokument na końcu. Dla każdego nowego komponentu: krótka sekwencja
   `InputBox` (Projekt → Typ → Nazwa, a dla Części dodatkowo Rodzaj i pola zależne — te
   same reguły co niżej; Złożenie nie ma rodzaju, tylko opcjonalną Masę), NIE przeglądarka
   (patrz "Różnice względem makr FreeCAD"). Nowo utworzone komponenty
   od razu dostają eksport STEP i własny wpis `EasyPDM_ItemId`, i są automatycznie
   podpinane pod swojego rodzica w strukturze BOM.
4. Sprawdza **Właściwości niestandardowe** dokumentu głównego:
   - **Już podpięty** (ma zapisane `EasyPDM_ItemId`) — pyta lokalnie o zgodę na dogranie
     bieżącej wersji jako nowej rewizji, bez otwierania przeglądarki (patrz "Różnice
     względem makr FreeCAD"). Eksport STEP następuje zawsze.
   - **Jeszcze niepodpięty** — otwiera przeglądarkę systemową (już zalogowaną, most
     token→ciasteczko) na popupie "oczekujące żądanie z makra CAD", z trzema opcjami do
     wyboru TAM: **Nowy element** (projekt, opcjonalnie rodzic, typ, nazwa — dla Części
     dodatkowo rodzaj i zależne od niego pola: Wykonywana → Materiał; Zakupowa →
     Producent/Numery zamówieniowe/Masa; Normalia → Materiał/Norma; Klienta → brak
     dodatkowych pól; **Złożenie nie ma rodzaju w ogóle** — tylko opcjonalna Masa — plus
     checkbox eksportu STEP), **Duplikuj** (wskazuje istniejący element, kopiuje jego
     właściwości do nowego, bez plików) albo **Dograj do istniejącego** (wyszukiwarka po
     całej bazie + ten sam checkbox STEP). Makro czeka (odpytuje co ~2s, limit 10 minut,
     Escape anuluje, postęp w pasku stanu SolidWorks) i kontynuuje automatycznie, gdy
     wybór zostanie zatwierdzony w przeglądarce.
   - **Istniejący element ze statusem "Wydany"** (obie ścieżki wyżej): pyta o zgodę na
     nową rewizję i opcjonalny komentarz — dokładnie ten sam mechanizm co w aplikacji
     webowej, jedyna decyzja świadomie zostająca lokalna nawet na ścieżce przez
     przeglądarkę.
5. **Kopiuje** bieżący plik dokumentu do PDM pod nazwą `numer (nazwa).REWIZJA.rozszerzenie`
   (ta sama konwencja co w aplikacji webowej i makrach FreeCAD). **Lokalny plik NIE jest
   ruszany** — nie jest ani przenoszony, ani usuwany. Jeśli magazyn PDM jest widoczny
   z tej maszyny (`GET /api/config`), kopia trafia do wspólnego `storage/components/` i
   jest **rejestrowana** bez ponownego przesyłania przez HTTP (zachowuje historię
   rewizji); jeśli nie (typowe, gdy SolidWorks i usługa EasyPDM działają jako różni
   użytkownicy Windows — magazyn jest w `C:\ProgramData\...`, do którego zwykły
   użytkownik zwykle nie ma prawa zapisu), zwykły upload HTTP (fallback bez zachowania
   historii rewizji) — **to nie błąd**, tylko poprawnie zadziałane zabezpieczenie.
6. Gdy eksport STEP jest włączony (checkbox w przeglądarce, albo zawsze dla ścieżek bez
   przeglądarki — patrz p.4): eksportuje widoczną geometrię do tymczasowego pliku `.step`
   (`IModelDocExtension.SaveAs`) i wgrywa jako załącznik z rolą `"step"`, zastępując
   poprzedni załącznik tej samej roli — zasila stały podgląd 3D w aplikacji webowej.
   Błąd eksportu (np. brak widocznej geometrii) NIE przerywa reszty operacji.
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

## Znane ryzyka / miejsca do sprawdzenia w pierwszej kolejności

Nieprzetestowane jeszcze fragmenty — napisane bez dostępu do SolidWorks/kompilatora VBA,
więc nawet SKŁADNIA nie została sprawdzona automatycznie (w odróżnieniu od makr FreeCAD,
gdzie `py_compile` dawał realną weryfikację). Sugerowana kolejność testów: zwykła Część
jeszcze niepodpięta → ta sama Część ponownie (ścieżka natywna) → Dograj/Duplikuj w
przeglądarce → Złożenie z nowymi komponentami → Escape/timeout w trakcie oczekiwania →
`EasyPDMDownload.bas`.

**Współdzielone przez oba pliki (nowe w tej rundzie):**
1. **`WaitForTicket`** — pętla `Sleep`/`DoEvents`/`GetAsyncKeyState(VK_ESCAPE)` +
   odpytywanie `GET /create-tickets/{ticket}` co ~2s, `swApp.Frame.SetStatusBarText` do
   pokazania postępu. Mechanizm (Win32 API przez `Declare`, brak `UserForm`) nie ma
   bezpośredniego odpowiednika gdzie indziej w tym repo — do sprawdzenia: czy Escape
   faktycznie przerywa pętlę, czy pasek stanu się aktualizuje/czyści poprawnie, czy
   SolidWorks zostaje responsywny (`DoEvents`) przez cały czas oczekiwania.
2. **`Scriptlet.TypeLib.Guid`** (`NewGuid`) i ręczny `UrlEncode` (percent-encoding przez
   `ADODB.Stream` do UTF-8 + ręczne omijanie BOM) — oba standardowe, udokumentowane
   obejścia braku wbudowanego GUID/URL-encodera w VBA, ale nieprzetestowane w tym
   konkretnym zastosowaniu (nazwa dokumentu z polskimi znakami w `name=` warto sprawdzić
   osobno).

**`EasyPDMUpload.bas` (nowe w tej rundzie):**
3. **`IModelDocExtension.SaveAs` do `.step`** (`UploadStepAttachment`) — dokładna liczba/
   znaczenie parametrów (`SaveAsVersion`/`SaveAsOptions`) napisane z dokumentacji API, NIE
   przetestowane — jeśli SolidWorks zgłosi błąd argumentów, sprawdzić w edytorze VBA (F1
   na `SaveAs`) dokładną sygnaturę zainstalowanej wersji.
4. **`IAssemblyDoc.GetComponents`/`IComponent2.GetModelDoc2`/`Name2`/`GetTitle`**
   (`VisitAssemblyComponents`/`ProcessAssemblyTree`) — zakłada, że późno wiązany
   `ModelDoc2` dla Złożenia można wywoływać bezpośrednio metodami `IAssemblyDoc` (typowy,
   udokumentowany wzorzec w makrach SolidWorks, ale nieprzetestowany tutaj). Zawieszone/
   nierozwiązane/wirtualne komponenty są pomijane przez sprawdzenie `GetModelDoc2() Is
   Nothing`/pusty `GetPathName()` zamiast sztywnej wartości enuma zawieszenia — do
   potwierdzenia, że to faktycznie wystarcza.

**`EasyPDMDownload.bas` (bez zmian od poprzedniej rundy):**
5. **`swApp.OpenDoc6`** — sygnatura (`FileName, Type, Options, Configuration, Errors,
   Warnings`) jest dobrze udokumentowanym, standardowym API, ale nie została przetestowana
   na żywo. `Options = 0` (brak specjalnych flag) powinno być bezpieczną wartością
   domyślną.
6. **`VBScript.RegExp`** (`NewRevisionRegex`) — używane do rozpoznawania konwencji nazw
   plików (`numer (nazwa).REWIZJA.rozszerzenie`) przy wykrywaniu aktualnej rewizji i
   starszych lokalnych kopii. Standardowy, stabilny mechanizm, ale nieprzetestowany w
   tym konkretnym zastosowaniu.
7. **Rekurencyjne pobieranie składników złożenia** (`DownloadChildrenRecursive`) —
   analogiczne do `_download_children_recursive` w `EasyPDMDownload.FCMacro` (w tym ten sam
   kształt odpowiedzi `GET /items/{id}/children`: element zagnieżdżony pod kluczem
   `"item"`), ale nieprzetestowane po stronie SolidWorks.
8. **`EnsureDirectory`** — ręczna implementacja tworzenia zagnieżdżonych folderów (VBA
   `MkDir` tworzy tylko jeden poziom naraz, w odróżnieniu od `os.makedirs`) — prosta
   logika, ale warto sprawdzić na ścieżce z kilkoma nieistniejącymi poziomami naraz.

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
