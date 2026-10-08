# EasyPDM — technische Dokumentation

[English](TECHNICAL.md) | [Polski](TECHNICAL.pl.md) | **Deutsch**

Dieses Dokument richtet sich an den Administrator, der EasyPDM installiert/betreut, sowie
an Entwickler. Eine Beschreibung des Werkzeugs selbst (wozu es dient und wie man es beim
Konstruieren benutzt) finden Sie in [README.de.md](README.de.md).

## Status

Projekte und Elemente werden manuell über die Web-Anwendung erstellt (Datei-Upload direkt
in den Speicher der API) oder über das FreeCAD- (`EasyPDM.FreeCad/`), SolidWorks-
(`EasyPDM.SolidWorks/`) oder Autodesk-Inventor-Makro (`EasyPDM.Inventor/`), die dieselbe
API aufrufen.
Der frühere Ansatz mit Festplatten-Scan (`EasyPDM.Core`, `EasyPDM.Indexer`) wurde aus dem
Repo entfernt — er war seit Migration `002` nicht mehr mit dem Schema kompatibel und
wurde von `Api` nie verwendet.

Das Frontend ist eine separate **React 19 + Vite + TypeScript**-Anwendung
(`EasyPDM.Web/`), die direkt nach `EasyPDM.Api/wwwroot/` gebaut wird. Die Oberfläche ist
vollständig übersetzt (Polnisch/Englisch/Deutsch) und hat einen hellen/dunklen Modus. Live
getestet auf: CachyOS, .NET 10, PostgreSQL 18.

## Was hier ist

- **`db/schema.sql`** — das vollständige Schema von Grund auf (aktueller Stand nach allen
  Migrationen).
- **`db/migrations/`** — Migrationen `002`–`046` für eine bereits bestehende Datenbank:
  Projekte, Elementtypen, Sichtbarkeit im Baum, Status/Revisionen, Materialien
  (+ Gruppen/Untergruppen), Anhänge, Stücklisten-Reihenfolge, Revisionskommentare,
  Anmeldung und Rollen, Projekteigenschaften, kaskadierendes Löschen, Reihenfolge der
  Baum-Wurzeln, Hersteller, gespeicherte Filter, projektbezogener Zugriff pro Benutzer,
  Eigentümer/Sperre eines Elements, Entfernung des toten Revisions-/Checkout-Schemas,
  Historie (Status/Revisionen/Anhänge/Sperre), Zeitplan für automatische Sicherungen,
  Nachverfolgung angewendeter Migrationen, Vorschau-/CAD-Rolle von Anhängen, Buchstaben-
  Präfix der Elementnummer pro Art, Kunden (Katalog + eigener Dateibaum), projektlose
  Elemente (ein Element kann ohne Projekt existieren, nur über "Gesamte Datenbank"
  erreichbar), Kontaktadresse von Hersteller/Kunde, Standardwert/Eindeutigkeit der
  Stücklistenposition, Benachrichtigungen + deren Einstellungen pro Typ, Markierung des
  Beispielprojekts, eine kleine interne Zustandstabelle `system_state`,
  Hersteller-Serien/Typen samt ihren Untertypen, den Status „Storniert", die
  Umstellung von Name 2 eines Kunden von einer einzelnen Spalte auf eine 1:N-Liste
  sowie eine eigene Adresse für jede Name 2 samt `name2_id` bei Kundenkontakten (NULL =
  gehört zum Kunden selbst, schreibgeschützt von jeder seiner Name 2 geerbt) sowie
  dieselbe `name2_id`-Aufteilung bei `client_nodes`, sodass jede Name 2 ihre eigenen
  Dateien haben kann, unabhängig vom Dateibaum des Kunden selbst, sowie eine neue
  Anhang-Rolle `"drawing"` (neben `pdf`/`step`/`cad`) für eine SolidWorks-Zeichnung
  (.SLDDRW), die neben der eigenen CAD-Datei des Teils/der Baugruppe hochgeladen wird.
  Seit Migration 027 sind die Dateien aus diesem Ordner in das Programm eingebettet (embedded
  resources) und werden **automatisch bei jedem Start** angewendet — siehe
  `MigrationRunner.cs` und "Inbetriebnahme" unten — sie müssen nicht mehr manuell per
  psql ausgeführt werden.
- **`EasyPDM.Api/`** — ASP.NET Core (minimale API, Npgsql ohne ORM), Endpunkte nach
  Funktion aufgeteilt unter `Endpoints/` — vollständige Liste unten unter
  "API-Endpunkte". Liefert auch das gebaute Frontend aus dem eigenen `wwwroot/` aus. Ein
  eigener `FileLoggerProvider` (ohne zusätzliches NuGet-Paket) schreibt Programmprotokolle
  nach `logs/` (tägliche Rotation, 30 Tage Aufbewahrung), sichtbar unter Einstellungen →
  Protokolle.
- **`EasyPDM.Web/`** — Frontend: React 19 + Vite + TypeScript + Tailwind v4 + shadcn/ui
  (Komponenten auf Basis von Base UI, Stil „base-nova"), i18n (pl/en/de), heller/dunkler
  Modus.
- **`EasyPDM.Api.Tests/`** — Integrationstests (xUnit + `WebApplicationFactory`), führen
  die GESAMTE Anwendung gegen ein echtes PostgreSQL aus (ein separates Schema `pdm_test`
  in derselben Datenbank, vor jeder Testklasse zurückgesetzt). Lokal: `dotnet test
  EasyPDM.Api.Tests` (die Connection-String zeigt standardmäßig auf ein lokales
  `pdm`/`pdm_user` — überschreibbar über die Variable
  `EASYPDM_TEST_CONNECTION_STRING`, genau wie in der CI).
- **`EasyPDM.FreeCad/`** — zwei Makros: `EasyPDMUpload.FCMacro` (wird aus FreeCAD heraus
  ausgeführt, speichert das aktive Dokument, delegiert die Wahl von Projekt/neu-oder-
  vorhanden/Eigenschaften an den Browser, erstellt ein Teil/eine Baugruppe im PDM, hängt
  die Datei als Anhang an, exportiert STEP und benennt die lokale Datei in
  `nummer(name)` um) und `EasyPDMDownload.FCMacro` (die umgekehrte Richtung: Teil/
  Baugruppe im Browser auswählen, zusammen mit dem GESAMTEN Baum der
  Baugruppenkomponenten abrufen — damit sich `App::Link`-Referenzen auflösen — und sofort
  in FreeCAD öffnen; überspringt bereits heruntergeladene Dateien, fragt vor dem
  Überschreiben einer älteren Revision durch eine neuere). **Beide Makros sind in ihrer
  aktuellen Version (Browser-basierter Ablauf) auf einem echten FreeCAD ungetestet** —
  siehe `EasyPDM.FreeCad/README.md`.
- **`EasyPDM.SolidWorks/`** — das Gegenstück zu oben für SolidWorks (VBA-Makros
  `EasyPDMUpload.bas`/`EasyPDMDownload.bas`), mit demselben Browser-basierten Ablauf,
  STEP-Export und automatischer Erkennung des Baugruppenbaums. **Nicht auf einem echten
  SolidWorks verifiziert** — siehe `EasyPDM.SolidWorks/README.md` für Details und bekannte
  Risiken.
- **`EasyPDM.Inventor/`** — das Gegenstück zu oben für Autodesk Inventor (VBA-Makros
  `EasyPDMUpload.bas`/`EasyPDMDownload.bas`), portiert aus `EasyPDM.SolidWorks/` mit
  demselben Browser-basierten Ablauf, STEP/PDF-Export und automatischer Erkennung des
  Baugruppenbaums. **Nicht auf einem echten Inventor verifiziert** — siehe
  `EasyPDM.Inventor/README.md` für Details und bekannte Risiken.
- **`Dockerfile`/`Dockerfile.postgres`/`docker-compose.yml`/`install-easypdm-docker.sh`**,
  **`install-easypdm-linux.sh`/`uninstall-easypdm-linux.sh`** und **`packaging/windows/`**
  (der `.exe`-Installer, Inno Setup) — drei Bereitstellungswege, ohne Backend/Frontend/
  Datenbank manuell einzeln zusammenzusetzen, siehe "Inbetriebnahme" unten.
- **`.github/workflows/`** — sieben CI-Workflows, alle auch manuell ausführbar
  (`workflow_dispatch`) oder über `gh workflow run <datei>`:
  - `build.yml` — bei jedem Push/PR: Backend-Build + Integrationstests
    (`EasyPDM.Api.Tests`, gegen einen `postgres`-Dienst in der CI) sowie
    Typen/Lint/Build des Frontends.
  - `build-windows-installer.yml` — baut `EasyPDM_Windows_v<Version>.exe` (siehe oben) und
    **installiert es zusätzlich tatsächlich** auf einem Windows-Runner (PostgreSQL über
    Chocolatey, `/VERYSILENT`), wobei zweimal geprüft wird (frische Installation +
    simuliertes Update), dass der Dienst startet und der Server antwortet — der einzige
    Weg, dies ohne einen physischen/virtuellen Windows-Rechner zu prüfen. Auch als
    wiederverwendbarer `workflow_call` deklariert (siehe `create-release-draft.yml` unten).
  - `build-linux-package.yml` — baut `EasyPDM-Linux-x64_v<Version>.tar.gz` (self-contained
    Backend + gebautes Frontend + Installations-/Deinstallationsskripte + `db/schema.sql`)
    und installiert es tatsächlich auf einem sauberen Ubuntu-Runner, um zu prüfen, dass der
    Dienst startet. Ebenfalls als wiederverwendbarer `workflow_call` deklariert.
  - `test-linux-installer.yml` — führt `install-easypdm-linux.sh` tatsächlich auf einem
    sauberen Ubuntu aus (frische Installation, "Update", `uninstall-easypdm-linux.sh`),
    was die lokale Entwicklungsumgebung (kein `sudo`-Passwort in dieser Sitzung) nicht
    zuließ.
  - `publish-docker-image.yml` — baut und veröffentlicht die Images `api` und `postgres`
    (Letzteres mit eingebettetem `db/schema.sql`) in die GitHub Container Registry
    (`ghcr.io/pawelcel/easypdm-api`, `ghcr.io/pawelcel/easypdm-postgres`) mit dem Tag
    `:edge` (+ Commit-SHA) bei jedem Push, der Server-Code betrifft — zum Prüfen des
    neuesten Stands von `main` vor einem Release, siehe "Docker" unten.
  - `publish-docker-release.yml` — dieselben zwei Images, aber nur beim Push eines
    Versions-Tags (`v*`); der einzige Workflow, der `:latest` aktualisiert (das, was
    `docker-compose.yml` tatsächlich zieht), plus einen passenden `:vX.Y.Z`-Tag. Siehe
    "Docker" unten.
  - `create-release-draft.yml` — ebenfalls beim Push eines Versions-Tags (`v*`), unabhängig
    von `publish-docker-release.yml`: prüft zuerst, ob `MyAppVersion` (`EasyPDM.iss`) und
    `APP_VERSION` (`version.ts`) tatsächlich mit dem Tag übereinstimmen (bricht sonst sofort
    ab), ruft dann `build-windows-installer.yml`/`build-linux-package.yml` als
    wiederverwendbare Workflows auf und erstellt einen **Entwurf** (draft) eines GitHub
    Release mit beiden angehängten Artefakten und Versionshinweisen aus dem passenden
    `## [X.Y]`-Abschnitt der `CHANGELOG.md`. Veröffentlicht ihn bewusst nie automatisch —
    jemand muss den Entwurf noch prüfen und auf "Publish release" klicken.

### Datenmodell — Elemente und Struktur

Vier Elementtypen (`item_type`): **Ordner** (reiner Container), **Teil**/**Baugruppe**
(haben eine Nummer aus der globalen Sequenz, einen Status, eine Revision und einen
Eigentümer), **Sonstige Datei** (beliebige Datei ohne eigene Struktur darunter). Die
Baum-/Stücklistenstruktur ist eine separate Tabelle `item_relations` (`parent_id`,
`child_id`, `quantity`, `position`) — dies erlaubt es, dass dasselbe Teil/dieselbe
Baugruppe gleichzeitig eine gemeinsam genutzte Komponente in mehreren
Baugruppen/Projekten ist.

Was darf unter was hinzugefügt werden (sowohl im Backend als auch im Frontend erzwungen):

| Elternteil | Erlaubte Kinder |
|---|---|
| Projekt / Ordner | alles (Ordner, Teil, Baugruppe, Datei) |
| Baugruppe | nur Teil und Baugruppe (Stückliste) |
| Teil / Datei | nichts — das sind Blätter der Struktur |

Das Löschen eines Elements hat zwei Modi: **„Aus Struktur entfernen"** (löst die
Beziehung / verbirgt die Wurzel, der Datensatz bleibt bestehen) und **„Vollständig
löschen"** (nur Administrator). Letzteres steigt **ausschließlich über Ordner** ab — ein
Ordner *besitzt* seinen Inhalt und nimmt ihn beim Löschen mit, während eine Baugruppe ihre
Komponenten nur *verwendet* (die Stücklistenbeziehung bedeutet „ist Teil von", nicht
„gehört zu"). Das Löschen einer Baugruppe entfernt daher nur diesen einen Datensatz; alle
Komponenten bleiben bestehen und verlieren lediglich diese eine Beziehung. Ein Teil/eine
Baugruppe ist ein eigenständiger Katalogeintrag (eigene Nummer, Revisionen, Historie,
Eigentümer, Anhänge) und kann später jeder anderen Baugruppe beitreten, wird also nie als
Nebeneffekt gelöscht. Innerhalb eines gelöschten Ordner-Teilbaums überlebt zusätzlich
alles, was auch einen Elternteil außerhalb davon hat (siehe `survivors` in
`ItemEndpoints.cs`).

Das vollständige Löschen entfernt auch die **Dateien des Elements aus dem Speicher**, nicht nur
seine Zeilen. `ON DELETE CASCADE` räumt die Datenbank auf, rührt die Festplatte aber nie an,
deshalb sammelt der Endpunkt alle Pfade *vor* dem `DELETE`: die eigene Datei des Elements, den
gesamten Inhalt von `item_attachments` (CAD-Datei, Zeichnung, PDF, STEP, Vorschaubild, gewöhnliche
Anhänge) sowie die Anhänge an seinen Kundenprüfungen — diese liegen in einer eigenen Tabelle, die
nach Prüfung und nicht nach Element verschlüsselt ist, und wurden genau deshalb zunächst übersehen
und blieben ohne jede Zeile zurück, über die man sie hätte finden können. Das Löschen eines
Projekts tut dasselbe mit `project_attachments`, und zwar nach dem Commit der Transaktion, damit
ein fehlgeschlagenes Löschen nicht die Dateien eines weiterhin bestehenden Projekts mitnimmt.

Ein Teil/eine Baugruppe kann auch **dupliziert** werden (die Kopie
erhält eine neue Nummer, einen frischen Status und Eigentümer) — im Baum landet die Kopie
direkt unter dem Original.

Auch das Projekt selbst kann gelöscht werden (`DELETE /api/projects/{id}`, nur
Administrator) — dies löscht NICHT seine Elemente: `project_id` wird bei allen auf `NULL`
gesetzt (nicht kaskadiert), sodass Teile/Baugruppen mit ihren Dateien, Anhängen, Tags,
Historie und Stücklisten-Beziehungen intakt bleiben, danach nur noch über "Gesamte
Datenbank" erreichbar. Dies schützt auch Elemente, die über `item_relations` in die
Stückliste eines anderen Projekts eingebunden sind — das Löschen des besitzenden Projekts
beschädigt die Struktur des anderen Projekts nicht mehr.

Ein Teil hat vier **Arten** (`properties.rodzaj`), jede mit einem anderen Satz von
Feldern und einem anderen Symbol im Baum: **Gefertigt** (Material, Preis, Zusätzliche
Informationen), **Zugekauft** (Hersteller, Serie/Typ, Untertyp, Bestellnummer 1/2, Masse, Preis,
Zusätzliche Informationen), **Norm** (Material, Norm, Zusätzliche Informationen),
**Kundenteil** (Kunde, Zusätzliche Informationen).

Eine Baugruppe hat drei eigene Arten im selben `properties.rodzaj`: **Wykonywane**
(gefertigt), **Zakupowe** (zugekauft — Hersteller, Serie/Typ, Untertyp) und **Klienta** (vom
Kunden — Kunde). Die Zeichenketten unterscheiden sich BEWUSST von denen des Teils ("Zakupowe"
statt "Zakupowa"), denn dieser Wert dient zugleich als Schlüssel für das Nummernpräfix —
die einzige gemeinsame Zeichenkette ist "Klienta", die sich auch das Präfix teilt. Über
die Felder ihrer Art hinaus hat eine Baugruppe weiterhin den generischen
Eigenschaften-Editor (Masse und beliebige eigene Schlüssel). Baugruppen aus früheren
Versionen haben keine Art und zeigen einen Hinweis, eine auszuwählen.

**Kunde** (`properties.client`, Tabelle `clients`) — für die Art Kundenteil, bei Teil
oder Baugruppe, ausgewählt aus dem Kundenkatalog (Reiter Kunden) nach demselben Muster
wie Hersteller/Material: Verknüpfung über den Namen, kein Fremdschlüssel. Daneben **Name 2**
(`properties.clientName2`) — einer der zweiten Namen/Handelsvarianten DIESES Kunden aus
dem Katalog (Tabelle `client_name2`, eine 1:N-Beziehung zum Kunden — ein Kunde kann
mehrere haben, z. B. verschiedene Tochtergesellschaften unter demselben Hauptnamen, keine
1:1-Spalte) — gesperrt, bis ein Kunde gewählt ist; die Optionsliste enthält alle Namen 2
dieses Kunden, leer, falls keiner vorhanden ist. Ein Kundenwechsel löscht einen zuvor
gewählten Name 2. Die linke Liste im Reiter Kunden spiegelt das direkt wider — eine
flache Name/Name-2-Tabelle, eine Zeile je Name 2 (ein Kunde ohne einen erhält eine Zeile
mit einem Strich), sodass jede Variante sichtbar ist, ohne den Kunden zu öffnen, mit einer
Löschen-Schaltfläche direkt an der Zeile. Das Namensfeld im Dialog „Kunde hinzufügen" ist
selbst ein Picker über denselben Katalog und ist „dynamisch": Wird ein bereits im Katalog
vorhandener Name eingegeben/ausgewählt, wechselt er vom Anlegen eines doppelten Kunden zum
Hinzufügen eines neuen Namens 2 zu diesem bestehenden Kunden (bestätigt mit einem
einzelnen „OK" statt „Hinzufügen") — genau das verhindert, dass ein Kunde (z. B. „Bosch")
in mehrere fast identische Katalogeinträge zerfällt, nur um verschiedene Name-2-Varianten
festzuhalten.

Unabhängig von `properties.client` oben ist der **Kundenkatalog** (Tabelle `clients`,
Reiter Kunden) eine eigenständige Entität erster Klasse: Name/Standort, seine Liste von
Namen 2 (`client_name2`), Kontaktpersonen (`client_contacts`) und ein eigener
Dokumentenbaum (`client_nodes`), z. B. für Normen oder Referenzdateien, unabhängig von
`items`/`item_relations`. Ein Projekt kann optional mit einem davon verknüpft werden
(`projects.client_id`) — der Detailbereich dieses Kunden listet dann jedes ihm zugewiesene
Projekt auf (im Rahmen dessen, worauf der aktuelle Benutzer Zugriff hat), mit einer
Schaltfläche zum direkten Wechsel dorthin. Ein Projekt kann optional auch auf einen
bestimmten Namen 2 dieses Kunden zeigen (`projects.client_name2_id`), ausgewählt direkt
neben dem Feld Kunde im eigenen Projektformular — wird beim Wechsel des Kunden geleert und
bei späterem Löschen dieses Namens 2 auf null zurückgesetzt (das Projekt bleibt bestehen).
Die Projektauswahlliste und die eigene Zeile des Projekts oben in seiner Struktur zeigen
dann "Projekt (Kunde, Name 2)", und die Projektliste ist überall sortiert nach Kundenname,
dann Name 2, zuletzt nach dem Namen des Projekts selbst.

Ein Projekt kann außerdem einen **Projektleiter** (`projects.lead_contact_id`) haben — eine
bestimmte Person aus der Kontaktliste dieses Kunden (`client_contacts`), angezeigt neben
Kunde/Name 2 in den eigenen Projekteigenschaften. Entweder ein Kontakt, der direkt zum
Kunden gehört, oder einer, der genau zu dem Namen 2 gehört, mit dem das Projekt verknüpft
ist (niemals ein Kontakt eines ANDEREN Namens 2 desselben Kunden — geprüft in
`ProjectEndpoints.ValidateLeadContactAsync`, derselben "muss tatsächlich zusammengehören"-
Regel wie bei `client_name2_id`). Wird beim Wechsel des Kunden oder des gewählten Namens 2
geleert und bei späterem Löschen dieses Kontakts auf null zurückgesetzt.

Ein Projekt trägt außerdem ein `closed`-Flag, umgeschaltet über eine Schaltfläche in seinen
eigenen Eigenschaften. Ein geschlossenes Projekt fällt aus den "aktiven" Listen heraus
(Auswahlliste, "Meine Projekte", der Projekt-Picker beim Hinzufügen eines Elements), bleibt
aber ansonsten unverändert -- seine Elemente bleiben über "Gesamte Datenbank" vollständig
durchsuchbar, und dieselbe Schaltfläche öffnet es wieder. `GET /api/projects` liefert immer
alle Projekte unabhängig von `closed` -- jede Liste entscheidet selbst, ob geschlossene
herausgefiltert werden (die Projektdetailansicht, direkt über die id erreicht, tut dies
absichtlich nicht, damit ein geschlossenes Projekt erreichbar und wieder umschaltbar bleibt,
sobald man dort angelangt ist).

**Serie/Typ** (`properties.productType`, Tabelle `manufacturer_product_types`) und
**Untertyp** (`properties.productSubtype`, Tabelle `manufacturer_product_subtypes` mit
Fremdschlüssel auf die Serie) bilden einen zweistufigen Katalog je Hersteller (Reiter
Hersteller). Die Verknüpfung mit einem Element erfolgt ausschließlich über den Namen, wie
bei Hersteller und Material, sodass das Löschen eines Katalogeintrags bereits beschriebene
Elemente nie verändert. Die gesamte Kette Hersteller → Serie/Typ → Untertyp kaskadiert in
beide Richtungen, jedoch unterschiedlich an den beiden Stellen, an denen sie vorkommt: im
Eigenschaftenformular des Elements (`ProductTypeAndSubtypeFields`, property-fields.tsx)
sind beide Felder IMMER sichtbar, nur gesperrt, solange die Ebene darüber leer ist
(Serie/Typ ohne Hersteller, Untertyp ohne Serie) — bewusst so, damit nichts zu
verschwinden scheint; in den Filtern von "Gesamte Datenbank"
(`ProductTypeFilterSelect`/`ProductSubtypeFilterSelect`) erscheint der niedrigere Filter
erst, wenn der darüber gesetzt ist. An beiden Stellen löscht bzw. verbirgt eine Änderung
(oder, bei den Filtern, das Zurücksetzen) einer höheren Ebene die darunter. Der Untertyp
ist optional — eine Serie ohne Untertypen bietet einfach eine leere Liste.

Ein Teil/eine Baugruppe hat eine Zustandsmaschine: `w_pracy → sprawdzany → (w_pracy |
wydany) → w_pracy` (in Bearbeitung → in Prüfung → (in Bearbeitung | freigegeben) → in
Bearbeitung), plus `wydany → anulowana → w_pracy` (freigegeben → storniert → in
Bearbeitung; die Rückkehr von `wydany`/freigegeben ODER `anulowana`/storniert erhöht die
Revisionsnummer, mit einem optionalen Kommentar zur Revision). `anulowana` ist
ausschließlich AUS `wydany` erreichbar — ein Element muss freigegeben gewesen sein, bevor
es sich als überflüssig erweisen kann. Eine Baugruppe mit einem stornierten Element
irgendwo in ihrer Stückliste (rekursiv, in beliebiger Verschachtelungstiefe —
`FindCancelledDescendantLabelsAsync` in `ItemEndpoints.cs`, dasselbe CTE-Muster wie
`BomEndpoints.FetchBomRowsAsync`) kann selbst nicht `wydany` werden; `PATCH /status`
lehnt das mit einem 400 ab, der die stornierten Elemente benennt und direkt im
Statusbestätigungsdialog des Frontends (`StatusControl`) landet, ohne separaten Dialog.
`anulowana` ist, wie `wydany`, immer eigentümerlos — `/lock`/`/release` lehnen beide
Statuswerte identisch ab. Außerhalb des Status `w_pracy`/in Bearbeitung ist das
Bearbeiten von Name/Eigenschaften gesperrt — Ausnahme: Preis/Währung/Preisart sind immer
bearbeitbar. Das Symbol eines Elements im Baum/in der Liste wird für den Status
`anulowana` rot (`STATUS_ICON_COLOR` in `item-visuals.ts`). Am unteren Rand des Eigenschaftenbereichs eines Teils/einer Baugruppe wird
die **Historie** angezeigt: wann und wer das Element erstellt hat, jede Statusänderung
(wann/wer/von-nach), jede Revision mit Kommentar (wann/wer/Beschreibung), jeder
hinzugefügte/entfernte Anhang (wann/wer/Dateiname) und jede Eigentümersperre/-freigabe
(wann/wer), zusammengefasst in einer chronologischen Liste.

**Eigentümer und Sperre** (`owner_id`/`owner_locked`) — unabhängig vom Status. Der
Ersteller eines Teils/einer Baugruppe wird sofort dessen/deren Eigentümer, und das
Element wird gesperrt: Solange die Sperre besteht, kann nur der Eigentümer es bearbeiten
(Eigenschaften, Name, Sichtbarkeit, Verschieben in ein anderes Projekt, Anhänge, die
Stücklistenstruktur darunter) — **nicht einmal ein Administrator umgeht dies**. Jeder
kann ein freigegebenes Element sperren und wird dadurch dessen neuer Eigentümer; nur der
aktuelle Eigentümer kann es freigeben — **außer einem Administrator, der auch eine fremde
Sperre übernehmen (`POST /lock`) oder erzwungen aufheben (`POST /release`) sowie den
Status eines gesperrten Elements ändern kann (`PATCH /status`), unabhängig vom
Eigentümer** — etwa bei Abwesenheit eines Mitarbeiters. Ein Element im Status
`wydany`/freigegeben ist immer freigegeben und ohne Eigentümer — es kann nicht gesperrt
werden. Im Baum wird dies durch ein Schloss-Symbol angezeigt: grün (von Ihnen gesperrt),
gelb (von jemand anderem), offen (freigegeben).

Die Stückliste einer Baugruppe zeigt: Position (editierbar durch Eingabe einer
Ganzzahl — muss innerhalb dieser Stückliste eindeutig sein — oder durch Ziehen der
Zeile), Name, Menge, Material, Norm, Hersteller, Bestellnummer 1/2 (fehlende Felder als
„-"), zusammen mit verschachtelten Elementen (Teile verschachtelter Baugruppen, Position in
der Form `2.1`). CSV-Export in zwei Varianten: vollständig (jedes Vorkommen einzeln
aufgeführt) und zusammengefasst (dieselbe Komponente mehrfach an verschiedenen Stellen
verwendet — eine Zeile mit der über die gesamte Kette aufgelösten Gesamtmenge).

Auch die umgekehrte Ansicht ist verfügbar (`GET /api/items/{id}/used-in`) — jede
Baugruppe, in beliebiger Tiefe und projektübergreifend, die ein gegebenes Element enthält,
angezeigt im Detailbereich des Elements selbst oberhalb der Historie.

Anhänge (`item_attachments`) sind ein von der Struktur getrennter Mechanismus — eine
beliebige Datei (z. B. CAD) kann über den Eigenschaftenbereich an ein Teil/eine
Baugruppe/eine Datei angehängt werden; sie können nicht über den Baum links hinzugefügt
oder entfernt werden. Von einem Projekt/einer Baugruppe/einem Teil aus lässt sich die
**Dokumentation** herunterladen — ein aus allen Anhängen in einem bestimmten Bereich
zusammengestelltes ZIP (das ganze Projekt oder eine bestimmte Baugruppe/ein Teil samt
Teilbaum), mit Auswahl, welche Dateierweiterungen einbezogen werden sollen.

Die Nummer eines Elements (`item_number`) stammt aus einer einzigen, globalen
PostgreSQL-Sequenz — das Löschen eines Elements gibt seine Nummer NICHT automatisch
frei (Standardverhalten von Sequenzen). Ein Administrator kann die Sequenz manuell auf
eine angegebene Nummer zurückdrehen (Einstellungen → Nummerierung) — dies funktioniert
nur, wenn kein vorhandenes Element diese Nummer oder eine höhere bereits hat, sodass sich
der von gelöschten Testelementen hinterlassene Nummern-"Schwanz" ohne Kollisionsrisiko
zurückgewinnen lässt.

Was der Benutzer sieht, ist diese Nummer, eingekleidet in drei Dinge, die alle **am Element**
gespeichert und bei dessen Erstellung eingefroren werden: ein Buchstabenpräfix je Art
(`item_number_prefix`, aus `item_number_prefixes`), eine Mindestbreite zum Auffüllen mit
Nullen (`item_number_digits`) und ob der Name des Elements überhaupt in Klammern angehängt
wird (`item_number_with_name`) — die letzten beiden aus `system_state`. Eine Änderung dieser
Einstellungen wirkt sich daher nur auf später erstellte Elemente aus. Das ist keine
Vorsicht um ihrer selbst willen: Die CAD-Makros bauen aus dieser Nummer den Dateinamen, der
somit auf der Festplatte und in `item_attachments` liegt, wo ihn nachträglich nichts mehr
umschreibt. `ItemNumbering.Label` setzt die drei Teile an einer Stelle zusammen, und die API
liefert das Ergebnis als `itemNumberLabel` neben `itemNumber`/`itemNumberPrefix` — dasselbe
Muster wie `revisionLabel`, damit das Web-Frontend und die drei CAD-Makros es nie selbst
zusammensetzen (und nie voneinander abweichen).

Die einzige Ausnahme ist ein früh bemerkter Fehler: Das Ändern der Art eines Teils/einer
Baugruppe berechnet das Präfix neu, solange das Element in keinem der vier hervorgehobenen
Felder einen Anhang hat (`preview_role` = `cad`, `drawing`, `pdf`, `step`, `image`). Das sind die
Dateien, deren Namen die Makros aus der Nummer ableiten; gewöhnliche Anhänge behalten ihre
eigenen Namen und blockieren nichts. Ist ein hervorgehobenes Feld belegt, LEHNT
`PATCH /properties` eine Änderung der Art ab, statt sie mit veraltetem Präfix zu übernehmen,
und das Element-Objekt führt `kindLocked`, damit die Oberfläche die Schaltflächen ausgrauen
kann. Abgelehnt wird nur eine tatsächliche Änderung — dieselbe Art erneut zu senden geht
durch. Die Nummer selbst ändert sich nie.

Der vollständige Name eines Elements — sein **Datensatzname**, einmal von
`ItemNumbering.RecordName` zusammengesetzt und als `recordName` ausgeliefert — lautet
`Präfix + aufgefüllte Nummer`, unmittelbar gefolgt vom Namen in Klammern: `C0001(Platte)`,
oder nur `C0001`, wenn der Name abgeschaltet ist. Für eine Datei auf der Festplatte kommen
noch der Revisionsbuchstabe und die Erweiterung hinzu: `C0001(Platte).A.sldprt`. Die
Namenszuordnung jedes Makros behandelt sowohl den Namen in Klammern als auch das Leerzeichen
älterer Dateien als optional und erkennt damit jede frühere Konvention.

Da der Name im Dateinamen fehlen kann, schreiben beide VBA-Makros ihn zusätzlich in eine
Dokumenteigenschaft, `EasyPDM_Name`, neben die dort bereits gepflegten Verknüpfungs-
eigenschaften — diese Eigenschaft ist dann die einzige Stelle im Dokument, an der der Name
überhaupt vorkommt, und Zeichnungsvorlagen können ihn von dort beziehen. Beide schreiben
außerdem `EasyPDM-Mass` und `EasyPDM_Material` und lesen sie unmittelbar nach dem Speichern
mit einem einzigen PATCH auf die Eigenschaften `mass`/`material` des Elements zurück.

In SolidWorks enthält keine der beiden einen Wert, sondern einen Ausdruck —
`"SW-Mass@@Default@<Dateiname>"` und `"SW-Material@@Default@<Dateiname>"` —, den SolidWorks
beim Neuaufbau/Speichern auflöst, sodass beide dem Modell von selbst folgen; das Makro liest
den *aufgelösten* Wert. Die umschließenden Anführungszeichen gehören zum Wert und sind keine
Schreibweise: ohne sie lässt SolidWorks den Text unangetastet und wertet ihn nie aus. Ein Wert,
der weiterhin wie der Ausdruck aussieht (unaufgelöst, etwa bei einem Teil ohne zugewiesenes
Material), wird protokolliert und verworfen statt gesendet. Inventor kennt keinen
entsprechenden Ausdruck, dort halten beide daher
eine zum Zeitpunkt des Hochladens aus `ComponentDefinition` gelesene Momentaufnahme und
werden erst beim nächsten Hochladen aktualisiert. Zeichnungen werden ganz übersprungen, das
Material nur für Teile geschrieben (eine Baugruppe hat kein eigenes), und eine Masse, die leer
oder keine schlichte Zahl ist, wird protokolliert und übersprungen — sie wird zuvor auf
Ziffern und einen Dezimalpunkt normalisiert, da sie mit Einheit und landesüblichem
Dezimalkomma eintreffen kann.

Noch davor übergibt das Makro beim Öffnen des Browsers zum Anlegen eines neuen Elements das
Material des Dokuments im Deep-Link (`&material=`), direkt aus der CAD-API gelesen und nicht
aus `EasyPDM_Material` — zu diesem Zeitpunkt wurde noch nichts gespeichert, der Ausdruck
existiert also nicht. `pending-create-ticket.ts` nimmt es entgegen und `AddNodeDialog` zeigt es
im Feld Material, sodass es bereits beim Anlegen sichtbar ist, statt unmittelbar nach dem
Hochladen von selbst aufzutauchen. Ein Duplikat behält die Eigenschaften des Quellelements:
dort war die Wahl bewusst.

Dieses Feld ist dann **schreibgeschützt** (`materialLocked`, an `MaterialField` durchgereicht):
Das Makro schreibt das Material ohnehin unmittelbar nach dem Hochladen aus `EasyPDM_Material`
auf das Element, eine hier getroffene Wahl würde also Augenblicke später überschrieben — und
genau das Anbieten dieser Wahl war das Verwirrende. Die bewusste Änderung erfolgt am bereits
angelegten Element, wo das Feld wie gewohnt bearbeitbar ist. Das Material eines Duplikats
bleibt bearbeitbar, denn es stammt aus einem Element, das jemand ausgewählt hat. Die gesperrte
Variante ist ein schlichtes deaktiviertes `Input` und keine deaktivierte `Combobox`: Das
Material steht beim Öffnen des Fensters womöglich noch nicht im Katalog, und eine `Combobox`
zeigt einen Wert außerhalb ihrer Liste nicht an.

Ein dem Katalog noch unbekanntes Material legt `MaterialCatalog` an
(`INSERT … ON CONFLICT (name) DO NOTHING`, damit zwei Makros, die dasselbe gleichzeitig
senden, nicht kollidieren), aufgerufen aus **beiden** Pfaden, die eines mitbringen können:
`PATCH /properties` (das Makro nach dem Hochladen) und `POST /nodes` (das Anlegen, wo das
Material aus dem vom Makro vorausgefüllten Fenster stammt). Solange dies nur `PATCH` tat, trug
ein mit CAD-Material angelegtes Element einen Namen, den der Katalog bis zum Abschluss des
Hochladens nicht kannte. Die Makros lesen das Material aus dem Dokument statt aus einer
Liste; ohne dies trüge das Element ein Material, das sich weder erneut auswählen noch als
Filter nutzen ließe. Angelegt wird nur der Name; Gruppe und Untergruppe bleiben leer.

### Mehrere Elemente im Baum auswählen

Zeilen werden mit Strg (Cmd) + Klick ausgewählt, neben der älteren Schaltfläche „Mehrere auswählen" und ihren Kontrollkästchen — die Schaltfläche bleibt, denn ein Tablet hat keine Strg-Taste. Ein Strg+Klick schaltet den Auswahlmodus selbst ein: Kontrollkästchen und Aktionsleiste gibt es nur dort, der erste solche Klick würde sonst etwas Unsichtbares auswählen.

Die Auswahl merkt sich den **Elternknoten der angeklickten Zeile**, nicht nur die Kennung des Elements. Dasselbe Element kann an mehreren Stellen zugleich im Baum hängen — als Wurzel des Projekts und unter einer Baugruppe — und „aus der Struktur entfernen" löst eine benannte Stelle: `removeChild(parent, id)` plus `moveItemToProject(id, null)` für ein Kind oder das Löschen von `showInTree` für eine Wurzel. Ohne den Elternknoten der Zeile ließe sich nicht sagen, welche davon gemeint war. Es ist genau das, was die Aktion für ein einzelnes Element tut, für jede ausgewählte Zeile ausgeführt.

Solange etwas ausgewählt ist, treten die Aktionsleisten des aktuellen Elements und des Projekts zurück. Beide liegen absolut positioniert über der Auswahlleiste, und beide tragen eine Schaltfläche wie eine der Sammelaktionen: Nebeneinander standen zwei „Aus der Struktur entfernen" — eine für das Element in der Vorschau, eine für die ganze Auswahl — ohne im Aussehen unterscheidbar zu sein. Die Sammelaktion heißt zudem „Ausgewählte aus der Struktur entfernen", passend zum benachbarten „Ausgewählte löschen". War das Element der Vorschau unter den gelösten, fällt die Anzeige auf das Projekt zurück, wie nach einem einzelnen Lösen — die Bereinigung „aus dem Baum verschwunden" greift hier nicht, denn der Datensatz existiert weiter, nur ohne Projekt.

### Die Modellvorschau ist ein Bild, kein gerendertes STEP

Das Vorschaufeld über den Eigenschaften eines Elements zeigt einen **PNG-Schnappschuss**, den das
CAD-Makro beim Hochladen aufnimmt und als Anhang mit `preview_role = 'image'` sendet. Die
STEP-Datei wird weiterhin genau wie bisher exportiert und hochgeladen (`preview_role = 'step'`) —
sie steht zum Herunterladen bereit, steuert aber die Anzeige nicht mehr.

Früher tat sie das. Der Browser holte das STEP, parste es mit `occt-import-js` (OpenCascade nach
WebAssembly kompiliert), tesselierte jede Fläche, ließ `THREE.EdgesGeometry` über jeden
entstandenen Körper laufen und rendete das Ganze mit three.js. All das ergab ein **unbewegtes
Bild**: ein `renderer.render()`, keine Animationsschleife, kein Drehen, nichts zum Ziehen. Der
Preis fiel bei jedem geöffneten Element an, bei jedem Benutzer, und richtete sich nicht nach der
Größe der STEP-Datei, sondern nach der Komplexität der Geometrie — eine 40-kB-Datei voller
Verrundungen und Freiformflächen tesselliert zu Hunderttausenden Dreiecken. Das Entfernen nahm
**7,8 MB** aus dem veröffentlichten Bundle, davon 7,6 MB allein die OpenCascade-`.wasm`-Binärdatei,
die jeder Browser herunterlud und kompilierte.

Für den Schnappschuss muss das Dokument **aktiv** sein: Das Speichern eines Bildes erfasst den
aktiven Viewport und nicht das im Aufruf benannte Dokument, und während eine Baugruppe
hochgeladen wird, ist das aktive Dokument die Baugruppe — jede Komponente erhielt also ein Bild
der Baugruppe, aus der sie stammt. SolidWorks und Inventor aktivieren daher das Dokument,
erstellen den Schnappschuss und aktivieren das vorherige wieder; FreeCAD kann die Ansicht eines
namentlich benannten Dokuments nehmen, ohne umzuschalten, dort bewegt sich also nichts am Bild.

Daraus, woher der Schnappschuss stammt, folgen zwei Dinge:

- **Kein STEP, kein Bild.** Das Makro erstellt den Schnappschuss innerhalb seines
  STEP-Upload-Schritts; wird das Häkchen „STEP exportieren" entfernt, bleibt das Element ohne
  Vorschau, und das Feld sagt das auch, statt einen leeren Rahmen zu zeigen.
- **Das Löschen des STEP löscht den Schnappschuss.** Er existiert nur, um dieses Modell
  darzustellen, deshalb entfernt `DELETE /api/attachments/{id}` auf einem `step`-Anhang auch den
  `image`-Anhang des Elements (`DeleteRoleAttachmentsAsync`). Sonst sammelten sich im Speicher
  Bilder, die nichts anzeigt und die sich keinem Modell zuordnen lassen.

`image` belegt einen einzelnen Slot wie `pdf`/`step` (sammelt sich nicht an wie `cad`/`drawing`):
Ein Schnappschuss zeigt die aktuelle Gestalt des Modells, ein neuer Upload ersetzt also den
vorherigen.

Vor dieser Änderung hochgeladene Elemente behalten ihr STEP und verlieren die Vorschau, bis sie
erneut hochgeladen werden — es gibt keinen Renderer mehr, auf den zurückgefallen werden könnte.
Das war eine bewusste Entscheidung: Ihn zu behalten hätte bedeutet, die 7,6-MB-Abhängigkeit für
alle zu behalten.

STEP/IGES/STL sind daher nirgends in der Anwendung mehr vorschaubar, auch nicht im
Anhang-Vorschaudialog; sie erhalten eine Schaltfläche zum Herunterladen. `previewKindOf` erkennt
jetzt ausschließlich PDFs und Rasterbilder.

### Ein Formular anfordern, ohne einen Tab zu öffnen

Das Hochladen einer Baugruppe braucht für jede neue Komponente ein Formular. Bisher öffnete das Makro dafür einen eigenen Tab, und vor jedem erschien ein natives Meldungsfenster — nicht zur Bestätigung, sondern weil der Windows-Schutz gegen Fokusdiebstahl nur dem **ersten** programmatischen Browseraufruf eines Laufs den Fokus zugesteht und jeden weiteren still im Hintergrund öffnet. Ohne Klick dazwischen erschien das Formular in einem Tab, den niemand sah, während `WaitForTicket` auf Eingaben wartete, die sich nicht machen ließen. Bei einer Baugruppe mit vierzig Teilen waren das vierzig Klicks und vierzig Tabs.

Der Browser ist bereits offen und fragt den Server ohnehin ab, ein neuer Tab ist also überflüssig. Das Makro hinterlegt seine Anfrage in `CadRequestStore` (im Arbeitsspeicher, nach Benutzer geschlüsselt, dieselbe Begründung wie bei `CreateTicketStore`), und der offene Tab nimmt sie über `use-cad-requests.ts` an und reicht sie an genau dieselben `PendingTicketBanner` und `AddNodeDialog` weiter, die auch ein Ticket aus der URL bedienen. Am Formular selbst hat sich nichts geändert.

Eine Anfrage richtet sich an **einen Lauf des Makros**, nicht an das Konto. Das CAD-Programm erzeugt eine Lauf-Kennung, übergibt sie dem Browser im Link und der Tab hält sie in `sessionStorage` — das übersteht ein Neuladen, erreicht aber keinen anderen Tab und keinen anderen Rechner. Ein Tab nimmt eine Anfrage nur an, wenn die Kennung übereinstimmt. Allein nach Benutzer zu schlüsseln setzte einen Lauf pro Person voraus, und das gilt nicht mehr, sobald dasselbe Konto in einem Browser auf einem zweiten Rechner angemeldet ist: Eine Anfrage von einem Rechner landete im Tab des anderen, der das Formular jemand völlig anderem zeigte. Das ist in der Praxis passiert, und deshalb gibt es die Kennung.

Daraus folgt auch, warum die **erste** Komponente eines Laufs weiterhin einen Tab öffnet: Dieses Öffnen ist der Weg, auf dem ein Tab auf diesem Rechner die Lauf-Kennung erfährt, und solange keiner sie kennt, hat das Makro niemanden, dem es eine Anfrage übergeben könnte. Dieses erste Öffnen zeigt kein Meldungsfenster mehr, denn der erste Browseraufruf eines Laufs holt den Fokus von selbst — die oben genannte Windows-Regel greift erst ab dem zweiten. Ein normaler Lauf ist damit ein Tab und kein Klick, statt ein Tab und ein Klick pro Komponente.

Hat ein Tab die Anfrage angenommen, gibt das Makro den Fokus mit `AppActivate "EasyPDM"` zurück. Das CAD-Programm kommt zwischenzeitlich nicht ohne Grund nach vorn — für die vorherige Komponente hat es die Datei gespeichert, das STEP exportiert und das Grafikfenster für den Schnappschuss neu gezeichnet —, gebraucht wird jetzt aber das Formular. Es funktioniert, weil Windows der Anwendung, die den Fokus *gerade* hat, erlaubt, ihn abzugeben: dieselbe Regel, die die Klicks erzwang, in die andere Richtung genutzt. Verglichen wird der Anfang des Fenstertitels, sitzt EasyPDM also in einem Hintergrund-Tab, geschieht nichts; ein Fehler wird verschluckt, denn das ist Bequemlichkeit und nicht Teil des Uploads. In FreeCAD lag es umgekehrt: Es musste gar nichts nach vorn geholt werden, denn der Tab kommt von selbst nach vorn, sobald er die Anfrage annimmt — den Fokus nahm ihm erst das eigene Wartefenster des Makros, kurz darauf erzeugt, da Qt ein neues Fenster beim Erzeugen aktiviert. `WA_ShowWithoutActivating` hat das nicht verhindert: Unter Wayland entscheidet der Compositor, nicht die Anwendung, ob ein neues Fenster den Fokus bekommt. Das Fenster ist daher weg — FreeCAD wartet so, wie die Makros für SolidWorks und Inventor schon immer gewartet haben: mit einer Meldung in der Statusleiste, während `processEvents` die Oberfläche am Leben hält. Der Versuch über `wmctrl`/`xdotool` bleibt als Rückfall nur für X11.

Dieselbe Regel erklärt, warum schon das *erste* Öffnen den Browser nicht nach vorn holte. Unter Wayland bekommt ein gestartetes Programm den Fokus nur, wenn der Startende ihm ein xdg-activation-Token übergibt, und Pythons `webbrowser.open` ruft einfach `xdg-open` ohne eines auf. FreeCAD öffnet Adressen jetzt über `QDesktopServices.openUrl` (`open_in_browser`), das seit Qt 6.6 im Namen von FreeCAD ein Token beim Compositor anfordert und weiterreicht — gültig in diesem Moment, weil der Benutzer gerade das Makro gestartet hat und FreeCAD den Fokus hat. `webbrowser.open` bleibt als Rückfall.

Das Token hilft allerdings nur einem Browser, der *gestartet* wird. Ein laufender bekommt die Adresse von einem kurzlebigen Hilfsprozess, und ob er das Token dann nutzt, liegt am Browser — in der Praxis kam ein geschlossener Browser nach vorn, ein offener nicht. Unter Linux bittet das Makro nach dem Öffnen einer Karte daher auch direkt den Compositor: `_raise_via_kwin` schreibt ein kleines KWin-Skript, lädt es über DBus (`org.kde.KWin /Scripting`), führt es aus und entlädt es — derselbe Weg wie bei `kdotool`, den Wayland erlaubt, weil der Compositor selbst das Fenster aktiviert. Es versucht das bei den ersten drei Abfragen, weil die Seite einen Moment zum Laden und Setzen ihres Titels braucht, und nicht öfter, um niemandem den Fokus zu entreißen, der bewusst zu FreeCAD zurückgekehrt ist. Erkannt wird das Fenster am vollständigen `<title>` der Anwendung am Anfang seines Titels: Ein Abgleich nur auf „EasyPDM“ schied nach einer Sonde in einer echten Sitzung aus, in der stattdessen ein Shotcut-Projekt namens `EasyPDM2.mlt` nach vorn gekommen wäre. Außerhalb von KDE bleibt `wmctrl`/`xdotool` (X11), und unter Windows läuft nichts davon.

Deshalb kann die Anwendung den Menschen auch selbst zurückrufen: Wird eine Anfrage angenommen und hat der Tab keinen Fokus, zeigt sie eine System-Benachrichtigung, deren Klick zu ihm wechselt. Ein Klick ist der einzige Mechanismus, den kein Fenstersystem verweigert, denn der Wechsel geht vom Menschen aus. Um Erlaubnis wird einmal gebeten, aus einer Schaltfläche im Dialog des Makros heraus — Browser lehnen eine Anfrage ohne frische Benutzeraktion ab —, und ein fokussierter Tab zeigt nichts, denn dann gibt es nichts zurückzurufen.

Eines lässt sich nicht voraussetzen: dass überhaupt jemand zusieht. Der Browser kann geschlossen, der Server nicht erreichbar sein. Deshalb veröffentlicht das Makro die Anfrage und fragt dann einige Sekunden lang `GET /api/cad-requests/taken` ab; der Tab markiert die Anfrage in dem Moment als angenommen, in dem er sie übernimmt. Kein Signal heißt, dass niemand da ist — und das Makro kehrt zum alten Weg samt Fenster und neuem Tab zurück, statt auf ein Formular zu warten, das niemand sehen wird. In diesem Rückfallweg lebt das Fenster weiter — vor dem Tab, den es öffnet, und erst ab der zweiten Komponente, wo die Fokus-Regel gilt.

`taken` kommt als flache `1`/`0` zurück und nicht als Wahrheitswert in einem verschachtelten Objekt, denn die JSON-Parser in den VBA-Makros kennen nur `JsonGetString` und `JsonGetLong` — eine flache Zahl ist die einzige Form, die sie ohne zusätzlichen Parser lesen können.

### Die Fortschrittsliste für Hoch- und Herunterladen

Während ein CAD-Makro hoch- oder herunterlädt, zeigt die Anwendung rechts eine Liste der beteiligten Dateien und hakt sie nach und nach ab. Der Zustand liegt in `TransferProgressStore` — im Arbeitsspeicher, ohne Tabelle, dieselbe Entscheidung und dieselbe Begründung wie bei `CreateTicketStore`: Er lebt Sekunden bis Minuten, danach braucht ihn niemand, und sein Verlust bei einem Neustart kostet nichts, denn Fortschritt ist eine Information ÜBER die Arbeit, nicht ein Teil davon.

Geschlüsselt wird nach **Benutzer**, nicht nach einer Sitzungskennung. Der Browser fragt damit schlicht „was macht mein Makro?" (`GET /api/progress`), ohne irgendwoher eine Kennung erfahren zu müssen — das funktioniert auch, wenn der Tab schon vor dem Makrolauf offen war. Ein Lauf pro Person zur selben Zeit ist eine sichere Annahme, und ein neuer Lauf ersetzt den alten einfach.

Das Makro meldet die **gesamte Liste im Voraus** und hakt erst danach ab. Ohne vollständige Liste von Anfang an würde der Zähler lügen: Aus „3 von 3" würde „3 von 9", sobald eine weitere Ebene des Baums auftaucht.

- **Das Hochladen** kennt die Liste bereits: `DiscoverComponentTree` durchläuft die Baugruppe, bevor die erste Datei gesendet wird, und die Reihenfolge ist blattweise von unten, weil eine Baugruppe keine Stücklistenbeziehung zu einem Teil bekommen kann, das es im PDM noch nicht gibt.
- **Das Herunterladen** kannte sie nicht. `DownloadChildrenRecursive` steigt Ebene für Ebene ab und weiß zu Beginn nicht, wie viele Dateien es werden — daher `GET /items/{id}/descendants`: eine rekursive Abfrage, die das Element und seinen gesamten Teilbaum liefert. Ihre Entduplizierung entspricht der Menge `seen` im Makro, sodass ein mehrfach verwendetes Teil einmal gezählt wird und der Zähler sein Ende erreicht. Die Liste ist Eltern-vor-Kind sortiert, weil das Makro TATSÄCHLICH in dieser Reihenfolge herunterlädt; jede andere Sortierung ließe die Einträge durcheinander abhaken. Beim Herunterladen spielt die Reihenfolge für die Korrektheit ohnehin keine Rolle — alle Dateien liegen auf der Platte, bevor überhaupt etwas geöffnet wird.

**Die Fortschrittsmeldung darf eine Übertragung niemals abbrechen.** Sie läuft in jedem Makro über einen eigenen, stillen HTTP-Pfad und nicht über `ApiPostJson`/`api_post_json`, die im Fehlerfall eine Ausnahme werfen. Eine fehlende Verbindung, ein neu gestarteter Server oder ein älterer Server ohne diese Endpunkte haben nichts damit zu tun, das zu stoppen, worum der Benutzer tatsächlich gebeten hat. Aus demselben Grund antwortet der Server auf einen unbekannten Schlüssel mit `matched: false` statt mit einem Fehler.

Der Browser fragt alle 1,5 s mit einem schlichten `setInterval` ab — das Muster, das sich in `use-notifications.ts` bereits bewährt hat. Ein erster Versuch wählte das Intervall dynamisch über ein `setTimeout`, das sich selbst neu plante; das erwies sich als messbar fragil (ein einziges Neueinhängen zerriss die Kette, und nichts setzte sie fort), und die Ersparnis war es nicht wert, denn eine Abfrage ist ein Wörterbuchzugriff und ein paar Dutzend Bytes, ohne die Datenbank zu berühren. Ein beendeter Lauf wird nach zwei Minuten nicht mehr ausgeliefert, damit eine Liste von vor einer Stunde nicht die nächste Person begrüßt, die die Anwendung öffnet.

**Als Baum angeordnet.** Bei einer Baugruppe steht die Liste in der Reihenfolge ihrer Struktur, jede Ebene unter ihrem Elternteil eingerückt, obwohl die Dateien von den Blättern her verarbeitet werden — abgehakt wird nach Schlüssel, nicht nach Position, also müssen die beiden Reihenfolgen nicht übereinstimmen. Das erledigt `ProgressTree.Arrange` einmal auf dem Server, für alle drei CAD-Programme. Die Upload-Makros schicken die Eltern-Kind-Paare mit, die sie ohnehin für die Stückliste halten (`edges` in `PUT /api/progress`); eine andere Quelle gibt es nicht, denn neue Komponenten existieren im PDM noch nicht, und die Schlüssel sind lokale Dateipfade. Das Herunterladen braucht keine Makroänderung: Dort sind die Schlüssel Element-IDs, und der Server liest die Beziehungen zwischen ihnen selbst aus `item_relations`. Ein in mehreren Baugruppen verwendetes Teil steht einmal, unter dem ersten Elternteil, den der Durchlauf erreicht — die Entscheidung des Benutzers, und so zählt der Zähler Dateien, nicht Zeilen. Ohne Beziehungen bleibt die Liste genau wie gesendet, flach; was wegen eines Zyklus unerreichbar ist, wird flach angehängt statt verworfen. Keine Symbole für Baugruppe oder Teil: Ein Symbol legte eine Art von Element nahe (Fertigung, Zukauf…), die die Liste nicht kennt.

**Abbrechen.** Die Schaltfläche „Abbrechen" im Panel kann selbst nichts anhalten — das Makro läuft in einem CAD-Programm auf einem anderen Rechner. `POST /api/progress/cancel` hält nur die Bitte fest, und jedes Makro fragt `GET /api/progress/cancelled` (eine flache `1`/`0`, für die VBA-Parser) an den zwei Stellen, an denen Anhalten sicher ist: bei jeder Abfrage nach einem Formular und vor jeder nächsten Datei. Die gerade laufende Datei wird immer fertig, denn ein Abbruch mitten im Schreiben ließe sie beschädigt zurück. Das erste „ja" wird für den Rest des Laufs gemerkt, damit der Server nicht erneut gefragt wird; jeder Fehler gilt als „nicht abgebrochen". In den VBA-Makros wird das Flag am Anfang von `main` zurückgesetzt, weil Modulvariablen dort einen Lauf überleben können und ein übrig gebliebenes `True` jeden späteren Lauf sofort anhielte.

Ein abgebrochener Lauf endet trotzdem mit `finish`, sodass das Panel schließt und der Bericht „abgebrochen" sagt, statt wie ein Fehler auszusehen — nie erreichte Einträge zählen als `pending`, nicht als `failed`. Damit das stimmt, mussten sich zwei Dinge ändern: Das Hauptdokument wurde nach der Rückkehr aus seiner Upload-Funktion bedingungslos als erledigt markiert, auch wenn der Upload aufgegeben war, und die Abbruchpfade in FreeCAD riefen `finish` gar nicht auf, sodass ein abgebrochener FreeCAD-Lauf das Panel bis zum Ablauf stehen ließ. Eine heruntergeladene Baugruppe wird nach einem Abbruch nicht geöffnet, denn ihre Verknüpfungen zeigten auf Dateien, die nie ankamen.

Die Rückfrage steht im Panel selbst und nicht in einem Dialog: Das Panel liegt über den Dialogen (`z-60`), ein Dialog auf `z-50` öffnete sich also darunter — womöglich während schon ein Formular des Makros zu sehen ist. Ist ein solches modales Formular offen, markiert der Dialog das Panel als `aria-hidden`; es bleibt oben und mit der Maus klickbar, doch per Tastatur ist „Abbrechen" erst erreichbar, wenn das Formular weg ist.

### Der Lauf meldet sich selbst in den Benachrichtigungen

Am Ende eines Laufs stellt der Server einen Bericht zusammen und hinterlegt ihn in der Glocke (`cad_transfer_finished`). Die Makros endeten bisher mit einem blockierenden Fenster — „als Element Nr. X hochgeladen" —, was richtig war, solange der Mensch noch im CAD-Programm saß. Seit der Fokus nach jeder Komponente an den Browser zurückgeht, stammt dieses Fenster von einem Programm im HINTERGRUND, erscheint also HINTER dem Browser und hängt dort und wartet auf einen Klick, den niemand sieht. Der Bericht geht deshalb dorthin, wo der Mensch ohnehin hinsieht — und bleibt, anders als ein Fenster, auffindbar.

Zusammengestellt wird er in `POST /api/progress/finish` aus dem Lauf, den der Fortschrittsspeicher ohnehin hält — nicht im Makro. Das ist dieselbe Entscheidung wie beim Zählen des Fortschritts auf dem Server: eine Stelle für drei CAD-Programme, der Bericht liest sich also gleich, aus welchem er auch kam, und das Makro braucht dafür keine Zeile über das `finish` hinaus, das es ohnehin schon rief.

`Finish` gibt den Lauf **nur beim ersten Aufruf** zurück, der ihn beendet. Ein Makro darf `finish` zweimal rufen — einmal aus dem Fehlerpfad, einmal aus dem normalen —, und zwei identische Berichte in der Glocke wären schlicht Rauschen.

Die Daten enthalten Zahlen und die Liste: `kind`, `total`, `done`, `skipped`, `failed`, `pending` sowie bis zu vierzig Einträge mit ihren Bezeichnungen. `done` schließt `skipped` bewusst NICHT ein: Eine übersprungene Komponente war bereits im PDM und musste nicht gesendet werden, sie als gesendet zu zählen würde den Bericht beschönigen. `pending` steht getrennt von `failed`, denn ein abgebrochener Lauf hat nichts kaputtgemacht — er kam nur nicht mehr dorthin. Die Glocke zeigt acht dieser Einträge, Fehlschläge zuerst: Bei zwanzig Dateien und einem Fehlschlag ließen die ersten acht in Listenreihenfolge genau den einen wichtigen Eintrag außerhalb des Bildes.

Die Glocke fragt alle 30 s ab, was für etwas, das vor den Augen des Benutzers geschieht, viel zu selten ist — die Fortschrittsliste sendet daher ein Fensterereignis in dem Moment, in dem der Server den Lauf erstmals als beendet ausliefert, und `use-notifications` lädt daraufhin neu. Die Liste steht dabei über den Dialogen (`z-60`), also musste das Aufklappfeld der Glocke noch höher: Sonst verdeckte die Liste genau die Benachrichtigung, die sie gerade angekündigt hatte.

Ein Fenster bleibt mit Absicht: Komponenten, die bereits mit einem PDM-Element verknüpft sind und den Status „in Prüfung" oder „freigegeben" haben. Diese aktualisiert das Makro nie, eine lokale Änderung daran ging also NICHT hoch — und die Dateiliste kann das nicht sagen, denn aus ihrer Sicht ist nichts geschehen.

### Anmeldung, Rollen und Projektzugriff

Jede Anfrage an `/api/*` (außer `/api/auth/login`) erfordert eine Anmeldung — eine
Sitzung ist ein zufälliges Token in einem httpOnly-Cookie (`pdm_session`, 30 Tage
gültig), gespeichert in der Tabelle `sessions`. Passwörter werden als PBKDF2 gespeichert
(eigene Implementierung in `PasswordHasher.cs`, nur `System.Security.Cryptography` —
ohne zusätzliche NuGet-Pakete).

Zwei Rollen (`users.role`): **Administrator** (voller Zugriff, sieht alle Projekte) und
**Benutzer** (Zugriff nur auf die ihm zugewiesenen Projekte — `project_users`, verwaltet
unter Einstellungen → Benutzer; ein nicht zugewiesenes Projekt ist für ihn in der Liste
unsichtbar und ohne Struktur). Ein gewöhnlicher Benutzer kann Elemente aus der Struktur
lösen, sie aber nicht vollständig aus der Datenbank löschen oder Konten verwalten. Das
System stellt sicher, dass immer mindestens ein Administrator übrig bleibt (der letzte
kann weder gelöscht noch degradiert werden). Die Einstellungen für Sprache und
Erscheinungsbild stehen jedem zur Verfügung; Benutzer, Dateispeicher und Protokolle nur
dem Administrator.

Wenn die Tabelle `users` beim Start der API leer ist, legt sie selbst ein Standardkonto
**`admin` / `admin`** an (siehe Konsole beim ersten Start) — ändern Sie dieses Passwort
sofort nach der Anmeldung (`PATCH /api/auth/password`, oder über die Web-Anwendung).

### Benachrichtigungen

Benachrichtigungen (Tabellen `notifications`/`notification_preferences`) sind an einen
bestimmten Benutzer adressiert und werden für zehn Ereignistypen ausgelöst: ein eigenes
Element geht in Prüfung/wird freigegeben/auf "In Bearbeitung" zurückgesetzt
(`status_review`/`status_released`/`status_regressed`), eine neue Revision
(`new_revision`), Zuweisung zu oder Entfernung aus einem Projekt
(`project_assigned`/`project_unassigned`), Löschung eines zugewiesenen Projekts
(`project_deleted`), Änderung Ihres Passworts durch einen Administrator
(`password_changed`), wenig Speicherplatz auf dem Speicher (`low_disk_space`, nur
Administratoren) und der einmalige Hinweis auf das Beispielprojekt (`sample_project`,
ausgelöst, wenn eine wirklich leere Datenbank beim ersten Start ein Demo-Projekt/
eine Baugruppe/zwei Teile anlegt — abgesichert durch die Flag
`system_state.sample_project_seeded`, sodass er nie erneut auftritt, auch nicht nach
einem manuellen Löschen in der Gefahrenzone). Jeder Typ kann pro Benutzer einzeln
deaktiviert werden (Einstellungen → Benachrichtigungen); eine Benachrichtigung kann als
gelesen markiert oder gelöscht werden (`DELETE /api/notifications/{id}`).

### API-Endpunkte

| Methode | Pfad | Was es tut |
|---|---|---|
| POST | `/api/auth/login` \| `/logout` | Anmeldung / Abmeldung — Login ist der einzige Endpunkt ohne erforderliche Sitzung |
| GET/PATCH | `/api/auth/me` \| `/password` | Daten des angemeldeten Benutzers / Änderung des EIGENEN Passworts |
| POST | `/api/auth/browser-bridge-ticket` | stellt ein einmaliges, kurzlebiges Anmelde-Ticket für die eigene Sitzung des Aufrufers aus |
| GET | `/api/auth/browser-login` | tauscht ein Anmelde-Ticket (nicht das rohe Sitzungstoken) gegen ein Browser-Cookie, für CAD-Makros (öffnet den Browser bereits angemeldet) |
| GET/POST/PATCH/DELETE | `/api/users[/{id}]` | Kontenverwaltung — **nur Administrator** |
| GET/POST/PATCH/DELETE | `/api/projects[/{id}]` | Liste/Erstellung/Bearbeitung/Löschung eines Projekts (Schreiben — nur Administrator; Liste nach Zugriff gefiltert) |
| GET/POST/DELETE | `/api/project-users`, `/api/projects/{projectId}/users/{userId}` | Verwaltung von Benutzer-Projekt-Zuweisungen — **nur Administrator** |
| GET | `/api/items?search=&tag=&projectId=` | gefilterte Elementliste (nach Projektzugriff gefiltert) |
| GET | `/api/items/{id}` | Elementdetails |
| GET | `/api/items/by-number/{itemNumber}` | Elementdetails nach Elementnummer statt Guid — verwendet vom SolidWorks-Makro, um eine Zeichnung (.SLDDRW) dem Teil/der Baugruppe zuzuordnen, die sie dokumentiert |
| POST | `/api/projects/{projectId}/nodes` | erstellt Ordner/Teil/Baugruppe/Datei ohne Upload (optional mit Ticket für ein CAD-Makro) |
| POST | `/api/projects/{projectId}/items` | **multipart/form-data**: Datei-Upload (optional `parentId`) |
| GET | `/api/items/{id}/file` | Download der hochgeladenen Datei |
| POST | `/api/items/{id}/duplicate` | dupliziert ein Teil/eine Baugruppe (neue Nummer, Status, Eigentümer) |
| PATCH | `/api/items/{id}/name` \| `/visibility` \| `/status` \| `/project` | Umbenennen / Sichtbarkeit im Baum ändern / Status ändern / in anderes Projekt verschieben. Eine Baugruppe, die auf `sprawdzany`/`wydany` wechselt, wird abgelehnt, solange ihre DIREKTEN Stücklistenkomponenten zurückliegen; `promoteChildren: true` setzt sie zusammen mit der Baugruppe in einer Transaktion |
| GET | `/api/items/{id}/status-precheck?target=` | was diese Statusänderung blockiert: separat zu behandelnde Unterbaugruppen, nicht anrührbare Komponenten (storniert / von jemand anderem gesperrt / Projekt ohne Zugriff) und Komponenten, die mitgezogen werden könnten. Nur lesend — `PATCH /status` prüft dieselbe Regel unabhängig |
| POST | `/api/items/{id}/lock` \| `/release` | Sperren (Eigentum übernehmen) / Freigeben eines Elements |
| DELETE | `/api/items/{id}` | vollständige Löschung (Rekursion nur über Ordner — Komponenten einer Baugruppe werden nie mitgelöscht) — **nur Administrator** |
| GET | `/api/projects/{projectId}/relations` | Eltern-Kind-Beziehungen (Struktur/Stückliste) eines Projekts |
| POST/DELETE | `/api/items/{parentId}/children[/{childId}]` | Hinzufügen/Lösen eines Kindelements |
| PATCH | `/api/items/{parentId}/children/{childId}/position` \| `/reorder` | Änderung der Stücklistenposition (einzelne Position oder gesamte neue Reihenfolge) |
| PATCH | `/api/projects/{projectId}/roots/reorder` | Änderung der Reihenfolge der Baum-Wurzeln eines Projekts |
| GET | `/api/items/{id}/bom` \| `/bom/csv` \| `/bom/aggregated-csv` | verschachtelte Stückliste (JSON) / CSV-Export (vollständig / zusammengefasst) |
| GET | `/api/items/{id}/used-in` | jede Baugruppe, in beliebiger Tiefe, die dieses Element enthält — die Umkehrung der Stückliste |
| GET | `/api/items/{id}/documentation/extensions`, `/documentation` | verfügbare Dateierweiterungen zum Download / ZIP mit Anhängen (Element + Teilbaum) |
| GET | `/api/projects/{projectId}/documentation/extensions`, `/documentation` | dasselbe, für ein gesamtes Projekt |
| GET | `/api/tags` | Tag-Liste |
| POST/DELETE | `/api/items/{id}/tags[/{tagName}]` | Tag-Verwaltung |
| PATCH/DELETE | `/api/items/{id}/properties[/{key}]` | Eigenschaftenverwaltung (gesperrt außerhalb des Status `w_pracy`/in Bearbeitung und bei Eigentümersperre — Ausnahme: Preisfelder) |
| GET | `/api/items/{id}/revisions` | Historie der Revisionskommentare (nur Revisionen mit Kommentar) |
| GET | `/api/items/{id}/history` | vollständige Historie: Erstellung, Statusänderungen, Revisionen, hinzugefügter/entfernter Anhang, Eigentümersperre/-freigabe (wann/wer/Beschreibung), chronologisch |
| GET/POST/PATCH/DELETE | `/api/materials[/{id}]` | Materialkatalog (Name + Gruppe/Untergruppe) |
| GET/POST/PATCH/DELETE | `/api/manufacturers[/{id}]`, `/api/manufacturers/{id}/contacts[/{contactId}]`, `/api/manufacturers/{id}/product-types[/{typeId}][/subtypes[/{subtypeId}]]` | Herstellerkatalog + Kontaktpersonen + Serien/Typen und deren Untertypen |
| GET/POST/PATCH/DELETE | `/api/clients[/{id}]`, `/api/clients/{id}/contacts[/{contactId}]`, `/api/clients/{id}/name2[/{name2Id}]` | Kundenkatalog + Kontaktpersonen + deren Namen-2-Liste |
| GET/POST/PATCH/DELETE | `/api/clients/{id}/nodes[/{nodeId}]`, `/nodes/folder`, `/nodes/file`, `/nodes/{nodeId}/file`, `/nodes/search` | eigener Dokumentenbaum eines Kunden (Ordner/Dateien — Upload/Download/Umbenennen/Löschen/Suche) |
| GET/POST/DELETE | `/api/items/{itemId}/attachments[/{id}]`, `/register`, `/api/attachments/{id}/file` | Anhänge (Upload/Registrierung einer vorhandenen Datei/Liste/Download/Löschen) |
| GET/POST/DELETE | `/api/saved-filters[/{id}]` | gespeicherte Filtersätze der Ansicht „Gesamte Datenbank" (privat pro Benutzer) |
| GET/POST/DELETE | `/api/notifications[/{id}]`, `/{id}/read`, `/read-all` | Benachrichtigungsliste / als gelesen markieren (einzeln oder alle) / löschen — für den angemeldeten Benutzer |
| GET/PATCH | `/api/notification-preferences` | Abschalten von Benachrichtigungen pro Typ für den angemeldeten Benutzer |
| GET/POST | `/api/create-tickets/{ticket}`, `/attach-existing` | Korrelation CAD-Makro ↔ Browser (siehe `EasyPDM.FreeCad/README.md`) |
| GET/POST | `/api/drawing-tickets/{ticket}`, `/resolve` | Korrelation SolidWorks-Makro ↔ Browser für "zu welchem Element gehört diese Zeichnung", wenn ihre Ansichten auf mehr als ein bereits verknüpftes Element verweisen |
| GET | `/api/config` | Speicherort für Dateien (z. B. zur Verwendung durch das FreeCAD-Makro) |
| GET/POST | `/api/settings/storage`, `/storage/move`, `/backup`, `/restore` | Speicherort/-statistiken, Verschieben, Sicherung (pg_dump + Dateien in einem ZIP), Wiederherstellung aus einer Sicherung — **nur Administrator** |
| GET/PATCH | `/api/settings/backup-schedule` | Zeitplan für automatische Sicherung (ein-/ausschalten, Häufigkeit, Tag, Uhrzeit, Anzahl aufbewahrter Kopien) — **nur Administrator** |
| GET/PATCH | `/api/settings/item-number-prefixes[/{rodzaj}]` | Buchstaben-Präfixe der Elementnummer pro Art (die 4 Teile-Arten plus `Zlozenie` = gefertigte Baugruppe; zugekaufte/Kunden-Baugruppen nutzen das Präfix der Teile-Art) — **nur Administrator** |
| GET/PATCH | `/api/settings/item-number-format` | Nummernformat für ab jetzt erstellte Elemente: `digits` (Mindestbreite zum Auffüllen mit Nullen, 0 = kein Auffüllen) und `withName` (ob der Elementname in Klammern angehängt wird). Beide bei PATCH optional und unabhängig gespeichert, da es in der Oberfläche zwei getrennte Abschnitte sind; beim Erstellen am Element eingefroren — **nur Administrator** |
| GET/POST | `/api/settings/item-number-sequence`, `/reset` | Vorschau/Zurückdrehen der Elementnummern-Sequenz — **nur Administrator** |
| GET | `/api/settings/logs`, `/logs/{date}`, `/logs/{date}/download` | Liste der Tage mit gespeichertem Protokoll, die letzten N Zeilen eines bestimmten Tages, Download der vollständigen Datei — **nur Administrator** |

## Inbetriebnahme

Das Backend liest die echten Zugangsdaten (Datenbankpasswort, Speicherpfad) aus
`EasyPDM.Api/appsettings.Local.json` — **diese Datei ist NICHT im Repository**
(gitignored, weil sie das Passwort enthält), daher muss sie bei einem frischen Klon aus
der Vorlage erstellt werden:

```bash
cp EasyPDM.Api/appsettings.Local.json.example EasyPDM.Api/appsettings.Local.json
# ...und dort das echte ConnectionString/StorageRoot für diese Maschine eintragen.
```

Das Programm **wendet neue Datenbankmigrationen bei jedem Start selbst an** (eingebettet
in die ausführbare Datei als embedded resources, nachverfolgt in der Tabelle
`schema_migrations` — siehe `MigrationRunner.cs`) — bei einer bereits bestehenden,
bekannten Datenbank reicht es also, sie einfach zu starten, ohne manuell
`db/migrations/` nachzuvollziehen. Der einzige Fall, in dem manuell etwas getan werden
muss, ist ein völlig **frisches, leeres** PostgreSQL — dann zunächst:

```bash
# Falls Rolle/Datenbank noch nicht existieren (frisches PostgreSQL):
sudo -u postgres psql -c "CREATE ROLE pdm_user LOGIN PASSWORD 'ihr-passwort';"
sudo -u postgres createdb -O pdm_user pdm

# ...und das Grundschema (ab diesem Punkt holt das Programm den Rest selbst nach):
psql -h localhost -U pdm_user -d pdm -f db/schema.sql

# Backend (liefert auch das gebaute Frontend aus wwwroot/ aus)
cd EasyPDM.Api
dotnet restore && dotnet build && dotnet run
```

Frontend — für die Arbeit an der UI mit Live-Vorschau (Proxy `/api` →
`http://localhost:5000`):

```bash
cd EasyPDM.Web
npm install
npm run dev      # http://localhost:5173
```

Für die Bereitstellung: `npm run build` in `EasyPDM.Web/` überschreibt
`EasyPDM.Api/wwwroot/` — `dotnet run` liefert das Ergebnis unter
`http://localhost:5000` ohne zusätzliche Konfiguration aus.

### Docker (empfohlen für die Server-Bereitstellung)

**Am einfachsten**: `./install-easypdm-docker.sh` — legt `.env` an (generiert ein
zufälliges Datenbankpasswort, falls Sie kein eigenes angeben), wählt selbst einen
FREIEN Host-Port (versucht ab 5000 aufwärts — nützlich auf einem Server, auf dem andere
Dienste möglicherweise schon Ports belegen, was in der Praxis ein häufiger Fall ist),
baut und startet die Container. Die Meldungen folgen der Systemsprache (Polnisch, Deutsch,
sonst Englisch; `LC_ALL`, dann `LC_MESSAGES`, dann `LANG`, wobei `LANGUAGE` Vorrang hat, sofern
die Locale nicht `C` ist), und `EASYPDM_LANG=en|pl|de` erzwingt eine Sprache. Führen Sie dasselbe Skript nach einem `git pull` erneut
aus, um zu aktualisieren — es erkennt eine vorhandene `.env` und überschreibt darin
nichts.

Oder manuell:

```bash
cp .env.example .env      # echtes PDM_DB_PASSWORD setzen
docker compose up -d --build
```

Startet zwei Container: `postgres` (Image `postgres:18`, Daten auf dem Volume `pgdata`,
Schema aus `db/schema.sql` wird bei leerem Volume automatisch angelegt) und `api`
(gebaut aus dem `Dockerfile` im Repo-Wurzelverzeichnis — baut das Frontend, veröffentlicht
das Backend, installiert zusätzlich `postgresql-client-18` für die
Sicherungs-/Wiederherstellungsfunktion in den Einstellungen). Dateispeicher, automatische
Sicherungen und Protokolle werden auf dem Volume `pdm-data` gehalten (`/data` im
Container) — sie überstehen einen Image-Rebuild bei einem Update. Nach dem Start:
`http://localhost:5000`. Falls Port 5000 auf dieser Maschine bereits belegt ist, setzen
Sie `PDM_HOST_PORT=anderer_port` in `.env` (NICHT über
`docker-compose.override.yml` — Compose HÄNGT Listenwerte wie `ports` zwischen
Dateien AN, statt sie zu ersetzen, sodass ein Override mit einem anderen Port trotzdem
versuchen würde, beide gleichzeitig zu binden, und am bereits belegten Port scheitern
würde).

**Update**: `git pull && docker compose up -d --build` — das neue `api`-Image erhält
den neuen Code, der Container wird neu erstellt, und Migrationen werden beim Start
automatisch angewendet, wie oben — es muss nichts weiter manuell getan werden. Der
Schritt `docker-entrypoint-initdb.d` mit
`schema.sql` läuft NUR beim ERSTEN, völlig leeren Start des `pgdata`-Volumes (frische
Installation); bei einem Update wird er überhaupt nicht berührt, da das Volume bereits
existiert.

#### Bereitstellung OHNE Klonen des Repos (nur fertiges Image)

Zwei Workflows veröffentlichen fertige Images in die GitHub Container Registry —
`ghcr.io/pawelcel/easypdm-api` und `ghcr.io/pawelcel/easypdm-postgres` (Letzteres ist ein
gewöhnliches `postgres:18` mit eingebettetem `db/schema.sql` — ohne dies bliebe eine
frische Datenbank leer, da `MigrationRunner.cs` bewusst nicht selbst das Grundschema
erstellt):

- `publish-docker-image.yml` — bei jedem Push auf `main`, der Server-Code betrifft,
  markiert beide Images mit `:edge` (+ Commit-SHA). Zum Prüfen des neuesten Stands von
  `main` vor einem Release (`docker pull ghcr.io/pawelcel/easypdm-api:edge`) — rührt
  `:latest` nie an.
- `publish-docker-release.yml` — nur beim Push eines Versions-Tags (`v0.1.2`, passend
  zu `EasyPDM.Web/src/version.ts` und `MyAppVersion` in
  `packaging/windows/EasyPDM.iss`), markiert beide Images mit `:latest` UND `:v0.1.2`.
  Das ist der EINZIGE Workflow, der `:latest` bewegt — `docker-compose.yml` (das
  `:latest` zieht) bekommt also immer eine bewusst veröffentlichte Version, nie einen
  beliebigen Commit von `main`. So wird ein neues Release veröffentlicht:
  ```bash
  git tag v0.1.2
  git push origin v0.1.2
  ```

Für die Bereitstellung allein muss also NICHT das gesamte Repo geklont werden (mit allen
CAD-Makros/Installern/Tests, die der Server überhaupt nicht braucht). Zwei Dateien
genügen:

```bash
mkdir easypdm-deploy && cd easypdm-deploy
curl -O https://raw.githubusercontent.com/pawelcel/EasyPDM/main/docker-compose.yml
curl -O https://raw.githubusercontent.com/pawelcel/EasyPDM/main/.env.example
cp .env.example .env      # echtes PDM_DB_PASSWORD setzen
docker compose pull
docker compose up -d
```

> Solange das Repo (und das Paket in GHCR) privat ist, erfordern das obige `curl` und
> `docker compose pull` eine Authentifizierung — `curl` mit einem
> `Authorization: Bearer <token>`-Header, und vor `docker compose pull` zusätzlich
> `docker login ghcr.io -u <login> -p <token>` (ein Token mit der Berechtigung
> `read:packages`). Nach der Veröffentlichung des Repos/Images als öffentlich ist keine
> Anmeldung mehr erforderlich.
>
> **Einmalig, nach der ersten Veröffentlichung**: JEDES Paket in GHCR ist standardmäßig
> PRIVAT, unabhängig von der Sichtbarkeit des Repos selbst — es muss einmal manuell auf
> öffentlich umgestellt werden, für BEIDE Pakete (GitHub → Reiter **Packages** beim Repo
> → `easypdm-api` / `easypdm-postgres` → **Package settings** → **Change visibility**),
> sonst erhält `docker compose pull` ohne vorheriges `docker login` selbst bei einem
> öffentlichen Repo einen 403/404-Fehler.

**Update** auf diesem Weg: `docker compose pull && docker compose up -d` — ohne
`git pull` (es gibt nichts zu pullen, Sie haben hier kein Repo), es wird einfach das
abgerufen, worauf `:latest` gerade zeigt — also das neueste VERÖFFENTLICHTE Release,
nicht zwangsläufig der neueste Commit auf `main`.

### Linux — native Installation als systemd-Dienst (ohne Docker)

```bash
sudo ./install-easypdm-linux.sh
```

Ein einziges Skript: installiert PostgreSQL, falls noch nicht vorhanden (erkennt
`pacman`/`apt`/`dnf` — unter Arch/CachyOS initialisiert es zusätzlich selbst den
Cluster, da das dortige Paket dies im Gegensatz zu Debian/Fedora nicht automatisch tut),
legt eine eigene Rolle und Datenbank `easypdm` an (zufälliges Passwort, sofern nicht
`sudo PDM_DB_PASSWORD=...` eines vorgibt; ein Update bleibt bei Datenbank, Rolle und Passwort,
die die Installation bereits nutzt — gelesen aus `/etc/easypdm/easypdm.env`;
`PDM_DB_NAME`/`PDM_DB_USER` verweisen anderswohin. Bis 0.6 hießen sie `pdm`/`pdm_user`, wie in
einer Entwicklungsumgebung, sodass das Installationsprogramm auf einem Entwicklerrechner jene
Datenbank übernahm und das Passwort ihrer Rolle zurücksetzte),
baut das Frontend und veröffentlicht das Backend als **eigenständige einzelne
ausführbare Datei** (`dotnet publish -r linux-x64 --self-contained
-p:PublishSingleFile=true` — der fertige Dienst benötigt kein installiertes .NET mehr,
nur zur Bauzeit), legt ein dediziertes, unprivilegiertes Systemkonto `easypdm` an und
installiert einen systemd-Dienst (`easypdm.service`, Autostart, `ProtectSystem=strict` +
`ReadWritePaths` beschränkt auf `/var/lib/easypdm` — der Dienst kann nirgendwo sonst im
System schreiben). Nach der Installation: die vom Installationsprogramm genannte Adresse (meist `http://localhost:5000`), Status über
`systemctl status easypdm`, Live-Protokolle über `journalctl -u easypdm -f`
(unabhängig vom eigenen Anwendungsprotokoll unter Einstellungen -> Protokolle).
Deinstallation: `sudo ./uninstall-easypdm-linux.sh` fragt vorab, ob auch Datenbank, Rolle
und der gesamte Inhalt von `/var/lib/easypdm` entfernt werden sollen — standardmäßig nein, mit
einer zusätzlichen Warnung, wenn die Datenbank nicht `easypdm` heißt, da sie dann etwas anderem
dienen kann. Ohne Terminal bleiben die Daten erhalten; `sudo PDM_REMOVE_DATA=yes|no` entscheidet
ohne Rückfrage. PostgreSQL selbst wird nie entfernt.

Schema und Migrationen vergeben Rechte `TO CURRENT_USER` statt an eine benannte Rolle — erst dadurch kann die native Installation eine eigene Rolle haben: Jede Bereitstellung führt sie mit der Rolle der Anwendung aus. Eine Sicherung behält ihre Rechte unter dem Rollennamen der Installation, aus der sie stammt; sie in eine Installation mit anders benannter Rolle zurückzuspielen (`pdm_user` aus Docker in eine Installation mit `easypdm` oder umgekehrt), wird noch nicht unterstützt.

**Port, Sprache, PostgreSQL-Authentifizierung.** Eine frische Installation lauscht auf dem ersten freien Port ab 5000 (normalerweise 5000 selbst); ein Update behält den Port, den der Dienst bereits nutzt, damit Lesezeichen, CAD-Makros und andere Rechner weiter funktionieren; `sudo PDM_PORT=8080 ./install-easypdm-linux.sh` setzt ihn ausdrücklich, auch bei einem Update. Erfolg wird erst gemeldet, wenn auf diesem Port EasyPDM selbst antwortet — geprüft wird der Seitentitel, ein anderes Programm auf dem Port zählt also nicht. Bis 0.7 war der Port fest 5000, und das Skript meldete den Dienst als laufend, auch wenn er auf einem von jemand anderem belegten Port abgestürzt war. Die Meldungen folgen der Systemsprache (Polnisch, Deutsch, sonst Englisch, nach denselben Regeln wie das Docker-Installationsprogramm), und `sudo EASYPDM_LANG=en ./install-easypdm-linux.sh` erzwingt eine — die Variable steht nach `sudo`, das `LANG`, `LANGUAGE` und `LC_*` durchlässt, andere aber verwirft. Unter Arch/CachyOS verwendet der vom Installationsprogramm angelegte Cluster `peer` für lokale Verbindungen und `scram-sha-256` über TCP; ein nacktes `initdb` setzt `trust`, wobei sich jeder Benutzer der Maschine als beliebige Rolle verbinden konnte, `postgres` eingeschlossen. Vor 0.7 angelegte Cluster bleiben unverändert — prüfen Sie `/var/lib/postgres/data/pg_hba.conf`.

**Update**: `git pull`, dann `sudo ./install-easypdm-linux.sh` erneut ausführen — es
erkennt die vorhandene Datenbank/das vorhandene Konto (überspringt deren Anlage), baut
nur die Anwendung neu und ersetzt sie, und **startet den Dienst explizit neu**
(`systemctl restart`, nicht nur `enable --now`, was bei einem bereits laufenden Dienst
nichts bewirken würde). Neue Datenbankmigrationen wendet das Programm beim Start
automatisch selbst an — es muss nichts Zusätzliches manuell getan werden.

> Das Skript baut aus den Quellen dieses Repositorys (wie `run.sh`, nur als dauerhafter
> Dienst statt als Vordergrundprozess) — es gibt (noch) kein separates, fertiges
> Binär-Release zum Herunterladen. Die eigenständige veröffentlichte ausführbare Datei
> selbst wurde tatsächlich ausgeführt und geprüft (liefert das Frontend aus,
> protokolliert), und der Inhalt der systemd-Unit wurde mit `systemd-analyze verify`
> überprüft; der vollständige Skriptablauf (Anlage von Rolle/Datenbank/Systemkonto über
> `sudo`) wurde noch nicht end-to-end ausgeführt — beobachten Sie beim ersten Start die
> Ausgabe und melden Sie, falls etwas nicht funktioniert.

### Windows — Installer (`.exe`, Inno Setup)

**Am einfachsten: `.github/workflows/build-windows-installer.yml`** baut automatisch ein
fertiges `EasyPDM_Windows_v<Version>.exe` (Versionsnummer aus `MyAppVersion`/
`OutputBaseFilename` in `packaging/windows/EasyPDM.iss`) auf einem Windows-Runner von
GitHub (der den Inno Setup Compiler werksseitig hat) bei jedem Push, der Backend/Frontend/
Installer betrifft — kein Windows oder Inno Setup lokal nötig. Manuell ausführen über
`gh workflow run build-windows-installer.yml`, warten (`gh run watch`), das Artefakt
herunterladen (`gh run download <id> -n EasyPDM_Windows_v<Version>`).

Alternativ, zum lokalen Bauen auf einem Windows-Rechner (.NET 10 SDK + Node.js +
[Inno Setup Compiler](https://jrsoftware.org/isinfo.php)):

```powershell
powershell -ExecutionPolicy Bypass -File packaging\windows\build.ps1
iscc packaging\windows\EasyPDM.iss
```

Es entsteht `packaging\windows\Output\EasyPDM_Windows_v<Version>.exe`. Der Installer: prüft, ob
PostgreSQL bereits installiert ist (falls nicht — verweist auf die Download-Seite und
bricht ab, versucht bewusst NICHT, im Hintergrund still einen mehrere hundert Megabyte
großen PostgreSQL-Installer nachzuinstallieren), fragt nach dem Passwort des
`postgres`-Superusers (einmalig, um die eigene Rolle und Datenbank `easypdm`
anzulegen — das Passwort selbst wird nirgends gespeichert; bis 0.6 hießen sie
`pdm`/`pdm_user` wie in einer Entwicklungsumgebung, sodass der Installer auf einem
Entwicklerrechner deren Datenbank übernahm und das Passwort ihrer Rolle änderte), legt das Schema an,
schreibt `appsettings.Production.json` mit den übrigen Einstellungen (Speicher/Sicherungen/
Protokolle in `%ProgramData%\EasyPDM`), registriert `EasyPDM.Api.exe` als
**Windows-Dienst** (Autostart, läuft im Hintergrund ohne Konsolenfenster) und erstellt
eine Verknüpfung, die `http://localhost:5000` öffnet. Die Deinstallation stoppt und
entfernt den Dienst (der Standard-Deinstaller von Inno Setup) , behält aber bewusst die
Datenbank und `%ProgramData%\EasyPDM`: anders als unter Linux bräuchte das Entfernen der
Datenbank das Passwort des Superusers `postgres`. Am Ende sagt er, was geblieben ist und wie
man es von Hand entfernt (`DROP DATABASE` und `DROP ROLE` mit den Namen der Installation).

**Update**: ein neues `EasyPDM_Windows_v<Version>.exe` bauen (wie oben) und erneut
ausführen. Die vorhandene Installation wird über die feste `AppId` erkannt (ihr
Uninstall-Schlüssel in der Registry), sodass Inno sie AN ORT UND STELLE ersetzt, statt
daneben zu installieren. Ein Update **fragt nicht nach dem Passwort des Superusers
`postgres`** — der Installer liest Datenbank, Rolle und deren Passwort aus der
`appsettings.Production.json` der vorherigen Installation und fasst Rolle und Datenbank gar
nicht an, das Rollenpasswort BLEIBT also unverändert (nichts anderes, was sich mit dieser
Datenbank verbindet — Sicherungsskripte, pgAdmin — hört auf zu funktionieren).
`PrepareToInstall` stoppt den Dienst VOR dem Austausch der Dateien (sonst würde Windows das
Überschreiben einer laufenden `.exe` blockieren) und startet ihn wieder, statt ihn neu zu
registrieren. Neue Datenbankmigrationen wendet das Programm beim Start selbst an, und in der
Anwendung geänderte Einstellungen (z. B. der Speicherort der Dateien) überstehen das Update —
sie liegen in `appsettings.Local.json`, während der Installer nur `Production.json` schreibt.

Die Installation einer **älteren** Version über eine neuere wird mit einer Meldung
abgelehnt: Datenbankmigrationen laufen ausschließlich vorwärts, eine ältere Version könnte
das bereits migrierte Schema nicht lesen.

> Das `.iss`-Skript lässt sich tatsächlich kompilieren (mit einem echten Inno Setup
> Compiler in der CI verifiziert, nicht nur durch Code-Review) — dabei wurden 5 echte,
> für den Pascal-Script-Dialekt von Inno Setup spezifische Fehler gefunden und behoben
> (u. a. keine lokalen `const`-Abschnitte in Funktionen, `LoadStringFromFile` erfordert
> `AnsiString`, kein `Randomize`/`RandSeed`/`GetTickCount` — es gibt keine
> dokumentierte Möglichkeit, den eingebauten `Random` manuell zu initialisieren, daher
> wird er unverändert verwendet). Die eigentliche End-to-End-Installation auf einer
> lebenden Maschine mit PostgreSQL wurde noch nicht manuell getestet — beobachten Sie
> beim ersten Start den Ablauf und melden Sie, was nicht funktioniert.

Erste Anmeldung: **`admin` / `admin`** (das Konto wird automatisch angelegt, falls die
Tabelle `users` leer ist — siehe "Anmeldung, Rollen und Projektzugriff" oben). Ändern
Sie dieses Passwort sofort nach der Anmeldung.

## Bekannte Einschränkungen

1. **Keine Validierung von Größe/Typ hochgeladener Dateien und Anhänge** — jede Datei
   wird akzeptiert, unabhängig von Erweiterung oder Größe.
2. **Der Dateispeicher (`storage/`) ist ein gewöhnlicher Ordner auf der Festplatte des
   Servers.** Sicherung/Wiederherstellung über die Einstellungen packt einen `pg_dump`
   der Datenbank zusammen mit dem Dateispeicher in ein einziges ZIP; es kann manuell
   heruntergeladen oder eine automatische Sicherung aktiviert werden (Einstellungen ->
   Dateispeicher -> Automatische Sicherung) mit Wahl der Häufigkeit
   (täglich/wöchentlich/monatlich) sowie Tag und Uhrzeit — im Hintergrund jede Minute
   vom `ScheduledBackupService` geprüft, gespeichert in einem separaten Verzeichnis
   `backups/` (unabhängig von `storage/`, damit eine Sicherung sich nicht selbst
   einpackt), mit einer konfigurierbaren Anzahl aufbewahrter letzter Kopien
   (standardmäßig 14 — ältere werden automatisch gelöscht). Die Dateiversionierung bei
   einem Revisionswechsel funktioniert heute nur im FreeCAD-Makro-Ablauf
   (`storage/components/`, eine Datei pro Revision, siehe `EasyPDM.FreeCad/README.md`) —
   gewöhnliche, aus der Web-Anwendung hinzugefügte Anhänge haben keine automatische
   Verknüpfung zur Revisionsnummer.
3. **Nicht jede Operation zeichnet auf, „wer es getan hat"** — die Erstellung eines
   Elements (`created_by`), eine Statusänderung, ein Revisionskommentar, das
   Hinzufügen/Entfernen eines Anhangs und die Eigentümersperre/-freigabe tun dies
   bereits (sichtbar in der „Historie"), aber z. B. das Ändern von
   Eigenschaften/Name/Tags zeichnet den Autor nicht auf.
4. **In Docker übersteht „Speicherort ändern" für den Dateispeicher (Einstellungen ->
   Dateispeicher) keinen Image-Rebuild** — diese Operation schreibt den neuen Pfad in
   `appsettings.json` innerhalb des `api`-Containers (außerhalb des Volumes
   `pdm-data`), sodass er nach `docker compose up --build` auf den Wert der im
   `Dockerfile` gesetzten Umgebungsvariable `StorageRoot` zurückfällt. Die Änderung des
   Speicherorts selbst funktioniert während der Lebensdauer des Containers korrekt —
   das Problem betrifft nur die Persistenz dieser Einstellung über Rebuilds hinweg.

## Nächste Schritte (vorgeschlagene Reihenfolge)

1. Upload-Validierung (Typ/Größe) für Elemente und Anhänge.
2. Aufzeichnung des Autors von Eigenschafts-/Name-/Tag-Änderungen (Punkt 3 oben).
