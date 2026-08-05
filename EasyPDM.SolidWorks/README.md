# EasyPDM — makro SolidWorks

Makro `EasyPDMUpload.bas` wysyła aktywny dokument SolidWorks wprost do EasyPDM, bez
przechodzenia przez przeglądarkę.

## ⚠️ Status: niezweryfikowane

W odróżnieniu od makra FreeCAD (`EasyPDM.FreeCad/`, sprawdzone `py_compile` i realnym
uruchomieniem) oraz instalatora Windows (skompilowany i przetestowany w CI na prawdziwym
Inno Setup Compilerze), **to makro nie zostało sprawdzone w żaden sposób** — środowisko,
w którym powstało, nie ma dostępu do SolidWorks, VBA ani nawet podstawowego lintera VBA.
Kod oparty jest wyłącznie o dobrze znane, standardowe wzorce VBA i SolidWorks API (opisane
niżej), ale realnie zweryfikować go może dopiero ktoś z zainstalowanym SolidWorks.

**Przed pierwszym użyciem:**
1. Zaimportuj plik do edytora VBA (patrz "Instalacja" niżej) — sam import/otwarcie
   pokaże ewentualne błędy składniowe.
2. Przetestuj na nieistotnym dokumencie/projekcie testowym, nie na prawdziwych danych.
3. Jeśli coś nie zadziała, najbardziej prawdopodobne miejsca problemów są wypisane w
   sekcji "Znane ryzyka" niżej — zacznij tam.

## Różnice względem makra FreeCAD

To **odpowiednik**, nie port 1:1 — VBA nie ma wbudowanego JSON ani okien dialogowych bez
osobnych plików binarnych (SolidWorks/VBA UserForm), więc kilka rzeczy jest rozwiązanych
inaczej:

- **JSON**: własny, minimalny parser/budowniczy w samym makrze — wystarczający do
  kształtów odpowiedzi tego konkretnego API, nie ogólnego przeznaczenia.
- **Okna**: zwykłe `InputBox`/`MsgBox` zamiast rozwijanych list i formularzy Qt z makra
  FreeCAD. Hasła nie da się zamaskować gwiazdkami zwykłym `InputBox` — wpisywane jest
  jawnym tekstem (widocznym na ekranie, niezapisywanym nigdzie poza samym oknem).
- **Rozpoznawanie "już wysłanego" dokumentu**: NIE przez etykietę/nazwę pliku (SolidWorks
  nie ma odpowiednika swobodnej etykiety FreeCAD) — przez **Właściwości niestandardowe**
  dokumentu (`EasyPDM_ItemId`, `EasyPDM_ItemNumber`), zapisywane w samym pliku po udanej
  wysyłce. To w rzeczywistości **trwalsze** podejście niż w FreeCAD — działa też w NOWEJ
  sesji SolidWorks, bez potrzeby ręcznego zapisu po zmianie etykiety (ograniczenie, które
  ma makro FreeCAD).
- **Świadomie pominięte w tej wersji**: automatyczne wykrywanie i wysyłanie całego drzewa
  złożenia naraz (odpowiednik `App::Link`/`discover_component_tree` z FreeCAD, oparty
  o `IAssemblyDoc`/`IComponent2` po stronie SolidWorks) — to najbardziej złożona i
  najbardziej ryzykowna (dla kodu pisanego bez możliwości testowania) część oryginalnego
  makra. To makro wysyła **jeden aktywny dokument na raz** (Część/Złożenie/Rysunek);
  strukturę BOM (podpięcie komponentów pod złożenie) buduje się w aplikacji webowej.

## Co robi

1. **Logowanie** — przy pierwszym uruchomieniu (albo gdy zapisana sesja wygasła/została
   unieważniona) pyta o adres API, nazwę użytkownika i hasło. Token sesji zapisywany jest
   w rejestrze Windows (`HKEY_CURRENT_USER\Software\VB and VBA Program Settings\EasyPDM`)
   przez wbudowane `SaveSetting`/`GetSetting` — kolejne uruchomienia (także po restarcie
   SolidWorks) nie proszą o ponowne logowanie, dopóki sesja jest ważna (30 dni).
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
   (ta sama konwencja co w aplikacji webowej i makrze FreeCAD). **Lokalny plik NIE jest
   ruszany** — nie jest ani przenoszony, ani usuwany. Jeśli magazyn PDM jest widoczny
   z tej maszyny (`GET /api/config`), kopia trafia do wspólnego `storage/components/` i
   jest **rejestrowana** bez ponownego przesyłania przez HTTP (zachowuje historię
   rewizji); jeśli nie, zwykły upload HTTP (fallback bez zachowania historii rewizji).
6. Zapisuje `EasyPDM_ItemId`/`EasyPDM_ItemNumber` we Właściwościach niestandardowych
   dokumentu i pokazuje potwierdzenie.

## Jak sprawdzić, czy zadziałało

Trzy niezależne sposoby, od najszybszego do najbardziej szczegółowego:

1. **Komunikat na końcu** — po udanej wysyłce makro pokazuje okno z numerem elementu i
   rewizją (np. "Wysłano do EasyPDM: element #67 (rewizja B)."). Brak takiego okna (albo
   okno z "Błąd: ...") oznacza, że coś nie wyszło.
2. **Aplikacja webowa** — najpewniejszy dowód: wejdź w projekt (albo "Cała baza"),
   znajdź element po numerze z komunikatu i sprawdź, czy ma dołączony plik (panel
   właściwości → Załączniki) i poprawne właściwości. Jeśli element/plik tam jest — na
   pewno zadziałało, niezależnie od tego, co pokazało samo makro.
3. **Log makra** — każde uruchomienie dopisuje (nie nadpisuje) szczegółowy,
   ostemplowany czasem przebieg do zwykłego pliku tekstowego:

   ```
   %TEMP%\EasyPDM_macro.log
   ```

   (wklej `%TEMP%` w pasek adresu Eksploratora Windows, żeby tam trafić). Zawiera m.in.
   każde wywołanie API z kodem odpowiedzi (`GET /items -> 200`), wynik logowania, czy
   magazyn plików był widoczny z tej maszyny, czy plik skopiował się/zarejestrował
   poprawnie, i pełną treść błędu, jeśli coś zawiodło. To pierwsze miejsce do sprawdzenia,
   gdy coś nie działa — ścieżka do niego jest też dopisana w oknie błędu/sukcesu na końcu.
   Serwerowe Ustawienia → Logi w aplikacji webowej pokażą dodatkowo, jeśli któreś z tych
   wywołań spowodowało błąd 500 po stronie samego API (rzadziej przydatne, bo nie loguje
   każdego żądania z osobna — to log makra jest tu głównym źródłem informacji).

## Instalacja

SolidWorks nie ma formatu makra czysto tekstowego (jak `.FCMacro` we FreeCAD) — makra to
projekty VBA. `.bas` to standardowy format eksportu/importu **modułu** VBA (nie całego
projektu makra), więc:

1. SolidWorks → **Narzędzia → Makro → Nowy...** — utwórz dowolny, nowy (pusty) projekt
   makra i zapisz go (np. `EasyPDM.swp`).
2. W otwartym edytorze VBA: **Plik → Importuj plik...** → wskaż `EasyPDMUpload.bas`.
3. Uruchamiaj przez **Narzędzia → Makro → Uruchom** (wskazując zapisany plik `.swp`) albo
   bezpośrednio z edytora VBA (F5) — domyślnie uruchamia się `Sub main`.
4. Osobny `Sub Logout` (w tym samym module) wylogowuje z EasyPDM — można go przypiąć do
   własnego przycisku/skrótu w SolidWorks, jeśli chcesz wylogować się bez uruchamiania
   całej wysyłki.

Adres API (domyślnie `http://localhost:5000/api`) zapisuje się automatycznie po pierwszym
podaniu przy logowaniu.

## Znane ryzyka / miejsca do sprawdzenia w pierwszej kolejności

Uporządkowane od najbardziej do najmniej prawdopodobnego źródła problemu:

1. **Nazwy stałych SolidWorks API** — `swCustomInfoText` i `swCustomPropertyReplaceValue`
   (w `SetLinkedItem`) nie zostały zweryfikowane względem realnej biblioteki typów. Jeśli
   edytor VBA zgłosi "zmienna niezdefiniowana" przy którejś z nich, sprawdź dokładną nazwę
   w przeglądarce obiektów (F2) pod `swCustomInfoType_e` / `swCustomPropertyAddOption_e`
   i popraw w kodzie.
2. **Odczyt ciasteczka sesji po zalogowaniu** (`ExtractSessionCookie`) — nie jest pewne,
   czy `MSXML2.XMLHTTP.6.0` w ogóle udostępnia nagłówek `Set-Cookie` przez
   `getResponseHeader`/`getAllResponseHeaders` (niektóre komponenty COM filtrują ten
   nagłówek ze względów bezpieczeństwa). Jeśli logowanie kończy się błędem "serwer nie
   zwrócił sesji" mimo poprawnego hasła, to pierwsze miejsce do zdebugowania — może
   wymagać zamiany na `WinHttp.WinHttpRequest.5.1` albo innego podejścia do odczytu
   ciasteczka.
3. **Wydajność kopiowania bajtów przy dużych plikach** (`CopyBytesInto`, używane w
   `ApiUploadFile`) — pętla kopiująca bajt po bajcie w VBA; dla bardzo dużych złożeń
   (setki MB) może być zauważalnie wolna. Dotyczy tylko trybu fallback (magazyn PDM
   niewidoczny z tej maszyny) — w normalnym trybie (kopiowanie przez `FileCopy`) nie ma
   tego problemu.
4. **`Save3` na dokumencie nigdy wcześniej niezapisanym** — zakłada, że SolidWorks samo
   otworzy okno "Zapisz jako", gdy dokument nie ma jeszcze ścieżki; jeśli zamiast tego
   zwróci błąd bez okna, trzeba będzie dodać jawne wywołanie okna zapisu.
5. Zwykłe literówki/drobne błędy składniowe — VBA IDE pokaże je od razu przy imporcie
   (podświetli linię), więc powinny być szybkie do znalezienia i poprawienia.

## Ograniczenia (świadomie poza zakresem tej wersji)

- Brak automatycznego wykrywania i wysyłania całego drzewa złożenia naraz (zob. wyżej) —
  strukturę BOM buduje się ręcznie w aplikacji webowej.
- Hasło przy logowaniu nie jest maskowane (zwykły `InputBox`, bez własnego `UserForm`).
- Wybór projektu/istniejącego elementu przez numer w liście tekstowej, nie przez
  wyszukiwarkę z podpowiedziami jak w FreeCAD.
- Kopiowanie/rejestrowanie pliku w `storage/` zakłada, że ten folder jest widoczny
  w systemie plików tej maszyny — tak samo jak w makrze FreeCAD.
