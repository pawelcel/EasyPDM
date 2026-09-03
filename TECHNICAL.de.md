# EasyPDM — technische Dokumentation

[English](TECHNICAL.md) | [Polski](TECHNICAL.pl.md) | **Deutsch**

Dieses Dokument richtet sich an den Administrator, der EasyPDM installiert/betreut, sowie
an Entwickler. Eine Beschreibung des Werkzeugs selbst (wozu es dient und wie man es beim
Konstruieren benutzt) finden Sie in [README.de.md](README.de.md).

## Status

Projekte und Elemente werden manuell über die Web-Anwendung erstellt (Datei-Upload direkt
in den Speicher der API) oder über das FreeCAD- (`EasyPDM.FreeCad/`) oder
SolidWorks-Makro (`EasyPDM.SolidWorks/`), die dieselbe API aufrufen.
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
- **`db/migrations/`** — Migrationen `002`–`041` für eine bereits bestehende Datenbank:
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
  Beispielprojekts, eine kleine interne Zustandstabelle `system_state` sowie
  Hersteller-Serien/Typen samt ihren Untertypen. Seit
  Migration 027 sind die Dateien aus diesem Ordner in das Programm eingebettet (embedded
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
  `nummer (name)` um) und `EasyPDMDownload.FCMacro` (die umgekehrte Richtung: Teil/
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
löschen"** (rekursiv, aber sicher für gemeinsam genutzte Komponenten — ein Element mit
einem Elternteil außerhalb des gelöschten Teilbaums verschwindet nicht; nur
Administrator). Ein Teil/eine Baugruppe kann auch **dupliziert** werden (die Kopie
erhält eine neue Nummer, einen frischen Status und Eigentümer) — im Baum landet die Kopie
direkt unter dem Original.

Ein Teil hat vier **Arten** (`properties.rodzaj`), jede mit einem anderen Satz von
Feldern und einem anderen Symbol im Baum: **Gefertigt** (Material, Preis, Zusätzliche
Informationen), **Zugekauft** (Hersteller, Serie/Typ, Untertyp, Bestellnummer 1/2, Masse, Preis,
Zusätzliche Informationen), **Norm** (Material, Norm, Zusätzliche Informationen),
**Kundenteil** (keine zusätzlichen Felder außer Zusätzlichen Informationen).

Eine Baugruppe hat drei eigene Arten im selben `properties.rodzaj`: **Wykonywane**
(gefertigt), **Zakupowe** (zugekauft — Hersteller, Serie/Typ, Untertyp) und **Klienta** (vom
Kunden). Die Zeichenketten unterscheiden sich BEWUSST von denen des Teils ("Zakupowe"
statt "Zakupowa"), denn dieser Wert dient zugleich als Schlüssel für das Nummernpräfix —
die einzige gemeinsame Zeichenkette ist "Klienta", die sich auch das Präfix teilt. Über
die Felder ihrer Art hinaus hat eine Baugruppe weiterhin den generischen
Eigenschaften-Editor (Masse und beliebige eigene Schlüssel). Baugruppen aus früheren
Versionen haben keine Art und zeigen einen Hinweis, eine auszuwählen.

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
Bearbeitung; die Rückkehr von `wydany`/freigegeben erhöht die Revisionsnummer, mit einem
optionalen Kommentar zur Revision). Außerhalb des Status `w_pracy`/in Bearbeitung ist das
Bearbeiten von Name/Eigenschaften gesperrt — Ausnahme: Preis/Währung/Preisart sind immer
bearbeitbar. Am unteren Rand des Eigenschaftenbereichs eines Teils/einer Baugruppe wird
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
Zeile), Name, Menge, Material, Hersteller, Bestellnummer 1/2 (fehlende Felder als „-"),
zusammen mit verschachtelten Elementen (Teile verschachtelter Baugruppen, Position in
der Form `2.1`). CSV-Export in zwei Varianten: vollständig (jedes Vorkommen einzeln
aufgeführt) und zusammengefasst (dieselbe Komponente mehrfach an verschiedenen Stellen
verwendet — eine Zeile mit der über die gesamte Kette aufgelösten Gesamtmenge).

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

### API-Endpunkte

| Methode | Pfad | Was es tut |
|---|---|---|
| POST | `/api/auth/login` \| `/logout` | Anmeldung / Abmeldung — Login ist der einzige Endpunkt ohne erforderliche Sitzung |
| GET/PATCH | `/api/auth/me` \| `/password` | Daten des angemeldeten Benutzers / Änderung des EIGENEN Passworts |
| GET | `/api/auth/browser-login` | Token→Cookie-Brücke für CAD-Makros (öffnet den Browser bereits angemeldet) |
| GET/POST/PATCH/DELETE | `/api/users[/{id}]` | Kontenverwaltung — **nur Administrator** |
| GET/POST/PATCH/DELETE | `/api/projects[/{id}]` | Liste/Erstellung/Bearbeitung/Löschung eines Projekts (Schreiben — nur Administrator; Liste nach Zugriff gefiltert) |
| GET/POST/DELETE | `/api/project-users`, `/api/projects/{projectId}/users/{userId}` | Verwaltung von Benutzer-Projekt-Zuweisungen — **nur Administrator** |
| GET | `/api/items?search=&tag=&projectId=` | gefilterte Elementliste (nach Projektzugriff gefiltert) |
| GET | `/api/items/{id}` | Elementdetails |
| POST | `/api/projects/{projectId}/nodes` | erstellt Ordner/Teil/Baugruppe/Datei ohne Upload (optional mit Ticket für ein CAD-Makro) |
| POST | `/api/projects/{projectId}/items` | **multipart/form-data**: Datei-Upload (optional `parentId`) |
| GET | `/api/items/{id}/file` | Download der hochgeladenen Datei |
| POST | `/api/items/{id}/duplicate` | dupliziert ein Teil/eine Baugruppe (neue Nummer, Status, Eigentümer) |
| PATCH | `/api/items/{id}/name` \| `/visibility` \| `/status` \| `/project` | Umbenennen / Sichtbarkeit im Baum ändern / Status ändern / in anderes Projekt verschieben |
| POST | `/api/items/{id}/lock` \| `/release` | Sperren (Eigentum übernehmen) / Freigeben eines Elements |
| DELETE | `/api/items/{id}` | vollständige Löschung (rekursiv, sicher für gemeinsam genutzte Elemente) — **nur Administrator** |
| GET | `/api/projects/{projectId}/relations` | Eltern-Kind-Beziehungen (Struktur/Stückliste) eines Projekts |
| POST/DELETE | `/api/items/{parentId}/children[/{childId}]` | Hinzufügen/Lösen eines Kindelements |
| PATCH | `/api/items/{parentId}/children/{childId}/position` \| `/reorder` | Änderung der Stücklistenposition (einzelne Position oder gesamte neue Reihenfolge) |
| PATCH | `/api/projects/{projectId}/roots/reorder` | Änderung der Reihenfolge der Baum-Wurzeln eines Projekts |
| GET | `/api/items/{id}/bom` \| `/bom/csv` \| `/bom/aggregated-csv` | verschachtelte Stückliste (JSON) / CSV-Export (vollständig / zusammengefasst) |
| GET | `/api/items/{id}/documentation/extensions`, `/documentation` | verfügbare Dateierweiterungen zum Download / ZIP mit Anhängen (Element + Teilbaum) |
| GET | `/api/projects/{projectId}/documentation/extensions`, `/documentation` | dasselbe, für ein gesamtes Projekt |
| GET | `/api/tags` | Tag-Liste |
| POST/DELETE | `/api/items/{id}/tags[/{tagName}]` | Tag-Verwaltung |
| PATCH/DELETE | `/api/items/{id}/properties[/{key}]` | Eigenschaftenverwaltung (gesperrt außerhalb des Status `w_pracy`/in Bearbeitung und bei Eigentümersperre — Ausnahme: Preisfelder) |
| GET | `/api/items/{id}/revisions` | Historie der Revisionskommentare (nur Revisionen mit Kommentar) |
| GET | `/api/items/{id}/history` | vollständige Historie: Erstellung, Statusänderungen, Revisionen, hinzugefügter/entfernter Anhang, Eigentümersperre/-freigabe (wann/wer/Beschreibung), chronologisch |
| GET/POST/PATCH/DELETE | `/api/materials[/{id}]` | Materialkatalog (Name + Gruppe/Untergruppe) |
| GET/POST/PATCH/DELETE | `/api/manufacturers[/{id}]`, `/api/manufacturers/{id}/contacts[/{contactId}]`, `/api/manufacturers/{id}/product-types[/{typeId}][/subtypes[/{subtypeId}]]` | Herstellerkatalog + Kontaktpersonen + Serien/Typen und deren Untertypen |
| GET/POST/DELETE | `/api/items/{itemId}/attachments[/{id}]`, `/register`, `/api/attachments/{id}/file` | Anhänge (Upload/Registrierung einer vorhandenen Datei/Liste/Download/Löschen) |
| GET/POST/DELETE | `/api/saved-filters[/{id}]` | gespeicherte Filtersätze der Ansicht „Gesamte Datenbank" (privat pro Benutzer) |
| GET/POST | `/api/create-tickets/{ticket}`, `/attach-existing` | Korrelation CAD-Makro ↔ Browser (siehe `EasyPDM.FreeCad/README.md`) |
| GET | `/api/config` | Speicherort für Dateien (z. B. zur Verwendung durch das FreeCAD-Makro) |
| GET/POST | `/api/settings/storage`, `/storage/move`, `/backup`, `/restore` | Speicherort/-statistiken, Verschieben, Sicherung (pg_dump + Dateien in einem ZIP), Wiederherstellung aus einer Sicherung — **nur Administrator** |
| GET/PATCH | `/api/settings/backup-schedule` | Zeitplan für automatische Sicherung (ein-/ausschalten, Häufigkeit, Tag, Uhrzeit, Anzahl aufbewahrter Kopien) — **nur Administrator** |
| GET/PATCH | `/api/settings/item-number-prefixes[/{rodzaj}]` | Buchstaben-Präfixe der Elementnummer pro Art (die 4 Teile-Arten plus `Zlozenie` = gefertigte Baugruppe; zugekaufte/Kunden-Baugruppen nutzen das Präfix der Teile-Art) — **nur Administrator** |
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
baut und startet die Container. Führen Sie dasselbe Skript nach einem `git pull` erneut
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
den neuen Code, der Container wird neu erstellt, und das Programm **wendet neue
Datenbankmigrationen beim Start selbst an** (eingebettet in die ausführbare Datei,
nachverfolgt in der Tabelle `schema_migrations` — siehe `MigrationRunner.cs`) — es muss
nichts weiter manuell getan werden. Der Schritt `docker-entrypoint-initdb.d` mit
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
legt die Rolle und die Datenbank `pdm` an (generiert ein zufälliges Passwort, falls Sie
kein eigenes über `PDM_DB_PASSWORD=... sudo -E ./install-easypdm-linux.sh` angeben),
baut das Frontend und veröffentlicht das Backend als **eigenständige einzelne
ausführbare Datei** (`dotnet publish -r linux-x64 --self-contained
-p:PublishSingleFile=true` — der fertige Dienst benötigt kein installiertes .NET mehr,
nur zur Bauzeit), legt ein dediziertes, unprivilegiertes Systemkonto `easypdm` an und
installiert einen systemd-Dienst (`easypdm.service`, Autostart, `ProtectSystem=strict` +
`ReadWritePaths` beschränkt auf `/var/lib/easypdm` — der Dienst kann nirgendwo sonst im
System schreiben). Nach der Installation: `http://localhost:5000`, Status über
`systemctl status easypdm`, Live-Protokolle über `journalctl -u easypdm -f`
(unabhängig vom eigenen Anwendungsprotokoll unter Einstellungen -> Protokolle).
Deinstallation: `sudo ./uninstall-easypdm-linux.sh` (rührt bewusst NICHT die Datenbank
selbst oder PostgreSQL an — das wird manuell entschieden, damit Daten nicht versehentlich
gelöscht werden).

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
`postgres`-Superusers (einmalig, um die eigene Rolle `pdm_user` und die Datenbank `pdm`
anzulegen — das Passwort selbst wird nirgends gespeichert), legt das Schema an,
schreibt `appsettings.Production.json` mit den übrigen Einstellungen (Speicher/Sicherungen/
Protokolle in `%ProgramData%\EasyPDM`), registriert `EasyPDM.Api.exe` als
**Windows-Dienst** (Autostart, läuft im Hintergrund ohne Konsolenfenster) und erstellt
eine Verknüpfung, die `http://localhost:5000` öffnet. Die Deinstallation stoppt und
entfernt den Dienst (der Standard-Deinstaller von Inno Setup) — genau wie unter Linux wird
bewusst die Datenbank selbst nicht angetastet.

**Update**: ein neues `EasyPDM_Windows_v<Version>.exe` bauen (wie oben) und erneut ausführen —
`PrepareToInstall` im `.iss`-Skript stoppt den Dienst VOR dem Austausch der Dateien
(sonst würde Windows das Überschreiben einer laufenden `.exe` blockieren), der
Installer erkennt die vorhandene Rolle/Datenbank (überspringt die Schemaerstellung) und
den vorhandenen Dienst (startet ihn wieder, statt ihn neu zu registrieren). Neue
Datenbankmigrationen wendet das Programm beim Start automatisch selbst an.

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
