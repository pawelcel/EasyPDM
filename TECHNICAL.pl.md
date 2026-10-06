# EasyPDM — dokumentacja techniczna

[English](TECHNICAL.md) | **Polski** | [Deutsch](TECHNICAL.de.md)

Ten dokument jest dla administratora, który instaluje/utrzymuje EasyPDM, oraz dla
programistów. Opis samego narzędzia (do czego służy i jak z niego korzystać przy
projektowaniu) jest w [README.pl.md](README.pl.md).

## Status

Ręczne tworzenie projektów i elementów przez aplikację webową (upload pliku wprost do
magazynu API) albo przez makro FreeCAD (`EasyPDM.FreeCad/`), SolidWorks
(`EasyPDM.SolidWorks/`) lub Autodesk Inventor (`EasyPDM.Inventor/`), które wołają to samo
API.
Wcześniejsze podejście ze skanowaniem dysku (`EasyPDM.Core`, `EasyPDM.Indexer`) zostało
usunięte z repo — było niezgodne ze schematem od migracji `002` i nigdy nieużywane przez
`Api`.

Frontend to osobna aplikacja **React 19 + Vite + TypeScript** (`EasyPDM.Web/`) budowana
wprost do `EasyPDM.Api/wwwroot/`. Interfejs jest w pełni przetłumaczony (polski/angielski/
niemiecki) i ma tryb jasny/ciemny. Przetestowane na żywo: CachyOS, .NET 10, PostgreSQL 18.

## Co tu jest

- **`db/schema.sql`** — pełny schemat od zera (aktualny stan po wszystkich migracjach).
- **`db/migrations/`** — migracje `002`–`046` dla już istniejącej bazy: projekty, typy
  elementów, widoczność w drzewku, status/rewizje, materiały (+ grupy/podgrupy), załączniki,
  kolejność BOM, komentarze do rewizji, logowanie i role, właściwości projektu, kaskadowe
  usuwanie, kolejność korzeni drzewka, producenci, zapisane filtry, dostęp do projektów per
  użytkownik, właściciel/blokada elementu, usunięcie martwego schematu rewizji/checkout,
  historia (status/rewizje/załączniki/blokada), harmonogram automatycznej kopii zapasowej,
  śledzenie zastosowanych migracji, rola podglądu/CAD załącznika, literowy prefiks numeru
  elementu per rodzaj, Klienci (katalog + własne drzewko plików), elementy bez projektu
  (element może istnieć bez żadnego projektu, dostępny wyłącznie przez "Cała baza"), adres
  kontaktu producenta/klienta, domyślna wartość/unikalność pozycji BOM, powiadomienia + ich
  preferencje per typ, znacznik przykładowego projektu, mała wewnętrzna tabela flag
  `system_state`, serie/typy producenta wraz z ich podtypami, status "Anulowana",
  zamiana Nazwy 2 klienta z pojedynczej kolumny na listę 1:N oraz własny adres każdej
  Nazwy 2 wraz z `name2_id` przy kontaktach klienta (NULL = kontakt należy do samego
  klienta, odziedziczony tylko do odczytu przez każdą jego Nazwę 2) oraz taki sam
  podział `name2_id` w `client_nodes`, dzięki czemu każda Nazwa 2 może mieć własne
  pliki, niezależne od drzewa plików samego klienta, oraz nowa rola załącznika
  `"drawing"` (obok `pdf`/`step`/`cad`) dla rysunku SolidWorks (.SLDDRW) wgrywanego
  obok własnego pliku CAD Części/Złożenia. Od migracji 027 pliki z tego folderu są wbudowane
  w program (embedded resources) i stosowane **automatycznie przy każdym starcie** — zob.
  `MigrationRunner.cs` i "Jak uruchomić" niżej — nie trzeba ich już odpalać ręcznie przez psql.
- **`EasyPDM.Api/`** — ASP.NET Core (minimal API, Npgsql bez ORM), endpointy podzielone
  po funkcjach w `Endpoints/` — pełna lista niżej w "Endpointy API". Serwuje też zbudowany
  frontend ze swojego `wwwroot/`. Własny `FileLoggerProvider` (bez dodatkowego pakietu NuGet)
  zapisuje logi programu do `logs/` (rotacja dzienna, 30 dni retencji), widoczne w
  Ustawienia → Logi.
- **`EasyPDM.Web/`** — frontend: React 19 + Vite + TypeScript + Tailwind v4 + shadcn/ui
  (komponenty na bazie Base UI, styl „base-nova”), i18n (pl/en/de), motyw jasny/ciemny.
- **`EasyPDM.Api.Tests/`** — testy integracyjne (xUnit + `WebApplicationFactory`),
  uruchamiają CAŁĄ aplikację przeciwko prawdziwemu PostgreSQL (osobny schemat `pdm_test` w
  tej samej bazie, zerowany przed każdą klasą testową). Lokalnie: `dotnet test
  EasyPDM.Api.Tests` (connection string domyślnie wskazuje na lokalny `pdm`/`pdm_user` —
  nadpisywalny zmienną `EASYPDM_TEST_CONNECTION_STRING`, tak jak w CI).
- **`EasyPDM.FreeCad/`** — dwa makra: `EasyPDMUpload.FCMacro` (uruchamiane z poziomu
  FreeCAD, zapisuje aktywny dokument, deleguje wybór projektu/nowy-czy-istniejący/
  właściwości do przeglądarki, tworzy Część/Złożenie w PDM, dogrywa plik jako załącznik,
  eksportuje STEP i zmienia nazwę lokalnego pliku na `numer(nazwa)`) i
  `EasyPDMDownload.FCMacro` (odwrotny kierunek: wybór Części/Złożenia w przeglądarce,
  pobiera je razem z CAŁYM drzewem składników Złożenia — żeby odnośniki `App::Link` się
  rozwiązały — i od razu otwiera w FreeCAD; pomija już pobrane pliki, pyta przed
  nadpisaniem starszej rewizji nowszą). **Oba makra nieprzetestowane na żywym FreeCAD w
  obecnej wersji** (przepływ przez przeglądarkę) — zob. `EasyPDM.FreeCad/README.md`.
- **`EasyPDM.SolidWorks/`** — odpowiednik powyższego dla SolidWorks (makra VBA
  `EasyPDMUpload.bas`/`EasyPDMDownload.bas`), z tym samym przepływem przez przeglądarkę,
  eksportem STEP i automatycznym wykrywaniem drzewa złożenia. **Niezweryfikowane na żywym
  SolidWorks** — zob. `EasyPDM.SolidWorks/README.md` po szczegóły i znane ryzyka.
- **`EasyPDM.Inventor/`** — odpowiednik powyższego dla Autodesk Inventor (makra VBA
  `EasyPDMUpload.bas`/`EasyPDMDownload.bas`), port z `EasyPDM.SolidWorks/` z tym samym
  przepływem przez przeglądarkę, eksportem STEP/PDF i automatycznym wykrywaniem drzewa
  złożenia. **Niezweryfikowane na żywym Inventorze** — zob. `EasyPDM.Inventor/README.md`
  po szczegóły i znane ryzyka.
- **`Dockerfile`/`Dockerfile.postgres`/`docker-compose.yml`/`install-easypdm-docker.sh`**,
  **`install-easypdm-linux.sh`/`uninstall-easypdm-linux.sh`** i **`packaging/windows/`**
  (instalator `.exe`, Inno Setup) — trzy ścieżki wdrożenia bez ręcznego składania z osobna
  backendu/frontendu/bazy, zob. "Jak uruchomić" niżej.
- **`.github/workflows/`** — siedem workflowów CI, wszystkie uruchamialne też ręcznie
  (`workflow_dispatch`) albo przez `gh workflow run <plik>`:
  - `build.yml` — przy każdym pushu/PR: build backendu + testy integracyjne
    (`EasyPDM.Api.Tests`, przeciwko usłudze `postgres` w CI) i typy/lint/build frontendu.
  - `build-windows-installer.yml` — buduje `EasyPDM_Windows_v<wersja>.exe` (zob. wyżej) i dodatkowo
    **realnie go instaluje** na windowsowym runnerze (PostgreSQL przez Chocolatey,
    `/VERYSILENT`), sprawdzając dwukrotnie (świeża instalacja + symulacja aktualizacji), że
    usługa startuje i serwer odpowiada — jedyny sposób, żeby to sprawdzić bez posiadania
    fizycznego/wirtualnego Windows. Zadeklarowany też jako reużywalny `workflow_call` (zob.
    `create-release-draft.yml` niżej).
  - `build-linux-package.yml` — buduje `EasyPDM-Linux-x64_v<wersja>.tar.gz` (self-contained
    backend + zbudowany frontend + skrypty instalacyjne + `db/schema.sql`) i realnie instaluje
    go na czystym runnerze Ubuntu, żeby sprawdzić, że usługa startuje. Też zadeklarowany jako
    reużywalny `workflow_call`.
  - `test-linux-installer.yml` — uruchamia `install-easypdm-linux.sh` naprawdę na czystym
    Ubuntu (świeża instalacja, "aktualizacja", `uninstall-easypdm-linux.sh`), czego lokalne
    środowisko deweloperskie (bez hasła do `sudo` w tej sesji) nie pozwalało zrobić.
  - `publish-docker-image.yml` — buduje i publikuje obrazy `api` i `postgres` (ten drugi
    z wbudowanym `db/schema.sql`) do GitHub Container Registry (`ghcr.io/pawelcel/easypdm-api`,
    `ghcr.io/pawelcel/easypdm-postgres`) z tagiem `:edge` (+ SHA commita) przy każdym pushu
    dotykającym kodu serwera — do sprawdzenia najnowszego stanu `main` przed wydaniem, zob.
    "Docker" niżej.
  - `publish-docker-release.yml` — te same dwa obrazy, ale tylko przy wypchnięciu taga
    wersji (`v*`); to jedyny workflow aktualizujący `:latest` (to, co realnie ściąga
    `docker-compose.yml`), plus pasujący tag `:vX.Y.Z`. Zob. "Docker" niżej.
  - `create-release-draft.yml` — też przy wypchnięciu taga wersji (`v*`), niezależnie od
    `publish-docker-release.yml`: najpierw sprawdza, czy `MyAppVersion` (`EasyPDM.iss`) i
    `APP_VERSION` (`version.ts`) faktycznie zgadzają się z tagiem (inaczej od razu przerywa),
    potem woła `build-windows-installer.yml`/`build-linux-package.yml` jako reużywalne
    workflowy i tworzy **szkic** (draft) Release'a na GitHubie z dołączonymi obydwoma
    artefaktami i notatkami wyciągniętymi z pasującej sekcji `## [X.Y]` w `CHANGELOG.md`.
    Świadomie nigdy nie publikuje go automatycznie — ktoś musi przejrzeć szkic i kliknąć
    "Publish release".

### Model danych — elementy i struktura

Cztery typy elementów (`item_type`): **Folder** (czysty kontener), **Część**/**Złożenie**
(mają numer z globalnej sekwencji, status, rewizję i właściciela), **Inny plik** (dowolny
plik bez struktury pod sobą). Struktura drzewa/BOM-u to osobna tabela `item_relations`
(`parent_id`, `child_id`, `quantity`, `position`) — pozwala tej samej Części/Złożeniu być
współdzielonym komponentem w wielu złożeniach/projektach jednocześnie.

Co wolno dodać pod czym (wymuszane i backendowo, i we froncie):

| Rodzic | Dozwolone dzieci |
|---|---|
| Projekt / Folder | wszystko (Folder, Część, Złożenie, Plik) |
| Złożenie | tylko Część i Złożenie (BOM) |
| Część / Plik | nic — to liście struktury |

Usuwanie elementu ma dwa tryby: **„Usuń ze struktury”** (odpina relację / chowa korzeń,
rekord zostaje) i **„Usuń całkowicie”** (tylko administrator). Ten drugi schodzi w dół
**wyłącznie przez Foldery** — Folder swoją zawartość *posiada*, więc usuwa się razem z nią,
natomiast Złożenie swoich komponentów tylko *używa* (relacja BOM znaczy „wchodzi w skład”,
nie „należy do”), więc usunięcie Złożenia kasuje TYLKO ten jeden rekord, a komponenty
zostają, tracąc jedynie tę jedną relację. Część/Złożenie to samodzielny byt katalogowy
(własny numer, rewizje, historia, właściciel, załączniki) i może wejść w skład dowolnego
innego złożenia także później, więc nigdy nie jest kasowane jako efekt uboczny usunięcia
złożenia, w którym akurat było użyte. Wewnątrz usuwanego poddrzewa Folderów przeżywa
dodatkowo wszystko, co ma rodzica także poza nim (zob. `survivors` w `ItemEndpoints.cs`).

Całkowite usunięcie kasuje też **pliki elementu z magazynu**, nie tylko jego wiersze.
`ON DELETE CASCADE` czyści bazę, ale dysku nie rusza, więc endpoint zbiera wszystkie ścieżki
*przed* `DELETE`: własny plik elementu, całą zawartość `item_attachments` (plik CAD, rysunek,
PDF, STEP, zrzut podglądu, zwykłe załączniki) oraz załączniki wiszące przy jego weryfikacjach
klienta — te leżą we własnej tabeli, kluczowanej weryfikacją, a nie elementem, i właśnie
dlatego zostały najpierw przeoczone i zostawały na dysku, bez żadnego wiersza, po którym dałoby
się je odnaleźć. Usunięcie Projektu robi to samo z `project_attachments`, już po zatwierdzeniu
transakcji, żeby nieudane usunięcie nie zabrało plików projektu, który nadal istnieje.

Część/Złożenie da się też **zduplikować** (kopia dostaje nowy numer, świeży status i
właściciela) — z poziomu drzewka kopia ląduje zaraz pod oryginałem.

Sam Projekt też da się usunąć (`DELETE /api/projects/{id}`, tylko administrator) — NIE
usuwa to jego elementów: `project_id` ustawia się na `NULL` u wszystkich (bez kaskady), więc
Części/Złożenia przetrwają z plikami, załącznikami, tagami, historią i relacjami BOM
nienaruszonymi, dostępne później wyłącznie przez "Cała baza". Chroni to też elementy
współdzielone w BOM-ie innego projektu przez `item_relations` — usunięcie macierzystego
projektu nie psuje już struktury tego innego projektu.

Część ma cztery **rodzaje** (`properties.rodzaj`), każdy z innym zestawem pól i inną ikoną
w drzewku: **Wykonywana** (Materiał, Cena, Dodatkowe informacje), **Zakupowa** (Producent,
Seria/Typ, Podtyp, Numer zamówieniowy 1/2, Masa, Cena, Dodatkowe informacje), **Normalia**
(Materiał, Norma, Dodatkowe informacje), **Klienta** (Klient, Dodatkowe informacje).

Złożenie ma trzy własne rodzaje w tym samym `properties.rodzaj`: **Wykonywane**,
**Zakupowe** (Producent, Seria/Typ, Podtyp) i **Klienta** (Klient). Napisy są CELOWO inne niż dla Części
("Zakupowe" vs "Zakupowa"), bo ta sama wartość jest kluczem prefiksu numeracji — jedynym
wspólnym napisem jest "Klienta", które i prefiks ma wspólny. Poza polami swojego rodzaju
Złożenie ma dalej generyczny edytor właściwości (Masa i dowolne własne klucze). Złożenia
sprzed tej wersji nie mają rodzaju i pokazują podpowiedź, żeby go wybrać.

**Klient** (`properties.client`, tabela `clients`) — dla rodzaju Klienta, Część lub
Złożenie, wybierany z katalogu Klientów (zakładka Klienci) tym samym wzorcem co
Producent/Materiał: powiązanie po nazwie, nie klucz obcy. Obok niego **Nazwa 2**
(`properties.clientName2`) — jedna z drugich nazw/wariantów handlowych TEGO klienta z
katalogu (tabela `client_name2`, relacja 1:N do klienta — jeden klient może mieć ich kilka,
np. różne spółki-córki handlujące pod tą samą nazwą główną, nie kolumna 1:1) —
zablokowana, dopóki nie wybrano klienta; lista opcji zawiera wszystkie Nazwy 2 tego
klienta, pustą gdy nie ma żadnej. Zmiana klienta czyści wcześniej wybraną Nazwę 2. Sama
lista po lewej w zakładce Klienci to odzwierciedla wprost — płaska tabela Nazwa/Nazwa 2,
jeden wiersz na każdą Nazwę 2 (klient bez żadnej dostaje jeden wiersz z kreską), więc
każdy wariant widać bez wchodzenia w danego klienta, z przyciskiem usuwania od razu przy
wierszu. Pole nazwy w oknie "Dodaj klienta" jest samo pickerem po tym samym katalogu i
jest "dynamiczne": wpisanie/wybranie nazwy, która już istnieje, przełącza je z zakładania
duplikatu klienta na dodanie temu istniejącemu klientowi nowej Nazwy 2 (potwierdzane
jednym "OK" zamiast "Dodaj") — to właśnie chroni przed rozdrobnieniem jednego klienta (np.
"Bosch") na kilka niemal identycznych wpisów w katalogu, zakładanych tylko po to, żeby
zapisać różne warianty Nazwy 2.

Niezależnie od `properties.client` powyżej, **katalog Klientów** (tabela `clients`,
zakładka Klienci) jest samodzielnym bytem pierwszej klasy: nazwa/lokalizacja, lista Nazw 2
(`client_name2`), osoby kontaktowe (`client_contacts`) i własne drzewko dokumentów
(`client_nodes`), np. na normy czy pliki referencyjne, niezależne od `items`/
`item_relations`. Projekt można opcjonalnie powiązać z jednym z nich (`projects.client_id`)
— panel szczegółów tego klienta wypisuje wtedy każdy przypisany do niego Projekt (w
zakresie dostępnym aktualnemu użytkownikowi), z przyciskiem do bezpośredniego przejścia.
Projekt może też opcjonalnie wskazać jedną konkretną Nazwę 2 tego klienta
(`projects.client_name2_id`), wybieraną tuż obok pola Klient we własnym formularzu projektu
— czyszczoną przy zmianie klienta, i zerowaną (nie kasującą projektu), gdy ta Nazwa 2
zostanie później usunięta. Rozwijana lista wyboru projektu i pierwszy wiersz struktury
samego projektu pokazują wtedy "Projekt (Klient, Nazwa 2)", a wszędzie lista projektów jest
sortowana po nazwie klienta, potem Nazwie 2, na końcu po nazwie samego projektu.

Projekt może też wskazywać **Prowadzącego projekt** (`projects.lead_contact_id`) — jedną
konkretną osobę z listy kontaktów tego klienta (`client_contacts`), pokazywaną obok
Klient/Nazwa 2 we własnych właściwościach projektu. Albo kontakt należący bezpośrednio do
klienta, albo kontakt należący dokładnie do tej Nazwy 2, z którą powiązany jest projekt
(nigdy kontakt INNEJ Nazwy 2 tego samego klienta — pilnowane w
`ProjectEndpoints.ValidateLeadContactAsync`, tą samą zasadą "musi faktycznie należeć razem"
co przy `client_name2_id`). Czyszczony przy zmianie klienta lub wybranej Nazwy 2, i zerowany
(nie kasujący projektu), gdy ten kontakt zostanie później usunięty.

Projekt niesie też flagę `closed`, przełączaną przyciskiem we własnych właściwościach.
Zamknięty projekt znika z list "aktywnych" (selektor, "Moje projekty", picker projektu przy
dodawaniu elementu), ale poza tym nic się nie zmienia -- jego elementy nadal są w pełni
wyszukiwalne przez "Cała baza", a ten sam przycisk otwiera go z powrotem.
`GET /api/projects` zawsze zwraca wszystkie projekty niezależnie od `closed` -- każda lista
sama decyduje, czy odfiltrować zamknięte (widok szczegółów projektu, osiągany bezpośrednio po
id, celowo tego nie robi, żeby zamknięty projekt zostawał osiągalny i przełączalny z powrotem
po dotarciu do niego).

**Seria/Typ** (`properties.productType`, tabela `manufacturer_product_types`) i
**Podtyp** (`properties.productSubtype`, tabela `manufacturer_product_subtypes` z kluczem
obcym do serii) tworzą dwupoziomowy katalog per producent (zakładka Producenci).
Powiązanie z elementem jest wyłącznie po nazwie, jak przy producencie i materiale, więc
usunięcie pozycji z katalogu nie zmienia niczego w opisanych już elementach. Cały łańcuch
Producent → Seria/Typ → Podtyp jest kaskadowy w obie strony, ale inaczej w dwóch miejscach,
gdzie się pojawia: we właściwościach elementu (`ProductTypeAndSubtypeFields`,
property-fields.tsx) oba pola są widoczne ZAWSZE, tylko zablokowane, dopóki poziom wyżej
jest pusty (Seria/Typ bez producenta, Podtyp bez serii) — celowo, żeby nie wyglądało, jakby
pole "znikało"; w filtrach "Całej bazie" (`ProductTypeFilterSelect`/
`ProductSubtypeFilterSelect`) filtr niższego poziomu pojawia się dopiero, gdy wyższy jest
ustawiony. W obu miejscach zmiana wyższego poziomu (albo, w filtrach, cofnięcie go)
czyści/chowa niższe. Podtyp jest opcjonalny — seria bez podtypów zwyczajnie ma pustą listę
do wyboru.

Część/Złożenie mają maszynę stanów: `w_pracy → sprawdzany → (w_pracy | wydany) → w_pracy`,
a z `wydany` dodatkowo `→ anulowana → w_pracy` (powrót z `wydany` LUB `anulowana` podnosi
numer rewizji, z opcjonalnym komentarzem do rewizji). `anulowana` wybieralna WYŁĄCZNIE z
`wydany` — element musiał zostać wydany, zanim okazał się zbędny. Złożenie z anulowanym
elementem gdziekolwiek w BOM-ie (rekurencyjnie, na dowolnej głębokości —
`FindCancelledDescendantLabelsAsync` w `ItemEndpoints.cs`, ten sam wzorzec CTE co
`BomEndpoints.FetchBomRowsAsync`) nie może samo przejść na `wydany`; `PATCH /status`
odrzuca to z 400 wymieniającym anulowane elementy, co ląduje wprost w oknie potwierdzenia
zmiany statusu na froncie (`StatusControl`) bez osobnego dialogu. `anulowana`, tak samo
jak `wydany`, zawsze jest bez właściciela — `/lock`/`/release` odrzucają obie te wartości
statusu identycznie. Poza statusem `w_pracy` edycja nazwy/właściwości jest zablokowana —
wyjątek: cena/waluta/typ ceny zawsze edytowalne. Ikonka elementu w drzewku/liście jest
czerwona dla statusu `anulowana` (`STATUS_ICON_COLOR` w `item-visuals.ts`). Na dole panelu
właściwości Części/Złożenia pokazuje się
**Historia**: kiedy i kto utworzył element, każda zmiana statusu (kiedy/kto/z-na), każda
rewizja z komentarzem (kiedy/kto/opis), każde dodanie/usunięcie załącznika
(kiedy/kto/nazwa pliku) i każde zablokowanie/zwolnienie właściciela (kiedy/kto), połączone
w jedną chronologiczną listę.

**Właściciel i blokada** (`owner_id`/`owner_locked`) — niezależne od statusu. Twórca
Części/Złożenia staje się od razu jej właścicielem i element jest zablokowany: dopóki trwa
blokada, tylko właściciel może go edytować (właściwości, nazwa, widoczność, przeniesienie
do innego projektu, załączniki, struktura BOM pod nim) — **nawet administrator jej nie
omija**. Każdy może zablokować zwolniony element, stając się jego nowym właścicielem;
zwolnienie może wykonać tylko aktualny właściciel — **z wyjątkiem administratora, który
może też przejąć (`POST /lock`) albo wymusić zwolnienie (`POST /release`) cudzej blokady,
oraz zmienić status zablokowanego elementu (`PATCH /status`) niezależnie od tego, kto jest
właścicielem** — na wypadek np. nieobecności pracownika. Element w statusie `wydany`
zawsze jest zwolniony i bez właściciela — nie da się go zablokować. W drzewku pokazuje to
ikona kłódki: zielona (zablokowane przez Ciebie), żółta (przez kogoś innego), otwarta
(zwolnione).

BOM złożenia pokazuje: L.p. (edytowalne wpisaniem liczby całkowitej — musi być unikalna
w tym BOM-ie — albo przeciągnięciem wiersza), Nazwa, Ilość, Materiał, Norma, Producent,
Numer zamówieniowy 1/2 (brakujące pola jako „-”), razem z zagłębionymi elementami (części
zagnieżdżonych złożeń, L.p. w formie `2.1`). Eksport do CSV w dwóch wariantach: pełny
(każde wystąpienie osobno) i zsumowany (ten sam komponent użyty kilka razy w różnych
miejscach — jeden wiersz z łączną, rozwiniętą przez cały łańcuch ilością).

Dostępny jest też widok odwrotny (`GET /api/items/{id}/used-in`) — każde złożenie, na
dowolnej głębokości i w dowolnym projekcie, które zawiera dany element, pokazywane na
panelu szczegółów elementu nad Historią.

Załączniki (`item_attachments`) to osobny mechanizm od struktury — dowolny plik (np. CAD)
można dopiąć do Części/Złożenia/Pliku z panelu właściwości; nie da się ich dodać ani usunąć
przez drzewko po lewej. Z poziomu Projektu/Złożenia/Części da się pobrać **dokumentację** —
ZIP zebrany ze wszystkich załączników w danym zakresie (cały projekt albo dane
Złożenie/Część razem z poddrzewem), z wyborem, które rozszerzenia plików uwzględnić.

Numer elementu (`item_number`) pochodzi z jednej, globalnej sekwencji PostgreSQL —
usunięcie elementu NIE zwalnia jego numeru automatycznie (standardowe zachowanie
sekwencji). Administrator może ręcznie cofnąć sekwencję do wskazanego numeru (Ustawienia
→ Numeracja) — działa tylko, gdy żaden istniejący element nie ma już takiego numeru lub
wyższego, więc pozwala odzyskać "ogon" numeracji po usuniętych elementach testowych bez
ryzyka kolizji.

To, co widzi użytkownik, to ten numer ubrany w trzy rzeczy, wszystkie trzymane **na
elemencie** i zamrażane przy jego tworzeniu: literowy prefiks rodzaju (`item_number_prefix`,
z `item_number_prefixes`), minimalną szerokość dopełnienia zerami (`item_number_digits`) oraz
to, czy nazwa samego elementu w ogóle dochodzi w nawiasie (`item_number_with_name`) — dwa
ostatnie z `system_state`. Zmiana któregokolwiek ustawienia dotyczy więc wyłącznie elementów
tworzonych później. To nie jest ostrożność dla samej ostrożności: makra CAD budują
z tego numeru nazwę pliku, więc nazwa ta żyje na dysku i w `item_attachments`, gdzie nic jej
wstecz nie przepisze. `ItemNumbering.Label` składa te trzy części w jednym miejscu, a API
podaje wynik jako `itemNumberLabel` obok `itemNumber`/`itemNumberPrefix` — tym samym wzorcem
co `revisionLabel`, żeby frontend i trzy makra CAD nigdy nie składały tego same (i nigdy się
nie rozjechały).

Jedyny wyjątek to pomyłka złapana wcześnie: zmiana rodzaju Części/Złożenia przelicza prefiks
dopóty, dopóki element nie ma załącznika w żadnym z wyróżnionych pól (`preview_role`
= `cad`, `drawing`, `pdf`, `step`, `image`). To są pliki, których nazwy makra wyprowadzają z numeru;
zwykłe załączniki zachowują własne nazwy i niczego nie blokują. Gdy wyróżnione pole jest już
zajęte, `PATCH /properties` ODRZUCA zmianę rodzaju, zamiast przyjąć ją ze starym prefiksem, a
obiekt elementu niesie `kindLocked`, żeby interfejs mógł wyszarzyć przyciski. Odrzucana jest
wyłącznie rzeczywista zmiana — ponowne przysłanie tego samego rodzaju przechodzi. Sam numer
nie zmienia się nigdy.

Pełna nazwa elementu — jego **nazwa rekordu**, składana raz przez `ItemNumbering.RecordName`
i podawana jako `recordName` — to `prefiks + dopełniony numer` i zaraz za nim nazwa w
nawiasie: `C0001(płyta)`, albo samo `C0001` przy wyłączonej nazwie. Dla pliku na dysku
dochodzi jeszcze litera rewizji i rozszerzenie: `C0001(płyta).A.sldprt`. Dopasowywanie nazw w
każdym makrze traktuje i nazwę w nawiasie, i spację, z którą powstawały starsze pliki, jako
opcjonalne — rozpoznaje więc pliki zapisane pod każdą wcześniejszą konwencją.

Ponieważ nazwy może w nazwie pliku nie być, oba makra VBA zapisują ją dodatkowo jako
właściwość dokumentu `EasyPDM_Name`, obok trzymanych tam już właściwości powiązania — wtedy
to jedyne miejsce w dokumencie, gdzie nazwa w ogóle występuje, i stamtąd mogą ją wciągnąć
szablony rysunku. Oba zapisują też `EasyPDM-Mass` i `EasyPDM_Material`, a tuż po zapisie
odczytują je z powrotem i jednym PATCH-em wpisują do właściwości `mass`/`material` elementu.

W SolidWorksie żadna z tych dwóch nie trzyma wartości, tylko wyrażenie —
`"SW-Mass@@Default@<nazwa pliku>"` i `"SW-Material@@Default@<nazwa pliku>"` — które SolidWorks
rozwiązuje przy przebudowie/zapisie, więc obie same nadążają za modelem; makro odczytuje
wartość *rozwiązaną*. Otaczające cudzysłowy są CZĘŚCIĄ WARTOŚCI, a nie zapisem: bez nich
SolidWorks zostawia tekst w spokoju i nigdy go nie wylicza. Wartość, która wraca wciąż
wyglądając jak wyrażenie (nierozwiązana, np. Część bez przypisanego materiału), trafia do logu
i jest odrzucana zamiast wysyłana — tak właśnie `SW-Material@@Default@C0014.A.SLDPRT` trafił
raz do pola materiału elementu. Inventor nie ma odpowiednika takiego wyrażenia, więc tam
obie trzymają
migawkę odczytaną z `ComponentDefinition` w chwili wysyłki i odświeżają się dopiero przy
kolejnej. Rysunki są pomijane w całości, materiał zapisywany wyłącznie dla Części (złożenie
nie ma własnego), a masa pusta albo niebędąca zwykłą liczbą trafia do logu i jest pomijana —
najpierw normalizowana do cyfr i jednej kropki dziesiętnej, bo bywa z jednostką i przecinkiem
dziesiętnym.

Zanim do tego dojdzie, otwierając przeglądarkę w celu utworzenia nowego elementu makro
przekazuje materiał dokumentu w deep-linku (`&material=`), odczytany wprost z API CAD-a, a nie
z `EasyPDM_Material` — w tym momencie nic jeszcze nie zostało zapisane, więc wyrażenie nie
istnieje. `pending-create-ticket.ts` go odbiera, a `AddNodeDialog` pokazuje go w polu Materiał,
dzięki czemu widać go już przy tworzeniu elementu, zamiast żeby pojawiał się sam zaraz po
wysyłce. Duplikat zachowuje właściwości elementu źródłowego: tam wybór był świadomy.

To pole jest wtedy **tylko do odczytu** (`materialLocked`, przekazywane do `MaterialField`):
makro i tak zapisuje materiał na elemencie z `EasyPDM_Material` zaraz po wysyłce, więc wybór
zrobiony tutaj zostałby po chwili nadpisany — i to właśnie dawanie takiego wyboru myliło.
Świadomą zmianę robi się na już utworzonym elemencie, gdzie pole jest normalnie edytowalne.
Materiał duplikatu zostaje edytowalny, bo pochodzi z elementu, który ktoś wskazał. Zablokowany
wariant to zwykły wyłączony `Input`, a nie wyłączony `Combobox`: materiału może jeszcze nie być
w katalogu w chwili otwarcia okna, a `Combobox` nie pokazuje wartości spoza swojej listy.

Materiał, którego katalog jeszcze nie zna, zakłada `MaterialCatalog`
(`INSERT … ON CONFLICT (name) DO NOTHING`, więc dwa makra wysyłające równolegle ten sam nie
mogą się zderzyć), wołany z **obu** ścieżek, którymi materiał może przyjść: `PATCH /properties`
(makro po wysyłce) oraz `POST /nodes` (tworzenie, gdzie materiał pochodzi z okna wypełnionego
przez makro). Dopóki robił to tylko `PATCH`, element utworzony z materiałem z CAD-a nosił
nazwę, której katalog nie miał aż do zakończenia wysyłki. Makra czytają materiał z dokumentu,
a nie z listy wyboru, więc bez tego element miałby materiał, którego nie da się ani wybrać
ponownie, ani użyć jako filtr. Zakładana jest sama nazwa; grupa i podgrupa zostają puste.

### Zaznaczanie wielu elementów w drzewie

Wiersze zaznacza się Ctrl (Cmd) + klikiem, obok starszego przycisku „Zaznacz wiele" i jego checkboxów — przycisk zostaje, bo na tablecie nie ma klawisza Ctrl. Ctrl+klik sam włącza tryb zaznaczania: checkboxy i belka akcji istnieją tylko w nim, więc pierwszy taki klik zaznaczałby inaczej coś, czego nie widać.

Zaznaczenie zapamiętuje **rodzica klikniętego wiersza**, nie sam identyfikator elementu. Ten sam element potrafi wisieć w drzewie w kilku miejscach naraz — jako korzeń projektu i pod złożeniem — a „usuń ze struktury" odpina jedno, wskazane miejsce: `removeChild(parent, id)` plus `moveItemToProject(id, null)` dla dziecka albo zgaszenie `showInTree` dla korzenia. Bez rodzica wiersza nie dałoby się powiedzieć, które z tych miejsc miało na myśli zaznaczenie. To dokładnie to, co robi akcja pojedynczego elementu, wykonane dla każdego zaznaczonego wiersza.

Dopóki cokolwiek jest zaznaczone, belki akcji bieżącego elementu i projektu ustępują. Obie są pozycjonowane absolutnie nad belką zaznaczenia i obie niosą przycisk nazwany jak jeden z masowych: obok siebie stały dwa „Usuń ze struktury" — jeden działający na element z podglądu, drugi na całe zaznaczenie — i nic w wyglądzie ich nie odróżniało. Masowy nazywa się przy tym „Usuń zaznaczone ze struktury", spójnie z sąsiednim „Usuń zaznaczone". Gdy element z podglądu był wśród odpinanych, panel wraca na projekt, tak samo jak po pojedynczym odpięciu — sprzątanie „zniknął z drzewa" tego nie łapie, bo rekord dalej istnieje, tylko bez projektu.

### Podgląd modelu to obrazek, a nie renderowany STEP

Box podglądu nad właściwościami elementu pokazuje **zrzut PNG**, który makro CAD robi w chwili
wysyłki i wysyła jako załącznik z `preview_role = 'image'`. Plik STEP eksportuje się i wgrywa
dokładnie jak dotąd (`preview_role = 'step'`) — jest do pobrania, po prostu nie zasila już
wyświetlania.

Kiedyś zasilał. Przeglądarka pobierała STEP, parsowała go przez `occt-import-js` (OpenCascade
skompilowany do WebAssembly), teselowała każdą powierzchnię, puszczała `THREE.EdgesGeometry` po
każdej powstałej bryle i renderowała całość przez three.js. Wszystko po to, żeby otrzymać
**nieruchomy obraz**: jedno `renderer.render()`, bez pętli animacji, bez obracania, bez niczego,
co dałoby się przeciągnąć. Cena była płacona przy każdym otwartym elemencie, przez każdego
użytkownika, i nie zależała od rozmiaru pliku STEP, tylko od złożoności geometrii — plik 40 kB
pełen zaokrągleń i powierzchni swobodnych teseluje się na setki tysięcy trójkątów. Usunięcie
tego zabrało **7,8 MB** z publikowanej paczki, w tym 7,6 MB samego binarnego OpenCascade
`.wasm`, który każda przeglądarka pobierała i kompilowała.

Zrobienie zrzutu wymaga, żeby dokument był **aktywny**: zapis obrazu łapie aktywny widok, a nie
dokument wskazany w wywołaniu, a w trakcie wysyłki złożenia aktywnym dokumentem jest złożenie —
więc każdy komponent dostawał obrazek złożenia, z którego pochodzi. SolidWorks i Inventor
aktywują więc dokument, robią zrzut i aktywują z powrotem poprzedni; FreeCAD potrafi wziąć widok
wskazanego z nazwy dokumentu bez żadnego przełączania, więc tam nic nie rusza się na ekranie.

Z tego, skąd bierze się zrzut, wynikają dwie rzeczy:

- **Nie ma STEP-a, nie ma obrazka.** Makro robi zrzut wewnątrz swojego kroku wysyłki STEP-a,
  więc odznaczenie opcji „eksportuj STEP" zostawia element bez podglądu, a box wprost to mówi
  zamiast pokazywać pustą ramkę.
- **Usunięcie STEP-a kasuje zrzut.** Istnieje on wyłącznie po to, żeby przedstawić ten model,
  więc `DELETE /api/attachments/{id}` na załączniku `step` usuwa też załącznik `image` tego
  elementu (`DeleteRoleAttachmentsAsync`). Bez tego w magazynie zbierałyby się obrazki, których
  nic nie wyświetla i których nie da się powiązać z żadnym modelem.

`image` jest jednoslotowe jak `pdf`/`step` (nie kumuluje się jak `cad`/`drawing`): zrzut
przedstawia bieżącą postać modelu, więc nowa wysyłka zastępuje poprzedni.

Elementy wgrane przed tą zmianą zachowują swój STEP i tracą podgląd do czasu ponownej wysyłki —
nie ma już renderera, do którego można by się cofnąć. To była świadoma decyzja: zostawienie go
oznaczałoby trzymanie tej zależności 7,6 MB dla wszystkich.

STEP/IGES/STL nie są więc już podglądalne nigdzie w aplikacji, łącznie z okienkiem podglądu
załącznika — dostają przycisk pobierania. `previewKindOf` rozpoznaje teraz wyłącznie PDF-y i
obrazy rastrowe.

### Prośba o formularz bez otwierania karty

Wysyłka złożenia potrzebuje formularza dla każdego nowego komponentu. Dotąd makro otwierało na to osobną kartę, a przed każdą pokazywało natywne okno — nie dla potwierdzenia, lecz dlatego, że ochrona Windows przed kradzieżą fokusu przepuszcza tylko **pierwsze** programowe otwarcie przeglądarki w danym biegu, a każde kolejne otwiera po cichu w tle. Bez kliknięcia pomiędzy formularz pojawiał się w karcie, której nikt nie widział, a `WaitForTicket` czekał na dane, których nie dało się wpisać. Przy złożeniu na czterdzieści części to czterdzieści kliknięć i czterdzieści kart.

Przeglądarka jest już otwarta i już odpytuje serwer, więc żadna nowa karta nie jest potrzebna. Makro zostawia prośbę w `CadRequestStore` (w pamięci, kluczowane użytkownikiem, to samo uzasadnienie co przy `CreateTicketStore`), a otwarta karta podejmuje ją przez `use-cad-requests.ts` i podaje dokładnie temu samemu `PendingTicketBanner` i `AddNodeDialog`, które obsługują bilet z adresu URL. W samym formularzu nic się nie zmieniło.

Prośba jest adresowana do **jednego biegu makra**, nie do konta. CAD generuje identyfikator biegu, przekazuje go przeglądarce w adresie, a karta trzyma go w `sessionStorage` — co przeżywa odświeżenie, ale nie dociera do żadnej innej karty ani do innego komputera. Karta podejmuje prośbę wyłącznie wtedy, gdy identyfikator się zgadza. Kluczowanie samym użytkownikiem zakładało jeden bieg na osobę, a to przestaje być prawdą w chwili, gdy to samo konto jest zalogowane w przeglądarce na drugim komputerze: prośba z jednego komputera trafiała do karty na drugim, która pokazywała formularz komuś zupełnie innemu. Zdarzyło się to w praktyce i to jest powód, dla którego identyfikator istnieje.

Z tego samego wynika, że **pierwszy** komponent biegu nadal otwiera kartę: to otwarcie jest jedynym sposobem, żeby karta na tej maszynie poznała identyfikator biegu, a dopóki żadna go nie zna, makro nie ma komu podać prośby. To pierwsze otwarcie nie pokazuje już okna, bo pierwsze otwarcie przeglądarki w biegu i tak przejmuje fokus — reguła Windows opisana wyżej gryzie dopiero od drugiego. Normalny bieg to więc jedna karta i zero kliknięć, zamiast jednej karty i jednego kliknięcia na komponent.

Gdy karta podejmie prośbę, makro oddaje fokus przez `AppActivate "EasyPDM"`. CAD wychodzi w międzyczasie na wierzch nie bez powodu — dla poprzedniego komponentu zapisywał plik, eksportował STEP i przerysowywał okno graficzne na potrzeby zrzutu — ale teraz potrzebny jest formularz. Działa to, bo Windows pozwala oddać fokus aplikacji, która *aktualnie* go ma: ta sama reguła, która wymuszała klikanie, użyta w drugą stronę. Dopasowanie idzie po początku tytułu okna, więc gdy EasyPDM siedzi w karcie w tle, nic się nie dzieje, a błąd jest połykany — to wygoda, a nie część wysyłki. We FreeCAD problem był odwrotny: niczego nie trzeba było wyciągać, bo karta wychodzi na wierzch sama, gdy podejmie prośbę — fokus odbierało jej dopiero własne okno oczekiwania makra, tworzone chwilę potem, bo Qt aktywuje nowe okno przy tworzeniu. `WA_ShowWithoutActivating` tego nie powstrzymało: pod Wayland o tym, czy nowe okno dostanie fokus, decyduje kompozytor, a nie aplikacja. Okna więc nie ma — FreeCAD czeka tak, jak makra SolidWorks i Inventor zawsze czekały: odpytując serwer z komunikatem na pasku stanu, a `processEvents` utrzymuje interfejs przy życiu. Próba przez `wmctrl`/`xdotool` zostaje jako zapas wyłącznie dla X11.

Ta sama zasada tłumaczy, czemu nawet *pierwsze* otwarcie nie wyciągało przeglądarki. Pod Wayland uruchomiony program dostaje fokus tylko wtedy, gdy uruchamiający przekaże mu token xdg-activation, a Pythonowy `webbrowser.open` po prostu woła `xdg-open` bez żadnego. FreeCAD otwiera teraz adresy przez `QDesktopServices.openUrl` (`open_in_browser`), który od Qt 6.6 sam prosi kompozytor o token w imieniu FreeCAD-a i przekazuje go dalej — ważny w tej chwili, bo użytkownik właśnie uruchomił makro i fokus ma FreeCAD. `webbrowser.open` zostaje jako zapas.

Dlatego też aplikacja potrafi zawołać człowieka sama: gdy prośba zostaje podjęta, a karta nie ma fokusu, pokazuje powiadomienie systemowe, którego kliknięcie na nią przełącza. Kliknięcie to jedyny mechanizm, którego nie odrzuca żaden system okien — bo przełącza się sam użytkownik. O zgodę pytamy raz, z przycisku w oknie prośby z makra (przeglądarki odrzucają pytanie bez świeżej interakcji), a karta z fokusem nie pokazuje nic, bo nie ma od czego wołać.

Jednego nie da się założyć: że ktokolwiek patrzy. Przeglądarka może być zamknięta, serwer nieosiągalny. Dlatego makro publikuje prośbę, a potem przez kilka sekund odpytuje `GET /api/cad-requests/taken`; karta oznacza prośbę jako podjętą w chwili, gdy ją przejmuje. Brak sygnału znaczy, że nikogo nie ma — i makro wraca do starej ścieżki, z oknem i nową kartą, zamiast czekać na formularz, którego nikt nie zobaczy. W tej ścieżce awaryjnej okno nadal żyje — przed kartą, którą otwiera, i tylko od drugiego komponentu w górę, czyli tam, gdzie reguła fokusu obowiązuje.

`taken` wraca jako płaskie `1`/`0`, a nie wartość logiczna w zagnieżdżonym obiekcie, bo parsery JSON w makrach VBA mają tylko `JsonGetString` i `JsonGetLong` — płaska liczba to jedyna postać, którą potrafią odczytać bez dokładania im parsera.

### Lista postępu wysyłki i pobierania

Gdy makro CAD wysyła albo pobiera, aplikacja pokazuje po prawej listę plików i odhacza je w trakcie. Stan siedzi w `TransferProgressStore` — w pamięci, bez tabeli, ten sam wybór i to samo uzasadnienie co przy `CreateTicketStore`: żyje sekundy do minut, po fakcie nikomu niepotrzebny, a utrata przy restarcie nic nie kosztuje, bo postęp jest informacją O pracy, a nie jej częścią.

Kluczem jest **użytkownik**, nie identyfikator sesji. Dzięki temu przeglądarka pyta po prostu „co robi moje makro?" (`GET /api/progress`), nie musząc skądkolwiek poznać identyfikatora — działa to też wtedy, gdy kartę otwarto przed uruchomieniem makra. Jeden bieg na osobę w danej chwili to założenie bezpieczne (nikt nie klika „Upload" w dwóch CAD-ach naraz), a nowy bieg po prostu zastępuje poprzedni.

Makro zgłasza **całą listę z góry** i dopiero potem odhacza pozycje. Bez pełnej listy od początku licznik by kłamał: „3 z 3" zamieniałoby się w „3 z 9", gdy znalazłby się kolejny poziom drzewa.

- **Wysyłka** zna listę: `DiscoverComponentTree` przechodzi złożenie, zanim poleci pierwszy plik, a kolejność jest liśćmi do góry, bo rodzic nie może dostać relacji BOM do dziecka, którego jeszcze nie ma w PDM.
- **Pobieranie** nie znało. `DownloadChildrenRecursive` schodzi poziom po poziomie i na starcie nie wie, ile plików będzie — stąd `GET /items/{id}/descendants`: jedno zapytanie rekurencyjne zwracające element i całe jego poddrzewo. Deduplikacja po stronie serwera odpowiada zbiorowi `seen` w makrze, więc część użyta w kilku złożeniach liczy się raz i licznik dochodzi do końca. Lista jest uporządkowana rodzic-przed-dzieckiem, bo w takiej kolejności makro FAKTYCZNIE pobiera; posortowanie inaczej sprawiłoby, że pozycje odhaczałyby się nie po kolei. Przy pobieraniu kolejność i tak nie wpływa na poprawność — wszystkie pliki lądują na dysku, zanim cokolwiek zostanie otwarte.

**Raportowanie postępu nie ma prawa przerwać transferu.** Całość idzie własną, cichą ścieżką HTTP w każdym makrze, a nie przez `ApiPostJson`/`api_post_json`, które rzucają wyjątkiem. Brak połączenia, restart serwera czy starszy serwer bez tych endpointów nie mogą zatrzymać tego, o co użytkownik faktycznie poprosił. Z tego samego powodu serwer na nieznany klucz odpowiada `matched: false`, a nie błędem.

Przeglądarka odpytuje co 1,5 s zwykłym `setInterval` — wzorcem sprawdzonym już w `use-notifications.ts`. Pierwsze podejście dobierało interwał dynamicznie przez `setTimeout` planujący sam siebie; okazało się mierzalnie kruche (jedno przemontowanie urywało łańcuch i nic go nie wznawiało), a oszczędność nie była tego warta, bo jedno odpytanie to odczyt ze słownika i kilkadziesiąt bajtów, bez dotykania bazy. Zakończony bieg przestaje być wydawany po dwóch minutach, żeby lista sprzed godziny nie witała kolejnej osoby otwierającej aplikację.

**Anulowanie.** Przycisk „Anuluj" w panelu nie może niczego zatrzymać sam — makro działa w programie CAD na innej maszynie. `POST /api/progress/cancel` tylko zapisuje prośbę, a każde makro pyta `GET /api/progress/cancelled` (płaskie `1`/`0`, dla parserów VBA) w dwóch miejscach, w których zatrzymanie jest bezpieczne: przy każdym odpytaniu o formularz i przed każdym kolejnym plikiem. Plik, który akurat leci, jest zawsze dokończony — przerwanie w połowie zapisu zostawiłoby go uszkodzonego. Pierwsze „tak" jest zapamiętywane do końca biegu, żeby nie pytać serwera ponownie; każdy błąd znaczy „nie anulowano". W makrach VBA flaga jest zerowana na starcie `main`, bo zmienne modułu potrafią tam przeżyć bieg, a pozostawione `True` zatrzymywałoby każdy kolejny od razu.

Anulowany bieg i tak kończy się `finish`, więc panel się zamyka, a raport mówi „przerwano", zamiast wyglądać na awarię — pozycje, do których nie doszło, liczą się jako `pending`, nie `failed`. Żeby to była prawda, trzeba było zmienić dwie rzeczy: dokument nadrzędny był odhaczany jako wysłany bezwarunkowo po powrocie z funkcji wysyłki, nawet gdy wysyłkę porzucono, a ścieżki anulowania we FreeCAD w ogóle nie wołały `finish`, więc anulowany bieg FreeCAD-a zostawiał panel do wygaśnięcia. Pobrane złożenie po anulowaniu nie jest otwierane — jego odnośniki wskazywałyby na pliki, które nie doszły.

Potwierdzenie siedzi w samym panelu, a nie w oknie dialogowym: panel stoi nad oknami (`z-60`), więc okno na `z-50` otworzyłoby się pod nim, i to możliwe, że przy formularzu z makra już na ekranie. Gdy taki modalny formularz jest otwarty, okno oznacza panel jako `aria-hidden` — panel zostaje na wierzchu i da się go kliknąć myszką, ale z klawiatury do „Anuluj" nie da się dojść, dopóki formularz nie zniknie.

### Bieg sam się raportuje w powiadomieniach

Na koniec biegu serwer składa raport i zostawia go w dzwonku (`cad_transfer_finished`). Makra kończyły dotąd blokującym oknem — „wysłano jako element nr X" — co było w porządku, dopóki człowiek siedział w CAD-zie. Odkąd po każdym komponencie fokus wraca do przeglądarki, to okno tworzy program stojący W TLE, więc powstaje ZA przeglądarką i wisi, czekając na kliknięcie, którego nikt nie widzi. Raport idzie więc tam, gdzie człowiek i tak patrzy — i w odróżnieniu od okna zostaje, żeby dało się go później odszukać.

Składa go `POST /api/progress/finish` z biegu, który magazyn postępu i tak trzyma — a nie makro. To ten sam wybór co liczenie postępu po stronie serwera: jedno miejsce na trzy CAD-y, więc raport czyta się tak samo niezależnie od tego, z czego wysyłano, a makro nie potrzebuje na to ani linijki ponad `finish`, które i tak wołało.

`Finish` oddaje bieg **tylko przy pierwszym wywołaniu**, które go kończy. Makro ma prawo zawołać `finish` dwa razy — raz ze ścieżki błędu, raz z normalnej — a dwa identyczne raporty w dzwonku byłyby zwykłym szumem.

W danych siedzą liczby i lista: `kind`, `total`, `done`, `skipped`, `failed`, `pending` oraz do czterdziestu pozycji z etykietami. `done` celowo NIE obejmuje `skipped`: pominięty komponent to taki, który już był w PDM i nie trzeba go było wysyłać, więc policzenie go jako wysłanego zawyżałoby raport. `pending` jest osobno od `failed`, bo przerwany bieg niczego nie zepsuł — po prostu do tych pozycji nie doszedł. Dzwonek pokazuje osiem pozycji, najpierw nieudane: przy dwudziestu plikach i jednym błędzie pokazanie ośmiu pierwszych z listy zostawiłoby jedyną ważną pozycję poza kadrem.

Dzwonek odpytuje co 30 s, czyli stanowczo za rzadko jak na coś, co dzieje się na oczach użytkownika — panel postępu wysyła więc zdarzenie w chwili, gdy serwer pierwszy raz odda „zakończony", a `use-notifications` odświeża się na nim. Panel stoi przy tym nad modalami (`z-60`), więc rozwijany panel dzwonka musiał pójść jeszcze wyżej: inaczej przykrywał dokładnie to powiadomienie, które sam zapowiedział.

Jedno okno zostaje świadomie: komponenty już podpięte do PDM ze statusem „sprawdzany" albo „wydany". Takich makro nigdy nie aktualizuje, więc lokalna zmiana w którymś z nich NIE poszła na serwer — a lista plików tego nie powie, bo z jej punktu widzenia nic się nie wydarzyło.

### Logowanie, role i dostęp do projektów

Każde żądanie do `/api/*` (poza `/api/auth/login`) wymaga zalogowania — sesja to losowy
token w ciasteczku httpOnly (`pdm_session`, 30 dni ważności), zapisany w tabeli `sessions`.
Hasła trzymane jako PBKDF2 (własna implementacja w `PasswordHasher.cs`, tylko
`System.Security.Cryptography` — bez dodatkowych pakietów NuGet).

Dwie role (`users.role`): **administrator** (pełny dostęp, widzi wszystkie projekty) i
**użytkownik** (dostęp tylko do przypisanych mu projektów — `project_users`, zarządzane w
Ustawienia → Użytkownicy; nieprzypisany projekt jest dla niego niewidoczny na liście i bez
struktury). Zwykły użytkownik może odpinać elementy ze struktury, ale nie usuwać ich
całkowicie z bazy ani zarządzać kontami. System pilnuje, żeby zawsze zostawał co najmniej
jeden administrator (nie da się usunąć ani zdegradować ostatniego). Ustawienia Języka i
Wyglądu są dostępne dla każdego; Użytkownicy, Magazyn plików i Logi tylko dla administratora.

Jeśli tabela `users` jest pusta przy starcie API, samo zakłada domyślne konto
**`admin` / `admin`** (patrz konsola przy pierwszym uruchomieniu) — zmień to hasło od razu
po zalogowaniu (`PATCH /api/auth/password`, albo z poziomu aplikacji webowej).

### Powiadomienia

Powiadomienia (tabele `notifications`/`notification_preferences`) są adresowane do
konkretnego użytkownika i wyzwalane dla dziesięciu typów zdarzeń: własny element wchodzi w
sprawdzanie/zostaje wydany/cofnięty do w_pracy (`status_review`/`status_released`/
`status_regressed`), nowa rewizja (`new_revision`), przypisanie do lub usunięcie z
projektu (`project_assigned`/`project_unassigned`), usunięcie przypisanego projektu
(`project_deleted`), zmiana Twojego hasła przez administratora (`password_changed`), mało
miejsca na dysku magazynu (`low_disk_space`, tylko administratorzy) oraz jednorazowa
notatka o przykładowym projekcie (`sample_project`, wyzwalana, gdy zupełnie pusta baza
zakłada przy pierwszym starcie jeden demonstracyjny projekt/złożenie/dwie części —
zabezpieczona flagą `system_state.sample_project_seeded`, więc nigdy nie wystąpi ponownie,
nawet po ręcznym wyczyszczeniu w Strefie zagrożenia). Każdy typ można osobno wyłączyć per
użytkownik (Ustawienia → Powiadomienia); powiadomienie można oznaczyć jako przeczytane albo
usunąć (`DELETE /api/notifications/{id}`).

### Endpointy API

| Metoda | Ścieżka | Co robi |
|---|---|---|
| POST | `/api/auth/login` \| `/logout` | logowanie / wylogowanie — login to jedyny endpoint bez wymaganej sesji |
| GET/PATCH | `/api/auth/me` \| `/password` | dane zalogowanego użytkownika / zmiana WŁASNEGO hasła |
| POST | `/api/auth/browser-bridge-ticket` | wystawia jednorazowy, krótkotrwały bilet logowania dla bieżącej sesji wołającego |
| GET | `/api/auth/browser-login` | zamienia bilet logowania (nie sam token sesji) na ciasteczko przeglądarki, dla makr CAD (otwiera przeglądarkę już zalogowaną) |
| GET/POST/PATCH/DELETE | `/api/users[/{id}]` | zarządzanie kontami — **tylko administrator** |
| GET/POST/PATCH/DELETE | `/api/projects[/{id}]` | lista/tworzenie/edycja/usunięcie projektu (zapis — tylko administrator; lista filtrowana wg dostępu) |
| GET/POST/DELETE | `/api/project-users`, `/api/projects/{projectId}/users/{userId}` | zarządzanie przypisaniami użytkowników do projektów — **tylko administrator** |
| GET | `/api/items?search=&tag=&projectId=` | lista elementów z filtrami (filtrowana wg dostępu do projektu) |
| GET | `/api/items/{id}` | szczegóły elementu |
| GET | `/api/items/by-number/{itemNumber}` | szczegóły elementu po numerze zamiast guidzie — używane przez makro SolidWorks do rozwiązania, do której Części/Złożenia należy rysunek (.SLDDRW) |
| POST | `/api/projects/{projectId}/nodes` | tworzy Folder/Część/Złożenie/Plik bez uploadu (opcjonalnie z ticketem dla makra CAD) |
| POST | `/api/projects/{projectId}/items` | **multipart/form-data**: upload pliku (opcjonalnie `parentId`) |
| GET | `/api/items/{id}/file` | pobranie wgranego pliku |
| POST | `/api/items/{id}/duplicate` | duplikuje Część/Złożenie (nowy numer, status, właściciel) |
| PATCH | `/api/items/{id}/name` \| `/visibility` \| `/status` \| `/project` | zmiana nazwy / widoczności w drzewku / statusu / przeniesienie do innego projektu. Złożenie przechodzące na `sprawdzany`/`wydany` dostaje odmowę, dopóki jego BEZPOŚREDNIE komponenty są w tyle; `promoteChildren: true` przestawia je razem ze złożeniem w jednej transakcji |
| GET | `/api/items/{id}/status-precheck?target=` | co stoi na przeszkodzie tej zmianie statusu: podzłożenia do osobnego załatwienia, komponenty, których nie wolno tknąć (anulowane / zablokowane przez kogoś / w projekcie bez dostępu) i komponenty, które da się pociągnąć. Tylko odczyt — `PATCH /status` sprawdza tę samą regułę niezależnie |
| POST | `/api/items/{id}/lock` \| `/release` | zablokowanie (przejęcie na własność) / zwolnienie elementu |
| DELETE | `/api/items/{id}` | usunięcie całkowite (rekurencja tylko przez Foldery — komponenty Złożenia nigdy nie są kasowane razem z nim) — **tylko administrator** |
| GET | `/api/projects/{projectId}/relations` | relacje rodzic-dziecko (struktura/BOM) danego projektu |
| POST/DELETE | `/api/items/{parentId}/children[/{childId}]` | dodanie/odpięcie podelementu |
| PATCH | `/api/items/{parentId}/children/{childId}/position` \| `/reorder` | zmiana L.p. w BOM-ie (pojedyncza pozycja albo cała nowa kolejność) |
| PATCH | `/api/projects/{projectId}/roots/reorder` | zmiana kolejności korzeni drzewka projektu |
| GET | `/api/items/{id}/bom` \| `/bom/csv` \| `/bom/aggregated-csv` | zagłębiony BOM (JSON) / eksport CSV (pełny / zsumowany) |
| GET | `/api/items/{id}/used-in` | każde złożenie, na dowolnej głębokości, które zawiera ten element — odwrotność BOM-u |
| GET | `/api/items/{id}/documentation/extensions`, `/documentation` | rozszerzenia plików dostępne do pobrania / ZIP z załącznikami (element + poddrzewo) |
| GET | `/api/projects/{projectId}/documentation/extensions`, `/documentation` | to samo, dla całego projektu |
| GET | `/api/tags` | lista tagów |
| POST/DELETE | `/api/items/{id}/tags[/{tagName}]` | zarządzanie tagami |
| PATCH/DELETE | `/api/items/{id}/properties[/{key}]` | zarządzanie właściwościami (zablokowane poza statusem `w_pracy` i poza blokadą właściciela — wyjątek: pola ceny) |
| GET | `/api/items/{id}/revisions` | historia komentarzy rewizji (tylko rewizje z komentarzem) |
| GET | `/api/items/{id}/history` | pełna historia: utworzenie, zmiany statusu, rewizje, dodanie/usunięcie załącznika, blokada/zwolnienie właściciela (kiedy/kto/opis), chronologicznie |
| GET/POST/PATCH/DELETE | `/api/materials[/{id}]` | katalog materiałów (nazwa + grupa/podgrupa) |
| GET/POST/PATCH/DELETE | `/api/manufacturers[/{id}]`, `/api/manufacturers/{id}/contacts[/{contactId}]`, `/api/manufacturers/{id}/product-types[/{typeId}][/subtypes[/{subtypeId}]]` | katalog producentów + osoby kontaktowe + serie/typy i ich podtypy |
| GET/POST/PATCH/DELETE | `/api/clients[/{id}]`, `/api/clients/{id}/contacts[/{contactId}]`, `/api/clients/{id}/name2[/{name2Id}]` | katalog klientów + osoby kontaktowe + ich lista Nazw 2 |
| GET/POST/PATCH/DELETE | `/api/clients/{id}/nodes[/{nodeId}]`, `/nodes/folder`, `/nodes/file`, `/nodes/{nodeId}/file`, `/nodes/search` | własne drzewko dokumentów klienta (foldery/pliki — upload/pobranie/zmiana nazwy/usunięcie/wyszukiwanie) |
| GET/POST/DELETE | `/api/items/{itemId}/attachments[/{id}]`, `/register`, `/api/attachments/{id}/file` | załączniki (upload/rejestracja istniejącego pliku/lista/pobranie/usunięcie) |
| GET/POST/DELETE | `/api/saved-filters[/{id}]` | zapisane zestawy filtrów widoku „Cała baza” (prywatne per użytkownik) |
| GET/POST/DELETE | `/api/notifications[/{id}]`, `/{id}/read`, `/read-all` | lista powiadomień / oznaczenie jako przeczytane (jedno lub wszystkie) / usunięcie — dla zalogowanego użytkownika |
| GET/PATCH | `/api/notification-preferences` | wyłączenie powiadomień per typ dla zalogowanego użytkownika |
| GET/POST | `/api/create-tickets/{ticket}`, `/attach-existing` | korelacja makro CAD ↔ przeglądarka (zob. `EasyPDM.FreeCad/README.md`) |
| GET/POST | `/api/drawing-tickets/{ticket}`, `/resolve` | korelacja makro SolidWorks ↔ przeglądarka dla "do którego elementu należy ten rysunek", gdy jego widoki wskazują na więcej niż jeden już podlinkowany element |
| GET | `/api/config` | lokalizacja magazynu plików (do użytku np. przez makro FreeCAD) |
| GET/POST | `/api/settings/storage`, `/storage/move`, `/backup`, `/restore` | lokalizacja/statystyki magazynu, przeniesienie, backup (pg_dump + pliki w ZIP), przywrócenie z backupu — **tylko administrator** |
| GET/PATCH | `/api/settings/backup-schedule` | harmonogram automatycznej kopii zapasowej (włącz/wyłącz, częstotliwość, dzień, godzina, liczba przechowywanych kopii) — **tylko administrator** |
| GET/PATCH | `/api/settings/item-number-prefixes[/{rodzaj}]` | prefiksy-litery numeru elementu per rodzaj (4 rodzaje Części + `Zlozenie` = złożenie wykonywane; złożenie zakupowe/klienta używa prefiksu rodzaju Części) — **tylko administrator** |
| GET/PATCH | `/api/settings/item-number-format` | format numeru dla elementów tworzonych od teraz: `digits` (minimalna szerokość dopełnienia zerami, 0 = bez dopełniania) i `withName` (czy dochodzi nazwa elementu w nawiasie). Oba opcjonalne przy PATCH i zapisywane niezależnie, bo w interfejsie to dwie osobne sekcje; zamrażane na elemencie przy jego tworzeniu — **tylko administrator** |
| GET/POST | `/api/settings/item-number-sequence`, `/reset` | podgląd/cofnięcie sekwencji numerów elementów — **tylko administrator** |
| GET | `/api/settings/logs`, `/logs/{date}`, `/logs/{date}/download` | lista dni z zapisanym logiem, ostatnie N wierszy z danego dnia, pobranie pełnego pliku — **tylko administrator** |

## Jak uruchomić

Backend czyta prawdziwe dane dostępowe (hasło do bazy, ścieżka magazynu) z
`EasyPDM.Api/appsettings.Local.json` — **plik NIE jest w repozytorium** (gitignored, bo
zawiera hasło), więc na nowym klonie trzeba go założyć z wzoru:

```bash
cp EasyPDM.Api/appsettings.Local.json.example EasyPDM.Api/appsettings.Local.json
# ...i wpisać tam prawdziwe ConnectionString/StorageRoot dla tej maszyny.
```

Program **sam stosuje nowe migracje bazy przy każdym starcie** (wbudowane w plik
wykonywalny jako embedded resources, śledzone w tabeli `schema_migrations` — zob.
`MigrationRunner.cs`) — więc na już istniejącej, znanej bazie wystarczy zwyczajnie ją
uruchomić, bez ręcznego dogania `db/migrations/`. Jedyny przypadek, kiedy trzeba coś zrobić
ręcznie, to zupełnie **świeży, pusty** PostgreSQL — wtedy najpierw:

```bash
# Jeśli nie istnieje jeszcze rola/baza (świeży PostgreSQL):
sudo -u postgres psql -c "CREATE ROLE pdm_user LOGIN PASSWORD 'twoje-haslo';"
sudo -u postgres createdb -O pdm_user pdm

# ...i podstawowy schemat (od tego miejsca program dogania resztę sam):
psql -h localhost -U pdm_user -d pdm -f db/schema.sql

# Backend (serwuje też zbudowany frontend z wwwroot/)
cd EasyPDM.Api
dotnet restore && dotnet build && dotnet run
```

Frontend — do pracy nad UI z podglądem na żywo (proxy `/api` → `http://localhost:5000`):

```bash
cd EasyPDM.Web
npm install
npm run dev      # http://localhost:5173
```

Do wdrożenia: `npm run build` w `EasyPDM.Web/` nadpisuje `EasyPDM.Api/wwwroot/` —
`dotnet run` serwuje wynik pod `http://localhost:5000` bez dodatkowej konfiguracji.

### Docker (zalecane do wdrożenia serwerowego)

**Najprościej**: `./install-easypdm-docker.sh` — zakłada `.env` (generuje losowe hasło do
bazy, jeśli nie podasz własnego), sam wybiera WOLNY port hosta (próbuje od 5000 wzwyż —
przydatne na serwerze, gdzie inne usługi mogą już coś tam trzymać, co w praktyce jest częstym
przypadkiem), buduje i uruchamia kontenery. Uruchom ten sam skrypt ponownie po `git pull`,
żeby zaktualizować — wykrywa istniejący `.env` i niczego w nim nie nadpisuje.

Albo ręcznie:

```bash
cp .env.example .env      # ustaw prawdziwe PDM_DB_PASSWORD
docker compose up -d --build
```

Uruchamia dwa kontenery: `postgres` (obraz `postgres:18`, dane na wolumenie `pgdata`, schemat
z `db/schema.sql` zakładany automatycznie przy pustym wolumenie) i `api` (budowany z
`Dockerfile` w korzeniu repo — buduje frontend, publikuje backend, doinstalowuje
`postgresql-client-18` dla funkcji backup/restore w Ustawieniach). Magazyn plików,
automatyczne kopie zapasowe i logi trzymane są na wolumenie `pdm-data` (`/data` w
kontenerze) — przetrwają przebudowanie obrazu przy aktualizacji. Po starcie:
`http://localhost:5000`. Jeśli port 5000 jest już zajęty na tej maszynie, ustaw
`PDM_HOST_PORT=inny_port` w `.env` (NIE przez `docker-compose.override.yml` — Compose
DOKLEJA listy jak `ports` między plikami zamiast je zastępować, więc override z innym
portem i tak próbowałby zbindować oba naraz i padłby na tym zajętym).

**Aktualizacja**: `git pull && docker compose up -d --build` — nowy obraz `api` dostaje nowy
kod, kontener się odtwarza, a migracje stosują się automatycznie przy starcie, jak wyżej —
nic więcej nie trzeba robić ręcznie. `docker-entrypoint-initdb.d`
z `schema.sql` odpala się TYLKO przy pierwszym, zupełnie pustym starcie wolumenu `pgdata`
(świeża instalacja); przy aktualizacji nie jest w ogóle dotykany, bo wolumen już istnieje.

#### Wdrożenie BEZ klonowania repo (tylko gotowy obraz)

Dwa workflowy publikują gotowe obrazy do GitHub Container Registry —
`ghcr.io/pawelcel/easypdm-api` i `ghcr.io/pawelcel/easypdm-postgres` (ten drugi to zwykły
`postgres:18` z wbudowanym `db/schema.sql` — bez tego świeża baza zostałaby pusta, bo
`MigrationRunner.cs` świadomie nie tworzy sam podstawowego schematu):

- `publish-docker-image.yml` — przy każdym pushu na `main` dotykającym kodu serwera,
  taguje oba obrazy jako `:edge` (+ SHA commita). Do sprawdzenia najnowszego stanu `main`
  przed wydaniem (`docker pull ghcr.io/pawelcel/easypdm-api:edge`) — nigdy nie rusza
  `:latest`.
- `publish-docker-release.yml` — tylko przy wypchnięciu taga wersji (`v0.1.2`, zgodnego
  z `EasyPDM.Web/src/version.ts` i `MyAppVersion` w `packaging/windows/EasyPDM.iss`),
  taguje oba obrazy jako `:latest` ORAZ `:v0.1.2`. To JEDYNY workflow ruszający
  `:latest` — więc `docker-compose.yml` (który ściąga `:latest`) zawsze dostaje
  świadomie wydaną wersję, nigdy przypadkowy commit z `main`. Żeby wydać nową wersję:
  ```bash
  git tag v0.1.2
  git push origin v0.1.2
  ```

Więc do samego wdrożenia NIE trzeba klonować całego repo (ze wszystkimi makrami CAD/
instalatorami/testami, których serwer w ogóle nie potrzebuje). Wystarczą dwa pliki:

```bash
mkdir easypdm-deploy && cd easypdm-deploy
curl -O https://raw.githubusercontent.com/pawelcel/EasyPDM/main/docker-compose.yml
curl -O https://raw.githubusercontent.com/pawelcel/EasyPDM/main/.env.example
cp .env.example .env      # ustaw prawdziwe PDM_DB_PASSWORD
docker compose pull
docker compose up -d
```

> Dopóki repo (i pakiet w GHCR) jest prywatne, `curl` powyżej i `docker compose pull`
> wymagają uwierzytelnienia — `curl` z nagłówkiem `Authorization: Bearer <token>`, a przed
> `docker compose pull` dodatkowo `docker login ghcr.io -u <login> -p <token>` (token z
> uprawnieniem `read:packages`). Po upublicznieniu repo/obrazu żadne logowanie nie będzie
> już potrzebne.
>
> **Jednorazowo, po pierwszej publikacji**: KAŻDY pakiet w GHCR domyślnie jest PRYWATNY
> niezależnie od widoczności samego repo — trzeba je raz ręcznie przełączyć na publiczne,
> OBA (GitHub → zakładka **Packages** przy repo → `easypdm-api` / `easypdm-postgres` →
> **Package settings** → **Change visibility**), inaczej `docker compose pull` bez
> wcześniejszego `docker login` dostanie 403/404 nawet na publicznym repo.

**Aktualizacja** tą ścieżką: `docker compose pull && docker compose up -d` — bez `git pull`
(nie ma czego pullować, nie masz tu repo), po prostu ściąga to, na co aktualnie wskazuje
`:latest` — czyli najnowsze WYDANE wydanie, niekoniecznie najnowszy commit na `main`.

### Linux — instalacja natywna jako usługa systemd (bez Dockera)

```bash
sudo ./install-easypdm-linux.sh
```

Jeden skrypt: instaluje PostgreSQL, jeśli go jeszcze nie ma (rozpoznaje `pacman`/`apt`/`dnf`
— na Arch/CachyOS dodatkowo sam inicjalizuje klaster, bo tamtejszy pakiet, w odróżnieniu od
Debiana/Fedory, nie robi tego automatycznie), zakłada rolę i bazę `pdm` (generuje losowe
hasło, jeśli nie podasz własnego przez `PDM_DB_PASSWORD=... sudo -E ./install-easypdm-linux.sh`),
buduje frontend i publikuje backend jako **self-contained pojedynczy plik wykonywalny**
(`dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true` — gotowa usługa
NIE wymaga już zainstalowanego .NET-a, tylko sam czas budowy), zakłada dedykowane,
nieuprzywilejowane konto systemowe `easypdm`, i instaluje usługę systemd
(`easypdm.service`, autostart, `ProtectSystem=strict` + `ReadWritePaths` ograniczone do
`/var/lib/easypdm` — usługa nie może pisać nigdzie indziej w systemie). Po instalacji:
`http://localhost:5000`, status przez `systemctl status easypdm`, logi na żywo przez
`journalctl -u easypdm -f` (niezależnie od własnego dziennika aplikacji w Ustawienia ->
Logi). Odinstalowanie: `sudo ./uninstall-easypdm-linux.sh` (celowo NIE rusza samej bazy danych ani
PostgreSQL — o tym decyduje się ręcznie, żeby nie skasować danych przez pomyłkę).

**Aktualizacja**: `git pull`, potem `sudo ./install-easypdm-linux.sh` ponownie — wykrywa istniejącą
bazę/konto (pomija ich zakładanie), przebudowuje i podmienia tylko aplikację, jawnie
**restartuje usługę** (`systemctl restart`, nie tylko `enable --now`, które na już
uruchomionej usłudze nic by nie zrobiło). Nowe migracje bazy program stosuje sam
automatycznie przy starcie — nic dodatkowego nie trzeba robić ręcznie.

> Skrypt buduje ze źródeł tego repozytorium (jak `run.sh`, tylko jako trwała usługa
> zamiast procesu na pierwszym planie) — nie ma (jeszcze) osobnego, gotowego wydania
> binarnego do pobrania. Sam self-contained publikowany plik wykonywalny był realnie
> uruchomiony i sprawdzony (serwuje frontend, loguje), a treść jednostki systemd
> zweryfikowana przez `systemd-analyze verify`; pełny przebieg skryptu (tworzenie
> roli/bazy/konta systemowego przez `sudo`) nie był jeszcze wykonany end-to-end — przy
> pierwszym uruchomieniu obserwuj wyjście i zgłoś, jeśli coś nie zagra.

### Windows — instalator (`.exe`, Inno Setup)

**Najprościej: `.github/workflows/build-windows-installer.yml`** buduje gotowy
`EasyPDM_Windows_v<wersja>.exe` (numer wersji z `MyAppVersion`/`OutputBaseFilename` w
`packaging/windows/EasyPDM.iss`) automatycznie na windowsowym runnerze GitHuba (ma Inno
Setup Compiler fabrycznie) przy każdym pushu dotykającym backendu/frontendu/instalatora —
nie trzeba mieć Windows ani Inno Setup lokalnie. Uruchom ręcznie przez `gh workflow run
build-windows-installer.yml`, poczekaj (`gh run watch`), pobierz artefakt (`gh run download
<id> -n EasyPDM_Windows_v<wersja>`).

Alternatywnie, do zbudowania lokalnie na maszynie z Windows (.NET 10 SDK + Node.js +
[Inno Setup Compiler](https://jrsoftware.org/isinfo.php)):

```powershell
powershell -ExecutionPolicy Bypass -File packaging\windows\build.ps1
iscc packaging\windows\EasyPDM.iss
```

Powstaje `packaging\windows\Output\EasyPDM_Windows_v<wersja>.exe`. Instalator: sprawdza, czy
PostgreSQL jest już zainstalowany (jeśli nie — kieruje na stronę pobierania i przerywa,
świadomie NIE próbuje cicho doinstalować kilkusetmegabajtowego instalatora PostgreSQL w
tle), pyta o hasło superużytkownika `postgres` (jednorazowo, do założenia własnej roli
`pdm_user` i bazy `pdm` — samo hasło nigdzie nie jest zapisywane), zakłada schemat, zapisuje
`appsettings.Production.json` z resztą ustawień (magazyn/kopie/logi w
`%ProgramData%\EasyPDM`), rejestruje `EasyPDM.Api.exe` jako **usługę Windows**
(autostart, działa w tle bez okna konsoli) i tworzy skrót otwierający
`http://localhost:5000`. Odinstalowanie zatrzymuje i usuwa usługę (standardowy deinstalator
Inno Setup) — tak samo jak na Linuksie, celowo nie rusza samej bazy danych.

**Aktualizacja**: zbuduj nowy `EasyPDM_Windows_v<wersja>.exe` (jak wyżej) i uruchom go
ponownie. Istniejąca instalacja jest wykrywana po stałym `AppId` (klucz Uninstall w
rejestrze), więc Inno podmienia ją W MIEJSCU zamiast instalować obok. Aktualizacja **nie
pyta o hasło superużytkownika `postgres`** — instalator odczytuje hasło roli `pdm_user` z
`appsettings.Production.json` poprzedniej instalacji i w ogóle nie dotyka roli ani bazy,
więc hasło roli ZOSTAJE bez zmian (nic, co łączy się do tej bazy poza EasyPDM — skrypty
kopii, pgAdmin — nie przestaje działać). `PrepareToInstall` zatrzymuje usługę PRZED podmianą
plików (inaczej Windows zablokowałby nadpisanie działającego `.exe`) i uruchamia ją z
powrotem zamiast rejestrować od nowa. Nowe migracje bazy program stosuje sam przy starcie, a
ustawienia zmienione w aplikacji (np. lokalizacja magazynu plików) przeżywają aktualizację —
siedzą w `appsettings.Local.json`, a instalator pisze tylko `Production.json`.

Instalacja **starszej** wersji na nowszej jest odrzucana z komunikatem: migracje bazy idą
wyłącznie w przód, więc starszy program nie umiałby odczytać już zmigrowanego schematu.

> Skrypt `.iss` faktycznie się kompiluje (zweryfikowane prawdziwym Inno Setup Compilerem w
> CI, nie tylko przeglądem kodu) — po drodze złapane i poprawione 5 realnych błędów
> specyficznych dla dialektu Pascal Script Inno Setup (m.in. brak lokalnych sekcji `const`
> w funkcjach, `LoadStringFromFile` wymagające `AnsiString`, brak `Randomize`/`RandSeed`/
> `GetTickCount` — nie ma żadnego udokumentowanego sposobu na ręczne zasianie wbudowanego
> `Random`, więc korzysta z niego wprost). Sama instalacja end-to-end na żywej maszynie z
> PostgreSQL nie była jeszcze ręcznie przetestowana — przy pierwszym uruchomieniu obserwuj
> przebieg i zgłoś, co nie zagra.

Pierwsze logowanie: **`admin` / `admin`** (konto zakładane automatycznie, jeśli tabela
`users` jest pusta — zob. "Logowanie, role i dostęp do projektów" wyżej). Zmień hasło od
razu po zalogowaniu.

## Znane ograniczenia

1. **Brak walidacji rozmiaru/typu wgrywanego pliku i załącznika** — każdy plik przejdzie,
   niezależnie od rozszerzenia czy wielkości.
2. **Magazyn plików (`storage/`) to zwykły folder na dysku serwera.** Backup/restore z
   poziomu Ustawień pakuje `pg_dump` bazy razem z magazynem plików w jeden ZIP; można go
   pobrać ręcznie albo włączyć automatyczną kopię (Ustawienia -> Magazyn plików ->
   Automatyczna kopia zapasowa) z wyborem częstotliwości (codziennie/co tydzień/co miesiąc)
   oraz dnia i godziny — sprawdzane co minutę przez `ScheduledBackupService` w tle, zapisywane
   do osobnego katalogu `backups/` (niezależnego od `storage/`, żeby kopia nie pakowała samej
   siebie), z konfigurowalną liczbą przechowywanych ostatnich kopii (domyślnie 14 — starsze
   są automatycznie kasowane). Wersjonowanie pliku przy zmianie
   rewizji działa dziś tylko w przepływie makra FreeCAD (`storage/components/`, jeden plik na
   rewizję, zob. `EasyPDM.FreeCad/README.md`) — zwykłe załączniki dodawane z aplikacji
   webowej nie mają automatycznego powiązania z numerem rewizji.
3. **Nie każda operacja zapisuje "kto to zrobił"** — utworzenie elementu (`created_by`),
   zmiana statusu, komentarz do rewizji, dodanie/usunięcie załącznika i blokada/zwolnienie
   właściciela już to robią (widać w „Historii"), ale np. zmiana właściwości/nazwy/tagów
   nie zapisuje autora.
4. **W Dockerze „Zmień lokalizację” magazynu plików (Ustawienia -> Magazyn plików) nie
   przetrwa przebudowania obrazu** — ta operacja zapisuje nową ścieżkę do
   `appsettings.json` wewnątrz kontenera `api` (poza wolumenem `pdm-data`), więc po
   `docker compose up --build` wraca do wartości ze zmiennej środowiskowej `StorageRoot`
   ustawionej w `Dockerfile`. Sama zmiana lokalizacji API działa poprawnie w trakcie życia
   kontenera — problem dotyczy tylko trwałości tego ustawienia między przebudowaniami.

## Następne kroki (proponowana kolejność)

1. Walidacja uploadu (typ/rozmiar) dla elementów i załączników.
2. Zapisywanie autora zmiany właściwości/nazwy/tagów (punkt 3 wyżej).
