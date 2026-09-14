# EasyPDM — makra Autodesk Inventor

[English](README.md) | **Polski** | [Deutsch](README.de.md)

Dwa makra `.bas`, każde w pełni samodzielne (osobny plik, osobny moduł VBA, bez zależności
między sobą poza współdzielonym miejscem w rejestrze Windows na sesję logowania) —
odpowiedniki `EasyPDM.SolidWorks/EasyPDMUpload.bas` i `EasyPDMDownload.bas` dla Inventora:

- **`EasyPDMUpload.bas`** — wysyła aktywny dokument Inventora do EasyPDM. Wybór
  projektu/nowy-czy-istniejący/duplikuj/właściwości elementu odbywa się w przeglądarce
  (ten sam wzorzec co makra SolidWorks i FreeCAD), poza jednym wyjątkiem — patrz "Różnice
  względem makr SolidWorks" niżej.
- **`EasyPDMDownload.bas`** — pobiera Część/Złożenie z EasyPDM (wraz ze wszystkimi
  składnikami złożenia) i otwiera je w Inventorze; wybór KTÓREGO elementu też odbywa się
  w przeglądarce.

## Status

**Jeszcze NIE przetestowane na żywo** — to środowisko nie ma dostępu do instalacji
Autodesk Inventor, więc w odróżnieniu od makr SolidWorks (które przeszły kilka rund testów
na żywo na prawdziwym SolidWorks 2026) ten port zweryfikowano wyłącznie statycznie: kod
tylko ASCII, zbalansowane bloki `Sub`/`Function`/`If`/`For`/`Do`/`Select Case`, brak
zduplikowanych nazw procedur, poprawne zakończenia linii CRLF. Ogólny kształt kodu — parser
JSON, HTTP, przepływ przez bilet w przeglądarce, logowanie, konwencja nazewnictwa rewizji —
jest identyczny z już zweryfikowanymi makrami SolidWorks i nie niesie żadnego ryzyka
specyficznego dla Inventora. Fragmenty, które naprawdę wymagają potwierdzenia na
prawdziwym Inventorze, to wąski zestaw wywołań Inventor Automation API wymienionych w
"Znane ryzyka / co sprawdzić najpierw" niżej — proszę przeczytać tę sekcję, i log makra,
przed poleganiem na tym w produkcji.

## Różnice względem makr SolidWorks

To **porty**, nie nowe projekty — ta sama architektura co
`EasyPDM.SolidWorks/EasyPDMUpload.bas`/`EasyPDMDownload.bas`, z tymi samymi nazwami
Sub/Function tam, gdzie pozwala na to samo API Inventora, żeby obie rodziny plików łatwo
było porównać obok siebie. Różni się tylko faktyczne wywołania API CAD-a:

- **Wykrywanie typu dokumentu**: `TypeName(oDoc)` (`"PartDocument"`/`"AssemblyDocument"`/
  `"DrawingDocument"`) zamiast `GetType()` z SolidWorks porównywanego z zahardkodowanymi
  wartościami `swDocumentTypes_e` — celowo zamiast zgadywać liczby całkowite
  `DocumentTypeEnum` Inventora, których nie da się potwierdzić bez żywej instalacji.
- **Custom Properties → iProperties**: `oDoc.PropertySets.Item("Inventor User Defined
  Properties")` zamiast `CustomPropertyManager` z SolidWorks — patrz "Znane ryzyka" po
  dokładną nazwę zestawu właściwości, która może wymagać poprawki na starszych wersjach
  Inventora.
- **Spacer po drzewie złożenia**: `AssemblyDocument.ComponentDefinition.Occurrences`
  (tylko komponenty najwyższego poziomu, ten sam zakres co `GetComponents(True)` z
  SolidWorks), `occ.Suppressed` (zwykły Boolean — prostsze niż nieprzezroczysty enum
  `GetSuppression2()` z SolidWorks), `occ.Definition.Document`, `occ.Name`.
- **Brak odpowiednika `ResolveAllLightWeightComponents`** — Inventor nie ma identycznego
  pojęcia "rozwiąż lekkie komponenty" per złożenie, więc ten krok jest po prostu pominięty
  (z wyjaśniającym komentarzem w miejscu, gdzie by się znajdował), zamiast zgadywać
  nieistniejące wywołanie API.
- **Widoki rysunku**: zagnieżdżona pętla `For Each oSheet In drawDoc.Sheets: For Each
  oView In oSheet.DrawingViews` zamiast płaskiego spaceru `GetFirstView`/`GetNextView` z
  SolidWorks; `view.ReferencedDocumentDescriptor.ReferencedDocument` zamiast
  `view.ReferencedDocument`.
- **Eksport STEP/PDF**: mechanizm `TranslatorAddIn` Inventora (`ApplicationAddIns.
  ItemById`, `TranslationContext`/`NameValueMap`/`DataMedium`, `SaveCopyAs`) zamiast
  `IModelDocExtension.SaveAs` z SolidWorks — patrz "Znane ryzyka" po CLSID translatorów,
  które są NIEPOTWIERDZONE.
- **Otwieranie dokumentu**: `InvApp.Documents.Open(path, Visible:=True)` (prostsza
  sygnatura, błędy przez wyjątek COM) zamiast `OpenDoc6` z SolidWorks (który wymaga
  jawnego argumentu typu dokumentu i zwraca błędy przez parametry `ByRef`).
- **Rozszerzenia plików**: `.ipt`/`.iam`/`.idw` zamiast `.sldprt`/`.sldasm`/`.slddrw`.

Wszystko inne — przepływ przez bilet w przeglądarce, parsowanie JSON, warstwa HTTP,
logowanie, konwencja nazewnictwa rewizji, zachowanie checkboxów STEP/PDF, wykrywanie
"już podpięte" wraz z zastrzeżeniem dot. Save-Copy-As, bilety w przeglądarce per nowy
komponent złożenia, wykrywanie usuniętych komponentów, STEP/PDF opcjonalne wszędzie —
jest niezmienione względem makr SolidWorks; pełny opis zachowania w ich własnym README
stosuje się tutaj identycznie.

## Co robi `EasyPDMUpload.bas`

1. **Logowanie** — przy pierwszym uruchomieniu (albo gdy zapisana sesja wygasła/została
   unieważniona) pyta o adres API, login i hasło. Token sesji zapisywany jest w rejestrze
   Windows (`HKEY_CURRENT_USER\Software\VB and VBA Program Settings\EasyPDM`) — kolejne
   uruchomienia (także po restarcie Inventora, także z `EasyPDMDownload.bas`, a także
   współdzielone z makrami SolidWorks/FreeCAD — sesja jest wspólna dla wszystkich) nie
   pytają ponownie o logowanie, dopóki sesja jest ważna (30 dni).
2. **Zapisuje aktywny dokument**, jeśli nie był jeszcze zapisany.
3. **Jeśli aktywny dokument to Złożenie**: wykrywa drzewo komponentów
   (`AssemblyDocument.ComponentDefinition.Occurrences`, rekurencyjnie). Podsumowanie
   `MsgBox` przed spacerem wylicza każdy odkryty komponent, oznaczając już podpięte ich
   numerem elementu i nazwą pliku w PDM. Po potwierdzeniu przechodzi liśćmi najpierw (ten
   dokument na końcu); dla każdego JESZCZE niepodpiętego komponentu otwiera własny bilet
   w przeglądarce (ten sam wybór Nowy element/Duplikat/Dograj do istniejącego co dla
   dokumentu głównego — jedna karta przeglądarki naraz, natywny `MsgBox` "kliknij OK, aby
   kontynuować" przed każdą kolejną kartą, bo tylko jedna karta otwarta programowo na
   uruchomienie makra może niezawodnie przejąć fokus Windows). Nowo utworzone komponenty
   dostają własny eksport STEP/PDF (wg własnego wyboru checkboxa tego komponentu w
   przeglądarce) oraz iProperty `EasyPDM_ItemId`, i są automatycznie dołączane pod swoim
   rodzicem w strukturze BOM. Już podpięte komponenty są tylko referencjonowane, nigdy
   ponownie wysyłane, niezależnie od statusu. Komponenty usunięte ze złożenia od ostatniego
   wysłania też są zgłaszane, z prośbą o potwierdzenie przed usunięciem ich powiązania z
   PDM (same elementy nigdy nie są usuwane, tylko ich dołączenie pod tym konkretnym
   rodzicem).
4. Sprawdza **iProperties** dokumentu głównego:
   - **Już podpięty** (ma zapisane `EasyPDM_ItemId`) — pyta lokalnie o zgodę na dołączenie
     bieżącej wersji jako nowej rewizji, bez otwierania przeglądarki, a następnie dwa
     kolejne natywne pytania Tak/Nie o eksport STEP (domyślnie Tak) i PDF (domyślnie Nie).
   - **Jeszcze niepodpięty** — otwiera przeglądarkę systemową (już zalogowaną, mostek
     token→ciasteczko) na wyskakującym oknie "oczekujące żądanie z makra CAD", z trzema
     opcjami do wyboru TAM: **Nowy element**, **Duplikat** albo **Dograj do
     istniejącego** — te same pola i logika rodzaju elementu co w makrach SolidWorks/
     FreeCAD. Makro czeka (odpytuje co ~2s, limit 10 minut, Escape anuluje, postęp
     pokazywany na pasku statusu Inventora) i kontynuuje automatycznie po potwierdzeniu
     wyboru w przeglądarce.
   - **Istniejący element ze statusem "Wydany"**: pyta o zgodę na nową rewizję i
     opcjonalny komentarz.
   - **Istniejący element ze statusem "W recenzji"**: dołączenie jest twardo blokowane
     natywnym komunikatem błędu.
5. **Kopiuje** bieżący plik dokumentu do PDM pod nazwą
   `numer (nazwa).REWIZJA.rozszerzenie` (ta sama konwencja co w aplikacji webowej i innych
   makrach CAD). Lokalny plik NIE jest ruszany. Jeśli magazyn PDM jest widoczny z tego
   komputera, kopia trafia bezpośrednio tam i jest rejestrowana bez drugiego uploadu HTTP;
   w przeciwnym razie zwykły upload HTTP (fallback, przez `WinHttp.WinHttpRequest.5.1`,
   tak samo jak w makrach SolidWorks).
6. Gdy eksport STEP/PDF jest włączony: eksportuje przez mechanizm `TranslatorAddIn`
   Inventora do tymczasowego pliku `.step`/`.pdf` i wysyła go jako załącznik z rolą
   `"step"`/`"pdf"`, zastępując poprzedni załącznik tej samej roli. Błąd eksportu NIE
   przerywa reszty operacji.
7. Zapisuje `EasyPDM_ItemId`/`EasyPDM_ItemNumber` w iProperties dokumentu i pokazuje
   potwierdzenie.

## Co robi `EasyPDMDownload.bas`

1. **Logowanie** — jak wyżej (sesja współdzielona z `EasyPDMUpload.bas`).
2. **Wybór elementu do pobrania odbywa się w przeglądarce** — to samo wyskakujące okno
   "oczekujące żądanie z makra CAD" co przy wysyłaniu, tylko od razu z samym
   wyszukiwaniem. Jedyną rzeczą, która pozostaje lokalnym `InputBox`, jest **folder
   docelowy** (domyślnie ostatnio użyty — ta sama preferencja co folder docelowy w
   `EasyPDMUpload.bas`).
3. Dla Złożenia: pobiera też **wszystkie jego komponenty rekurencyjnie** do TEGO SAMEGO
   folderu co plik główny.
4. Dla każdego pliku: pomija, jeśli folder ma już plik o dokładnie tej samej nazwie i
   rozmiarze; pyta przed zastąpieniem starszej lokalnej rewizji nowszą z serwera.
5. Na końcu otwiera plik główny (wybrany) w Inventorze (`InvApp.Documents.Open`) — pliki
   komponentów zostają tylko na dysku, Inventor sam rozwiązuje referencje złożenia do
   nich.

Skąd biorą się pobierane pliki: EasyPDM przechowuje bieżący plik CAD jako załącznik, a
wcześniejsze rewizje zostają jako osobne załączniki obok niego — `EasyPDMDownload.bas`
rozpoznaje konwencję nazewnictwa ustaloną przez `EasyPDMUpload.bas`, żeby trafić na
załącznik odpowiadający BIEŻĄCEJ rewizji.

## Jak sprawdzić, czy zadziałało

1. **Komunikat na końcu** — po udanej operacji makro pokazuje okno z podsumowaniem. Okno
   z "Error: ..." oznacza, że coś poszło nie tak.
2. **Aplikacja webowa** — wejdź do projektu (albo "Cała baza"), znajdź element po numerze
   z komunikatu i sprawdź, czy ma dołączony plik i poprawne właściwości.
3. **Log makra** — każde uruchomienie dopisuje szczegółowy, oznaczony czasem zapis do
   pliku tekstowego:

   ```
   %TEMP%\EasyPDM_inventor_macro.log            <- EasyPDMUpload.bas
   %TEMP%\EasyPDM_inventor_download_macro.log   <- EasyPDMDownload.bas
   ```

   (wklej `%TEMP%` w pasek adresu Eksploratora Windows, żeby tam trafić). To pierwsze
   miejsce do sprawdzenia, gdy coś nie działa — ścieżka do niego jest też dopisana w oknie
   błędu/sukcesu na końcu.

## Instalacja

Podobnie jak SolidWorks, Inventor nie ma czysto tekstowego formatu makr — makra to
projekty VBA. `.bas` to standardowy format eksportu/importu **modułu** VBA:

1. Autodesk Inventor → zakładka **Narzędzia** → panel **Makro** → **Edytor Visual
   Basic** (albo Alt+F11).
2. W edytorze VBA: **Plik → Importuj plik...** → wskaż `EasyPDMUpload.bas` albo
   `EasyPDMDownload.bas`. Importuj do dowolnego projektu VBA — projekt "globalny",
   współdzielony między wszystkimi dokumentami, to zwykle dobry wybór, żeby makro było
   dostępne niezależnie od tego, który dokument jest aktywny.
3. Uruchamiaj przez zakładkę **Narzędzia** → panel **Makro** → **Makra...** → wybierz
   `main` → Uruchom (albo F5 w edytorze VBA, **z kursorem wewnątrz `Sub main()`**).
4. Osobny `Sub Logout` (w obu modułach) wylogowuje z EasyPDM — można go przypiąć do
   własnego przycisku/skrótu na pasku narzędzi.

Adres API (domyślnie `http://localhost:5000/api`) jest zapisywany automatycznie po
jednorazowym wpisaniu przy logowaniu — współdzielony też z makrami SolidWorks/FreeCAD.

## Znane ryzyka / co sprawdzić najpierw

Ten port napisano wyłącznie na podstawie udokumentowanego zachowania Inventor Automation
API, bez dostępu do żywej instalacji — wszystko poniżej jest oznaczone `UNVERIFIED`
bezpośrednio w kodzie i powinno być pierwszym miejscem do sprawdzenia, jeśli coś pójdzie
nie tak przy pierwszym prawdziwym uruchomieniu:

1. **`InvApp.StatusBarText`** jako właściwość z możliwością ustawienia (używana przez
   `WaitForTicket` do pokazywania postępu podczas czekania na przeglądarkę) — owinięta w
   `On Error Resume Next`, więc błędne założenie degraduje się do "brak tekstu na pasku
   statusu" zamiast awarii, ale warto potwierdzić.
2. **CLSID translatorów** do eksportu STEP/PDF
   (`ExportViaTranslator`/`UploadStepAttachment`/`UploadPdfAttachment`) — wartości z
   publicznej dokumentacji, niepotwierdzone tutaj:
   - STEP: `{90AF7F40-0C01-11D5-8E83-0010B541CD80}`
   - PDF: `{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}`
   Również `oContext.Type = 2` (zamierzone jako `kFileBrowseIOMechanism`) w tej samej
   ścieżce eksportu.
3. **`PropertySets.Item("Inventor User Defined Properties")`** — dokładna nazwa zestawu
   właściwości; niektóre wersje Inventora mogą używać innej nazwy (np. bez prefiksu
   "Inventor "). Ścieżka zapisu już ma fallback z "ustaw" na "dodaj", gdy właściwość
   jeszcze nie istnieje, ale zła nazwa zestawu zepsułaby oba warianty.
4. **`oDoc.Save`/`oDoc.SaveAs(path, False)`** dokładne zachowanie i sygnalizacja błędów
   (wyjątek COM, nie parametry wyjściowe `ByRef` jak `Save3` w SolidWorks) — ogólny
   kształt jest dobrze udokumentowany, ale jeszcze nieprzećwiczony na prawdziwym pliku.
5. **`oDoc.DisplayName`** jako odpowiednik `GetTitle()` z SolidWorks.
6. **`view.ReferencedDocumentDescriptor.ReferencedDocument`** — dokładna ścieżka
   właściwości do rozwiązania referencji widoku rysunku do modelu; oczekiwane zwrócenie
   `Nothing`, gdy model nie jest aktualnie otwarty, to samo ograniczenie co
   `view.ReferencedDocument` w makrze SolidWorks.
7. **Brak odpowiednika `ResolveAllLightWeightComponents`** — celowo pominięty (patrz
   "Różnice względem makr SolidWorks"); jeśli okaże się, że Inventor ma własny odpowiednik
   problemu z lekkimi komponentami, tutaj trzeba by go dodać.

## Ograniczenia (celowo poza zakresem tej wersji)

- `EasyPDMDownload.bas`: nie próbuje pobrać KONKRETNEJ starszej rewizji — zawsze celuje w
  bieżącą. Wszystkie pliki (główny + komponenty) lądują płasko w jednym folderze, bez
  odtwarzania struktury BOM jako podfolderów.
- Hasło logowania nie jest maskowane (zwykły `InputBox`, bez dedykowanego `UserForm`).
- Folder docelowy pobierania to zwykły `InputBox` ze ścieżką jako tekstem, nie systemowa
  przeglądarka plików.
- Kopiowanie/rejestrowanie/pobieranie pliku przez `storage/` zakłada, że ten folder jest
  widoczny w systemie plików tego komputera — tak samo jak w makrach SolidWorks/FreeCAD.
