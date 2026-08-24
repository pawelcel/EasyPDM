# EasyPDM — FreeCAD-Makros

[English](README.md) | [Polski](README.pl.md) | **Deutsch**

Zwei unabhängige Makros, eines zum Senden, eines zum Herunterladen/Öffnen:

- **`EasyPDMUpload.FCMacro`** sendet das aktive FreeCAD-Dokument an EasyPDM. Das Makro
  fragt lokal NICHTS ab außer dem Speicherordner — sogar die Wahl "neues Element oder
  Anhängen an ein vorhandenes" fällt im **System-Browser** (weiter unten in dieser Datei
  beschrieben), auf demselben Formular/derselben Leiste wie in der Web-Anwendung.
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
     nie ein Materialfeld (nur ein Teil). Das Popup hat außerdem eine **Checkbox
     "STEP-Modell exportieren und senden"**. Das Ticket ist EXPLIZIT an genau dieses eine
     Popup gebunden — kein ANDERES "Hinzufügen" in der Anwendung (im Projektbaum, im
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
     derselben STEP-Checkbox. Wenn das Label des lokalen Dokuments wie
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
5. **Exportiert optional STEP und lädt es automatisch als Anhang mit der Rolle "step"
   hoch** — das Makro hat hier keine lokale Wahl mehr. Für ein **neues** Element
   entscheidet darüber AUSSCHLIESSLICH die Checkbox im Browser-Formular (Schritt 2 oben).
   Beim Anhängen an ein **vorhandenes** Element und bei automatisch erkannten
   Baugruppenkomponenten (beide durchlaufen nie den Browser) wird STEP **immer**
   exportiert, ohne Nachfrage. Beim Export funktioniert es über exakt denselben
   Mechanismus wie die manuelle Schaltfläche "STEP" im Anhänge-Bereich der Web-Anwendung,
   sodass es sofort die dauerhafte 3D-Vorschau im Elementbereich speist. Exportiert wird
   die gesamte **sichtbare** Geometrie des Dokuments (alle Objekte mit einem
   Volumenkörper, deren Sichtbarkeit eingeschaltet ist — bei einem Teil ist das meist ein
   einzelner Volumenkörper/Body, bei einer automatisch erkannten Baugruppe sind es die
   aufgelösten `App::Link`s, sodass das STEP die gesamte Baugruppe wiedergibt). Der
   vorherige Anhang mit der Rolle "step" wird **ersetzt** (vor dem Hochladen des neuen
   gelöscht), damit die Vorschau stets die aktuelle Revision zeigt. Hat das Dokument
   keinen sichtbaren Volumenkörper (eine bloße Skizze, ein leeres Dokument) oder schlägt
   der Export/Upload fehl — wird der Schritt **stillschweigend übersprungen**, er bricht
   den Rest des Sendevorgangs weder ab noch macht er ihn rückgängig (die `.FCStd`-Datei
   ist zu diesem Zeitpunkt bereits sicher im PDM gespeichert).

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
- **Bereits gesendete Komponenten**: erkannt am Label im Format `Nummer (Name).REVISION`
  (dasselbe Format, das das Makro selbst nach dem Senden vergibt) — existiert diese
  Nummer bereits im PDM, wird die Komponente NICHT erneut erstellt, sondern nur mit der
  berechneten Menge an die Stückliste angehängt.
- **Neue Komponenten**: für jede noch nicht gesendete Datei erscheint ein separates,
  kurzes **natives** Fenster (Projekt/Typ/Name und — NUR für Teile — Art und die davon
  abhängigen Felder; eine Baugruppe hat überhaupt keine Art, sie erhält nur eine
  optionale Masse — dieselben Regeln wie das Browser-Formular für ein einzelnes neues
  Element, siehe oben) — **bewusst ohne Browser**, damit das Senden einer Baugruppe mit
  vielen neuen Komponenten nicht so viele Browserfenster wie Komponenten erfordert; der
  Typ (Teil/Baugruppe) wird danach vorgeschlagen, ob die betreffende Datei selbst weitere
  Links hat. Das HAUPT-Baugruppendokument selbst (dasjenige, auf dem das Makro ausgeführt
  wurde) durchläuft trotzdem das Browser-Formular wie jedes andere neue Element — nur
  seine automatisch erkannten KOMPONENTEN umgehen den Browser.
- Die Wahl von **"Nein"** bei der Frage nach dem automatischen Senden sendet NUR das
  aktuelle Dokument, genau wie bisher (ohne Unterelemente) — die Stücklistenstruktur
  bleibt dann zur manuellen Ergänzung in der Web-Anwendung, wie zuvor.

## Einschränkungen der ersten Version

- **Schritt 2** (neues Element/Anhängen an ein vorhandenes, beide über den Browser)
  erfordert, dass der Standard-System-Browser die Adresse des PDM-Servers öffnen kann
  (dieselbe wie die API-Adresse in den Makro-Einstellungen) — bei einer typischen
  Installation (Client und Server im selben Netzwerk) funktioniert dies ohne zusätzliche
  Konfiguration. Das Wartefenster in FreeCAD hat ein Limit von **10 Minuten** — wird es
  überschritten (oder Abbrechen gedrückt), endet das Makro mit einer Meldung, ohne eine
  Datei zu erstellen/anzuhängen.
- Die automatische Baugruppenerkennung funktioniert nur für Verweise auf **externe,
  gespeicherte Dateien** (`App::Link`) — nicht für Baugruppen, die als `App::Part`-
  Container in einer einzigen Datei gehalten werden (diese haben keine separaten
  Dateien, die einzeln gesendet werden könnten; sie müssen dann manuell gesendet werden,
  Teil für Teil, wie bisher).
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

## Verifizierung

⚠️ **Die folgenden Tests betreffen die Version VOR der Änderung "Formular für neues
Element im Browser"** (beschrieben in Schritt 2 oben) — diese Änderung ist **auf einem
echten FreeCAD ungetestet**. Bisher verifiziert: Syntax (`py_compile`), strukturelle
Konsistenz (keine verwaisten Verweise auf entfernte Felder des nativen Dialogs), und
End-to-End auf Ebene der Backend-Endpunkte selbst (`GET /api/auth/browser-login` — Setzen
des Cookies + Weiterleitung + Schutz vor Open-Redirect, `POST /projects/{id}/nodes` mit
einem Ticket + `GET /create-tickets/{ticket}` — Rückgabe korrekter Elementdaten) in einer
isolierten Testumgebung, siehe `EasyPDM.Api.Tests/CreateTicketEndpointsTests.cs` und
`AuthEndpointsTests.cs`. NICHT live verifiziert: `webbrowser.open()` selbst aus FreeCAD
heraus, das Fenster "Warte auf Browser" (`WaitForTicketDialog`) in einer echten GUI, sowie
der gesamte Ablauf vom Klick auf "Hinzufügen" im Browser bis zum automatischen Fortfahren
in FreeCAD. Beobachten Sie beim ersten Start den Ablauf aufmerksam und melden Sie, was
nicht funktioniert.

⚠️ **Die folgenden Tests betreffen die Version VOR der Änderung "Speichern unter der
lokalen Datei unter dem PDM-Namen"** (beschrieben in Schritt 3 oben) — insbesondere
beschreiben die Punkte, die besagen, dass die lokale Datei/`doc.FileName` sich "nicht
ändert"/"unangetastet bleibt", das FRÜHERE Verhalten, nicht das aktuelle.

Der Speichern-unter-Mechanismus selbst **wurde live vom Benutzer bestätigt** — das Senden
einer Baugruppe mit einem Teil und das anschließende Herunterladen über
`EasyPDMDownload.FCMacro` öffnete sich OHNE den Fehler "Link broken" (zuvor, vor dieser
Änderung, meldete die Baugruppe genau diesen Fehler, da sie nach dem ursprünglichen
Dateinamen von vor dem Senden suchte). Zwei NEUERE, unmittelbar danach hinzugefügte Dinge
wurden noch nicht live getestet: das **Fenster zur Wahl des Zielordners** (Schritt 1a —
geteilt mit dem Download-Ordner in `EasyPDMDownload.FCMacro`) und die **verbesserte
Abschlussmeldung** (die unterscheidet, ob die lokale Datei tatsächlich verschoben wurde
oder bereits dort lag).

Die Logik (ohne das Dialogfenster selbst) wurde automatisch über `freecadcmd` gegen ein
echtes `EasyPDM.Api` getestet:
- **Anmeldung/Sitzung**: ein API-Aufruf ohne Sitzung wird abgelehnt (401); ein falsches
  Passwort wird mit einem gewöhnlichen Fehler abgelehnt (das lokale Token bleibt leer);
  eine korrekte Anmeldung speichert das Token und den angezeigten Benutzernamen in den
  FreeCAD-Einstellungen, weitere API-Aufrufe mit diesem Token funktionieren; das Abmelden
  löscht Token/Namen lokal UND macht die Sitzung serverseitig ungültig (ein weiterer
  Aufruf mit demselben, bereits ungültigen Token erhält erneut 401),
- **die vier Teile-Arten** (Gefertigt/Zugekauft/Norm/Kundenteil) in der Combobox, mit
  einer Feldsichtbarkeit, die genau wie in `PartPropertyForm` von der gewählten Art
  abhängt (geprüft am echten Fenster `PdmUploadDialog`, von Qt im `offscreen`-Modus
  dargestellt); die Art der Baugruppe ist auf drei Optionen ohne "Kundenteil" beschränkt,
  mit Masse immer sichtbar unabhängig von der Art, aber ohne Materialfeld (nur ein Teil
  hat es); die Erstellung eines Teils "Norm" (mit Material und Norm) und "Kundenteil"
  (ohne zusätzliche Felder) wurde durch Auslesen der gespeicherten Eigenschaften vom
  Server bestätigt,
- **automatische Baugruppenerkennung** (ein dreistufiger Baum: ein Teil, das 2× über
  separate Links + 1× über ein Muster von 2 Stück in der Hauptbaugruppe verlinkt ist,
  plus dasselbe Teil noch einmal innerhalb einer separaten Unterbaugruppe): erkannte
  Blätter-zuerst-Sendereihenfolge, korrekt summierte Menge (2+2=4) für ein mehrfach
  verwendetes Teil, korrekte Eltern-Kind-Kanten auf allen Ebenen (einschließlich von
  einer Unterbaugruppe zu ihrem eigenen Teil) — direkt bestätigt über
  `GET /api/projects/{projectId}/relations` nach einem vollständigen Durchlauf von
  `process_assembly_tree` (mit ausgetauschtem Fenster für neue Komponenten, damit dies
  ohne GUI ausgeführt werden konnte). Eine erneute Prüfung in derselben Sitzung erkennt
  bereits gesendete Komponenten an ihrem Label (ohne Duplikate zu erstellen).
- Erstellung eines Teils mit einem Material, Erstellung einer Baugruppe unter einem
  Ordner mit einer Stücklistenverknüpfung,
- **eine außerhalb des Speichers gespeicherte Datei (z. B. ein simulierter Desktop)
  bleibt dort, wo sie war** — nach dem Senden existiert die lokale Datei weiterhin unter
  demselben Pfad und Namen, `doc.FileName` ändert sich nicht, und in
  `storage/components/` gelangt nur eine KOPIE davon (bytegleich, bestätigt durch den
  Vergleich der lokalen Datei mit der Kopie auf dem Server),
- Elemente aus ZWEI verschiedenen Projekten landen im selben, gemeinsamen
  `storage/components/` — ohne separate Unterordner pro Projekt,
- der Registrierungs-Endpunkt: Ablehnung eines Pfads außerhalb des Speichers (400) und
  einer nicht existierenden Datei (404), korrekte Registrierung (bestätigt über
  `file_path` in der Datenbank),
- `revision_label()`: 1→A, 2→B, 26→Z, 27→AA,
- **vollständiger Revisionszyklus**: das erste Senden erstellt eine Kopie
  `N (Name).A.ext` auf dem Server mit einem einzigen Anhang (die lokale Datei bleibt die
  ganze Zeit unverändert); ein erneutes Senden OHNE Statusänderung überschreibt nur
  dieselbe `.A.`-Kopie (vervielfacht die Kopien nicht, die lokale Datei bleibt
  unangetastet); Senden an ein Element mit Status "Freigegeben" mit Ablehnung → nichts
  ändert sich (der Status bleibt "Freigegeben", Kopie und Anhang A unangetastet); mit
  Bestätigung → der Status kehrt zu "In Bearbeitung" zurück, die Revisionsnummer steigt,
  auf dem Server entsteht eine NEUE Kopie `.B.ext` **neben** `.A.ext` (die alte wird
  NICHT gelöscht, die lokale Datei bleibt die ganze Zeit unter demselben, unveränderten
  Namen/Pfad), und im PDM gibt es jetzt genau ZWEI Anhänge — einen pro Revision,
- **automatische Erkennung eines vorhandenen Elements anhand des Namens** (der einzige
  Test, der ein echtes `PdmUploadDialog`-Fenster aufbaut, ohne `exec()`): genau eine
  Namensübereinstimmung → der Modus wechselt zu "Vorhandenes Element" und das Element ist
  sofort ausgewählt; keine Übereinstimmung → es bleibt bei "Neues Element"; mehrere
  Elemente mit demselben Namen (verschiedene Projekte) → der Modus wechselt und die Suche
  wird auf diesen Namen eingegrenzt, aber nichts wird automatisch ausgewählt; das
  Eingeben des Namens live im Feld "Name" löst dieselbe Prüfung aus,
- **Revisionskommentar**: bei Bestätigung einer neuen Revision mit Kommentar —
  `GET /api/items/{id}/revisions` gibt genau einen Eintrag mit diesem Kommentar und der
  korrekten Revisionsnummer zurück; eine Ablehnung der Revision oder eine Bestätigung mit
  LEEREM Kommentar tragen dort nichts ein (Revisionen ohne Kommentar haben schlicht
  keinen Eintrag).

Alles verlief korrekt — einschließlich einmal live durch den Benutzer selbst in der
FreeCAD-GUI (Erstellung eines Elements und einer Revision A→B), was das Verhalten von
Dateien und Anhängen in einer echten Umgebung bestätigte, nicht nur in automatisierten
Tests. Das Dialogfenster selbst (PySide6, einschließlich der Suche nach vorhandenen
Elementen und des Fensters "Neue Revision" mit Kommentar) wurde ebenfalls manuell
überprüft — getestet auf FreeCAD 1.1.3 mit PySide6.

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

⚠️ **Auf einem echten FreeCAD ungetestet** — im Gegensatz zu `EasyPDMUpload.FCMacro`
(das einen vollständigen Testzyklus über `freecadcmd` gegen einen echten Server
durchlaufen hat, plus manuelle Verifizierung in der GUI), konnte dieses Makro nur
verifiziert werden: syntaktisch (`ast.parse`), hinsichtlich der Korrektheit polnischer
Zeichen (ein Skript zur Prüfung der Zeichenhäufigkeit — null Verfälschungen) und durch
eine sorgfältige Überprüfung der Logik anhand der tatsächlichen API-Endpunkte
(`GET /api/items`, `/items/{id}/attachments`, `/items/{id}/children`,
`/attachments/{id}/file`, jeder im Code von `EasyPDM.Api` überprüft). Beobachten Sie beim
ersten Start auf einem echten FreeCAD den Ablauf (das Log im Fenster am Ende und in der
FreeCAD-Berichtskonsole) und melden Sie, was nicht funktioniert — die riskantesten
Stellen sind: die Erkennung von Anhangsnamen per Regex (falls eine Datei einen
untypischen Namen hat) und ob sich die `App::Link`-Verweise in der heruntergeladenen
Baugruppe tatsächlich automatisch auflösen, nachdem alle Dateien in einem einzigen,
flachen Ordner platziert wurden (dies hängt davon ab, wie die Link-Pfade in der
ursprünglichen Datei gespeichert sind — siehe "Einschränkungen" oben).

Die Wahl des Elements über den Browser (Schritt 1 oben) ist die NEUESTE Änderung in
dieser Datei, direkt auf demselben, bereits verifizierten Mechanismus aus
`EasyPDMUpload.FCMacro` aufgebaut (`GET /api/auth/browser-login`, das Ticket
`GET /api/create-tickets/{ticket}` + `POST /create-tickets/{ticket}/attach-existing`,
die Leiste "wartende Anfrage vom Makro" in der Web-Anwendung) — Backend/Frontend dieses
Teils wurden End-to-End getestet (`curl` in einer isolierten Umgebung), aber
`EasyPDMDownload.FCMacro` selbst (`webbrowser.open`, `WaitForTicketDialog` in einer
echten GUI) wurde noch nicht auf einem echten FreeCAD ausgeführt.
