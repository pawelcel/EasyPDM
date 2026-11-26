<p align="center"><img src="EasyPDM.Web/public/favicon.svg" alt="EasyPDM logo" width="110"></p>

# EasyPDM — PDM-System für CAD-Dateien

[English](README.md) | [Polski](README.pl.md) | **Deutsch**

[![Release](https://img.shields.io/github/v/release/pawelcel/EasyPDM?label=release)](https://github.com/pawelcel/EasyPDM/releases/latest) [![License: MIT](https://img.shields.io/github/license/pawelcel/EasyPDM)](LICENSE) [![Tests](https://img.shields.io/github/actions/workflow/status/pawelcel/EasyPDM/build.yml?branch=main&label=tests)](https://github.com/pawelcel/EasyPDM/actions/workflows/build.yml) [![Wiki](https://img.shields.io/badge/Doku-Wiki-blue)](https://github.com/pawelcel/EasyPDM/wiki) [![YouTube](https://img.shields.io/badge/YouTube-Videoserie-FF0000?logo=youtube&logoColor=white)](https://www.youtube.com/playlist?list=PLe2vSF7-ieUA)

**Ein kostenloses, selbst gehostetes PDM für kleine Konstruktionsteams.** Teilenummern,
Revisionen, Status, eine fertige Stückliste und Schriftfelder, die sich selbst ausfüllen —
direkt aus SolidWorks, FreeCAD und SolidCraft. Schluss mit `Halter_v3_FINAL_WIRKLICH.SLDPRT`
auf dem Netzlaufwerk und der Frage „welche Version ist die aktuelle?“.

**[⬇ Neueste Version herunterladen](https://github.com/pawelcel/EasyPDM/releases/latest)** ·
**[Live-Demo ausprobieren](https://easypdmdemo.celmer.info/)** (Anmeldung `admin` / `admin`, täglich zurückgesetzt) ·
**[Videos ansehen](https://www.youtube.com/playlist?list=PLe2vSF7-ieUA)** (auf Englisch)

## Warum EasyPDM

- **Eine Nummer, eine Historie** — jedes Teil und jede Baugruppe hat eine eigene Nummer,
  Revision, einen Status und ein Protokoll, wer wann was geändert hat.
- **Senden und Holen aus dem CAD-Programm** — ein Klick lädt ein Teil oder eine ganze
  Baugruppe hoch, ein Klick holt sie mit allen Komponenten zurück.
- **Stückliste sofort** — jede Baugruppe listet ihre Komponenten mit Menge, Werkstoff,
  Hersteller und Bestellnummern, bereit für den CSV-Export.
- **Zeichnungen füllen sich selbst aus** — Benennung, Zeichnungsnummer, Revision, Werkstoff,
  Bearbeitungen, Firmenlogo und auf Baugruppenzeichnungen eine Stückliste, nummeriert wie
  die Positionsnummern.
- **Status, Prüfung und Sperren** — in Arbeit → in Prüfung → freigegeben, und niemand
  überschreibt, woran jemand anderes arbeitet.
- **Ihres, auf Ihrem eigenen Rechner** — kein Cloud-Konto, kein Abo, Open Source (MIT).

## Funktioniert mit Ihrem CAD

| CAD-Programm | Was Sie bekommen |
|---|---|
| **SolidWorks** | Add-in mit EasyPDM-Registerkarte und Dokumentbereich, Zeichnungsblätter — [Repository](https://github.com/pawelcel/EasyPDM-SolidWorks-Addon) · [Wiki](https://github.com/pawelcel/EasyPDM-SolidWorks-Addon/wiki) |
| **FreeCAD** | EasyPDM-Arbeitsbereich mit Dokumentbereich, Zeichnungsblätter — [Repository](https://github.com/pawelcel/EasyPDM-FreeCAD-Addon) · [Wiki](https://github.com/pawelcel/EasyPDM-FreeCAD-Addon/wiki) |
| **SolidCraft** | Plugin und Begleit-App — [Repository](https://github.com/pawelcel/EasyPDM-SolidCraft-Addon) · [Wiki](https://github.com/pawelcel/EasyPDM-SolidCraft-Addon/wiki) |
| Autodesk Inventor | Upload- und Download-Makros, nicht mehr weiterentwickelt — [Details](EasyPDM.Inventor/README.de.md) |

Alles andere — die Browser-App, Kataloge, Stückliste — ist gleich, egal womit Sie
konstruieren.

## Schnellstart

1. **Den Server einmal installieren**, auf einem beliebigen Rechner in der Firma, der
   eingeschaltet bleibt:
   - **Windows** — `EasyPDM_Windows_v<Version>.exe` aus der
     [neuesten Version](https://github.com/pawelcel/EasyPDM/releases/latest) ausführen; der
     Assistent erledigt den Rest.
   - **Docker** —
     ```bash
     git clone https://github.com/pawelcel/EasyPDM.git && cd EasyPDM && ./install-easypdm-docker.sh
     ```
   - **Linux ohne Docker** — das `.tar.gz`-Paket aus derselben Version,
     [Schritt für Schritt im Wiki](https://github.com/pawelcel/EasyPDM/wiki/Installation).
2. **`http://localhost:5000` öffnen** (oder die Adresse des Rechners von einem anderen aus)
   und als `admin` / `admin` anmelden — danach dieses Passwort ändern. Ein Beispielprojekt
   wartet schon.
3. **Ihr CAD-Programm anbinden** — das Add-on aus der Tabelle oben installieren und sich
   darin anmelden.

Alle anderen im Team öffnen einfach die Adresse im Browser.

## Mehr erfahren

- 📖 **[Wiki](https://github.com/pawelcel/EasyPDM/wiki)** (auf Englisch, mit Screenshots):
  [Installation](https://github.com/pawelcel/EasyPDM/wiki/Installation) ·
  [EasyPDM verwenden](https://github.com/pawelcel/EasyPDM/wiki/Using-EasyPDM) ·
  [CAD-Integrationen](https://github.com/pawelcel/EasyPDM/wiki/CAD-Integrations) ·
  [Einstellungen](https://github.com/pawelcel/EasyPDM/wiki/Settings) ·
  [Technischer Überblick](https://github.com/pawelcel/EasyPDM/wiki/Technical-Overview)
- ▶️ **[Videoserie](https://www.youtube.com/playlist?list=PLe2vSF7-ieUA)** — am besten mit
  [1. Was ein PDM ist und warum EasyPDM](https://youtu.be/OaqyJQCHkt4) beginnen; die neueste:
  [8. Stückliste und Dokumentation](https://youtu.be/mJW8TSQ7EpU)
- 🛠 **Administratoren und Entwickler** — Architektur, API und Serverdetails in
  [`TECHNICAL.md`](TECHNICAL.md)
- 📝 **Was ist neu** — [`CHANGELOG.md`](CHANGELOG.md)

## Über das Projekt

Ich bin Konstrukteur im Maschinenbau und wusste genau, was mir bei der Arbeit mit
CAD-Dateien fehlte. Den Code habe ich nicht selbst geschrieben: Claude, ein KI-Modell von
Anthropic, hat ihn nach meinen Anforderungen geschrieben. Ich habe EasyPDM für den eigenen
Gebrauch gebaut, und da es funktioniert — warum nicht teilen.

Einen Fehler gefunden oder fehlt etwas? [Issue eröffnen](https://github.com/pawelcel/EasyPDM/issues).
Wenn EasyPDM Ihnen Zeit spart, können Sie mir
[einen Kaffee spendieren](https://buymeacoffee.com/easypdm). Lizenz: [MIT](LICENSE).
