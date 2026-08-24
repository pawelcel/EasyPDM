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

**Die frühere Version (ohne Browser, native `InputBox`/`MsgBox` zur Wahl von
Projekt/Element/Art) wurde live verifiziert** (SolidWorks 2026, 2026-08-20) — siehe
„Historie behobener Probleme" unten, weiterhin aktuell für die gemeinsam genutzte
Anmelde-/JSON-/HTTP-Infrastruktur, die die aktuelle Version vollständig beibehält.

**Die aktuelle Version (Browser-Muster + STEP-Export + automatische Erkennung des
Baugruppenbaums) ist UNVERIFIZIERT** — geschrieben ohne Zugriff auf SolidWorks/einen
VBA-Compiler in der Umgebung, in der sie entstanden ist (im Gegensatz zu den
FreeCAD-Makros, wo `py_compile` eine echte Syntaxverifizierung lieferte, ist hier die
einzige Verifizierung eine manuelle Code-Durchsicht). Erfordert einen vollständigen Test
auf einem echten SolidWorks vor dem Produktiveinsatz — siehe „Bekannte Risiken" unten, wo
die unsichersten Stellen genau angegeben sind.

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
  Makro den Browser überhaupt NICHT — es fragt lokal nur nach der Zustimmung zu einer
  neuen Revision, genau wie vor dieser Änderung. SolidWorks kennt das Element dann mit
  100%iger Sicherheit, sodass der Browser hier nichts beitragen würde; FreeCAD geht immer
  zum Browser, weil es keinen so zuverlässigen lokalen Mechanismus hat.
- **Erkennung eines bereits gesendeten Dokuments** (`EasyPDMUpload.bas`): NICHT über ein
  Label/einen Dateinamen (SolidWorks hat kein Äquivalent zum freien Label von FreeCAD) —
  über die **Custom Properties** des Dokuments (`EasyPDM_ItemId`, `EasyPDM_ItemNumber`),
  die nach einem erfolgreichen Upload in der Datei selbst gespeichert werden. Das ist
  tatsächlich ein **dauerhafterer** Ansatz als in FreeCAD — er funktioniert auch in einer
  NEUEN SolidWorks-Sitzung, und (neu) wird jetzt auch für JEDE Baugruppenkomponente
  einzeln verwendet (siehe „Erkennung des Baugruppenbaums" unten), nicht nur für das
  Hauptdokument.
- **Neue Baugruppenkomponenten werden über eine Folge von `InputBox`-Abfragen gesammelt,
  nicht über den Browser** — derselbe Grund wie beim nativen Komponenten-Dialog in
  FreeCAD (N Browser-Tabs für N neue Baugruppenkomponenten wären eine schlechtere UX als
  ein natives Prompt pro Komponente), hier umso mehr gerechtfertigt durch das
  vollständige Fehlen von `UserForm` in dieser Datei überhaupt.
- **Der STEP-Export für Pfade ohne Browser exportiert immer** (bereits verknüpftes
  Dokument; automatisch erkannte Baugruppenkomponenten) — dort gibt es kein
  Browser-Formular, in dem eine Checkbox sitzen könnte. Nur der Pfad über das Ticket
  (neues Element/Duplikat/an vorhandenes anhängen) hat die STEP-Checkbox im Browser,
  genau wie bei FreeCAD.

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
3. **Wenn das aktive Dokument eine Baugruppe ist**: Es erkennt deren Komponentenbaum
   (`IAssemblyDoc.GetComponents`, rekursiv) und fragt, ob automatisch zusammen mit ihr
   alle Komponenten gesendet werden sollen, die NOCH NICHT mit dem PDM verknüpft sind
   (erkannt anhand der Custom Properties an JEDER Komponente einzeln, siehe unten) —
   zuerst die Blätter, dieses Dokument zuletzt. Für jede neue Komponente: eine kurze
   Folge von `InputBox`-Abfragen (Projekt → Typ → Name, und bei Teilen zusätzlich die Art
   und deren abhängige Felder — dieselben Regeln wie unten; eine Baugruppe hat keine Art,
   nur eine optionale Masse), NICHT der Browser (siehe „Unterschiede zu den
   FreeCAD-Makros"). Neu erstellte Komponenten erhalten sofort einen STEP-Export und
   einen eigenen `EasyPDM_ItemId`-Eintrag und werden automatisch unter ihrem Elternelement
   in der Stücklistenstruktur eingehängt.
4. Prüft die **Custom Properties** des Hauptdokuments:
   - **Bereits verknüpft** (hat eine gespeicherte `EasyPDM_ItemId`) — fragt lokal nach der
     Zustimmung, die aktuelle Version als neue Revision anzuhängen, ohne den Browser zu
     öffnen (siehe „Unterschiede zu den FreeCAD-Makros"). Der STEP-Export erfolgt immer.
   - **Noch nicht verknüpft** — öffnet den System-Browser (bereits angemeldet,
     Token→Cookie-Brücke) auf dem Popup „ausstehende Anfrage von einem CAD-Makro" mit
     drei DORT zu wählenden Optionen: **Neues Element** (Projekt, optional ein
     Elternelement, Typ, Name — bei Teilen zusätzlich die Art und deren abhängige Felder:
     Gefertigt → Material; Zugekauft → Hersteller/Bestellnummern/Masse; Normteil →
     Material/Norm; Kundenteil → keine zusätzlichen Felder; **eine Baugruppe hat
     überhaupt keine Art** — nur eine optionale Masse — plus eine
     STEP-Export-Checkbox), **Duplizieren** (verweist auf ein vorhandenes Element,
     kopiert dessen Eigenschaften in ein neues, ohne Dateien) oder **An vorhandenes
     anhängen** (Suche über die gesamte Datenbank + dieselbe STEP-Checkbox). Das Makro
     wartet (fragt etwa alle 2s ab, Limit 10 Minuten, Escape bricht ab, Fortschritt in
     der SolidWorks-Statusleiste) und setzt automatisch fort, sobald die Wahl im Browser
     bestätigt wurde.
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
   hat), ein gewöhnlicher HTTP-Upload (Fallback ohne Erhalt der Revisionshistorie) —
   **das ist kein Fehler**, sondern eine korrekt funktionierende Sicherheitsmaßnahme.
6. Wenn der STEP-Export aktiviert ist (Checkbox im Browser, oder immer bei Pfaden ohne
   Browser — siehe Punkt 4): exportiert es die sichtbare Geometrie in eine temporäre
   `.step`-Datei (`IModelDocExtension.SaveAs`) und lädt sie als Anhang mit der Rolle
   `"step"` hoch, wobei der vorherige Anhang derselben Rolle ersetzt wird — versorgt die
   dauerhafte 3D-Vorschau in der Web-Anwendung. Ein Exportfehler (z. B. keine sichtbare
   Geometrie) bricht den Rest des Vorgangs NICHT ab.
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

## Historie behobener Probleme (aus dem ersten echten Test von `EasyPDMUpload.bas`)

Gefunden und behoben am 2026-08-20 auf einem echten SolidWorks 2026 — hier als
dokumentiertes Beispiel dafür belassen, worauf bei der Verifizierung von
`EasyPDMDownload.bas` zu achten ist (das dieselbe, bereits korrigierte Infrastruktur
nutzt, aber auch EIGENEN, noch nicht überprüften Code hat — Regex, rekursives
Herunterladen, `OpenDoc6`):

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
   falsch") beim Upload über den HTTP-Fallback. Behoben durch Verpacken der Bytes in
   einen binären `ADODB.Stream` und Senden des Streams anstelle des Arrays.

## Bekannte Risiken / zuerst zu prüfende Stellen

Noch ungetestete Abschnitte — geschrieben ohne Zugriff auf SolidWorks/einen
VBA-Compiler, sodass nicht einmal die SYNTAX automatisch überprüft wurde (im Gegensatz
zu den FreeCAD-Makros, wo `py_compile` eine echte Verifizierung lieferte). Vorgeschlagene
Testreihenfolge: ein gewöhnliches, noch nicht verknüpftes Teil → dasselbe Teil erneut
(nativer Pfad) → Anhängen/Duplizieren im Browser → eine Baugruppe mit neuen Komponenten
→ Escape/Timeout während des Wartens → `EasyPDMDownload.bas`.

**Von beiden Dateien gemeinsam genutzt (neu in dieser Runde):**
1. **`WaitForTicket`** — eine `Sleep`/`DoEvents`/`GetAsyncKeyState(VK_ESCAPE)`-Schleife +
   Abfrage von `GET /create-tickets/{ticket}` etwa alle 2s, `swApp.Frame.SetStatusBarText`
   zur Fortschrittsanzeige. Der Mechanismus (Win32-API über `Declare`, kein `UserForm`)
   hat sonst nirgendwo in diesem Repo ein direktes Gegenstück — zu prüfen: ob Escape die
   Schleife tatsächlich unterbricht, ob sich die Statusleiste korrekt aktualisiert/leert,
   ob SolidWorks während der gesamten Wartezeit reaktionsfähig bleibt (`DoEvents`).
2. **`Scriptlet.TypeLib.Guid`** (`NewGuid`) und manuelles `UrlEncode`
   (Prozent-Kodierung über `ADODB.Stream` nach UTF-8 + manuelles Umgehen des BOM) —
   beides Standard-, dokumentierte Workarounds für das Fehlen eines eingebauten
   GUID-/URL-Encoders in VBA, aber in diesem konkreten Einsatz ungetestet (ein
   Dokumentname mit polnischen Zeichen in `name=` sollte gesondert geprüft werden).

**`EasyPDMUpload.bas` (neu in dieser Runde):**
3. **`IModelDocExtension.SaveAs` nach `.step`** (`UploadStepAttachment`) — die genaue
   Anzahl/Bedeutung der Parameter (`SaveAsVersion`/`SaveAsOptions`) wurde aus der
   API-Dokumentation geschrieben, NICHT getestet — falls SolidWorks einen Argumentfehler
   meldet, im VBA-Editor (F1 auf `SaveAs`) die genaue Signatur der installierten Version
   prüfen.
4. **`IAssemblyDoc.GetComponents`/`IComponent2.GetModelDoc2`/`Name2`/`GetTitle`**
   (`VisitAssemblyComponents`/`ProcessAssemblyTree`) — geht davon aus, dass ein spät
   gebundenes `ModelDoc2` einer Baugruppe direkt mit `IAssemblyDoc`-Methoden aufgerufen
   werden kann (ein typisches, dokumentiertes Muster in SolidWorks-Makros, hier aber
   ungetestet). Unterdrückte/nicht aufgelöste/virtuelle Komponenten werden durch die
   Prüfung von `GetModelDoc2() Is Nothing`/einem leeren `GetPathName()` übersprungen,
   statt durch einen festen Unterdrückungs-Enum-Wert — zu bestätigen, dass dies
   tatsächlich ausreicht.

**`EasyPDMDownload.bas` (unverändert gegenüber der vorherigen Runde):**
5. **`swApp.OpenDoc6`** — die Signatur (`FileName, Type, Options, Configuration, Errors,
   Warnings`) ist eine gut dokumentierte Standard-API, wurde aber nicht live getestet.
   `Options = 0` (keine besonderen Flags) sollte ein sicherer Standardwert sein.
6. **`VBScript.RegExp`** (`NewRevisionRegex`) — wird verwendet, um die
   Dateinamenskonvention (`nummer (name).REVISION.erweiterung`) bei der Erkennung der
   aktuellen Revision und älterer lokaler Kopien zu erkennen. Ein Standard-, stabiler
   Mechanismus, aber in diesem konkreten Einsatz ungetestet.
7. **Rekursives Herunterladen von Baugruppenkomponenten**
   (`DownloadChildrenRecursive`) — analog zu `_download_children_recursive` in
   `EasyPDMDownload.FCMacro` (einschließlich derselben Antwortform von
   `GET /items/{id}/children`: das Element verschachtelt unter dem Schlüssel `"item"`),
   aber auf der SolidWorks-Seite ungetestet.
8. **`EnsureDirectory`** — eine manuelle Implementierung zum Erstellen verschachtelter
   Ordner (VBAs `MkDir` erstellt nur eine Ebene auf einmal, im Gegensatz zu
   `os.makedirs`) — einfache Logik, aber es lohnt sich, sie an einem Pfad mit mehreren
   gleichzeitig nicht existierenden Ebenen zu prüfen.

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
