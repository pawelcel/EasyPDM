# EasyPDM — system PDM dla plików CAD

[English](README.md) | **Polski** | [Deutsch](README.de.md)

[![Postaw kawę na buycoffee.to](https://img.shields.io/badge/☕_Postaw_kawę-buycoffee.to-FFDD00?style=for-the-badge)](https://buycoffee.to/easypdm)

EasyPDM to miejsce, w którym Twoje Części i Złożenia mają jeden, wspólny dla całego
zespołu porządek: każdy element ma swój numer, rewizję, status i historię zmian, a
złożenia — gotowe zestawienie części (BOM). Koniec z
`wspornik_v3_NAPRAWDE_FINALNA.SLDPRT` na wspólnym dysku i pytaniem "która wersja jest
aktualna?". Do FreeCAD, SolidWorks i Autodesk Inventor są gotowe makra, które wysyłają i
pobierają pliki wprost z poziomu programu CAD — reszta (przeglądarka, katalogi
materiałów/producentów, BOM) działa tak samo niezależnie od tego, w czym projektujesz.

Jestem konstruktorem mechanikiem i dokładnie wiedziałem, jak takie narzędzie powinno
wyglądać i działać na co dzień — czego mi brakowało w pracy z plikami CAD. Sam tego nie
zaprogramowałem: całą aplikację napisała dla mnie Claude (model AI od Anthropic) na
podstawie moich wymagań i opisów. Zbudowałem EasyPDM na własny użytek, a skoro już
powstał i działa — czemu nie udostępnić go innym.

## Co to daje

- **Jeden numer, jedna historia** — każda Część i Złożenie dostaje numer nadawany
  automatycznie, którego nikt inny nie dostanie drugi raz. Widać, kto i kiedy co zmienił,
  kto ma dany element aktualnie "na warsztacie", i jaka rewizja jest aktualna.
- **Zestawienie części od ręki** — złożenie samo pokazuje listę swoich komponentów z
  ilościami, materiałem, producentem, numerami zamówieniowymi — gotowe do eksportu do CSV.
- **Wspólne katalogi materiałów i producentów** — wybiera się z listy zamiast wpisywać
  ręcznie za każdym razem, więc nazwy się nie rozjeżdżają między projektami.
- **Wyszukiwanie po całej firmowej bazie**, nie tylko w bieżącym projekcie — przydatne, gdy
  szukasz, czy podobna część już gdzieś powstała.
- **Blokada elementu** — dopóki nad czymś pracujesz, nikt inny nie nadpisze Twoich zmian
  bez Twojej zgody (administrator może w razie potrzeby przejąć albo zwolnić cudzą
  blokadę — np. gdy właściciel jest nieobecny).
- **Powiadomienia** — ikonka dzwonka pokazuje, co wymaga Twojej uwagi: Twój element
  czeka na sprawdzenie, został wydany albo cofnięty do "W pracy", ma nową rewizję, przyszła
  ocena klienta do czegoś, co projektowałeś, element został dodany do projektu lub z niego
  usunięty, a dla administratorów — mało miejsca na dysku. Każdy typ można osobno wyłączyć
  w Ustawieniach.

## Pierwsze uruchomienie

EasyPDM instaluje się RAZ — na jednym komputerze w firmie (niekoniecznie jakimś
specjalnym "serwerze", spokojnie może to być zwykły komputer, który zwyczajnie zostaje
włączony). Od tej pory każdy łączy się z nim zwykłą przeglądarką internetową, tak jak
z dowolną stroną — tylko pod adresem widocznym wyłącznie w Waszej sieci firmowej, nie
w całym internecie.

**Jeśli EasyPDM już działa w Twojej firmie** — poproś osobę, która je zainstalowała, o
adres (będzie wyglądał np. tak: `http://192.168.1.20:5000`, albo `http://localhost:5000`,
jeśli EasyPDM stoi na Twoim własnym komputerze). Wpisz go w pasek adresu przeglądarki,
tak jak każdy inny adres strony internetowej, i zaloguj się.

**Jeśli jeszcze nikt go nie zainstalował, a to Ty masz to zrobić:**

**Windows** (bez żadnej wiedzy informatycznej) — wejdź na stronę
[Releases tego repozytorium](https://github.com/pawelcel/EasyPDM/releases), pobierz
najnowszy plik `EasyPDM_Windows_v<wersja>.exe` i uruchom go — kreator instalacji przeprowadzi Cię
przez resztę krok po kroku i zostawi na pulpicie skrót do EasyPDM (jedyne, o co może
zapytać: czy masz już zainstalowany PostgreSQL, czyli program przechowujący dane — jeśli
nie, wskaże stronę, skąd go pobrać, zanim będzie mógł kontynuować).

**Linux** (wystarczy podstawowa obsługa terminala — wybierz jedno):

- *Docker* (zalecane, jeśli na maszynie jest już zainstalowany Docker):
  ```bash
  git clone https://github.com/pawelcel/EasyPDM.git
  cd EasyPDM
  ./install-easypdm-docker.sh
  ```
- *Instalacja natywna, bez Dockera* — pobierz gotową paczkę
  `EasyPDM-Linux-x64_v<wersja>.tar.gz` ze [strony Releases](https://github.com/pawelcel/EasyPDM/releases)
  albo sklonuj repo samodzielnie, potem:
  ```bash
  mkdir easypdm && tar xzf EasyPDM-Linux-x64_v<wersja>.tar.gz -C easypdm && cd easypdm   # jeśli pobrałeś paczkę
  sudo ./install-easypdm-linux.sh
  ```
  Instaluje PostgreSQL (jeśli go brakuje) i samo EasyPDM jako usługę `systemd`,
  startującą automatycznie razem z maszyną.

W obu przypadkach EasyPDM ląduje pod `http://localhost:5000` (albo adresem maszyny w
sieci, z innego komputera) — a gdy port 5000 jest już zajęty, pod kolejnym wolnym;
instalator podaje adres na końcu. Pełne szczegóły, aktualizacja i deinstalacja: patrz
[`TECHNICAL.pl.md`](TECHNICAL.pl.md).

Przy pierwszym logowaniu do całkiem świeżo zainstalowanego EasyPDM: login `admin`, hasło
`admin` — zmień to hasło od razu po zalogowaniu (Ustawienia → Użytkownicy → znajdź konto
`admin` na liście → zmień hasło).

Zupełnie świeża, pusta baza dostaje też przy pierwszym uruchomieniu jeden przykładowy
projekt — złożenie z dwoma częściami, coś do rozejrzenia się zamiast pustki. Powiadomienie
przypomina o jego usunięciu (Ustawienia → Pliki → Strefa zagrożenia) przed rozpoczęciem
prawdziwej pracy.

Po zalogowaniu: wybierz projekt (albo utwórz nowy, jeśli masz uprawnienia) — to kontener
na Twoje pliki i strukturę złożenia — i doinstaluj makro do swojego programu CAD, patrz
niżej.

## Praca z poziomu FreeCAD / SolidWorks / Inventor

Makra dodają w CAD-zie dwie proste operacje: **Upload** (wyślij aktywny dokument do PDM) i
**Download** (pobierz Część/Złożenie z PDM, razem z całym złożeniem, i otwórz w programie).

Instalacja i szczegóły:
- FreeCAD: [`EasyPDM.FreeCad/README.md`](EasyPDM.FreeCad/README.md)
- SolidWorks: [`EasyPDM.SolidWorks/README.md`](EasyPDM.SolidWorks/README.md)
- Autodesk Inventor: [`EasyPDM.Inventor/README.md`](EasyPDM.Inventor/README.md)

**Upload** — masz otwarty i zapisany plik, klikasz Upload. Otwiera się przeglądarka
(automatycznie zalogowana) z pytaniem: nowy element, duplikat istniejącego (kopiuje jego
właściwości, bez plików) czy dogranie nowej wersji do już istniejącego elementu. Wybierasz,
zatwierdzasz w przeglądarce — makro samo wykrywa zakończenie i kończy wysyłkę (zmienia
nazwę lokalnego pliku na numer z PDM, dogrywa plik i — jeśli zaznaczysz to w przeglądarce —
eksportuje STEP i/lub PDF, i zapisuje zdjęcie modelu). Dla całego złożenia z nowymi, jeszcze niewysłanymi
komponentami: makro samo je wykrywa i przeprowadza przez każdy z osobna, zanim wyśle główny
plik. Każdy taki komponent można utworzyć bez przypisania do jakiegokolwiek projektu — żeby
część istniejąca wyłącznie jako pozycja w BOM nie zaśmiecała drzewka projektu.

**Rysunki techniczne** są rozpoznawane jako rysunki (plik `.SLDDRW`/`.idw`/`.dwg` albo strona
TechDraw z FreeCAD-a) i dopasowywane do Części/Złożenia, które dokumentują — przez odczytanie,
na jakie modele faktycznie wskazują widoki rysunku, a nie przez zgadywanie z nazwy pliku.
Rysunek trafia do PDM jako osobny załącznik, jeden na rewizję, obok własnego pliku CAD
modelu, i opcjonalnie można go wyeksportować do PDF. Jeśli rysunek dokumentuje coś, czego
nigdy nie wysłano, makro proponuje najpierw wysłać tę Część/Złożenie, a potem od razu
przechodzi do rysunku.

Makra SolidWorks i Inventor zapisują dodatkowo nazwę elementu do właściwości dokumentu
`EasyPDM_Name`, żeby dało się ją wciągnąć do własnych szablonów rysunku i tabelek. Oba
uzupełniają też same
**masę i materiał**: makro zapisuje je do dokumentu, odczytuje z powrotem i wpisuje na element
w EasyPDM. W SolidWorksie obie właściwości trzymają wyrażenie SolidWorksa, a nie wartość, więc
same nadążają za modelem; w Inventorze są migawką z chwili wysyłki. Materiał wędruje też
do przeglądarki
w momencie tworzenia elementu, więc pole Materiał startuje wypełnione, zamiast zostawiać Cię
ze zgadywaniem — a ten, którego EasyPDM jeszcze nie zna, trafia automatycznie do katalogu
materiałów, żeby dało się go wybrać ponownie i filtrować po nim. To ona
sprawia, że usunięcie nazwy z nazwy pliku jest praktyczne: nazwa dalej wędruje razem z
dokumentem, tyle że nie w jego nazwie.

**Download** — klikasz Download, w przeglądarce wskazujesz Część/Złożenie do pobrania.
Dla złożenia od razu ściąga się CAŁE drzewo komponentów, a główny plik otwiera się
automatycznie w CAD-zie. Aktualny rysunek, jeśli istnieje, zapisuje się obok pliku modelu,
bez otwierania.

## Praca w przeglądarce

### Projekty i struktura

Każdy projekt ma drzewko: Foldery (czyste kontenery do porządkowania), Części i Złożenia
(mają numer/status/rewizję), oraz Inne pliki (dowolny dokument bez własnej struktury pod
sobą). Złożenie może zawierać Części i inne Złożenia (BOM) — ten sam komponent może być
używany w wielu złożeniach i projektach naraz, więc zmiana w jednym miejscu jest widoczna
wszędzie, gdzie ten komponent jest użyty.

Element można **odpiąć ze struktury** (zostaje w bazie, znika tylko z tego miejsca w
drzewku) albo **usunąć całkowicie** (tylko administrator). Usunięcie Złożenia usuwa samo
Złożenie — jego komponenty zostają i tracą jedynie tę jedną pozycję w BOM, bo Złożenie
swoich części tylko *używa*, podczas gdy Folder swoją zawartość *posiada*. Usunięcie
Folderu zabiera więc jego zawartość ze sobą, poza tym, co leży również gdzieś poza nim.
Część/Złożenie da się też **zduplikować** — kopia dostaje własny numer i od razu ląduje
obok oryginału, z jego skopiowanymi właściwościami.

Jedno i drugie usuwanie działa też **na wielu elementach naraz**. Zaznacza się je
**Ctrl+klikiem** w wiersz drzewa (albo przyciskiem „Zaznacz wiele" i checkboxami, jeśli
wygodniej myszką), a belka u góry pokazuje wtedy, ile jest zaznaczonych, i pozwala je
odpiąć ze struktury, usunąć całkowicie, nadać im tag albo zmienić status. Odpięcie
wielu działa tak samo jak pojedyncze: elementy zostają w bazie i nadal są znajdywalne
w „Całej bazie".

Skończony projekt można **zamknąć** jednym przyciskiem na belce — wypada wtedy z listy
wyboru projektu i z okna dodawania elementów, ale nic poza tym się w nim nie zmienia: jego
elementy są nadal w pełni wyszukiwalne przez "Całą bazę", a ten sam przycisk otwiera go
z powrotem.

Sam projekt też da się usunąć (tylko administrator) — NIE usuwa to jego Części/Złożeń: stają
się bezprojektowe i zostają w pełni nienaruszone (pliki, załączniki, tagi, historia, relacje
BOM), dostępne później przez "Całą bazę".

### Dokumenty zamówienia i prowadzący projekt

Obok właściwości samego projektu leżą dokumenty, które przychodzą razem ze zleceniem:
**oferta** i **potwierdzenie zlecenia** mają własne, wyróżnione okna, a do tego jest otwarta
kategoria na wszystko pozostałe (korespondencja, specyfikacje klienta, notatki ze spotkań).
Dwa nazwane okna przyjmują wiele plików, zamiast podmieniać poprzedni — oferta bywa
poprawiana i wysyłana ponownie, a wcześniejsza wersja jest warta zachowania — i przy każdym
pliku widać datę wgrania oraz kto go wgrał.

W projekcie można też wskazać **osobę prowadzącą go po stronie klienta**, wybieraną z
kontaktów tego klienta (zarówno tych przypisanych do samego klienta, jak i tych należących
do konkretnej drugiej nazwy, z którą projekt jest powiązany). Przycisk obok pola pokazuje
jej telefon, e-mail i stanowisko bez wychodzenia z projektu.

### Części i Złożenia — rodzaje i właściwości

Część ma jeden z czterech **rodzajów**, każdy z innym zestawem pól:

| Rodzaj | Dodatkowe pola |
|---|---|
| Wykonywana | Materiał, Cena, Dodatkowe informacje |
| Zakupowa | Producent, Seria/Typ, Podtyp, Numer zamówieniowy 1/2, Cena, Dodatkowe informacje |
| Normalia | Materiał, Norma, Dodatkowe informacje |
| Klienta | Klient, Nazwa 2, Dodatkowe informacje |

**Masa** stoi nad nimi, bo jako jedyne pole jest wspólna dla wszystkich rodzajów — i Części
każdego rodzaju, i Złożenia. Makra CAD wpisują ją przy wysyłce, a zmiana rodzaju elementu jej
nie rusza.

Złożenie ma jeden z trzech **rodzajów**: Wykonywane, Zakupowe (Producent, Seria/Typ
i Podtyp) albo Klienta (Klient). Niezależnie od rodzaju można mu wpisać dowolne własne
właściwości.

**Klient** — dla rodzaju Klienta, wybierany z tego samego katalogu co zakładka Klienci. Obok
pojawia się **Nazwa 2** — jedna z drugich nazw/wariantów handlowych TEGO klienta z katalogu
(klient może mieć ich kilka, np. różne spółki-córki) — pole zablokowane, dopóki nie
wybierzesz klienta.

**Seria/Typ** to pozycja z listy danego producenta (zakładka Producenci), a **Podtyp** to
uszczegółowienie w obrębie tej serii (np. seria „Łożyska walcowe” → podtypy NU/NJ/NUP), oba
pola widoczne obok siebie. Seria/Typ jest zablokowana, dopóki nie wybierzesz producenta, a
Podtyp — dopóki nie wybierzesz serii; zmiana producenta albo serii czyści to, co niżej.

**Jak wygląda numer**, ustawia się raz, w Ustawieniach → Numeracja: każdy rodzaj może dostać
własny literowy prefiks (np. `C` dla części klienta), numery można dopełniać zerami do stałej
szerokości, a nazwę samego elementu da się z nazwy rekordu całkiem usunąć — przez co element
czyta się jako `C0001(płyta)` albo samo `C0001`, zamiast `1(płyta)`. Wszystkie trzy
są odciskane na elemencie przy jego tworzeniu i nigdy potem nieprzeliczane — makra CAD budują
z tego numeru nazwę każdego pliku, więc późniejsza zmiana zostawiłaby na dyskach pliki
mówiące coś innego. Warto więc ustawić to przed pierwszym prawdziwym elementem. Jedyny
wyjątek to pomyłka złapana wcześnie: poprawienie rodzaju Części poprawia też jej prefiks —
ale tylko dopóki jej pola CAD/rysunek/PDF/3D są puste. Gdy w którymś leży już plik, rodzaj
też jest przesądzony, bo plik na dysku nosi numer, o którym rodzaj decyduje.

### Podgląd modelu

Nad właściwościami elementu jest box podglądu z przełącznikiem **2D/3D**: 2D pokazuje rysunek
PDF, a 3D — **zdjęcie modelu**, które makro CAD robi w momencie wysyłki.

To zdjęcie zastępuje to, co działo się tu wcześniej. Plik STEP był pobierany i renderowany w
przeglądarce przy każdym otwarciu elementu — i tak dając nieruchomy obraz, bo nigdy nie było
czym obracać. Było to wolne, tym wolniejsze, im bardziej szczegółowy model (niezależnie od
tego, jak mały wydawał się plik), i potrafiło zamulić całą maszynę, gdy serwer stał na tym
samym komputerze. Zrobienie jednego zdjęcia przy wysyłce, na maszynie, która i tak ma model
otwarty, załatwia to samo raz zamiast w kółko.

Wynikają z tego dwie rzeczy:

- **Zdjęcie przychodzi razem ze STEP-em.** Jeśli odznaczysz „Eksportuj i wyślij model STEP"
  w oknie wysyłki, zdjęcia też nie będzie — i box wprost to napisze.
- **Usunięcie STEP-a kasuje zdjęcie.** Przedstawiało wyłącznie ten model.

Sam plik STEP się nie zmienia — eksportuje się, wgrywa i da się pobrać dokładnie jak dotąd.
Po prostu nie musi już być renderowany, żebyś zobaczył, jak część wygląda. Elementy wysłane
przed tą wersją zachowują swój STEP, ale nie mają zdjęcia do czasu ponownej wysyłki.

Przy dużych modelach okno wysyłki ostrzega, zanim zatwierdzisz: eksport STEP-a robi Twój
program CAD i przy dużym złożeniu potrafi to zająć minuty, z programem zajętym przez cały ten
czas.

### Wysyłka złożenia

Makro potrzebuje jednego formularza na każdy nowy komponent. Nie otwiera już na to osobnej karty przeglądarki ani nie prosi o kliknięcie OK przed każdą z nich: prośbę podejmuje karta, którą masz już otwartą, i pokazuje formularz na miejscu. Przy złożeniu na czterdzieści części było to czterdzieści kliknięć i czterdzieści kart.

Jeśli żadna przeglądarka nie jest otwarta, makro zauważa to w kilka sekund i wraca do starego sposobu — komunikat i nowa karta — więc nic nie przepada.

### Podgląd wysyłki i pobierania

Gdy makro wysyła pliki do PDM albo je pobiera, aplikacja pokazuje po prawej stronie listę tych plików. Każdy jest odhaczany po zakończeniu, bieżący się kręci, a licznik mówi „3 z 7". Jeśli coś się nie uda, pozycja jest zaznaczona na czerwono, a reszta leci dalej. Lista złożenia jest ułożona tak jak jego drzewo — podzłożenia i części z wcięciem pod złożeniem, w którym są — a część użyta w kilku miejscach stoi raz, pod pierwszym z nich.

Przycisk **Anuluj** na liście zatrzymuje resztę przesyłania — po potwierdzeniu. To, co już poszło, zostaje; makro staje przed kolejnym plikiem, więc ten, który akurat leci, zostanie dokończony. Formularz z makra, jeśli właśnie wisi na ekranie, znika, a w powiadomieniach zostaje raport, ile zdążyło przejść.

Najbardziej przydaje się to przy złożeniu. Wysyłka takiego do tej pory była czekaniem bez żadnej informacji — nie dało się stwierdzić, czy makro jest przy drugim komponencie, czy przy ostatnim, ani nad którym plikiem akurat pracuje. Lista pojawia się około sekundy po starcie makra i zostaje chwilę na koniec z kompletem ptaszków, co jest potwierdzeniem, że całość poszła.

Działa tak samo z SolidWorksa, Inventora i FreeCAD-a, w obie strony. Jeśli w trakcie padnie połączenie albo serwer się zrestartuje, lista po prostu przestaje się odświeżać — sama wysyłka czy pobieranie lecą dalej, nietknięte.

### Status i rewizje

Część/Złożenie przechodzą przez cztery statusy: **w pracy → sprawdzany → wydany**, a z
wydanego dodatkowo **→ anulowana** (dla elementu, który się jednak nie przyda). W statusie
"w pracy" można edytować wszystko; poza nim nazwa i właściwości są zablokowane (cena zawsze
zostaje edytowalna) i element jest zawsze zwolniony (bez właściciela). Powrót ze statusu
"wydany" LUB "anulowana" do "w pracy" podnosi rewizję o jedną literę (A → B → C...) i
pozwala dodać komentarz, co się zmieniło. Złożenie z anulowanym elementem gdziekolwiek w
swoim zestawieniu (nawet głęboko zagnieżdżonym) nie może samo przejść na "wydany" — próba
pokazuje, który element jest anulowany. W drzewku/liście anulowany element ma czerwoną
ikonkę. Na dole panelu elementu widać pełną **historię**: kto utworzył, każda zmiana
statusu, każda rewizja z komentarzem, każdy dodany/usunięty załącznik, każda blokada/
zwolnienie.

**Złożenie nie może wyprzedzać swojego zestawienia części.** Na "sprawdzany" przechodzi
dopiero, gdy każdy komponent o jeden poziom niżej jest co najmniej sprawdzany, a na
"wydany" — gdy każdy z nich jest wydany. Sprawdzany jest wyłącznie jeden poziom w dół; to,
co leży głębiej, pilnuje ta sama reguła zastosowana do podzłożenia, kiedy przyjdzie jego
kolej, więc komunikat zawsze wskazuje coś, co widać na ekranie. Jeśli komponent nie jest
gotowy, zmiana statusu nie jest odrzucana, tylko proponowana: okno wypisuje dokładnie
które komponenty są w tyle i pyta, czy zmienić je razem ze złożeniem. Odmowa nie zmienia
nic; zatwierdzenie zmienia najpierw komponenty, potem złożenie, za jednym razem, a każdy
przestawiony komponent dostaje własny wpis w historii. Dwa przypadki są z tej propozycji
wyłączone — **podzłożenie** ma własne zestawienie części, więc zostaje wymienione z nazwy
do osobnego załatwienia, a komponent **anulowany, zablokowany przez kogoś innego albo
leżący w projekcie bez dostępu** jest wypisany razem z powodem i nietknięty.

### Weryfikacja klienta

Gdy Część/Złożenie jest już **wydane**, przycisk "Weryfikacja klienta" na belce otwiera
bieżący zapis tego, co klient o nim powiedział. Każdy wpis niesie wynik — **zweryfikowany**,
**do poprawy** albo brak wyniku, co oznacza po prostu, że element poszedł do klienta i
czekasz — opcjonalny komentarz i własne załączniki, na przykład potwierdzającego maila.
Wpisy się kumulują, więc runda uwag, a po niej akceptacja, zostaje czytelna jako historia,
razem z tym, kto i kiedy ją dodał.

Panel projektu pokazuje całość rozbitą na trzy tabele (do poprawy, w trakcie weryfikacji,
zweryfikowane), a przy każdym wierszu jest przycisk przenoszący wprost do tego elementu w
strukturze. Najnowszy wynik widać też jako plakietkę w panelu samego elementu, razem z datą
i rewizją, której dotyczył. Gdy przyjdzie ocena, osoba, która utworzyła element, dostaje
powiadomienie.

Weryfikacja należy do elementu **w danym projekcie**, a nie do samego elementu: tę samą
Część użytą dla dwóch odbiorców akceptują dwie różne osoby, więc każdy projekt prowadzi
własny zapis, a nic z tego nie pokazuje się w "Całej bazie", gdzie nie ma kontekstu
projektu. Każdy wpis pamięta też, której rewizji dotyczył — po wydaniu nowej rewizji stara
akceptacja zostaje widoczna, ale jest wyraźnie oznaczona jako niedotycząca już tego, czym
element jest teraz.

### Kto edytuje — blokada elementu

Twórca Części/Złożenia od razu staje się jej właścicielem, a element jest zablokowany —
dopóki blokada trwa, właściwości może edytować tylko właściciel (tego nie omija nawet
administrator). Administrator może za to przejąć albo zwolnić cudzą blokadę i zmienić
status zablokowanego elementu — np. gdy pracownik jest nieobecny, a jego niedokończony
element trzeba odblokować. W drzewku widać to po kolorze kłódki: zielona — zablokowane
przez Ciebie, żółta — przez kogoś innego, otwarta — zwolnione (może zablokować każdy).
Element wydany jest zawsze zwolniony.

### Zestawienie części (BOM)

Złożenie pokazuje listę swoich komponentów: pozycję, nazwę, ilość, materiał, normę,
producenta, numery zamówieniowe — razem z komponentami zagnieżdżonych złożeń. Kolejność
pozycji można zmienić przeciągnięciem albo wpisując numer wprost. Eksport do CSV w dwóch
wariantach: pełny (każde wystąpienie osobno) albo zsumowany (ten sam komponent użyty
kilka razy — jeden wiersz z łączną ilością).

Dostępny jest też widok odwrotny — **Gdzie użyto**: panel szczegółów elementu pokazuje
każde złożenie, na dowolnej głębokości i w dowolnym projekcie, które go zawiera, z
przyciskiem do bezpośredniego przejścia.

### Materiały, Producenci i Klienci

Osobne, wspólne dla całej firmy katalogi (zakładki **Lista materiałów**, **Producenci** i
**Klienci** w menu głównym) — materiał ma nazwę i grupę/podgrupę, producent ma nazwę,
osoby kontaktowe i listę Seria/Typ + Podtyp tego, co dostarcza. Wybiera się je z listy przy
uzupełnianiu właściwości Części/Złożenia, zamiast wpisywać ręcznie.

**Klient** ma nazwę (opcjonalnie lokalizację), listę drugich nazw/wariantów handlowych
(dowolną liczbę — wpisanie w oknie "Dodaj klienta" nazwy już istniejącego klienta dodaje mu
kolejną, zamiast zakładać duplikat), własne osoby kontaktowe i własne drzewko dokumentów,
np. norm czy plików referencyjnych — osobne od plików projektu. Projekt można opcjonalnie
powiązać z Klientem, a gdy ten ma ich kilka — z konkretną drugą nazwą. Lista wyboru projektu
pokazuje wtedy "Projekt (Klient, Druga nazwa)", a panel szczegółów klienta wypisuje każdy
przypisany do niego Projekt, z przyciskiem do bezpośredniego przejścia.

### Wyszukiwanie i cała baza

Zakładka **Cała baza** przeszukuje wszystkie elementy niezależnie od projektu — po nazwie,
numerze, tagach, rodzaju. Znalezione filtry można zapisać do ponownego użycia, a przycisk
"Wyczyść filtry" resetuje wszystko naraz.

### Dokumentacja do pobrania

Z poziomu Projektu, Złożenia albo Części da się pobrać komplet załączonych plików jako ZIP
(z wyborem, jakie rozszerzenia uwzględnić) — przydatne np. do wysłania kompletu rysunków
klientowi.

### Powiadomienia

Ikonka dzwonka (u góry po prawej, obok Twojego imienia) pokazuje listę zdarzeń: Twój
element czeka na sprawdzenie, został wydany albo cofnięty do "W pracy", ma nową rewizję,
przyszedł wynik weryfikacji klienta (uwagi albo akceptacja) do elementu, który utworzyłeś,
zostałeś dodany do projektu lub z niego usunięty, przypisany do Ciebie projekt został
usunięty, Twoje hasło zostało zmienione przez administratora, albo (tylko administratorzy)
mało miejsca na dysku na przechowywanie plików. Zostaje tu też raport z zakończonego biegu
makra CAD: ile plików poszło albo przyszło, które się nie powiodły i które pominięto, bo
były już w PDM. Każde powiadomienie można osobno oznaczyć
jako przeczytane albo usunąć, a każdy typ można wyłączyć w Ustawienia → Powiadomienia.

## Konta i dostęp

Dwie role: **administrator** (pełny dostęp, widzi wszystkie projekty, zarządza kontami i
ustawieniami serwera) i **użytkownik** (widzi i pracuje tylko w projektach, do których go
przypisano). Każdy sam zarządza swoim językiem interfejsu (polski/angielski/niemiecki) i
motywem jasny/ciemny w Ustawieniach.

## Dla administratorów i programistów

Instalacja serwera (Docker / Linux / Windows), architektura, pełna lista endpointów API i
znane ograniczenia — zobacz [`TECHNICAL.pl.md`](TECHNICAL.pl.md).
