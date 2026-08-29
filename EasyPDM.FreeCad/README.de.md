# EasyPDM — FreeCAD-Makros

[English](README.md) | [Polski](README.pl.md) | **Deutsch**

Zwei unabhängige Makros, eines zum Senden, eines zum Herunterladen/Öffnen:

- **`EasyPDMUpload.FCMacro`** sendet das aktive FreeCAD-Dokument an EasyPDM. Das Makro
  fragt lokal NICHTS ab außer dem Speicherordner — sogar die Wahl "neues Element oder
  Anhängen an ein vorhandenes" fällt im **System-Browser** (weiter unten in dieser Datei
  beschrieben), auf demselben Formular/derselben Leiste wie in der Web-Anwendung — **mit
  einer Ausnahme**: Wenn das Label des Dokuments bereits so aussieht, als gehöre es zu
  einem vorhandenen PDM-Element, fragt das Makro zuerst lokal, ob es dort als neue
  Revision angehängt werden soll, und umgeht den Browser dabei vollständig (siehe
  „Lokale Abkürzung für ein bereits erkanntes Dokument" unten).
- **`EasyPDMDownload.FCMacro`** lädt ein Teil/eine Baugruppe aus EasyPDM herunter
  (zusammen mit ALLEN seinen/ihren Komponenten, falls es sich um eine Baugruppe handelt)
  und öffnet es/sie sofort in FreeCAD (in einem eigenen Abschnitt am Ende dieser Datei
  beschrieben).

Beide teilen sich dieselbe Anmeldung und API-Adresse (dieselben FreeCAD-Einstellungen) —
die Anmeldung bei einem reicht für das andere.

## Installation

Es muss nichts als Workbench installiert werden. Für jedes der beiden Makros einzeln, in
FreeCAD:

- **Macro → Macros… → Add path to macro path list** und diesen Ordner
  (`EasyPDM.FreeCad/`) angeben, dann das Makro aus der Liste ausführen — **oder**
- **Macro → Macros… → Execute** und direkt die Datei angeben (`EasyPDMUpload.FCMacro`
  oder `EasyPDMDownload.FCMacro`), jedes Mal von einem beliebigen Ort auf der Festplatte
  aus.

Die API-Adresse (standardmäßig `http://localhost:5000/api`) wird nach dem ersten Start in
den FreeCAD-Einstellungen gespeichert (`User parameter:BaseApp/EasyPDM`) — sie kann im
Fenster "An PDM senden" geändert werden (das erste Fenster bei jedem Durchlauf —
Zielordner + Konto), im Feld "API-Adresse".

## Anmeldung

EasyPDM.Api erfordert für jeden Aufruf außer der Anmeldung selbst eine Anmeldung — also
meldet sich auch das Makro an. Beim ersten Start (oder wenn die gespeicherte Sitzung
abgelaufen oder ungültig gemacht wurde) erscheint ein Anmeldefenster (Benutzername +
Passwort, dieselben Konten wie in der Web-Anwendung). Das Sitzungstoken landet in
denselben FreeCAD-Einstellungen wie die API-Adresse, sodass **spätere Makro-Läufe — auch
nach einem FreeCAD-Neustart — NICHT erneut nach der Anmeldung fragen**, solange die
Sitzung gültig ist (30 Tage, genau wie in der Web-Anwendung). Im Fenster "An PDM senden"
sieht man, wer angemeldet ist, und kann sich von dort aus **abmelden** (Schaltfläche
"Abmelden") — dies macht die Sitzung serverseitig ungültig und löscht das lokal
gespeicherte Token, sodass der nächste Makro-Lauf sofort wieder zur Anmeldung auffordert.

**Warum braucht das Makro überhaupt eine eigene Sitzung, wenn die Entscheidung
neu/vorhanden und das gesamte Formular im Browser liegen?** Weil sich das Makro selbst die
ganze Zeit über authentifizieren können muss, um (a) abzufragen, ob der Browser bereits
fertig ist (`GET /api/create-tickets/{ticket}`), und (b) die CAD-Datei selbst an das
angegebene Element anzuhängen — das erledigt weiterhin das Makro, nicht der Browser, da
sonst die gesamte Automatik der Dateiumbenennung/des STEP-Exports/der
Baugruppen-Stückliste verloren ginge. Dieselbe Makro-Sitzung ist übrigens auch das, was
den Browser AUTOMATISCH anmeldet — `GET /api/auth/browser-login` tauscht das Token des
Makros gegen ein Browser-Cookie — die Anmeldung im Makro und die "kostenlose" Anmeldung
des Browsers sind also **ein und derselbe Vorgang**, nicht zwei getrennte.

## Was es tut

1. **Speichert das aktive Dokument** — falls das Dokument noch keinen Pfad auf der
   Festplatte hatte, fragt das Makro nach "Speichern unter" (der Standard-FreeCAD-Dialog),
   bevor irgendetwas gesendet wird.
1a. **Fragt nach dem Zielordner** für lokale Kopien (Speichern unter) aller in dieser
    Sitzung gesendeten Dokumente (siehe Schritt 3 unten) — standardmäßig wird der zuletzt
    verwendete vorgeschlagen (**eine mit `EasyPDMDownload.FCMacro` geteilte
    Einstellung** — stellen Sie in beiden Makros denselben Ordner ein, damit gesendete und
    heruntergeladene Dateien zusammen an einem Ort landen). Wird EINMAL, ganz am Anfang,
    gefragt — dies deckt auch den automatisch erkannten Baugruppenbaum ab (Teile/
    Unterbaugruppen, die vor dem Öffnen des Hauptfensters gesendet werden).
1b. **Lokale Abkürzung für ein bereits erkanntes Dokument.** FreeCAD hat kein
    dauerhaftes Äquivalent zur Custom Property `EasyPDM_ItemId` des SolidWorks-Makros
    (siehe die README des SolidWorks-Makros für diesen Mechanismus) — stattdessen fragt
    das Makro **lokal, nativ**, wenn das **Label** des Dokuments wie
    `Nummer (Name).REVISION` aussieht und diese Nummer tatsächlich zu einem vorhandenen
    PDM-Element (Nummer/Dateiname) passt (`match_existing_item`): die aktuelle Version
    diesem Element als neue Revision anhängen? Eine Bestätigung zeigt zwei weitere
    native Ja/Nein-Abfragen (STEP exportieren — Standard Ja; PDF exportieren — Standard
    Nein) und hängt direkt an dieses Element an, **wobei der Browser für dieses Dokument
    vollständig umgangen wird**. Eine Ablehnung fällt zurück auf den normalen
    Browser-Ablauf in Schritt 2 unten, wobei die passende Elementnummer als Vorschlag im
    Suchfeld „An Vorhandenes anhängen" vorausgefüllt ist. Da dies auf einem freien Label
    statt auf einer dauerhaften Eigenschaft beruht, kann es in seltenen Fällen falsch
    liegen — z. B. kopiert FreeCADs eigenes „Speichern unter" das Label des Dokuments auf
    eine wirklich neue, unabhängige Datei, wodurch sie sich fälschlich selbst als
    vorhandenes Element „erkennen" könnte; das Ablehnen der Bestätigung ist immer sicher
    (fällt zurück auf den Browser).
2. **Es erscheint kein natives Fenster mehr.** Das Makro öffnet sofort den
   **System-Browser**, bereits angemeldet (Token→Cookie-Brücke, siehe "Anmeldung" oben),
   auf einer Leiste, die "eine Anfrage von einem CAD-Makro wartet" anzeigt (sichtbar auf
   JEDEM Bildschirm der Web-Anwendung, solange das Ticket wartet). Die Leiste zeigt eine
   **explizite Wahl mit drei Schaltflächen** — die Wahl "neues Element, Duplikat oder
   Anhängen an ein vorhandenes" fällt dort, nicht lokal und nicht durch einen zufälligen
   Klick auf irgendein "Hinzufügen" irgendwo in der Anwendung (bewusst NICHT auf diese
   Weise):
   - **"Neues Element"** — öffnet ein **eigenständiges Popup**, ohne dass vorher im
     Projektbereich links navigiert werden muss: erst IN DIESEM POPUP wählt man das
     Projekt, optional ein übergeordnetes Element, den Typ (Teil/Baugruppe), den Namen
     (standardmäßig aus dem Label des Dokuments vorgeschlagen), die Art und die davon
     abhängigen Felder: bei einem Teil ist die Art erforderlich — **Gefertigt** →
     Material, **Zugekauft** → Hersteller/Bestellnummer 1 und 2/Masse, **Norm** →
     Material/Norm, **Kundenteil** → keine zusätzlichen Felder. Bei einer Baugruppe ist
     die Art optional und auf Gefertigt/Zugekauft/Norm beschränkt (ohne "Kundenteil"), und
     die Masse ist immer sichtbar, unabhängig von der gewählten Art — eine Baugruppe hat
     nie ein Materialfeld (nur ein Teil). Das Popup hat außerdem die **Checkboxen
     „STEP exportieren" und „PDF exportieren"** (STEP standardmäßig angehakt, PDF nicht —
     siehe Schritt 5 unten, was jede davon beim Export genau bewirkt). Das Ticket ist
     EXPLIZIT an genau dieses eine Popup gebunden — kein ANDERES "Hinzufügen" in der Anwendung (im Projektbaum, im
     Detailbereich) "schluckt" es jemals versehentlich. **Abbrechen** im Popup kehrt zur
     Wahl "Neues Element"/"Duplizieren"/"An Vorhandenes anhängen" zurück, ohne etwas zu
     erstellen.
   - **"Duplizieren"** — zunächst zeigt eine Suche das **Quell**-Element (Teil/Baugruppe)
     aus der gesamten Datenbank an, dann öffnet sich DASSELBE Popup wie bei "Neues
     Element", nur vorab mit dessen Eigenschaften ausgefüllt (Art/Material/Hersteller/
     Bestellnummern/Norm/Masse) — **ohne irgendeine Datei zu kopieren**. Alle Felder
     lassen sich vor dem Speichern weiterhin bearbeiten — es ist eine gewöhnliche
     Erstellung eines neuen Elements, nur mit Daten aus der Quelle vorbefüllt.
   - **"An Vorhandenes anhängen"** — öffnet eine Suche für ein Teil/eine Baugruppe aus der
     **gesamten Datenbank** (nicht nur dem aktuellen Projekt, da eine Komponente gemeinsam
     genutzt werden kann), mit Vorschlägen während der Eingabe (nach Nummer oder Name) und
     denselben STEP-/PDF-Checkboxen. Wenn das Label des lokalen Dokuments wie
     `Nummer (Name).REVISION` aussieht (weil dasselbe Makro es nach einem früheren Senden
     bereits so benannt hat), schlägt die Suche sofort das passende Element vor — das ist
     nur ein **Vorschlag**, die Wahl kann immer geändert werden. Das ausgewählte Element
     wird nicht neu erstellt — das aktuelle Dokument wird ihm als **aktuelle Version
     seiner aktuellen Revision** angehängt (was als Nächstes mit dem Status
     "Freigegeben"/einer neuen Revision geschieht, beschreibt Schritt 2a unten — das ist
     die EINZIGE Entscheidung, die bewusst lokal bleibt, in FreeCAD, unmittelbar vor dem
     eigentlichen Anhängen der Datei).

   Ein gemeinsames Formular/eine gemeinsame Leiste im Browser für ALLES, sodass diese
   Regeln zwischen dem Makro und der Web-Anwendung nicht mehr auseinanderlaufen können.
   Nachdem im Browser entschieden wurde (auf einem der drei oben genannten Wege), erkennt
   FreeCAD (das Fenster "Warte auf Browser", das den Server alle ~2 s abfragt, Limit
   10 Minuten) den Abschluss von selbst und fährt ab Schritt 3 unten fort — man muss nicht
   manuell zu FreeCAD zurückkehren. Ein Abbruch im Browser oder im Wartefenster beendet
   das Makro mit einer Meldung, ohne eine Datei zu erstellen/anzuhängen (das Element
   könnte im PDM bereits entstanden sein, falls es im Browser noch rechtzeitig gespeichert
   wurde — dann muss das Makro erneut ausgeführt und über die Leiste "wartende Anfrage vom
   Makro" daran angehängt werden).
2a. **Die einzige Entscheidung, die bewusst LOKAL BLIEB** — für ein Element, an das
    angehängt werden soll und das den Status **"Freigegeben"** hat (in diesem Status
    erlaubt das PDM kein Anhängen von Dateien), öffnet das Makro das Fenster "Neue
    Revision": es fragt, ob eine neue Revision erstellt werden soll, und erlaubt die
    Eingabe eines optionalen **Revisionskommentars** (was sich geändert hat) — genau
    derselbe Kommentar, der in der Web-Anwendung bei derselben Statusänderung hinzugefügt
    werden kann. Das ist eine direkte Bestätigung dessen, was das Makro gleich mit der
    Datei auf der Festplatte tun wird, keine Elementdaten im PDM — daher blieb es lokal.
    Ein Abbruch speichert nichts; eine Bestätigung ändert den Status auf "In Bearbeitung"
    (derselbe Mechanismus wie in der Web-Anwendung — erhöht die Revisionsnummer und
    speichert den Kommentar, falls angegeben) und sendet erst danach die Datei.

    Für den Status **"In Prüfung"** wird das Anhängen an ein vorhandenes Element
    stattdessen **hart blockiert**, mit einer nativen Fehlermeldung — das Makro setzt den
    Status NICHT still zurück auf "In Bearbeitung", um trotzdem hochzuladen (das war ein
    echter, behobener Fehler: das Element von jemand anderem prüfen und es sich durch
    einen erneuten Upload still unter den Füßen zurücksetzen lassen). Wer prüft, muss den
    Status selbst in der Web-Anwendung aus "In Prüfung" herausbewegen, bevor eine neue
    Datei angehängt werden kann.
3. Das Element existiert an diesem Punkt bereits im PDM (neu — über das Formular im
   Browser erstellt; bereits vorhanden — dort über dieselbe Leiste angegeben, siehe
   Schritt 2). In beiden Fällen **KOPIERT** das Makro die aktuelle Dokumentdatei in das
   PDM unter dem Namen `Nummer (Name).REVISION.Erweiterung` — dasselbe Nummer/Name-Format,
   in dem das PDM Teile/Baugruppen überall sonst anzeigt, plus die Revision als
   **Großbuchstabe** (A, B, C... — dieselbe `revisionLabel()`-Konvention wie in der
   Web-Anwendung; die Zahl in der Datenbank ändert sich nicht, es ist reine
   Formatierungssache). **Das lokale Dokument wird ebenfalls (Speichern unter) unter
   DIESEM SELBEN Namen gespeichert**, im in Schritt 1a gewählten Ordner (oder im Ordner
   des Originals, falls gerade nichts dorthin verschoben werden musste — die Datei lag
   dort schon unter dem richtigen Namen) — die alte Datei bleibt unangetastet auf der
   Festplatte, aber `doc.FileName` zeigt ab jetzt auf die neue. Dadurch speichert eine
   Baugruppe, die auf dieses Dokument verlinkt (`App::Link`) und NACH ihm in derselben
   Sitzung gespeichert wird (der automatisch erkannte Baugruppenbaum speichert die
   Baugruppe immer zuletzt — siehe unten), ihren Verweis bereits unter dem neuen Namen —
   genau demjenigen, unter dem `EasyPDMDownload.FCMacro` heruntergeladene Dateien später
   speichert, sodass die Verweise nach dem Herunterladen sofort stimmen. **Ohne dies würde
   die Baugruppe nach dem Herunterladen nach dem ursprünglichen, vor dem Senden gültigen
   Dateinamen suchen** (in der Praxis bestätigt durch die FreeCAD-Meldung: `Link broken!
   ... File: <Originalname>.FCStd`).
4. Wenn der PDM-Speicher von diesem Rechner aus sichtbar ist (siehe `GET /api/config`),
   landet die Kopie im **GEMEINSAMEN Ordner** `storage/components/` (einer für alle
   Projekte — ein Teil/eine Baugruppe wird mitunter projektübergreifend als
   BOM-Komponente gemeinsam genutzt) und wird **REGISTRIERT, OHNE erneut per HTTP
   übertragen zu werden** (`POST /api/items/{id}/attachments/register`) — die Kopie liegt
   bereits physisch im Speicher des Servers. **Das PDM BEHÄLT Kopien früherer
   Revisionen** — z. B. bleibt `67 (Name).A.FCStd` erhalten, wenn Revision B entsteht;
   überschrieben wird nur die Kopie DERSELBEN Revision (also ein erneutes Senden ohne
   Statusänderung). Ist der Speicher von diesem Rechner aus nicht erreichbar (z. B.
   FreeCAD auf einem anderen Computer als der Server), gelangt die Kopie stattdessen über
   einen gewöhnlichen HTTP-Upload dorthin (`POST /api/items/{id}/attachments`, derselbe
   Mechanismus wie das Anhängen von CAD-Dateien über den Eigenschaftenbereich in der
   Web-Anwendung) — in diesem Fall wird die Revisionshistorie nicht bewahrt.
5. **Exportiert optional STEP und/oder PDF und lädt sie automatisch als Anhänge mit den
   Rollen „step"/„pdf" hoch.** Woher das Ja/Nein kommt, hängt vom gewählten Pfad oben ab:
   für ein **neues Element** oder **An Vorhandenes anhängen**, im Browser entschieden
   (Schritt 2), stammt es aus den Checkboxen desselben Tickets; für jede
   **Baugruppenkomponente** mit eigenem Ticket (siehe „Automatische Baugruppenerkennung"
   unten) aus DEREN eigenen Checkboxen; für die **lokale Abkürzung** (Schritt 1b) aus den
   zwei dort stattdessen gezeigten nativen Ja/Nein-Abfragen. Es gibt keinen verbleibenden
   Pfad mehr, auf dem STEP bedingungslos ohne jede Wahl erzwungen wird.
   - **STEP**: funktioniert über denselben zugrundeliegenden Mechanismus wie die manuelle
     Schaltfläche „STEP" im Anhänge-Bereich der Web-Anwendung, sodass es sofort die
     dauerhafte 3D-Vorschau im Elementbereich speist. Das Dokument wird zuerst
     `recompute()`-t (nötig, damit Baugruppen-Container/-Links ihre tatsächliche
     Geometrie melden statt eines veralteten, leeren Compounds), dann wird die Geometrie
     gesammelt, wobei Konstruktions-/Bezugsobjekte (Ursprung, Achsen, Ebenen) explizit
     ausgeschlossen werden; ist ein Container-Objekt vorhanden
     (`Assembly::AssemblyObject`, `App::Part`, `PartDesign::Body` — d. h. es handelt sich
     um eine Baugruppe, kein bloßes Teil), wird NUR die bereits vollständig platzierte
     eigene Form dieses Containers exportiert (der Export auch seiner Kind-Links würde
     die Geometrie jedes Teils duplizieren), andernfalls wird jedes verbleibende sichtbare
     Objekt mit Volumenkörper zu einem Compound zusammengefasst. Diese Form wird direkt
     nach STEP exportiert (nicht über FreeCADs generisches `Part.export`, das
     Baugruppen-/Link-Objekte mit der Warnung „is not a shape" still verwirft, obwohl sie
     vollkommen gültige Geometrie tragen — das war ein echter, behobener Fehler:
     Baugruppen erzeugten früher eine STEP-Datei ganz ohne Geometrie darin).
   - **PDF**: nutzt FreeCADs `Gui.export(...)` auf denselben sichtbaren Objekten — dies
     ist nicht FreeCADs üblicher, auf TechDraw-Zeichnungen basierender PDF-Pfad, daher
     als Best-Effort für ein gewöhnliches Teil/einen Body/eine Baugruppe betrachten,
     nicht als garantierter Mechanismus; in der Praxis als funktionierend bestätigt, aber
     falls es für ein bestimmtes Dokument einmal still fehlschlägt, ist das die erste
     Stelle, die zu prüfen ist.
   - Jeder Anhang **ersetzt** den vorherigen mit derselben Rolle (vor dem Hochladen des
     neuen gelöscht), damit die Vorschau stets die aktuelle Revision zeigt. Hat das
     Dokument keinen sichtbaren Volumenkörper (eine bloße Skizze, ein leeres Dokument)
     oder schlägt ein Export/Upload fehl — wird dieser eine Anhang **stillschweigend
     übersprungen**, er bricht den Rest des Sendevorgangs weder ab noch macht er ihn
     rückgängig (die `.FCStd`-Datei ist zu diesem Zeitpunkt bereits sicher im PDM
     gespeichert).

## Automatische Baugruppenerkennung

Wenn das aktive Dokument über `App::Link` (die Standardmethode zum Aufbau von Baugruppen
in der Workbench Assembly/Assembly4) auf **andere, gespeicherte `.FCStd`-Dateien**
verlinkt, fragt das Makro **vor** dem Öffnen des Hauptfensters, ob der gesamte Baum
automatisch gesendet werden soll:

- **Erkennung**: durchläuft die Objekte des Dokuments, findet `App::Link`s, die auf
  andere Dokumente verweisen, und steigt rekursiv in die Tiefe (eine Unterbaugruppe kann
  ebenfalls weiter verlinken). Die Menge wird aus der Anzahl der Verweise auf dieselbe
  Datei berechnet — mehrere separate Links und Link-Muster/-Arrays (`ElementCount`) werden
  zusammengezählt (z. B. 2 separate Schrauben + ein Muster von 2 Stück derselben Schraube
  = 4 in der Stückliste).
- **Sendereihenfolge**: zuerst Blätter (Teile ohne weitere Links), dann Unterbaugruppen,
  zuletzt das Hauptdokument — damit jede Komponente im PDM existiert, bevor sie als
  Unterelement angehängt wird.
- **Bereits gesendete Komponenten**: erkannt am Label im Format `Nummer (Name).REVISION`,
  das zu Nummer/Dateiname eines vorhandenen PDM-Elements passt (dieselbe
  `match_existing_item`-Prüfung wie die lokale Abkürzung auf oberster Ebene, Schritt 1b
  oben) — eine solche Komponente wird rein **referenziert**: kein Upload, kein
  Browser-Ticket, überhaupt nichts wird für sie gesendet, sie wird nur mit der
  berechneten Menge und ihrer vorhandenen Element-ID an die Stückliste angehängt —
  unabhängig von ihrem Status. Ist dieser Status „In Prüfung" oder „Freigegeben", wird
  das auch in der abschließenden Erfolgsmeldung markiert — als Erinnerung, dass lokale
  Änderungen daran NICHT gesendet wurden (eine bereits verknüpfte Komponente wird nie
  erneut gesendet, unabhängig vom Status).
- **Neue Komponenten laufen ebenfalls über den Browser, eine nach der anderen** — jede
  noch nicht erkannte Datei erhält ihr EIGENES Browser-Ticket (dieselbe Wahl Neu/
  Duplizieren/An Vorhandenes anhängen, einschließlich eigener STEP-/PDF-Checkboxen, wie
  das Hauptdokument), nacheinander geöffnet, nie mehrere Tabs gleichzeitig, zuerst die
  Blätter. Da pro Makrolauf nur ein Browser-Tab zuverlässig den Windows-Vordergrundfokus
  erhalten kann, erscheint unmittelbar vor jedem weiteren Tab (nach dem ersten) ein
  natives „Zum Fortfahren OK klicken"-`MsgBox` — der Klick zählt als frische
  Benutzereingabe, die es dem nächsten Browserfenster erlaubt, den Fokus zu erhalten,
  statt still im Hintergrund zu öffnen (Taskleiste prüfen, falls ein Schritt hängen zu
  bleiben scheint). Der vorgeschlagene Typ (Teil/Baugruppe) füllt das Browser-Formular
  danach vor, ob die betreffende Datei selbst weitere Links hat.
- **Neu erstellte Komponenten werden aus dem Stamm des Projektbaums ausgeblendet**,
  sobald sie an ihrem eigentlichen Elternelement angehängt sind
  (`PATCH /items/{id}/visibility {showInTree: false}`) — dies gilt nur für Komponenten,
  die IN DIESEM Lauf tatsächlich über ihr eigenes Ticket erstellt wurden; eine bereits
  vorhandene, rein referenzierte Komponente (vorheriger Punkt) behält jede Sichtbarkeit,
  die sie bereits hatte, da es sich um einen bewusst unabhängigen Katalogeintrag handeln
  kann, der auch anderswo verwendet wird.
- Die Wahl von **"Nein"** bei der Frage nach dem automatischen Senden sendet NUR das
  aktuelle Dokument, genau wie bisher (ohne Unterelemente) — die Stücklistenstruktur
  bleibt dann zur manuellen Ergänzung in der Web-Anwendung, wie zuvor.
- **Entfernte Komponenten werden ebenfalls markiert** — für jedes Elternelement (das
  Hauptdokument und jede Unterbaugruppe) prüft das Makro vor dem Anhängen seiner
  aktuellen lokalen Kinder, ob das PDM noch eine Stücklistenbeziehung zu einem Kind hat,
  das nicht mehr in der lokalen Struktur ist (seit dem letzten Hochladen aus dem
  FreeCAD-Baum entfernt). Falls ja, wird nativ um Bestätigung vor dem Entfernen dieser
  Beziehung gebeten (die Elemente selbst werden nie gelöscht, nur ihre Zuordnung zu
  diesem konkreten Elternelement) — eine Ablehnung lässt alles unverändert. Eine
  Bestätigung entfernt das Kind zusätzlich aus JEDEM Projekt, statt es in den Stamm des
  aktuellen Projekts zu werfen — es bleibt vollständig sichtbar und über die globale
  Suche „Cała baza" auffindbar, hört aber auf, die Struktur eines Projekts zu belasten,
  mit dem es nichts mehr zu tun hat (derselbe Mechanismus wie beim manuellen „Aus der
  Struktur entfernen" in der Web-Anwendung).

## Einschränkungen der ersten Version

- **Schritt 2** (neues Element/Anhängen an ein vorhandenes, beide über den Browser)
  erfordert, dass der Standard-System-Browser die Adresse des PDM-Servers öffnen kann
  (dieselbe wie die API-Adresse in den Makro-Einstellungen) — bei einer typischen
  Installation (Client und Server im selben Netzwerk) funktioniert dies ohne zusätzliche
  Konfiguration. Das Wartefenster in FreeCAD hat ein Limit von **10 Minuten** — wird es
  überschritten (oder Abbrechen gedrückt), endet das Makro mit einer Meldung, ohne eine
  Datei zu erstellen/anzuhängen.
- Die automatische Baugruppenerkennung funktioniert nur für Verweise auf **externe,
  gespeicherte Dateien** (`App::Link` und davon abgeleitete Typen, z. B. das
  `Assembly::AssemblyLink` der nativen Assembly-Workbench für Unterbaugruppen-
  Komponenten — geprüft wird `isDerivedFrom("App::Link")`, kein exakter Typvergleich,
  genau weil sich ein exakter Vergleich früher still eine neu hinzugefügte
  Unterbaugruppen-Komponente komplett entgehen ließ, ganz ohne Fehlermeldung — ein
  echter, in der Praxis behobener Fehler) — nicht für Baugruppen, die als `App::Part`-
  Container in einer einzigen Datei gehalten werden (diese haben keine separaten
  Dateien, die einzeln gesendet werden könnten; sie müssen dann manuell gesendet werden,
  Teil für Teil, wie bisher). Dies deckt FreeCADs native Assembly-Workbench ab, solange
  deren Komponenten separate gespeicherte Dokumente sind (die typische, und bestätigt
  funktionierende, Art, darin eine Baugruppe aufzubauen) — ihre eigenen Container
  (`Assembly::AssemblyObject`) und Gelenke haben keine separate Datei und werden vom
  Erkennungsdurchlauf korrekt ignoriert.
- **Der PDF-Export ist Best-Effort** (siehe Schritt 5 oben, `Gui.export(...)`) — er läuft
  nicht über FreeCADs üblichen, auf TechDraw basierenden PDF-Pfad, sodass die Ergebnisse
  je nach FreeCAD-Version oder Dokumenttyp variieren können, auch wenn er in der Praxis
  als funktionierend bestätigt ist.
- Die Erkennung eines "bereits gesendeten" Bauteils beruht auf dem Label des Dokuments —
  das Makro vergibt es selbst nach dem Senden und speichert es sofort auf der Festplatte
  (Speichern unter dem PDM-Namen), sodass es auch in einer NEUEN FreeCAD-Sitzung
  funktioniert, sofern die umbenannte Datei (die unter dem PDM-Namen) geöffnet wird — die
  alte Kopie von vor dem Senden (unangetastet auf der Festplatte belassen) trägt weiterhin
  das ursprüngliche Label und wird nicht erkannt.
- **Die lokale Dokumentdatei wird bei jedem Senden auf einen neuen Namen VERSCHOBEN
  (Speichern unter)** — die alte Datei (unter dem ursprünglichen Namen) bleibt auf der
  Festplatte, wird aber nicht mehr aktiv bearbeitet/geöffnet; eine manuelle Änderung der
  alten Datei gelangt NICHT automatisch ins PDM (sie muss als weitere Revision
  zurückgesendet werden).
- **Das Kopieren/Registrieren der Datei in `storage/` setzt voraus, dass dieser Ordner im
  Dateisystem dieses Rechners sichtbar ist** — heute laufen Client (FreeCAD) und Server
  (`EasyPDM.Api`) auf derselben Festplatte, sodass dies ohne zusätzliche Konfiguration
  funktioniert. Ist `GET /api/config` nicht erreichbar oder der Pfad nicht beschreibbar
  (z. B. FreeCAD auf einem anderen Rechner als der Server), gelangt die Kopie stattdessen
  über einen gewöhnlichen HTTP-Upload ins PDM — in diesem Fallback-Modus wird die
  Revisionshistorie NICHT bewahrt (jedes Senden überschreibt die vorherige Kopie auf der
  PDM-Seite).
- Der Registrierungs-Endpunkt akzeptiert ausschließlich Pfade innerhalb des
  konfigurierten Speichers (`StorageRoot`) — damit lässt sich keine beliebige Datei von
  der Festplatte des Servers "anhängen".
- Das gespeicherte Dokument wird in seinem aktuellen Zustand gesendet — das Makro
  validiert z. B. nicht, ob das Dokument in anderen verknüpften Dateien ungespeicherte
  Änderungen offen hat.
- **Der Stern "ungespeicherte Änderungen" neben dem Dokumentnamen kann auch NACH einem
  erfolgreichen Senden sichtbar bleiben (sowohl nach dem expliziten
  `recompute()`+`save()` am Ende als auch bei jedem Dokument einzeln)** — bestätigt als
  unabhängig vom Makro: Dasselbe Dokument erhält den Stern sogar nach einem gewöhnlichen,
  manuellen `doc.save()`, direkt in die Python-Konsole von FreeCAD eingegeben (genau
  genommen bereits durch die bloße Nutzung der Konsole, noch vor `save()`), ohne
  Beteiligung irgendeines Codes aus dieser Datei. Dies ist ein Verhalten von FreeCAD
  selbst (vermutlich der Assembly-Workbench) — das Makro kann dies nicht verhindern, da
  das Problem nicht in dem liegt, was es tut. Es wurde nicht überprüft, ob die DATEI auf
  der Festplatte trotzdem korrekt mit dem aktuellen Inhalt gespeichert ist
  (wahrscheinlich, da `save()` tatsächlich ausgeführt wird — nur der "geändert"-Indikator
  in der GUI verschwindet nicht).

## Status

**Durchgängig live auf echtem FreeCAD verifiziert**, über mehrere Runden von Tests und
Fehlerbehebungen hinweg (zuletzt 2026-08-27): Anmeldung, der Browser-Ticket-Ablauf (neues
Element/Duplikat/An Vorhandenes anhängen) sowohl für das Hauptdokument als auch für jede
Baugruppenkomponente einzeln, die native „bereits erkanntes Dokument"-Abkürzung (Schritt
1b), Speichern-unter-dem-PDM-Namen mit anschließend korrekt aufgelösten
`App::Link`-Verweisen nach einem späteren Download, STEP-Export (einschließlich der
Korrektur für Dokumente der nativen Assembly-Workbench, die zuvor eine geometrielose
STEP-Datei erzeugten) und PDF-Export — alles vom Benutzer gegen einen echten Server
bestätigt, nicht nur statisch durchgesehen (in der Umgebung, in der diese Dateien
bearbeitet werden, gibt es weiterhin kein verfügbares FreeCAD, sodass jede Korrektur hier
daher rührt, dass der Benutzer ein Problem live nachgestellt hat — in einem Fall durch
Einfügen der Ausgabe von FreeCADs eigenem Report View zur zeilenweisen Diagnose — nicht
aus einem Build-/Testschritt).

Früher in der Historie dieser Datei wurde die zugrundeliegende Anmelde-/Ticket-/
Revisionslogik zusätzlich automatisch über `freecadcmd` gegen ein echtes `EasyPDM.Api`
verifiziert (Sitzungsbehandlung, `revision_label()`-Nummerierung, der vollständige
Revisionszyklus A→B, die Pfadvalidierung des Registrierungs-Endpunkts, die
Blätter-zuerst-Reihenfolge und Mengensummierung der automatischen Baugruppenerkennung) —
siehe Git-Historie um 2026-08-2x für die vollständige Liste, da genau der native Dialog,
den diese Tests geprüft haben (`PdmUploadDialog`/`PartPropertyForm`), aus der Zeit vor
dem oben beschriebenen Wechsel zur browserbasierten Elementerstellung stammt und in
dieser Form in dieser Datei nicht mehr existiert — das zugrundeliegende Verhalten der
Datenschicht, das damit verifiziert wurde (Revisionen, Stücklistenkanten,
Speicherregistrierung), ist unverändert.

---

# EasyPDMDownload.FCMacro — Herunterladen und Öffnen

Das zweite Makro: Anstatt zu senden, **lädt** es ein Teil/eine Baugruppe aus EasyPDM
**herunter** und **öffnet** es/sie sofort in FreeCAD. Anmeldung und API-Adresse sind
genauso konfiguriert wie in `EasyPDMUpload.FCMacro` (dieselben FreeCAD-Einstellungen) —
separate Installation/Ausführung (siehe "Installation" oben), aber eine gemeinsame
Sitzung. Wie in `EasyPDMUpload.FCMacro` fällt die Wahl des Elements im **System-Browser**,
nicht in einem nativen Fenster — das Makro fragt lokal NUR nach dem Zielordner.

## Was es tut

1. Ein natives Fenster fragt NUR nach dem Zielordner (standardmäßig wird der zuletzt
   verwendete vorgeschlagen, eine separate FreeCAD-Einstellung `DownloadFolder`, mit
   einer Schaltfläche **"..."** zum Ändern) und zeigt das Konto/**Abmelden** an. Nach der
   Bestätigung öffnet das Makro sofort den **System-Browser**, bereits angemeldet
   (Token→Cookie-Brücke), auf einer Leiste, die "eine Download-Anfrage von einem
   CAD-Makro wartet" anzeigt (sichtbar auf jedem Bildschirm der Web-Anwendung) — erst dort
   wird ein Teil/eine Baugruppe aus der **gesamten Datenbank** gesucht (nach Nummer oder
   Name) und die Wahl bestätigt. FreeCAD (das Fenster "Warte auf Browser", das alle ~2 s
   abfragt, Limit **10 Minuten**) erkennt die Bestätigung von selbst, und das
   Herunterladen beginnt automatisch — man muss nicht manuell zu FreeCAD zurückkehren.
   Ein Abbruch im Browser/Wartefenster beendet das Makro, ohne irgendetwas
   herunterzuladen.
2. Für eine **Baugruppe**: lädt auch ALLE ihre Komponenten rekursiv herunter (direkte
   Kinder, dann deren Kinder, und so weiter — die gesamte Stückliste), in DENSELBEN
   Ordner wie die Hauptdatei. Ohne dies hätte eine auf `App::Link`-Verweisen zu externen,
   gespeicherten Dateien aufgebaute Baugruppe (Standard in der Workbench
   Assembly/Assembly4) nichts, womit sie sich öffnen ließe — FreeCAD löst diese Verweise
   erst beim Öffnen des Dokuments auf, sodass die Komponentendateien bereits auf der
   Festplatte liegen müssen, BEVOR die Hauptdatei geöffnet wird.
3. Wenn im Zielordner bereits eine Datei mit **exakt demselben Namen** (also derselben
   Revision) und derselben Größe wie auf dem Server vorhanden ist — wird sie übersprungen
   und nicht ein zweites Mal heruntergeladen.
4. Wenn im Ordner bereits eine Datei DESSELBEN Elements vorhanden ist, aber in einer
   **anderen (älteren) Revision**, und auf dem Server eine neuere vorliegt — wird
   gefragt, ob die neuere heruntergeladen werden soll, statt stillschweigend zu
   überschreiben oder die veraltete Datei stillschweigend zu belassen.
5. Am Ende **öffnet** es die Haupt- (ausgewählte) Datei in FreeCAD (`App.openDocument`) —
   die Komponentendateien bleiben nur auf der Festplatte, sie werden nicht automatisch
   als separate Dokumente geöffnet (genau so, wie FreeCAD selbst eine Baugruppe öffnet:
   verlinkte Dateien werden im Hintergrund geladen).

## Woher es die Dateien nimmt

EasyPDM hat keine separate "Elementdatei" für ein Teil/eine Baugruppe — die aktuelle
CAD-Datei ist ein Anhang (`item_attachments`), und bei JEDEM Senden einer neuen Revision
über `EasyPDMUpload.FCMacro` BLEIBT die vorherige Kopie erhalten (ein neuer Anhang neben
dem alten, mit unterschiedlichen Namen: `Nummer (Name).REVISION.Erweiterung`) — die
Revisionshistorie lässt sich also in der Praxis allein aus der Anhangsliste
rekonstruieren, ohne eine separate API für "alte Dateiversionen" zu benötigen. Das Makro
erkennt diese Namenskonvention, um den Anhang zu treffen, der der AKTUELLEN Revision des
Elements entspricht; wenn ein Element noch nie ein CAD-Makro durchlaufen hat (z. B.
manuell in der Web-Anwendung angehängt, Anhänge mit beliebigen, ursprünglichen Namen),
nimmt es einfach den zuletzt hochgeladenen Anhang als beste Annäherung.

## Einschränkungen der ersten Version

- **Die Wahl des Elements** (Schritt 1 oben) erfordert, dass der Standard-System-Browser
  die Adresse des PDM-Servers öffnen kann (dieselbe wie die API-Adresse in den
  Makro-Einstellungen) — bei einer typischen Installation (Client und Server im selben
  Netzwerk) funktioniert dies ohne zusätzliche Konfiguration. Das Wartefenster in
  FreeCAD hat ein Limit von **10 Minuten** — wird es überschritten (oder Abbrechen
  gedrückt), endet das Makro, ohne irgendetwas herunterzuladen.
- Es zielt immer auf die AKTUELLE Revision — es kann damit nicht eine bestimmte,
  ausgewählte ältere Revision heruntergeladen werden (ältere lokale Kopien dienen
  ausschließlich dazu, "Sie haben eine veraltete Version" zu erkennen, Punkt 4 oben).
- Alle Dateien (Haupt- + Komponentendateien) landen flach in EINEM Ordner, ohne die
  Stücklistenstruktur als Unterordner nachzubilden. Dies ist die sicherste Standardwahl
  für `App::Link`-Verweise, die als Pfade RELATIV zum Dokumentordner gespeichert sind
  (typisch für Assembly4), aber wenn das ursprüngliche Modell mit Dateien in separaten
  Unterordnern oder mit Verweisen als ABSOLUTE Pfade von einem anderen Rechner aufgebaut
  wurde, lösen sich die Verweise unter Umständen trotzdem nicht automatisch auf — dann
  müssen sie manuell in FreeCAD korrigiert werden (Assembly4 hat dafür ein Werkzeug
  "Make link relative"/zum Ändern des Link-Pfads).
- Eine gemeinsam genutzte Komponente (an mehreren Stellen im Baum verwendet) wird nur
  einmal heruntergeladen (erkannt an der Element-ID) — genauso wie beim Senden in
  `EasyPDMUpload.FCMacro`.
- Die Erkennung "das ist die Datei dieses Elements" beruht auf derselben Namenskonvention
  wie beim Senden (`Nummer (Name).REVISION.Erweiterung`) — ein Element, dessen EINZIGER
  Anhang einen völlig anderen Namen hat (nie ein CAD-Makro durchlaufen), wird trotzdem
  heruntergeladen (es nimmt den neuesten Anhang), aber die Erkennung "Sie haben bereits
  eine ältere Revision" (Punkt 4) funktioniert dann nicht, da es nichts gibt, woran sich
  der Revisionsbuchstabe im Namen der lokalen Datei erkennen ließe.

## Verifizierungsstatus

⚠️ **Noch nicht live auf FreeCAD bestätigt**, im Gegensatz zu `EasyPDMUpload.FCMacro`
oben (das es ist, über mehrere Testrunden hinweg). Dieses Makro teilt sich denselben
zugrundeliegenden Browser-Ticket-Mechanismus (`GET /api/auth/browser-login`, `GET
/api/create-tickets/{ticket}` + `POST /create-tickets/{ticket}/attach-existing`, die
Leiste "wartende Anfrage vom Makro" in der Web-Anwendung), der von
`EasyPDMUpload.FCMacro` bereits live durchgespielt wurde — die Ticket-/Wartefenster-
Mechanik selbst ist also nichts Neues oder Ungewöhnliches —, aber die Besonderheiten
DIESES Makros (rekursiver Komponenten-Download, die Erkennung von
bereits-heruntergeladen/veraltete-Revision anhand des Dateinamens, das Öffnen des
Ergebnisses mit `App.openDocument`) wurden nur verifiziert: syntaktisch (`ast.parse`),
hinsichtlich der Korrektheit polnischer Zeichen (ein Skript zur Prüfung der
Zeichenhäufigkeit — null Verfälschungen) und durch eine sorgfältige Überprüfung der
Logik anhand der tatsächlichen API-Endpunkte (`GET /api/items`,
`/items/{id}/attachments`, `/items/{id}/children`, `/attachments/{id}/file`, jeder im
Code von `EasyPDM.Api` überprüft). Beobachten Sie beim ersten Start auf einem echten
FreeCAD den Ablauf (das Log im Fenster am Ende und in der FreeCAD-Berichtskonsole) und
melden Sie, was nicht funktioniert — die riskantesten Stellen sind: die Erkennung von
Anhangsnamen per Regex (falls eine Datei einen untypischen Namen hat) und ob sich die
`App::Link`-Verweise in der heruntergeladenen Baugruppe tatsächlich automatisch auflösen,
nachdem alle Dateien in einem einzigen, flachen Ordner platziert wurden (dies hängt davon
ab, wie die Link-Pfade in der ursprünglichen Datei gespeichert sind — siehe
"Einschränkungen" oben).
