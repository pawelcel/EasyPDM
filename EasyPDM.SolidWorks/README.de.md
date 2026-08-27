# EasyPDM — SolidWorks-Makros

[English](README.md) | [Polski](README.pl.md) | **Deutsch**

Zwei `.bas`-Makros, jedes vollständig eigenständig (separate Datei, separates VBA-Modul,
ohne Abhängigkeiten untereinander außer einem gemeinsamen Platz in der
Windows-Registrierung für die Anmeldesitzung):

- **`EasyPDMUpload.bas`** — sendet das aktive SolidWorks-Dokument an EasyPDM. Die Wahl
  von Projekt/neu-oder-vorhanden/Duplizieren/Elementeigenschaften erfolgt im Browser
  (dasselbe Muster wie bei den FreeCAD-Makros), mit einer Ausnahme — siehe „Unterschiede
  zu den FreeCAD-Makros" unten.
- **`EasyPDMDownload.bas`** — ruft ein Teil/eine Baugruppe aus EasyPDM ab (zusammen mit
  allen Komponenten der Baugruppe) und öffnet es/sie in SolidWorks; die Wahl, WELCHES
  Element, erfolgt ebenfalls im Browser.

## Status

**Durchgängig live auf echtem SolidWorks 2026 verifiziert**, über mehrere Runden von
Tests und Fehlerbehebungen hinweg (beginnend 2026-08-20, zuletzt 2026-08-27): Anmeldung,
der Browser-Ticket-Ablauf (neues Element/Duplikat/an vorhandenes anhängen),
Datei-Upload/-Registrierung, STEP/PDF-Anhangsexport, automatische Erkennung des
Baugruppenbaums mit Browser-Tickets pro Komponente sowie das rekursive Herunterladen von
Komponenten in `EasyPDMDownload.bas` — alles gegen einen echten Server bestätigt, nicht
nur statisch durchgesehen (in der Umgebung, in der diese Dateien bearbeitet werden, gibt
es weiterhin keinen VBA-Compiler, sodass jede Korrektur hier daher rührt, dass der
Benutzer ein Problem live nachgestellt und das eigene Protokoll des Makros eingefügt hat,
nicht aus einem Build-Schritt). Siehe „Historie behobener Probleme" unten für die
konkreten Fehler, die auf diese Weise gefunden wurden.

Ein paar schmale, selten durchlaufene Codepfade bleiben in der Praxis tatsächlich
ungetestet (im Quelltext direkt mit `UNVERIFIED` markiert) — siehe „Bekannte Risiken"
unten.

## Unterschiede zu den FreeCAD-Makros

Das sind **Gegenstücke**, kein 1:1-Port — VBA hat kein eingebautes JSON und keine
Dialogfenster ohne separate Binärdateien (SolidWorks/VBA UserForm), daher wird einiges
anders gelöst:

- **JSON**: ein eigener, minimaler Parser/Builder direkt im Makro (in beiden Dateien
  dupliziert, nicht gemeinsam genutzt — VBA hat keine zuverlässige Möglichkeit, ein Modul
  aus einem anderen zu importieren) — ausreichend für die Antwortformen dieser konkreten
  API, nicht für allgemeine Zwecke.
- **Kein `UserForm`** — anstelle der Qt-Formulare von FreeCAD sind alle nativen Fenster
  einfache `InputBox`/`MsgBox` (`UserForm` ist eine separate Binärdatei, sie lässt sich
  nicht in ein einzelnes, über „Datei → Datei importieren..." importiertes `.bas`
  einbetten). Das Passwort bei der Anmeldung lässt sich mit einer einfachen `InputBox`
  nicht mit Sternchen maskieren. Das Warten auf den Browser (siehe unten) zeigt den
  Fortschritt in der SolidWorks-Statusleiste statt in einem Fenster mit
  Abbrechen-Schaltfläche — Escape ist die einzige verfügbare Abbruchgeste.
- **Wahl von Projekt/neu-oder-vorhanden/Duplizieren/Elementeigenschaften im Browser** —
  dasselbe Muster aus Ticket+`GET /api/auth/browser-login`+Popup „ausstehende Anfrage von
  einem CAD-Makro" wie bei den FreeCAD-Makros, **mit einer bewussten Ausnahme**: Wenn das
  Dokument BEREITS mit einem PDM-Element verknüpft ist (siehe Punkt unten), öffnet das
  Makro den Browser überhaupt NICHT — es fragt lokal (natives `MsgBox`) nach der
  Zustimmung zu einer neuen Revision, plus ob STEP und/oder PDF exportiert werden sollen
  (siehe unten), genau wie vor dieser Änderung. SolidWorks kennt das Element dann mit
  100%iger Sicherheit, sodass der Browser hier nichts beitragen würde; FreeCAD geht
  immer zum Browser (oder fragt seit Kurzem zuerst nativ nach, wenn eine
  Label-Übereinstimmung gefunden wird), weil es keinen ebenso zuverlässigen lokalen
  Mechanismus hat.
- **Erkennung eines bereits gesendeten Dokuments** (`EasyPDMUpload.bas`): NICHT über ein
  Label/einen Dateinamen (SolidWorks hat kein Äquivalent zum freien Label von FreeCAD) —
  über die **Custom Properties** des Dokuments (`EasyPDM_ItemId`, `EasyPDM_ItemNumber`),
  die nach einem erfolgreichen Upload in der Datei selbst gespeichert werden. Das ist
  tatsächlich ein **dauerhafterer** Ansatz als in FreeCAD — er funktioniert auch in einer
  NEUEN SolidWorks-Sitzung, und wird auch für JEDE Baugruppenkomponente einzeln
  verwendet (siehe „Erkennung des Baugruppenbaums" unten), nicht nur für das
  Hauptdokument.
- **Neue Baugruppenkomponenten laufen ebenfalls über den Browser, eine nach der
  anderen** — jede noch nicht verknüpfte Komponente, die beim Durchlaufen des Baums
  gefunden wird, öffnet ihr eigenes Ticket + einen Browser-Tab (zuerst die Blätter,
  dieselbe Wahl Neu/Duplizieren/An vorhandenes anhängen wie beim Hauptdokument),
  nacheinander, nie mehrere Tabs gleichzeitig — dasselbe Muster, an das das
  FreeCAD-Makro später angepasst wurde. Da pro Makrolauf nur ein Browser-Tab zuverlässig
  den Windows-Vordergrundfokus erhalten kann, erscheint unmittelbar vor jedem weiteren
  Tab ein natives „Zum Fortfahren OK klicken"-`MsgBox` — der Klick zählt als frische
  Benutzereingabe, die es dem nächsten Browserfenster erlaubt, den Fokus zu erhalten,
  statt still im Hintergrund zu öffnen (Taskleiste prüfen, falls ein Schritt hängen zu
  bleiben scheint).
- **STEP- und PDF-Export sind überall optional** — je eine Checkbox auf den
  Ticket-basierten Browser-Pfaden (neues Element/Duplikat/an vorhandenes anhängen,
  sowohl für das Hauptdokument als auch pro Baugruppenkomponente), oder zwei native
  Ja/Nein-Abfragen (`ExportStepPrompt`/`ExportPdfPrompt`, STEP standardmäßig Ja, PDF
  standardmäßig Nein) auf dem browserlosen „bereits verknüpft"-Pfad. Der PDF-Export
  nutzt SolidWorks' eigenes „Speichern unter PDF", markiert mit der Anhangsrolle
  `"pdf"`, unabhängig vom STEP-Anhang.

## Was `EasyPDMUpload.bas` tut

1. **Anmeldung** — beim ersten Start (oder wenn die gespeicherte Sitzung
   abgelaufen/ungültig geworden ist) wird nach der API-Adresse, dem Benutzernamen und
   dem Passwort gefragt. Das Sitzungstoken wird über die eingebauten
   `SaveSetting`/`GetSetting` in der Windows-Registrierung gespeichert
   (`HKEY_CURRENT_USER\Software\VB and VBA Program Settings\EasyPDM`) — bei weiteren
   Starts (auch nach einem SolidWorks-Neustart, auch von `EasyPDMDownload.bas` aus — die
   Sitzung wird gemeinsam genutzt) wird nicht erneut nach der Anmeldung gefragt, solange
   die Sitzung gültig ist (30 Tage).
2. **Speichert das aktive Dokument**, falls es noch nicht gespeichert wurde (das
   Standard-„Speichern unter"-Fenster von SolidWorks).
3. **Wenn das aktive Dokument eine Baugruppe ist**: Zuerst werden alle Lightweight-
   Komponenten aufgelöst (`ResolveAllLightWeightComponents`, direkt auf dem
   Baugruppenmodell aufgerufen — der Aufruf über `.Extension` tut stattdessen still
   nichts und war ein echter, in Tests gefundener Fehler: `GetModelDoc2()` liefert für
   eine noch nicht aufgelöste Lightweight-Komponente `Nothing`, wodurch sie bis zur
   Auflösung nicht von „Komponente nicht gefunden" zu unterscheiden ist), dann wird der
   Komponentenbaum erkannt (`IAssemblyDoc.GetComponents`, rekursiv). Ein
   zusammenfassendes `MsgBox` vor dem Durchlaufen listet jede gefundene Komponente auf
   und markiert bereits verknüpfte mit ihrer Ziel-PDM-Elementnummer und ihrem Dateinamen
   — eine bewusste Absicherung, da SolidWorks' eigenes „Speichern unter" die Custom
   Properties still mitkopiert, was ein wirklich neues Teil fälschlich dazu bringen
   kann, sich selbst als vorhandenes Element zu „erkennen", wenn dieser
   Zusammenfassung keine Beachtung geschenkt wird. Nach Bestätigung wird zuerst mit den
   Blättern durchlaufen (dieses Dokument zuletzt); für jede NOCH NICHT verknüpfte
   Komponente wird ein eigenes Browser-Ticket geöffnet (dieselbe Wahl Neu/Duplizieren/
   An vorhandenes anhängen wie beim Hauptdokument, siehe „Unterschiede zu den
   FreeCAD-Makros" für das Fokus-`MsgBox` vor jedem Tab). Neu erstellte Komponenten
   erhalten ihren eigenen STEP/PDF-Export (je nach eigener Checkbox-Wahl dieser
   Komponente im Browser) und einen `EasyPDM_ItemId`-Eintrag und werden automatisch
   unter ihrem Elternelement in der Stücklistenstruktur eingehängt.
4. Prüft die **Custom Properties** des Hauptdokuments:
   - **Bereits verknüpft** (hat eine gespeicherte `EasyPDM_ItemId`) — fragt lokal nach der
     Zustimmung, die aktuelle Version als neue Revision anzuhängen, ohne den Browser zu
     öffnen (siehe „Unterschiede zu den FreeCAD-Makros"), gefolgt von zwei weiteren
     nativen Ja/Nein-Abfragen für STEP-Export (Standard Ja) und PDF-Export (Standard
     Nein).
   - **Noch nicht verknüpft** — öffnet den System-Browser (bereits angemeldet,
     Token→Cookie-Brücke) auf dem Popup „ausstehende Anfrage von einem CAD-Makro" mit
     drei DORT zu wählenden Optionen: **Neues Element** (Projekt, optional ein
     Elternelement, Typ, Name — bei Teilen zusätzlich die Art und deren abhängige Felder:
     Gefertigt → Material; Zugekauft → Hersteller/Bestellnummern/Masse; Normteil →
     Material/Norm; Kundenteil → keine zusätzlichen Felder; **eine Baugruppe hat
     überhaupt keine Art** — nur eine optionale Masse — plus STEP- und
     PDF-Export-Checkboxen), **Duplizieren** (verweist auf ein vorhandenes Element,
     kopiert dessen Eigenschaften in ein neues, ohne Dateien) oder **An vorhandenes
     anhängen** (Suche über die gesamte Datenbank + dieselben STEP/PDF-Checkboxen). Das
     Makro wartet (fragt etwa alle 2s ab, Limit 10 Minuten, Escape bricht ab, Fortschritt
     in der SolidWorks-Statusleiste) und setzt automatisch fort, sobald die Wahl im
     Browser bestätigt wurde.
   - **Vorhandenes Element im Status „Freigegeben"** (beide Pfade oben): fragt nach der
     Zustimmung zu einer neuen Revision und einem optionalen Kommentar — genau derselbe
     Mechanismus wie in der Web-Anwendung, die einzige Entscheidung, die bewusst auch auf
     dem Browser-Pfad lokal bleibt.
5. **Kopiert** die aktuelle Dokumentdatei in das PDM unter dem Namen
   `nummer (name).REVISION.erweiterung` (dieselbe Konvention wie in der Web-Anwendung und
   den FreeCAD-Makros). **Die lokale Datei wird NICHT angetastet** — sie wird weder
   verschoben noch gelöscht. Wenn der PDM-Speicher von dieser Maschine aus sichtbar ist
   (`GET /api/config`), landet die Kopie im gemeinsamen `storage/components/` und wird
   **registriert**, ohne erneut per HTTP hochgeladen zu werden (bewahrt die
   Revisionshistorie); wenn nicht (typisch, wenn SolidWorks und der EasyPDM-Dienst als
   unterschiedliche Windows-Benutzer laufen — der Speicher liegt unter
   `C:\ProgramData\...`, worauf ein gewöhnlicher Benutzer normalerweise kein Schreibrecht
   hat), ein gewöhnlicher HTTP-Upload (Fallback ohne Erhalt der Revisionshistorie, der für
   genau diesen Aufruf `WinHttp.WinHttpRequest.5.1` verwendet — siehe „Historie
   behobener Probleme" unten) — **das ist kein Fehler**, sondern eine korrekt
   funktionierende Sicherheitsmaßnahme. Dieser Fallback-Pfad prüft außerdem vor dem
   Hochladen, ob bereits ein Anhang mit der Rolle `"cad"` und exakt demselben Dateinamen
   existiert, und löscht ihn — erneutes Speichern auf derselben Revisionsstufe sammelt
   dadurch keine doppelten Anhänge mehr an.
6. Wenn STEP-/PDF-Export aktiviert ist (Checkbox im Browser, oder die nativen Abfragen
   auf dem browserlosen Pfad — siehe Punkt 4): exportiert es die sichtbare Geometrie in
   eine temporäre `.step`-/`.pdf`-Datei (`IModelDocExtension.SaveAs`) und lädt sie als
   Anhang mit der Rolle `"step"`/`"pdf"` hoch, wobei der vorherige Anhang derselben Rolle
   ersetzt wird — versorgt die dauerhafte 3D-Vorschau (STEP) in der Web-Anwendung. Ein
   Exportfehler (z. B. keine sichtbare Geometrie) bricht den Rest des Vorgangs NICHT ab.
7. Speichert `EasyPDM_ItemId`/`EasyPDM_ItemNumber` in den Custom Properties des Dokuments
   und zeigt eine Bestätigung.

## Was `EasyPDMDownload.bas` tut

1. **Anmeldung** — wie oben (Sitzung gemeinsam mit `EasyPDMUpload.bas`).
2. **Die Wahl des herunterzuladenden Elements erfolgt im Browser** — dasselbe Popup
   „ausstehende Anfrage von einem CAD-Makro" wie beim Senden, nur gleich mit der reinen
   Suche (ohne die Wahl Neu/Duplizieren, die beim Herunterladen irrelevant ist). Das
   Makro wartet genauso wie beim Senden (Escape bricht ab, Limit 10 Minuten). Das
   Einzige, was eine lokale `InputBox` bleibt, ist der **Zielordner** (schlägt
   standardmäßig den zuletzt verwendeten vor — dieselbe Präferenz wie der Zielordner in
   `EasyPDMUpload.bas`, sodass gesendete und heruntergeladene Dateien an einem Ort landen
   können).
3. Bei einer Baugruppe: Es werden auch **alle ihre Bestandteile rekursiv** abgerufen
   (direkte Kinder, dann deren Kinder und so weiter — die gesamte Stückliste), in
   DENSELBEN Ordner wie die Hauptdatei. Ohne dies hätte eine Baugruppe, die auf externen
   Verweisen auf gespeicherte Dateien basiert (typisch in SolidWorks), nichts, wogegen
   sie sich öffnen könnte.
4. Für jede Datei: Wenn im Ordner bereits eine Datei mit GENAU demselben Namen (also
   derselben Revision) und derselben Größe wie auf dem Server vorhanden ist — wird sie
   übersprungen (nicht erneut heruntergeladen). Wenn eine Datei dieses Elements in einer
   anderen (älteren) Revision vorhanden ist und auf dem Server eine neuere existiert —
   wird gefragt, ob die neuere heruntergeladen werden soll.
5. Am Ende öffnet es die Haupt-(gewählte) Datei in SolidWorks (`swApp.OpenDoc6`) — die
   Komponentendateien bleiben nur auf der Festplatte, SolidWorks löst die
   Baugruppenverweise zu ihnen selbst auf.

Woher es die herunterzuladenden Dateien nimmt: EasyPDM speichert die aktuelle CAD-Datei
als Anhang (es gibt keinen separaten „Elementdatei"-Mechanismus), und frühere Revisionen
bleiben als separate Anhänge daneben bestehen — `EasyPDMDownload.bas` erkennt die von
`EasyPDMUpload.bas` vergebene Namenskonvention, um den Anhang zu treffen, der der
AKTUELLEN Revision entspricht; falls ein Element noch nie ein CAD-Makro durchlaufen hat
(manuell in der Web-Anwendung angehängt), nimmt es einfach den zuletzt hochgeladenen
Anhang.

## Wie man prüft, ob es funktioniert hat

Drei unabhängige Wege, vom schnellsten zum detailliertesten:

1. **Die Meldung am Ende** — nach einem erfolgreichen Vorgang zeigt das Makro ein Fenster
   mit einer Zusammenfassung (z. B. „An EasyPDM gesendet: Element #67 (Revision B)."
   oder ein Download-Protokoll). Ein Fenster mit „Fehler: ..." bedeutet, dass etwas
   nicht funktioniert hat.
2. **Die Web-Anwendung** — der sicherste Beweis für einen Upload: ins Projekt gehen
   (oder „Gesamte Datenbank"), das Element anhand der Nummer aus der Meldung finden und
   prüfen, ob es eine angehängte Datei hat (Eigenschaftenbereich → Anhänge) und korrekte
   Eigenschaften.
3. **Das Makro-Protokoll** — jeder Lauf hängt einen detaillierten, zeitgestempelten
   Ablauf an eine gewöhnliche Textdatei an (überschreibt sie nicht; eine separate Datei
   für jedes Makro, damit sie sich nicht vermischen):

   ```
   %TEMP%\EasyPDM_macro.log            <- EasyPDMUpload.bas
   %TEMP%\EasyPDM_download_macro.log   <- EasyPDMDownload.bas
   ```

   (fügen Sie `%TEMP%` in die Adressleiste des Windows-Explorers ein, um dorthin zu
   gelangen). Enthält unter anderem jeden API-Aufruf mit dem Antwortcode
   (`GET /items -> 200`), das Anmeldeergebnis, ob der Dateispeicher von dieser Maschine
   aus sichtbar war, ob die Datei erfolgreich kopiert/registriert/heruntergeladen wurde,
   und den vollständigen Fehlertext, falls etwas fehlgeschlagen ist. Das ist die erste
   Stelle, die man prüfen sollte, wenn etwas nicht funktioniert — der Pfad dorthin steht
   am Ende auch im Fehler-/Erfolgsfenster.

## Installation

SolidWorks hat kein reines Textformat für Makros (wie `.FCMacro` bei FreeCAD) — Makros
sind VBA-Projekte. `.bas` ist das Standard-Export-/Importformat für ein VBA-**Modul**
(nicht für das gesamte Makroprojekt), daher für JEDE der beiden Dateien einzeln:

1. SolidWorks → **Extras → Makro → Neu...** — ein neues (leeres) Makroprojekt erstellen
   und speichern (z. B. `EasyPDMUpload.swp` / `EasyPDMDownload.swp`).
2. Im geöffneten VBA-Editor: **Datei → Datei importieren...** → `EasyPDMUpload.bas` bzw.
   `EasyPDMDownload.bas` auswählen. Falls im Projekt noch ein leeres, automatisch
   erzeugtes Modul vorhanden ist (typischerweise `Module1`/`Upload1`) — entfernen Sie es
   (Rechtsklick im Projektbaum → Remove... → No, wenn nach dem Export gefragt wird),
   damit nicht zwei Module gleichzeitig übrig bleiben.
3. Ausführen über **Extras → Makro → Ausführen** (unter Angabe der gespeicherten
   `.swp`-Datei) oder direkt aus dem VBA-Editor (F5, **mit dem Cursor innerhalb von
   `Sub main()`** — wenn im Projekt weitere Prozeduren vorhanden sind, führt F5
   diejenige aus, in der sich der Cursor gerade befindet, nicht automatisch immer
   `main`).
4. Ein separates `Sub Logout` (in jedem der Module) meldet von EasyPDM ab — es kann an
   eine eigene Schaltfläche/einen eigenen Shortcut in SolidWorks gebunden werden.

Die API-Adresse (standardmäßig `http://localhost:5000/api`) wird nach der ersten Eingabe
bei der Anmeldung automatisch gespeichert — gemeinsam für beide Makros.

## Historie behobener Probleme

Gefunden und behoben am 2026-08-20, während des ersten echten Tests von
`EasyPDMUpload.bas` auf einem echten SolidWorks 2026 — hier als dokumentiertes Beispiel
für die Art von VBA-spezifischer Falle belassen, auf die dieser Code schon einmal
gestoßen ist (beide Makros nutzen inzwischen dieselbe, bereits korrigierte
Infrastruktur):

1. **Kein Sitzungstoken im Anmelde-Body** — `MSXML2.XMLHTTP.6.0` gab keinen
   zuverlässigen Zugriff auf den `Set-Cookie`-Header. Serverseitig behoben
   (`POST /auth/login` fügt `sessionToken` direkt in den JSON-Body ein) — gilt für beide
   Makros.
2. **Nicht deklarierte SolidWorks-Konstanten** (`swCustomInfoText`,
   `swCustomPropertyReplaceValue`, `swDocPART`, `swDocASSEMBLY`) — das gesamte Modul
   verwendet bewusst spätes Binden (Typ `Object` statt `SldWorks.*`), sodass die bloßen
   Namen der SolidWorks-Enums keine Werte hatten, von denen sie sie hätten beziehen
   können. Behoben durch explizite `Long`-Konstanten mit dokumentierten Werten aus der
   SolidWorks-API, gruppiert direkt am Anfang des Moduls.
3. **`swApp` nicht deklariert** — ENTGEGEN einer früheren Annahme ist `swApp` NICHT
   automatisch in jedem VBA-Modul eines Projekts sichtbar, sondern nur in dem, das
   SolidWorks selbst bei „Neu..." erzeugt hat. Ein importiertes Modul benötigt am Anfang
   von `main()` eine eigene Deklaration und Zuweisung
   (`Set swApp = Application.SldWorks`).
4. **Mojibake polnischer Zeichen** — VBA importierte die `.bas`-Datei nicht als UTF-8;
   polnische Zeichen (in Kommentaren UND in für den Benutzer sichtbaren Fenstern) kamen
   als Kauderwelsch heraus. Behoben durch Übersetzung der gesamten Datei in reines ASCII
   (Englisch).
5. **`MSXML2.XMLHTTP.send()` lehnte ein rohes `Byte()`-Array ab** („Der Parameter ist
   falsch") beim Upload über den HTTP-Fallback. Ein erster Versuch verpackte die Bytes
   in einen binären `ADODB.Stream`, gesendet über dasselbe `MSXML2.XMLHTTP`-Objekt —
   das schlug bei einem echten Upload selbst noch mit einem allgemeinen Fehler „KEINE
   VERBINDUNG" fehl. Die eigentliche Korrektur: `ApiUploadFile` wurde speziell auf
   `WinHttp.WinHttpRequest.5.1` umgestellt, das ein `Byte()`-Array direkt über `.Send()`
   akzeptiert; jeder andere Aufruf (`ApiGet`, Anmeldung usw.) nutzt weiterhin
   `MSXML2.XMLHTTP.6.0`.

Spätere Runden von Live-Tests (bis 2026-08-27) fanden und behoben noch mehrere weitere
Probleme, hier zusammengefasst statt einzeln aufgeführt — die volle Historie steht in der
Git-Historie: nicht erkannte Lightweight-Baugruppenkomponenten
(`ResolveAllLightWeightComponents` muss direkt auf dem Modell aufgerufen werden, nicht
über `.Extension`, das still nichts tut); GET-Anfragen von `MSXML2.XMLHTTP.6.0`, die
unbegrenzt aus dem lokalen HTTP-Cache von Windows bedient wurden, behoben durch
zusätzliche `Cache-Control`/`Pragma`-Header sowie einen Cache-Busting-Query-Parameter
`_ts=` bei jedem `ApiGet`-Aufruf in BEIDEN Dateien (jede hat ihre eigene, unabhängige
Kopie von `ApiGet`, und `EasyPDMDownload.bas` brauchte zusätzlich dieselbe Korrektur in
`ApiGetBinary`, da eine veraltete zwischengespeicherte Antwort dort still die
tatsächlichen Bytes der heruntergeladenen Datei verfälscht hätte, nicht nur ein
Statusfeld); eine neue Baugruppenkomponente, die als unerwünschtes Duplikat im
Projektstamm auftauchte, behoben durch explizites Verstecken neu erstellter
Blattelemente aus dem Baumstamm, sobald sie an ihrem eigentlichen Elternelement
eingehängt sind; und sich wiederholende Anhänge der Rolle „cad", die sich bei jedem
Speichern auf derselben Revision ansammelten, behoben durch Prüfen und Löschen eines
vorhandenen Anhangs mit demselben Dateinamen vor dem Plain-HTTP-Upload-Fallback.

## Bekannte Risiken / zuerst zu prüfende Stellen

Die Hauptabläufe sind inzwischen live verifiziert (siehe „Status" oben). Was tatsächlich
ungetestet bleibt, sind ein paar schmale, selten durchlaufene Details, im Quelltext
direkt mit `UNVERIFIED` markiert:

1. **Die genaue Parametersignatur von `IModelDocExtension.SaveAs`** für den
   `.step`/`.pdf`-Export (`UploadStepAttachment`/`UploadPdfAttachment`) — aus der
   SolidWorks-API-Dokumentation geschrieben; falls SolidWorks einen Argumentfehler
   meldet, im VBA-Editor (F1 auf `SaveAs`) die genaue Signatur der installierten Version
   prüfen.
2. **Der Pfad „im selben Format speichern"** für ein Dokument, das in dieser Sitzung
   bereits einmal gespeichert wurde (überspringt den vollständigen Speichern-unter-
   Dialog) — der übliche Fall „einfach erneut speichern und das Makro erneut ausführen"
   ist gut durchgeprüft, dieser konkrete Zweig weniger.
3. **Der genaue Wert der Konstante `SW_SAVE_AS_SILENT`**, mit der SolidWorks' eigene
   Bestätigungsdialoge während der `SaveAs`-Aufrufe für den STEP/PDF-Export
   unterdrückt werden.

Keiner dieser Punkte hat bisher zu einem gemeldeten Fehlschlag geführt — sie stehen hier
als erste Anlaufstelle, falls jemals etwas SolidWorks-versionsspezifisches beim Export
oder Speichern schiefgeht.

## Einschränkungen (bewusst außerhalb des Umfangs dieser Version)

- `EasyPDMDownload.bas`: versucht nicht, eine BESTIMMTE ältere Revision
  herunterzuladen — zielt immer auf die aktuelle. Alle Dateien (Haupt- +
  Komponentendateien) landen flach in einem Ordner, ohne die Stücklistenstruktur als
  Unterordner nachzubilden.
- Kein Äquivalent zum FreeCAD-„Zielordner für lokale Kopien" — `RenameAndUpload`
  kopiert/sendet die Datei direkt in den PDM-Speicher, hinterlässt keine umbenannte
  Kopie neben den Arbeitsdateien des Benutzers.
- Das Anmeldepasswort wird nicht maskiert (einfache `InputBox`, ohne eigenes
  `UserForm`).
- Der Zielordner für den Download ist eine einfache `InputBox` mit einem Pfad als Text,
  kein System-Dateibrowser.
- Das Kopieren/Registrieren/Herunterladen einer Datei über `storage/` setzt voraus, dass
  dieser Ordner im Dateisystem dieser Maschine sichtbar ist — genau wie bei den
  FreeCAD-Makros.
