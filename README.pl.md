# EasyPDM — system PDM dla plików CAD

[English](README.md) | **Polski** | [Deutsch](README.de.md)

[![Postaw kawę na buycoffee.to](https://img.shields.io/badge/☕_Postaw_kawę-buycoffee.to-FFDD00?style=for-the-badge)](https://buycoffee.to/easypdm)

EasyPDM to miejsce, w którym Twoje Części i Złożenia mają jeden, wspólny dla całego
zespołu porządek: każdy element ma swój numer, rewizję, status i historię zmian, a
złożenia — gotowe zestawienie części (BOM). Koniec z
`wspornik_v3_NAPRAWDE_FINALNA.SLDPRT` na wspólnym dysku i pytaniem "która wersja jest
aktualna?". Do FreeCAD i SolidWorks są gotowe makra, które wysyłają i pobierają pliki
wprost z poziomu programu CAD — reszta (przeglądarka, katalogi materiałów/producentów,
BOM) działa tak samo niezależnie od tego, w czym projektujesz.

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
- *Instalacja natywna, bez Dockera* — pobierz gotową paczkę `EasyPDM-Linux-x64_v<wersja>`
  (budowaną automatycznie przez CI tego repo — z zakładki
  [Actions](https://github.com/pawelcel/EasyPDM/actions/workflows/build-linux-package.yml),
  najnowszy udany przebieg, sekcja "Artifacts") albo sklonuj repo samodzielnie, potem:
  ```bash
  tar xzf EasyPDM-Linux-x64_v<wersja>.tar.gz && cd EasyPDM-Linux-x64_v<wersja>   # jeśli pobrałeś paczkę
  sudo ./install-easypdm-linux.sh
  ```
  Instaluje PostgreSQL (jeśli go brakuje) i samo EasyPDM jako usługę `systemd`,
  startującą automatycznie razem z maszyną.

W obu przypadkach EasyPDM ląduje pod `http://localhost:5000` (albo adresem maszyny w
sieci, z innego komputera). Pełne szczegóły, aktualizacja i deinstalacja: patrz
[`TECHNICAL.pl.md`](TECHNICAL.pl.md).

Przy pierwszym logowaniu do całkiem świeżo zainstalowanego EasyPDM: login `admin`, hasło
`admin` — zmień to hasło od razu po zalogowaniu (Ustawienia → Użytkownicy → znajdź konto
`admin` na liście → zmień hasło).

Po zalogowaniu: wybierz projekt (albo utwórz nowy, jeśli masz uprawnienia) — to kontener
na Twoje pliki i strukturę złożenia — i doinstaluj makro do swojego programu CAD, patrz
niżej.

## Praca z poziomu FreeCAD / SolidWorks

Makra dodają w CAD-zie dwie proste operacje: **Upload** (wyślij aktywny dokument do PDM) i
**Download** (pobierz Część/Złożenie z PDM, razem z całym złożeniem, i otwórz w programie).

Instalacja i szczegóły:
- FreeCAD: [`EasyPDM.FreeCad/README.md`](EasyPDM.FreeCad/README.md)
- SolidWorks: [`EasyPDM.SolidWorks/README.md`](EasyPDM.SolidWorks/README.md)

**Upload** — masz otwarty i zapisany plik, klikasz Upload. Otwiera się przeglądarka
(automatycznie zalogowana) z pytaniem: nowy element, duplikat istniejącego (kopiuje jego
właściwości, bez plików) czy dogranie nowej wersji do już istniejącego elementu. Wybierasz,
zatwierdzasz w przeglądarce — makro samo wykrywa zakończenie i kończy wysyłkę (zmienia
nazwę lokalnego pliku na numer z PDM, dogrywa plik, eksportuje podgląd STEP). Dla całego
złożenia z nowymi, jeszcze niewysłanymi komponentami: makro samo je wykrywa i pyta o dane
każdego z osobna, zanim wyśle główny plik.

**Download** — klikasz Download, w przeglądarce wskazujesz Część/Złożenie do pobrania.
Dla złożenia od razu ściąga się CAŁE drzewo komponentów, a główny plik otwiera się
automatycznie w CAD-zie.

## Praca w przeglądarce

### Projekty i struktura

Każdy projekt ma drzewko: Foldery (czyste kontenery do porządkowania), Części i Złożenia
(mają numer/status/rewizję), oraz Inne pliki (dowolny dokument bez własnej struktury pod
sobą). Złożenie może zawierać Części i inne Złożenia (BOM) — ten sam komponent może być
używany w wielu złożeniach i projektach naraz, więc zmiana w jednym miejscu jest widoczna
wszędzie, gdzie ten komponent jest użyty.

Element można **odpiąć ze struktury** (zostaje w bazie, znika tylko z tego miejsca w
drzewku) albo **usunąć całkowicie** (tylko administrator) — usuwanie całkowite jest
bezpieczne dla współdzielonych komponentów: element z rodzicem gdzie indziej nie zniknie
razem z usuwanym poddrzewem. Część/Złożenie da się też **zduplikować** — kopia dostaje
własny numer i od razu ląduje obok oryginału, z jego skopiowanymi właściwościami.

### Części i Złożenia — rodzaje i właściwości

Część ma jeden z czterech **rodzajów**, każdy z innym zestawem pól:

| Rodzaj | Dodatkowe pola |
|---|---|
| Wykonywana | Materiał, Cena |
| Zakupowa | Producent, Typ produktu, Numer zamówieniowy 1/2, Masa, Cena |
| Normalia | Materiał, Norma |
| Klienta | (bez dodatkowych pól) |

Złożenie ma jeden z trzech **rodzajów**: Wykonywane, Zakupowe (Producent i Typ
produktu) albo Klienta. Niezależnie od rodzaju można mu wpisać opcjonalną Masę i
dowolne własne właściwości.

**Typ produktu** to pozycja z listy typów danego producenta (zakładka Producenci) —
pole pojawia się dopiero po wybraniu producenta i pokazuje wyłącznie jego typy.

### Status i rewizje

Część/Złożenie przechodzą przez trzy statusy: **w pracy → sprawdzany → wydany**. W statusie
"w pracy" można edytować wszystko; poza nim nazwa i właściwości są zablokowane (cena zawsze
zostaje edytowalna). Powrót ze statusu "wydany" do "w pracy" podnosi rewizję o jedną literę
(A → B → C...) i pozwala dodać komentarz, co się zmieniło. Na dole panelu elementu widać
pełną **historię**: kto utworzył, każda zmiana statusu, każda rewizja z komentarzem, każdy
dodany/usunięty załącznik, każda blokada/zwolnienie.

### Kto edytuje — blokada elementu

Twórca Części/Złożenia od razu staje się jej właścicielem, a element jest zablokowany —
dopóki blokada trwa, właściwości może edytować tylko właściciel (tego nie omija nawet
administrator). Administrator może za to przejąć albo zwolnić cudzą blokadę i zmienić
status zablokowanego elementu — np. gdy pracownik jest nieobecny, a jego niedokończony
element trzeba odblokować. W drzewku widać to po kolorze kłódki: zielona — zablokowane
przez Ciebie, żółta — przez kogoś innego, otwarta — zwolnione (może zablokować każdy).
Element wydany jest zawsze zwolniony.

### Zestawienie części (BOM)

Złożenie pokazuje listę swoich komponentów: pozycję, nazwę, ilość, materiał, producenta,
numery zamówieniowe — razem z komponentami zagnieżdżonych złożeń. Kolejność pozycji można
zmienić przeciągnięciem albo wpisując numer wprost. Eksport do CSV w dwóch wariantach:
pełny (każde wystąpienie osobno) albo zsumowany (ten sam komponent użyty kilka razy —
jeden wiersz z łączną ilością).

### Materiały i Producenci

Osobne, wspólne dla całej firmy katalogi (zakładki **Lista materiałów** i **Producenci** w
menu głównym) — materiał ma nazwę i grupę/podgrupę, producent ma nazwę i osoby kontaktowe.
Wybiera się je z listy przy uzupełnianiu właściwości Części, zamiast wpisywać ręcznie.

### Wyszukiwanie i cała baza

Zakładka **Cała baza** przeszukuje wszystkie elementy niezależnie od projektu — po nazwie,
numerze, tagach, rodzaju. Znalezione filtry można zapisać do ponownego użycia.

### Dokumentacja do pobrania

Z poziomu Projektu, Złożenia albo Części da się pobrać komplet załączonych plików jako ZIP
(z wyborem, jakie rozszerzenia uwzględnić) — przydatne np. do wysłania kompletu rysunków
klientowi.

## Konta i dostęp

Dwie role: **administrator** (pełny dostęp, widzi wszystkie projekty, zarządza kontami i
ustawieniami serwera) i **użytkownik** (widzi i pracuje tylko w projektach, do których go
przypisano). Każdy sam zarządza swoim językiem interfejsu (polski/angielski/niemiecki) i
motywem jasny/ciemny w Ustawieniach.

## Dla administratorów i programistów

Instalacja serwera (Docker / Linux / Windows), architektura, pełna lista endpointów API i
znane ograniczenia — zobacz [`TECHNICAL.pl.md`](TECHNICAL.pl.md).
