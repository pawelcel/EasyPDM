# EasyPDM — PDM-System für CAD-Dateien

[English](README.md) | [Polski](README.pl.md) | **Deutsch**

[<img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" width="136">](https://buymeacoffee.com/easypdm)

EasyPDM ist der Ort, an dem Ihre Teile und Baugruppen eine einzige, für das ganze Team
gemeinsame Ordnung erhalten: Jedes Element hat seine eigene Nummer, Revision, Status und
Änderungshistorie, und Baugruppen erhalten eine fertige Stückliste (BOM). Schluss mit
`halterung_v3_ENDGUELTIG_FINAL.SLDPRT` auf dem gemeinsamen Laufwerk und der Frage
"welche Version ist eigentlich aktuell?". Für FreeCAD und SolidWorks gibt es fertige
Makros, die Dateien direkt aus dem CAD-Programm heraus senden und abrufen — der Rest
(Browser-Anwendung, Material-/Herstellerkataloge, Stückliste) funktioniert unabhängig
davon, womit Sie konstruieren, genauso.

Ich bin Konstrukteur im Maschinenbau und wusste genau, wie ein solches Werkzeug
aussehen und im Arbeitsalltag mit CAD-Dateien funktionieren sollte — was mir gefehlt
hat. Programmiert habe ich es nicht selbst: Die gesamte Anwendung wurde für mich von
Claude (einem KI-Modell von Anthropic) anhand meiner Anforderungen und Beschreibungen
geschrieben. Ich habe EasyPDM für den eigenen Gebrauch gebaut, und da es nun einmal
existiert und funktioniert — warum es nicht auch anderen zur Verfügung stellen.

## Was es bietet

- **Eine Nummer, eine Historie** — jedes Teil und jede Baugruppe erhält automatisch eine
  Nummer, die niemand sonst je wieder bekommt. Man sieht, wer wann was geändert hat, wer
  ein Element gerade "auf der Werkbank" hat und welche Revision aktuell ist.
- **Stückliste auf Knopfdruck** — eine Baugruppe zeigt selbst die Liste ihrer Komponenten
  mit Mengen, Material, Hersteller, Bestellnummern — fertig zum CSV-Export.
- **Gemeinsame Material- und Herstellerkataloge** — man wählt aus einer Liste, statt es
  jedes Mal von Hand einzutippen, sodass sich Namen nicht zwischen Projekten
  auseinanderentwickeln.
- **Suche über die gesamte Firmendatenbank**, nicht nur im aktuellen Projekt — praktisch,
  wenn Sie prüfen wollen, ob ein ähnliches Teil schon irgendwo existiert.
- **Elementsperre** — solange Sie an etwas arbeiten, überschreibt niemand sonst Ihre
  Änderungen ohne Ihre Zustimmung (ein Administrator kann bei Bedarf eine fremde Sperre
  übernehmen oder aufheben — z. B. wenn der Besitzer abwesend ist).

## Erste Inbetriebnahme

EasyPDM wird EINMAL installiert — auf einem Computer in der Firma (nicht unbedingt ein
besonderer "Server", ein gewöhnlicher Computer, der einfach eingeschaltet bleibt, reicht
völlig aus). Von da an verbindet sich jeder mit einem gewöhnlichen Webbrowser damit,
genau wie mit jeder Webseite — nur unter einer Adresse, die ausschließlich im
Firmennetzwerk sichtbar ist, nicht im gesamten Internet.

**Wenn EasyPDM in Ihrer Firma bereits läuft** — fragen Sie die Person, die es
installiert hat, nach der Adresse (sie wird z. B. so aussehen: `http://192.168.1.20:5000`,
oder `http://localhost:5000`, wenn EasyPDM auf Ihrem eigenen Computer läuft). Geben Sie
sie in die Adressleiste des Browsers ein, genau wie jede andere Webseiten-Adresse, und
melden Sie sich an.

**Wenn es noch niemand installiert hat und Sie es tun müssen:**

**Windows** (ohne IT-Kenntnisse) — Gehen Sie auf die
[Releases-Seite dieses Repositorys](https://github.com/pawelcel/EasyPDM/releases),
laden Sie die neueste Datei `EasyPDM_Windows_v<Version>.exe` herunter und starten Sie sie — der
Installationsassistent führt Sie Schritt für Schritt durch den Rest und hinterlässt eine
Verknüpfung zu EasyPDM auf dem Desktop (das Einzige, wonach er fragen könnte: ob
PostgreSQL, das Programm zur Datenspeicherung, bereits installiert ist — falls nicht,
verweist er auf die Download-Seite, bevor er fortfahren kann).

**Linux** (grundlegende Terminal-Kenntnisse reichen — wählen Sie eine Variante):

- *Docker* (empfohlen, wenn Docker auf der Maschine bereits installiert ist):
  ```bash
  git clone https://github.com/pawelcel/EasyPDM.git
  cd EasyPDM
  ./install-easypdm-docker.sh
  ```
- *Native Installation, ohne Docker* — laden Sie das fertige Paket `EasyPDM-Linux-x64_v<Version>`
  herunter (wird automatisch von der CI dieses Repos gebaut — im
  [Actions-Tab](https://github.com/pawelcel/EasyPDM/actions/workflows/build-linux-package.yml),
  letzter erfolgreicher Lauf, Abschnitt "Artifacts") oder klonen Sie das Repo selbst,
  dann:
  ```bash
  tar xzf EasyPDM-Linux-x64_v<Version>.tar.gz && cd EasyPDM-Linux-x64_v<Version>   # falls Sie das Paket heruntergeladen haben
  sudo ./install-easypdm-linux.sh
  ```
  Installiert PostgreSQL (falls nicht vorhanden) und EasyPDM selbst als
  `systemd`-Dienst, der automatisch mit der Maschine startet.

In beiden Fällen landet EasyPDM unter `http://localhost:5000` (oder der Adresse der
Maschine im Netzwerk, von einem anderen Computer aus). Vollständige Details,
Aktualisierung und Deinstallation: siehe [`TECHNICAL.de.md`](TECHNICAL.de.md).

Erste Anmeldung bei einem frisch installierten EasyPDM: Benutzername `admin`, Passwort
`admin` — ändern Sie dieses Passwort sofort nach der Anmeldung (Einstellungen →
Benutzer → das Konto `admin` in der Liste suchen → Passwort ändern).

Nach der Anmeldung: wählen Sie ein Projekt (oder erstellen Sie ein neues, falls Sie die
Berechtigung haben) — das ist der Container für Ihre Dateien und die
Baugruppenstruktur — und installieren Sie das Makro für Ihr CAD-Programm, siehe unten.

## Arbeiten von FreeCAD / SolidWorks aus

Die Makros fügen im CAD-Programm zwei einfache Operationen hinzu: **Upload** (aktives
Dokument an das PDM senden) und **Download** (Teil/Baugruppe aus dem PDM abrufen,
zusammen mit der gesamten Baugruppe, und im Programm öffnen).

Installation und Details:
- FreeCAD: [`EasyPDM.FreeCad/README.md`](EasyPDM.FreeCad/README.md)
- SolidWorks: [`EasyPDM.SolidWorks/README.md`](EasyPDM.SolidWorks/README.md)

**Upload** — Sie haben eine gespeicherte Datei geöffnet und klicken auf Upload. Der
Browser öffnet sich (automatisch angemeldet) mit der Frage: neues Element, Duplikat
eines vorhandenen (kopiert dessen Eigenschaften, ohne Dateien) oder eine neue Version an
ein bereits vorhandenes Element anhängen. Sie wählen, bestätigen im Browser — das Makro
erkennt den Abschluss selbstständig und beendet den Upload (benennt die lokale Datei in
die PDM-Nummer um, hängt die Datei an, exportiert eine STEP-Vorschau). Für eine ganze
Baugruppe mit neuen, noch nicht hochgeladenen Komponenten: Das Makro erkennt sie
selbstständig und fragt für jede einzeln nach den Daten, bevor die Hauptdatei gesendet
wird.

**Download** — Sie klicken auf Download und geben im Browser das abzurufende
Teil/Baugruppe an. Bei einer Baugruppe wird sofort der GESAMTE Komponentenbaum
abgerufen, und die Hauptdatei öffnet sich automatisch im CAD-Programm.

## Arbeiten im Browser

### Projekte und Struktur

Jedes Projekt hat einen Baum: Ordner (reine Container zur Organisation), Teile und
Baugruppen (haben eine Nummer/Status/Revision) sowie Sonstige Dateien (ein beliebiges
Dokument ohne eigene Struktur darunter). Eine Baugruppe kann Teile und andere Baugruppen
enthalten (Stückliste) — dieselbe Komponente kann gleichzeitig in mehreren Baugruppen
und Projekten verwendet werden, sodass eine Änderung an einer Stelle überall dort
sichtbar ist, wo diese Komponente verwendet wird.

Ein Element kann **aus der Struktur gelöst** werden (bleibt in der Datenbank, verschwindet
nur an dieser Stelle im Baum) oder **vollständig gelöscht** werden (nur Administrator) —
das vollständige Löschen ist sicher für gemeinsam genutzte Komponenten: Ein Element mit
einem Elternteil außerhalb des gelöschten Teilbaums verschwindet nicht mit. Ein
Teil/eine Baugruppe kann auch **dupliziert** werden — die Kopie erhält eine eigene
Nummer und landet sofort neben dem Original, mit dessen kopierten Eigenschaften.

### Teile und Baugruppen — Arten und Eigenschaften

Ein Teil hat eine von vier **Arten**, jede mit einem anderen Satz von Feldern:

| Art | Zusätzliche Felder |
|---|---|
| Gefertigt | Material, Preis |
| Zugekauft | Hersteller, Serie/Typ, Untertyp, Bestellnummer 1/2, Masse, Preis |
| Norm | Material, Norm |
| Kundenteil | (keine zusätzlichen Felder) |

Eine Baugruppe hat eine von drei **Arten**: Gefertigt, Zugekauft (Hersteller, Serie/Typ
und Untertyp) oder Vom Kunden. Unabhängig von der Art kann sie zusätzlich eine
optionale Masse und beliebige eigene Eigenschaften tragen.

**Serie/Typ** ist ein Eintrag aus der Liste des gewählten Herstellers (Reiter Hersteller),
und **Untertyp** verfeinert sie innerhalb dieser Serie (z. B. Serie „Zylinderrollenlager“
→ Untertypen NU/NJ/NUP), beide nebeneinander angezeigt. Serie/Typ ist gesperrt, bis ein
Hersteller gewählt ist, Untertyp, bis eine Serie gewählt ist; ein Wechsel des Herstellers
oder der Serie löscht alles darunter.

### Status und Revisionen

Teile/Baugruppen durchlaufen drei Status: **in Bearbeitung → in Prüfung →
freigegeben**. Im Status "in Bearbeitung" kann alles bearbeitet werden; außerhalb davon
sind Name und Eigenschaften gesperrt (Preis bleibt immer bearbeitbar). Die Rückkehr vom
Status "freigegeben" zu "in Bearbeitung" erhöht die Revision um einen Buchstaben
(A → B → C...) und erlaubt einen Kommentar dazu, was sich geändert hat. Am unteren Rand
des Elementbereichs sieht man die vollständige **Historie**: wer es erstellt hat, jede
Statusänderung, jede Revision mit Kommentar, jeden hinzugefügten/entfernten Anhang, jede
Sperrung/Freigabe.

### Wer bearbeitet — Elementsperre

Der Ersteller eines Teils/einer Baugruppe wird sofort dessen/deren Eigentümer, und das
Element wird gesperrt — solange die Sperre besteht, kann nur der Eigentümer dessen
Eigenschaften bearbeiten (nicht einmal ein Administrator umgeht dies). Ein Administrator
kann jedoch eine fremde Sperre übernehmen oder aufheben und den Status eines gesperrten
Elements ändern — z. B. wenn ein Mitarbeiter abwesend ist und dessen unfertiges Element
freigegeben werden muss. Im Baum wird dies durch die Farbe des Schloss-Symbols angezeigt:
grün — von Ihnen gesperrt, gelb — von jemand anderem, offen — freigegeben (jeder kann es
sperren). Ein freigegebenes Element ist immer entsperrt.

### Stückliste (BOM)

Eine Baugruppe zeigt die Liste ihrer Komponenten: Position, Name, Menge, Material,
Hersteller, Bestellnummern — zusammen mit den Komponenten verschachtelter Baugruppen.
Die Reihenfolge der Positionen kann per Ziehen oder durch direkte Eingabe einer Nummer
geändert werden. CSV-Export in zwei Varianten: vollständig (jedes Vorkommen einzeln
aufgeführt) oder zusammengefasst (dieselbe Komponente mehrfach verwendet — eine Zeile mit
der Gesamtmenge).

### Materialien und Hersteller

Separate, unternehmensweite Kataloge (Reiter **Materialliste** und **Hersteller** im
Hauptmenü) — ein Material hat einen Namen und eine Gruppe/Untergruppe, ein Hersteller hat
einen Namen und Kontaktpersonen. Sie werden beim Ausfüllen der Eigenschaften eines Teils
aus einer Liste ausgewählt, statt von Hand eingetippt zu werden.

### Suche und gesamte Datenbank

Der Reiter **Gesamte Datenbank** durchsucht alle Elemente unabhängig vom Projekt — nach
Name, Nummer, Tags, Art. Gefundene Filter können zur Wiederverwendung gespeichert
werden.

### Herunterladbare Dokumentation

Von einem Projekt, einer Baugruppe oder einem Teil aus lässt sich der komplette Satz
angehängter Dateien als ZIP herunterladen (mit Auswahl, welche Dateierweiterungen
einbezogen werden sollen) — praktisch z. B., um einen kompletten Zeichnungssatz an einen
Kunden zu senden.

## Konten und Zugriff

Zwei Rollen: **Administrator** (voller Zugriff, sieht alle Projekte, verwaltet Konten
und Servereinstellungen) und **Benutzer** (sieht und arbeitet nur in den Projekten, denen
er zugewiesen wurde). Jeder verwaltet seine eigene Oberflächensprache
(Polnisch/Englisch/Deutsch) und den hellen/dunklen Modus selbst in den Einstellungen.

## Für Administratoren und Entwickler

Serverinstallation (Docker / Linux / Windows), Architektur, die vollständige Liste der
API-Endpunkte und bekannte Einschränkungen — siehe [`TECHNICAL.de.md`](TECHNICAL.de.md).
