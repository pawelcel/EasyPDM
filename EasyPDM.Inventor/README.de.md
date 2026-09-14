# EasyPDM — Autodesk-Inventor-Makros

[English](README.md) | [Polski](README.pl.md) | **Deutsch**

Zwei `.bas`-Makros, jedes vollständig eigenständig (separate Datei, separates
VBA-Modul, keine Abhängigkeiten untereinander außer einer gemeinsamen Stelle in der
Windows-Registrierung für die Anmeldesitzung) — die Inventor-Gegenstücke zu
`EasyPDM.SolidWorks/EasyPDMUpload.bas` und `EasyPDMDownload.bas`:

- **`EasyPDMUpload.bas`** — sendet das aktive Inventor-Dokument an EasyPDM. Die Auswahl
  von Projekt/neu-oder-vorhanden/duplizieren/Elementeigenschaften erfolgt im Browser
  (dasselbe Muster wie bei den SolidWorks- und FreeCAD-Makros), mit einer Ausnahme —
  siehe "Unterschiede zu den SolidWorks-Makros" unten.
- **`EasyPDMDownload.bas`** — lädt ein Teil/eine Baugruppe aus EasyPDM (zusammen mit
  allen Komponenten der Baugruppe) herunter und öffnet es/sie in Inventor; die Auswahl,
  WELCHES Element, erfolgt ebenfalls im Browser.

## Status

**Noch NICHT live getestet** — in dieser Umgebung ist keine Autodesk-Inventor-
Installation verfügbar. Im Gegensatz zu den SolidWorks-Makros (die mehrere Runden von
Live-Tests auf echtem SolidWorks 2026 durchlaufen haben) wurde dieser Port daher nur
statisch verifiziert: reiner ASCII-Quellcode, ausgeglichene `Sub`/`Function`/`If`/`For`/
`Do`/`Select Case`-Blöcke, keine doppelten Prozedurnamen, korrekte CRLF-Zeilenenden. Die
grundsätzliche Struktur des Codes — JSON-Parsing, HTTP, der Browser-Ticket-Ablauf,
Anmeldung, Revisions-Namenskonvention — ist identisch mit den bereits verifizierten
SolidWorks-Makros und birgt kein Inventor-spezifisches Risiko. Die Teile, die auf einer
echten Inventor-Installation wirklich bestätigt werden müssen, sind der schmale Satz von
Inventor-Automation-API-Aufrufen, die unten unter "Bekannte Risiken / zuerst zu prüfende
Stellen" aufgeführt sind — bitte diesen Abschnitt sowie die Logdatei des Makros lesen,
bevor Sie sich im produktiven Einsatz darauf verlassen.

## Unterschiede zu den SolidWorks-Makros

Dies sind **Ports**, keine Neuentwürfe — dieselbe Architektur wie
`EasyPDM.SolidWorks/EasyPDMUpload.bas`/`EasyPDMDownload.bas`, mit denselben Sub-/
Function-Namen, wo Inventors eigene API dies zulässt, damit sich die beiden
Dateifamilien leicht nebeneinander vergleichen lassen. Nur die tatsächlichen CAD-
API-Aufrufe unterscheiden sich:

- **Dokumenttyp-Erkennung**: `TypeName(oDoc)` (`"PartDocument"`/`"AssemblyDocument"`/
  `"DrawingDocument"`) statt SolidWorks' `GetType()` verglichen mit fest codierten
  `swDocumentTypes_e`-Werten — bewusst gewählt, statt Inventors `DocumentTypeEnum`-
  Ganzzahlen zu erraten, die ohne echte Installation nicht bestätigt werden können.
- **Custom Properties → iProperties**: `oDoc.PropertySets.Item("Inventor User Defined
  Properties")` statt SolidWorks' `CustomPropertyManager` — siehe "Bekannte Risiken" für
  den genauen Namen des Eigenschaftssatzes, der bei älteren Inventor-Versionen
  angepasst werden muss.
- **Baugruppenbaum-Durchlauf**: `AssemblyDocument.ComponentDefinition.Occurrences` (nur
  Komponenten der obersten Ebene, derselbe Umfang wie SolidWorks' `GetComponents(True)`),
  `occ.Suppressed` (ein einfacher Boolean — einfacher als SolidWorks' undurchsichtiges
  `GetSuppression2()`-Enum), `occ.Definition.Document`, `occ.Name`.
- **Kein Äquivalent zu `ResolveAllLightWeightComponents`** — Inventor kennt kein
  identisches Konzept "leichte Komponenten pro Baugruppe auflösen", daher wird dieser
  Schritt einfach ausgelassen (mit einem erklärenden Kommentar an der Stelle, wo er
  gestanden hätte), statt einen nicht existierenden API-Aufruf zu erraten.
- **Zeichnungsansichten**: verschachtelte Schleife `For Each oSheet In drawDoc.Sheets:
  For Each oView In oSheet.DrawingViews` statt SolidWorks' flachem `GetFirstView`/
  `GetNextView`-Durchlauf; `view.ReferencedDocumentDescriptor.ReferencedDocument` statt
  `view.ReferencedDocument`.
- **STEP/PDF-Export**: Inventors `TranslatorAddIn`-Mechanismus (`ApplicationAddIns.
  ItemById`, `TranslationContext`/`NameValueMap`/`DataMedium`, `SaveCopyAs`) statt
  SolidWorks' `IModelDocExtension.SaveAs` — siehe "Bekannte Risiken" für die
  Translator-CLSIDs, die UNBESTÄTIGT sind.
- **Dokument öffnen**: `InvApp.Documents.Open(path, Visible:=True)` (einfachere
  Signatur, Fehler über COM-Ausnahme) statt SolidWorks' `OpenDoc6` (das ein explizites
  Dokumenttyp-Argument benötigt und Fehler über `ByRef`-Parameter zurückgibt).
- **Dateierweiterungen**: `.ipt`/`.iam`/`.idw` statt `.sldprt`/`.sldasm`/`.slddrw`.

Alles andere — der Browser-Ticket-Ablauf, JSON-Parsing, HTTP-Schicht, Anmeldung,
Revisions-Namenskonvention, das Verhalten der STEP/PDF-Kontrollkästchen, die
"bereits verknüpft"-Erkennung samt ihres Save-Copy-As-Vorbehalts, Browser-Tickets pro
neuer Baugruppenkomponente, die Erkennung entfernter Komponenten, STEP/PDF überall
optional — ist unverändert gegenüber den SolidWorks-Makros; die vollständige
Verhaltensbeschreibung in deren eigenem README gilt hier identisch.

## Was `EasyPDMUpload.bas` tut

1. **Anmeldung** — beim ersten Lauf (oder wenn die gespeicherte Sitzung abgelaufen/
   ungültig geworden ist) wird nach API-Adresse, Benutzername und Passwort gefragt. Das
   Sitzungstoken wird in der Windows-Registrierung gespeichert
   (`HKEY_CURRENT_USER\Software\VB and VBA Program Settings\EasyPDM`) — spätere Läufe
   (auch nach einem Inventor-Neustart, auch von `EasyPDMDownload.bas` aus, und auch mit
   den SolidWorks/FreeCAD-Makros geteilt — die Sitzung wird von allen gemeinsam genutzt)
   fragen nicht erneut nach der Anmeldung, solange die Sitzung gültig ist (30 Tage).
2. **Speichert das aktive Dokument**, falls es noch nicht gespeichert wurde.
3. **Wenn das aktive Dokument eine Baugruppe ist**: erkennt den Komponentenbaum
   (`AssemblyDocument.ComponentDefinition.Occurrences`, rekursiv). Eine Zusammenfassung
   vor dem Durchlauf (`MsgBox`) listet jede gefundene Komponente auf und markiert bereits
   verknüpfte mit ihrer Ziel-PDM-Elementnummer und ihrem Dateinamen. Nach Bestätigung
   wird zuerst mit den Blättern durchlaufen (dieses Dokument zuletzt); für jede NOCH
   NICHT verknüpfte Komponente wird ein eigenes Browser-Ticket geöffnet (dieselbe Auswahl
   Neues Element/Duplikat/An vorhandenes anhängen wie beim übergeordneten Dokument — ein
   Browser-Tab nach dem anderen, mit einer nativen "OK klicken, um fortzufahren"-MsgBox
   vor jedem weiteren Tab, da nur ein programmatisch geöffneter Browser-Tab pro
   Makrolauf zuverlässig den Windows-Vordergrundfokus erhalten kann). Neu erstellte
   Komponenten erhalten ihren eigenen STEP/PDF-Export (gemäß der eigenen
   Kontrollkästchen-Wahl dieser Komponente im Browser) und eine `EasyPDM_ItemId`-
   iProperty und werden automatisch unter ihrem übergeordneten Element in der
   Stücklistenstruktur angehängt. Bereits verknüpfte Komponenten werden nur referenziert,
   nie erneut hochgeladen, unabhängig vom Status. Seit dem letzten Upload aus der
   Baugruppe entfernte Komponenten werden ebenfalls gemeldet, mit einer Bestätigung vor
   dem Entfernen ihrer PDM-Verknüpfung (die Elemente selbst werden nie gelöscht, nur ihre
   Zuordnung unter diesem speziellen übergeordneten Element).
4. Prüft die **iProperties** des Hauptdokuments:
   - **Bereits verknüpft** (hat eine gespeicherte `EasyPDM_ItemId`) — fragt lokal nach
     Zustimmung, die aktuelle Version als neue Revision anzuhängen, ohne den Browser zu
     öffnen, gefolgt von zwei weiteren nativen Ja/Nein-Abfragen für STEP-Export
     (Standard: Ja) und PDF-Export (Standard: Nein).
   - **Noch nicht verknüpft** — öffnet den System-Browser (bereits angemeldet, Token-zu-
     Cookie-Brücke) auf dem Popup "ausstehende Anfrage von einem CAD-Makro", mit drei
     dort zu treffenden Optionen: **Neues Element**, **Duplikat** oder **An vorhandenes
     anhängen** — dieselben Felder und dieselbe Elementarten-Logik wie bei den
     SolidWorks/FreeCAD-Makros. Das Makro wartet (fragt alle ~2s ab, Limit 10 Minuten,
     Escape bricht ab, Fortschritt wird in Inventors Statusleiste angezeigt) und fährt
     automatisch fort, sobald die Wahl im Browser bestätigt wurde.
   - **Vorhandenes Element mit Status "Freigegeben"**: fragt nach Zustimmung zu einer
     neuen Revision und einem optionalen Kommentar.
   - **Vorhandenes Element mit Status "In Prüfung"**: das Anhängen wird mit einer
     nativen Fehlermeldung hart blockiert.
5. **Kopiert** die aktuelle Dokumentdatei in das PDM unter dem Namen
   `Nummer (Name).REVISION.Erweiterung` (dieselbe Konvention wie in der Webanwendung und
   den anderen CAD-Makros). Die lokale Datei wird NICHT angefasst. Wenn der PDM-
   Speicherort von diesem Rechner aus sichtbar ist, geht die Kopie direkt dorthin und
   wird ohne einen zweiten HTTP-Upload registriert; andernfalls ein normaler HTTP-Upload
   (Fallback, über `WinHttp.WinHttpRequest.5.1`, genau wie bei den SolidWorks-Makros).
6. Wenn STEP/PDF-Export aktiviert ist: exportiert über Inventors `TranslatorAddIn`-
   Mechanismus in eine temporäre `.step`/`.pdf`-Datei und lädt sie als Anhang mit der
   Rolle `"step"`/`"pdf"` hoch, wobei der vorherige Anhang derselben Rolle ersetzt wird.
   Ein Exportfehler bricht den Rest der Operation NICHT ab.
7. Speichert `EasyPDM_ItemId`/`EasyPDM_ItemNumber` in den iProperties des Dokuments und
   zeigt eine Bestätigung an.

## Was `EasyPDMDownload.bas` tut

1. **Anmeldung** — wie oben (Sitzung mit `EasyPDMUpload.bas` geteilt).
2. **Die Auswahl des herunterzuladenden Elements erfolgt im Browser** — dasselbe Popup
   "ausstehende Anfrage von einem CAD-Makro" wie beim Hochladen, nur sofort mit der
   Suche. Das Einzige, was eine lokale `InputBox` bleibt, ist der **Zielordner**
   (standardmäßig der zuletzt verwendete — dieselbe Einstellung wie der Zielordner von
   `EasyPDMUpload.bas`).
3. Bei einer Baugruppe: lädt auch **alle ihre Komponenten rekursiv** in DENSELBEN Ordner
   wie die Hauptdatei.
4. Für jede Datei: überspringt sie, wenn der Ordner bereits eine Datei mit exakt
   demselben Namen und derselben Größe enthält; fragt vor dem Ersetzen einer älteren
   lokalen Revision durch eine neuere vom Server.
5. Am Ende wird die Hauptdatei (die ausgewählte) in Inventor geöffnet
   (`InvApp.Documents.Open`) — Komponentendateien bleiben nur auf der Festplatte,
   Inventor löst die Baugruppenreferenzen zu ihnen selbst auf.

Woher die heruntergeladenen Dateien stammen: EasyPDM speichert die aktuelle CAD-Datei
als Anhang, und frühere Revisionen bleiben als separate Anhänge daneben erhalten —
`EasyPDMDownload.bas` erkennt die von `EasyPDMUpload.bas` festgelegte
Namenskonvention, um beim Anhang der AKTUELLEN Revision zu landen.

## Wie man prüft, ob es funktioniert hat

1. **Die Meldung am Ende** — nach einem erfolgreichen Vorgang zeigt das Makro ein
   Fenster mit einer Zusammenfassung. Ein Fenster mit "Error: ..." bedeutet, dass etwas
   schiefgegangen ist.
2. **Die Webanwendung** — ins Projekt (oder "Gesamte Datenbank") gehen, das Element
   anhand der Nummer aus der Meldung suchen und prüfen, ob eine Datei angehängt ist und
   die Eigenschaften stimmen.
3. **Das Makro-Log** — jeder Lauf hängt einen detaillierten, zeitgestempelten Eintrag an
   eine Textdatei an:

   ```
   %TEMP%\EasyPDM_inventor_macro.log            <- EasyPDMUpload.bas
   %TEMP%\EasyPDM_inventor_download_macro.log   <- EasyPDMDownload.bas
   ```

   (`%TEMP%` in die Adressleiste des Windows-Explorers einfügen, um dorthin zu
   gelangen). Dies ist die erste Stelle, die man prüfen sollte, wenn etwas nicht
   funktioniert — der Pfad dazu wird auch im Fehler-/Erfolgsfenster am Ende angehängt.

## Installation

Wie SolidWorks hat auch Inventor kein reines Textformat für Makros — Makros sind
VBA-Projekte. `.bas` ist das Standard-Export-/Importformat für ein VBA-**Modul**:

1. Autodesk Inventor → Register **Extras** → Bereich **Makro** → **Visual-Basic-
   Editor** (oder Alt+F11).
2. Im VBA-Editor den Projekt-Explorer öffnen (Strg+R, falls nicht sichtbar). Inventor
   zeigt für jedes geöffnete Dokument einen eigenen Eintrag (ein **Dokumentprojekt**, in
   genau dieser einen Datei eingebettet) sowie ein separates **externes/globales
   Projekt**, das von allen Dokumenten gemeinsam genutzt wird, unabhängig davon, welches
   gerade aktiv ist. **In das externe/globale Projekt importieren, niemals in das
   eingebettete Projekt eines Dokuments.** Ein Live-Test unter Inventor 2027.1 zeigte,
   dass sowohl das Schreiben eigener iProperties als auch der STEP/PDF-Export gegen ein
   Dokument fehlschlugen, sobald das Makro in das in DIESEM SELBEN Dokument eingebettete
   Projekt importiert wurde (allgemeiner COM-Fehler mit `Err.Source = "DocumentProject"`)
   — passend zu einer fehlerhaften Behandlung durch Inventor, wenn ein Dokument durch sein
   eigenes eingebettetes Makro verändert wird. Der Import in das externe/globale Projekt
   ist zudem schlicht die richtige Wahl für ein Makro für den allgemeinen Gebrauch wie
   dieses — so bleibt es unabhängig vom aktiven Dokument verfügbar.
3. **Datei → Datei importieren...** → `EasyPDMUpload.bas` oder `EasyPDMDownload.bas`
   auswählen, während im Projekt-Explorer das externe/globale Projekt ausgewählt ist.
4. Ausführen über Register **Extras** → Bereich **Makro** → **Makros...** →
   `main` auswählen → Ausführen (oder F5 im VBA-Editor, **mit dem Cursor innerhalb von
   `Sub main()`**).
5. Ein separates `Sub Logout` (in beiden Modulen) meldet von EasyPDM ab — es kann an
   eine eigene Symbolleisten-Schaltfläche/Tastenkombination gebunden werden.

Die API-Adresse (Standard `http://localhost:5000/api`) wird nach einmaliger Eingabe bei
der Anmeldung automatisch gespeichert — auch mit den SolidWorks/FreeCAD-Makros geteilt.

## Bekannte Risiken / zuerst zu prüfende Stellen

Dieser Port wurde ausschließlich anhand dokumentierten Verhaltens der Inventor-
Automation-API geschrieben, ohne Zugriff auf eine echte Installation — alles Folgende
ist im Quellcode direkt mit `UNVERIFIED` markiert und sollte die erste Stelle sein, die
man prüft, wenn beim ersten echten Lauf etwas schiefgeht:

1. **`InvApp.StatusBarText`** als beschreibbare Eigenschaft (von `WaitForTicket`
   verwendet, um den Fortschritt beim Warten auf den Browser anzuzeigen) — in
   `On Error Resume Next` eingehüllt, sodass eine falsche Annahme zu "kein
   Statusleistentext" statt zu einem Absturz führt, aber eine Bestätigung ist sinnvoll.
2. **Translator-CLSIDs** für den STEP/PDF-Export
   (`ExportViaTranslator`/`UploadStepAttachment`/`UploadPdfAttachment`) — öffentlich
   dokumentierte Werte, hier nicht bestätigt:
   - STEP: `{90AF7F40-0C01-11D5-8E83-0010B541CD80}`
   - PDF: `{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}`
   Ebenso `oContext.Type = 2` (beabsichtigt als `kFileBrowseIOMechanism`) im selben
   Exportpfad.
3. **`PropertySets.Item("Inventor User Defined Properties")`** — der genaue Name des
   Eigenschaftssatzes; manche Inventor-Versionen verwenden möglicherweise einen anderen
   Namen (z. B. ohne das Präfix "Inventor "). Der Schreibpfad hat bereits einen
   Fallback von "setzen" auf "hinzufügen", wenn eine Eigenschaft noch nicht existiert,
   aber ein falscher Satzname würde beides scheitern lassen.
4. **`oDoc.Save`/`oDoc.SaveAs(path, False)`** genaues Verhalten und Fehlersignalisierung
   (COM-Ausnahme, nicht `ByRef`-Ausgabeparameter wie SolidWorks' `Save3`) — die
   grundsätzliche Form ist gut dokumentiert, aber noch nicht an einer echten Datei
   erprobt.
5. **`oDoc.DisplayName`** als Äquivalent zu SolidWorks' `GetTitle()`.
6. **`view.ReferencedDocumentDescriptor.ReferencedDocument`** — der genaue
   Eigenschaftspfad zur Auflösung des referenzierten Modells einer Zeichnungsansicht;
   erwartete Rückgabe von `Nothing`, wenn das Modell aktuell nicht geöffnet ist,
   dieselbe Einschränkung wie `view.ReferencedDocument` im SolidWorks-Makro.
7. **Kein Äquivalent zu `ResolveAllLightWeightComponents`** — bewusst ausgelassen
   (siehe "Unterschiede zu den SolidWorks-Makros"); sollte sich herausstellen, dass
   Inventor eine eigene Falle bei leichten Komponenten hat, müsste sie hier ergänzt
   werden.

## Einschränkungen (bewusst außerhalb des Umfangs dieser Version)

- `EasyPDMDownload.bas`: versucht nicht, eine BESTIMMTE ältere Revision
  herunterzuladen — zielt immer auf die aktuelle. Alle Dateien (Haupt- + Komponenten)
  landen flach in einem Ordner, ohne die Stücklistenstruktur als Unterordner
  nachzubilden.
- Das Anmeldepasswort wird nicht maskiert (einfache `InputBox`, kein dediziertes
  `UserForm`).
- Der Zielordner beim Download ist eine einfache `InputBox` mit einem Pfad als Text,
  kein systemeigener Datei-Browser.
- Das Kopieren/Registrieren/Herunterladen einer Datei über `storage/` setzt voraus,
  dass dieser Ordner im Dateisystem dieses Rechners sichtbar ist — genau wie bei den
  SolidWorks/FreeCAD-Makros.
