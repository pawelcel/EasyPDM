<p align="center"><img src="EasyPDM.Web/public/favicon.svg" alt="EasyPDM logo" width="110"></p>

# EasyPDM — system PDM dla plików CAD

[English](README.md) | **Polski** | [Deutsch](README.de.md)

[![Release](https://img.shields.io/github/v/release/pawelcel/EasyPDM?label=release)](https://github.com/pawelcel/EasyPDM/releases/latest) [![License: MIT](https://img.shields.io/github/license/pawelcel/EasyPDM)](LICENSE) [![Tests](https://img.shields.io/github/actions/workflow/status/pawelcel/EasyPDM/build.yml?branch=main&label=tests)](https://github.com/pawelcel/EasyPDM/actions/workflows/build.yml) [![Wiki](https://img.shields.io/badge/dokumentacja-wiki-blue)](https://github.com/pawelcel/EasyPDM/wiki) [![YouTube](https://img.shields.io/badge/YouTube-seria_filmów-FF0000?logo=youtube&logoColor=white)](https://www.youtube.com/playlist?list=PLe2vSF7-ieUA)

**Darmowy PDM na własnym serwerze dla małych zespołów konstrukcyjnych.** Numery części,
rewizje, statusy, gotowy BOM i tabelki rysunkowe, które same się wypełniają — prosto
z SolidWorks, FreeCAD i SolidCraft. Koniec z `wspornik_v3_OSTATECZNY_NAPRAWDE.SLDPRT` na
dysku sieciowym i pytaniem „która wersja jest aktualna?”.

**[⬇ Pobierz najnowsze wydanie](https://github.com/pawelcel/EasyPDM/releases/latest)** ·
**[Wypróbuj demo](https://easypdmdemo.celmer.info/)** (login `admin`, hasło `admin`, czyszczone codziennie) ·
**[Obejrzyj filmy](https://www.youtube.com/playlist?list=PLe2vSF7-ieUA)** (po angielsku)

## Dlaczego EasyPDM

- **Jeden numer, jedna historia** — każda Część i każde Złożenie ma własny numer, rewizję,
  status i zapis tego, kto co i kiedy zmienił.
- **Wysyłanie i pobieranie z programu CAD** — jednym kliknięciem wysyłasz część albo całe
  złożenie, jednym kliknięciem pobierasz je z powrotem ze wszystkimi komponentami.
- **BOM od ręki** — każde złożenie ma listę komponentów z ilościami, materiałem,
  producentem i numerami zamówieniowymi, gotową do eksportu do CSV.
- **Rysunki same się wypełniają** — nazwa, numer rysunku, rewizja, materiał, obróbki, logo
  firmy, a na rysunkach złożeń lista części numerowana tak jak balony.
- **Statusy, przeglądy i blokady** — w pracy → do przeglądu → zatwierdzone, i nikt nie
  nadpisze tego, nad czym pracuje ktoś inny.
- **Twoje, na Twoim komputerze** — bez konta w chmurze, bez abonamentu, open source (MIT).

## Współpracuje z Twoim CAD

| Program CAD | Co dostajesz |
|---|---|
| **SolidWorks** | Add-in z zakładką EasyPDM i panelem dokumentu, arkusze rysunkowe — [repozytorium](https://github.com/pawelcel/EasyPDM-SolidWorks-Addon) · [wiki](https://github.com/pawelcel/EasyPDM-SolidWorks-Addon/wiki) |
| **FreeCAD** | Środowisko pracy EasyPDM z panelem dokumentu, arkusze rysunkowe — [repozytorium](https://github.com/pawelcel/EasyPDM-FreeCAD-Addon) · [wiki](https://github.com/pawelcel/EasyPDM-FreeCAD-Addon/wiki) |
| **SolidCraft** | Wtyczka i aplikacja towarzysząca — [repozytorium](https://github.com/pawelcel/EasyPDM-SolidCraft-Addon) · [wiki](https://github.com/pawelcel/EasyPDM-SolidCraft-Addon/wiki) |
| Autodesk Inventor | Makra Upload i Download, nierozwijane — [szczegóły](EasyPDM.Inventor/README.pl.md) |

Cała reszta — aplikacja w przeglądarce, katalogi, BOM — działa tak samo, niezależnie od
tego, w czym projektujesz.

## Szybki start

1. **Zainstaluj serwer raz**, na dowolnym komputerze w firmie, który zostaje włączony:
   - **Windows** — uruchom `EasyPDM_Windows_v<wersja>.exe` z
     [najnowszego wydania](https://github.com/pawelcel/EasyPDM/releases/latest); kreator
     zrobi resztę.
   - **Docker** —
     ```bash
     git clone https://github.com/pawelcel/EasyPDM.git && cd EasyPDM && ./install-easypdm-docker.sh
     ```
   - **Linux bez Dockera** — paczka `.tar.gz` z tego samego wydania,
     [krok po kroku na wiki](https://github.com/pawelcel/EasyPDM/wiki/Installation).
2. **Otwórz `http://localhost:5000`** (albo adres tego komputera z innej maszyny) i zaloguj
   się jako `admin` / `admin` — potem zmień to hasło. Czeka na Ciebie przykładowy projekt.
3. **Dodaj swój program CAD** — zainstaluj dodatek z tabeli wyżej i zaloguj się z niego.

Reszta zespołu po prostu otwiera ten adres w przeglądarce.

## Filmy

Na YouTube, po angielsku — [cała playlista](https://www.youtube.com/playlist?list=PLe2vSF7-ieUA):

1. [Po co PDM i czym jest EasyPDM](https://youtu.be/OaqyJQCHkt4)
2. [Instalacja — Windows, Docker albo Linux](https://youtu.be/ZwAWCJN9tWg)
3. [Projekty i struktura](https://youtu.be/ZB3pXQY6-e0)
4. [Z CAD do PDM — makro wysyłania](https://youtu.be/vMQPxwWQrGc)
5. [Całe złożenie — wysyłanie, pobieranie, aktualizacja i rysunki](https://youtu.be/vXHYbRlPu_s)
6. [Pola części — statusy, blokady, materiały i obróbki](https://youtu.be/JxMLm7bmuck)
7. [PDM → rysunek — tabelka wypełnia się sama](https://youtu.be/ZJiMQNJRsB0)
8. [BOM i dokumentacja — zestawienie, którego nikt nie przepisuje](https://youtu.be/mJW8TSQ7EpU)
9. [Klient — dla kogo projektujesz](https://youtu.be/hkTmkpIT-MA)

## Więcej informacji

- 📖 **[Wiki](https://github.com/pawelcel/EasyPDM/wiki)** (po angielsku, ze zrzutami
  ekranu): [Instalacja](https://github.com/pawelcel/EasyPDM/wiki/Installation) ·
  [Praca z EasyPDM](https://github.com/pawelcel/EasyPDM/wiki/Using-EasyPDM) ·
  [Programy CAD](https://github.com/pawelcel/EasyPDM/wiki/CAD-Integrations) ·
  [Ustawienia](https://github.com/pawelcel/EasyPDM/wiki/Settings) ·
  [Przegląd techniczny](https://github.com/pawelcel/EasyPDM/wiki/Technical-Overview)
- 🛠 **Administratorzy i programiści** — architektura, API i szczegóły serwera w
  [`TECHNICAL.md`](TECHNICAL.md)
- 📝 **Co nowego** — [`CHANGELOG.md`](CHANGELOG.md)

## O projekcie

Jestem konstruktorem mechanikiem i dobrze wiedziałem, czego brakuje mi przy pracy z plikami
CAD. Kodu nie pisałem sam: napisał go Claude, model AI firmy Anthropic, na podstawie moich
wymagań. Zbudowałem EasyPDM na własny użytek, a skoro działa — czemu się nie podzielić.

Znalazłeś błąd albo czegoś brakuje? [Zgłoś to](https://github.com/pawelcel/EasyPDM/issues).
Jeśli EasyPDM oszczędza Ci czas, możesz
[postawić mi kawę](https://buycoffee.to/easypdm). Licencja [MIT](LICENSE).
