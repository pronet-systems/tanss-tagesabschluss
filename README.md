# TANSS Tagesabschluss

Findet die Zeitfenster, in denen gearbeitet, aber **keine Leistung erfasst** wurde — und
schliesst sie.

> **Stand: in Entwicklung.** Was fertig ist und was nicht, steht unten unter
> [Stand der Umsetzung](#stand-der-umsetzung). Insbesondere: Mehrere Annahmen über TANSS sind
> aus der Beschreibung übernommen und **noch nicht gegen eine Instanz gemessen**. Sie stehen
> als Tests in `tests/TanssTagesabschluss.Live.Tests` bereit.

---

## Worum es geht

In der Zeiterfassung steht, wann jemand da war. In den Leistungen steht, woran er gearbeitet
hat. Zwischen beidem klafft im Alltag regelmässig eine Lücke: Ein Anruf zwischendurch, eine
halbe Stunde am Server des Kunden, ein Gang in die Nachbarabteilung — getan, aber nicht
erfasst.

Diese Lücken sind kein Ordnungsproblem. **Sie sind Umsatz, der nicht auf der Rechnung steht.**
Bei einem Techniker mit vierzig Wochenstunden genügen fünfundvierzig vergessene Minuten am
Tag, um im Jahr eine fünfstellige Summe zu verlieren — und niemand merkt es, weil nichts
fehlt, was jemand vermissen würde.

Dieses Werkzeug legt beide Seiten übereinander und zeigt, was dazwischen offen bleibt.

## Was es tut

| | |
|---|---|
| **Liest die Zeiterfassung** | Kommen, Gehen, Pausenbeginn, Pausenende — samt Arbeitszeitmodell des Tages. |
| **Liest die Leistungen** | Alles, was im Zeitraum erfasst ist, einschliesslich Termine und Abwesenheiten. |
| **Liest Urlaub und Krankheit** | Genehmigte Abwesenheiten erklären eine fehlende Leistung, auch halbtags. |
| **Kennt die Feiertage** | Des maßgeblichen Bundeslands, bestimmt aus der Postleitzahl der eigenen Firma. |
| **Rechnet die Lücken** | Anwesenheit minus erfasste Leistungen minus Abwesenheiten. |
| **Erinnert zweimal am Tag** | Morgens an den Vortag, abends vor Feierabend an den laufenden Tag. |
| **Trägt nach** | Direkt aus der Liste: Text, Ticket, Gerät, intern — mit Existenzprüfung davor. |
| **Formuliert auf Wunsch aus** | Sprachmodell-Unterstützung für Korrektur und Ausformulierung; standardmässig aus. |

## Was es ausdrücklich **nicht** tut

Das gehört an den Anfang und nicht ins Kleingedruckte: Wer ein Werkzeug an seine Arbeitszeiten
lässt, soll wissen, wo dessen Grenzen verlaufen.

- **Es setzt keine Zeitstempel.** `POST /api/v1/timestamps` würde den Mitarbeiter ein- oder
  ausstempeln. Diese Route wird ausschliesslich gelesen. Die Arbeitszeit eines Menschen ändert
  dieses Werkzeug nicht.
- **Es schliesst keinen Tag ab.** `POST /api/v1/timestamps/dayClosing` schreibt die gerechneten
  Werte fest und lässt sich nur über `DELETE` und erneutes Anlegen berichtigen. Das ist die
  Entscheidung eines Menschen in TANSS, nicht die eines Hintergrunddienstes. Der Name dieses
  Werkzeugs meint denselben *Vorgang*, nicht dieselbe Route: Es **bereitet** den Tagesabschluss
  vor, indem es die Lücken zeigt.
- **Es bucht nichts von selbst.** Jede Leistung entsteht, weil jemand sie geschrieben und
  bestätigt hat.
- **Es übermittelt nichts nach draussen**, solange die Sprachmodell-Unterstützung aus ist.
  Ausnahme sind zwei öffentliche Dienste, an die ein Jahr, ein Länderkürzel und eine
  Postleitzahl gehen — kein Firmenname, keine Kennung, keine Zeiten. Siehe
  [Fremde Dienste](#fremde-dienste).

---

## Wie die Lücken gerechnet werden

Der ganze Rechenweg in einem Satz: **Anwesenheit minus erfasste Leistungen minus genehmigte
Abwesenheiten.** Was übrig bleibt und länger ist als die eingestellte Schwelle, ist eine Lücke.

Dahinter stehen vier Entscheidungen, die den Unterschied zwischen brauchbar und unbrauchbar
ausmachen:

### Pausen können gar nicht als Lücke erscheinen

TANSS schneidet die Abschnitte selbst: Ein `WORK`-Abschnitt endet bei `PAUSE_START`, der
nächste beginnt bei `PAUSE_END`. Die Mittagspause steht deshalb in **keinem**
Anwesenheitsabschnitt — und da nur *innerhalb* der Anwesenheit gesucht wird, kann sie
strukturell nicht herauskommen. Das ist der Grund, warum dieses Werkzeug die **Abschnitte**
liest und nicht die rohen Stempel.

### Der laufende Tag wird bei „jetzt“ abgeschnitten

Ein noch laufender Abschnitt bekommt von TANSS als Ende das Tagesende. Ungeschnitten hätte,
wer um zehn Uhr nachsieht, eine Lücke von vierzehn Stunden — nämlich den Rest des Tages, den er
noch gar nicht gearbeitet hat.

### Ein Tag ganz ohne Zeiterfassung ist ein eigener Befund

Wo keine Anwesenheit steht, gibt es auch keine Lücke. Ein vollständig vergessener Arbeitstag
fiele durch jede reine Lückenrechnung — deshalb ist er ein eigenes Urteil und nicht das Fehlen
eines Befunds.

### Eine Schwelle ist nötig, sonst wird die Liste unbrauchbar

Zwischen zwei Leistungen liegen fast immer ein paar Minuten. Ohne Schwelle stünden dreissig
Einträge von je vier Minuten in der Liste, und die eine Stunde, um die es wirklich geht, ginge
darin unter. Vorgabe sind fünfzehn Minuten — die übliche Abrechnungseinheit in TANSS.

---

## Die TANSS-Anbindung im Einzelnen

### Die benutzten Routen

| Route | Zweck | Beschrieben | Gemessen |
|---|---|---|---|
| `GET /api/v1/timestamps/statistics` | **Die wichtigste.** Zeitstempel, Abschnitte nach Art, Arbeitszeitmodell | ja | nein |
| `PUT /api/v1/supports/list` | Leistungen lesen (PUT, obwohl es liest) | ja | nein |
| `POST /api/v1/supports` | Leistung anlegen | ja | nein |
| `POST /api/v1/supports/properties` | Leistung vorbelegen lassen | **nein** | im Schwesterprojekt, für `TIMER` |
| `PUT /api/v1/vacationRequests/list` | Urlaub, Krankheit, Abwesenheit | ja | nein |
| `GET /api/v1/timestamps/pauseConfigs` | Pausenregeln der Instanz | ja | nein |
| `PUT /api/v1/pcs`, `/peripheries`, `/components` | Geräte einer Firma | ja | nein |
| `PUT /api/v1/search` | Firmensuche | ja | im Schwesterprojekt |
| `GET /api/erp/v1/companies/employees` | Umweg zur eigenen Firma | ja | **nein** |
| `GET /api/v1/jwts/tanss_app` | Token prägen | **nein** | im Schwesterprojekt |

### Zwei Regeln, deren Bruch am häufigsten kostet

- `/api/v1/**` verlangt `loggedInUserId` **zwingend** — sonst 403.
- `/api/tanss.x/v1/**` braucht ihn nicht.

`TanssRoutes.NeedsLoggedInUserId` entscheidet das; der Client setzt den Parameter selbsttätig,
ein Aufrufer nie von Hand.

### Die Regel, deren Bruch am teuersten ist

> **TANSS dedupliziert Leistungen nicht.** Ein zweiter Aufruf mit demselben Inhalt erzeugt einen
> zweiten Datensatz — und damit einen zweiten Posten auf der Rechnung des Kunden.

Daraus folgt:

> **Vor jeder Wiederholung steht die Existenzprüfung.** Eine Leistung mit unbekanntem Ausgang
> wird nie einfach noch einmal gesendet.

Und der Umkehrschluss, den man leicht falsch macht: **Scheitert die Existenzprüfung selbst,
heisst das „unbekannt“, nicht „nicht vorhanden“.** Dann wird nicht gesendet. Wer diese
Fehlerbehandlung später „vereinfacht“ und im Zweifel sendet, erzeugt doppelt berechnete
Arbeitszeit in der Produktivinstanz eines Kunden.

### Zwei Einheiten, die aufeinanderstossen

TANSS rechnet Zeitstempel in **Sekunden** und Leistungsdauern in **Minuten**. Beides trifft in
diesem Werkzeug unmittelbar aufeinander. Die Umrechnung steht an genau einer Stelle
(`TanssTime` beziehungsweise `SupportEntry.Span`); einen Millisekunden-Umrechner gibt es
bewusst nicht, weil ein Millisekundenwert nicht abgewiesen, sondern als Sekundenwert gelesen
würde — und daraus ein Datum im Jahr 51667 entstünde.

### Die Zuordnungstypen

Eine Leistung erwartet `linkTypeId` als **Zahl**, die Beschreibung führt nur die **Namen**. Die
Zuordnung stammt aus `tns/core/linkType/TnsLinkTypes` in `TanssApi-10.10.0.jar`:

| Name | Kennung |
|---|---|
| `PC` / `SERVER` | 1 |
| `COMPANY` | 2 |
| `EMPLOYEE` | 3 |
| `PERIPHERY` | 4 |
| `COMPONENT` | 5 |
| `TICKET` | 11 |

`PC` und `SERVER` tragen beide die 1 — kein Lesefehler, sondern die Bauform: Ein Server ist in
TANSS ein PC mit gesetztem Serverkennzeichen.

---

## Fremde Dienste

Zwei öffentliche Dienste werden befragt, beide ohne Schlüssel und ohne Anmeldung:

| Dienst | Wofür | Was hinausgeht |
|---|---|---|
| `openplzapi.org` | Postleitzahl → Bundesland | die Postleitzahl der eigenen Firma, einmalig bei der Einrichtung |
| `feiertage-api.de` | Feiertage des Bundeslands | ein Jahr und ein Länderkürzel, höchstens einmal im Monat |

Beide Antworten werden lokal zwischengespeichert. **Hinaus geht kein Firmenname, keine Kennung
und keine Zeit.** Wer das trotzdem nicht möchte: Ohne Bundesland rechnet das Werkzeug weiter,
nur ohne Feiertage — und sagt das in seinen Hinweisen.

Die Sprachmodell-Unterstützung ist der dritte Fall und der einzige, bei dem Text das Haus
verlässt. Sie ist **standardmässig abgeschaltet**, verlangt eine ausdrückliche Einwilligung und
einen eigenen Schlüssel.

---

## Aufbau

```
src/
  TanssTagesabschluss.Api/        HTTP, Auth, Routen, Modelle, Repositories — kennt kein Windows
  TanssTagesabschluss.Workday/    Die Lückenrechnung, Feiertage, Bundesländer — kennt kein Netz
  TanssTagesabschluss.Storage/    Konfiguration, DPAPI-Token, Feiertagszwischenspeicher
  TanssTagesabschluss.App/        WPF-Oberfläche, Hintergrunddienste, Sprachmodell-Anbindung
tests/
  *.Tests/                        Je Fachmodul eines; Live.Tests läuft gegen eine echte Instanz
```

`Api` und `Workday` zielen bewusst **ohne** Windows-Ziel: Die Fachlogik muss sich ohne Fenster
und ohne Netz prüfen lassen — und genau das tut `GapFinderTests`.

## Bauen und testen

```bash
dotnet restore TanssTagesabschluss.slnx
dotnet build   TanssTagesabschluss.slnx -c Release -warnaserror
dotnet test    TanssTagesabschluss.slnx -c Release
```

Die Testsuite läuft **ohne Netz und ohne Zugangsdaten**. `TanssTagesabschluss.Live.Tests`
überspringt sich ohne `TANSS_BASE_URL`, `TANSS_USER` und `TANSS_PASSWORD` selbst —
„übersprungen“ ist dabei ausdrücklich nicht „bestanden“.

```bash
TANSS_BASE_URL=https://tanss.kunde.de/backend TANSS_USER=... TANSS_PASSWORD=... \
  dotnet test tests/TanssTagesabschluss.Live.Tests
```

Ein Setup entsteht mit `build\publish.ps1`; Einzelheiten in
[`installer/README.md`](installer/README.md).

---

## Stand der Umsetzung

**Fertig und geprüft (107 Tests):**

- Die Lückenrechnung samt Grenzfällen: Pause, halber Urlaubstag, Feiertag, laufender Tag,
  vergessener Tag, bedingter Feiertag, Home-Office.
- Die Mengenlehre darunter (`TimeSegment`) — halboffene Intervalle, Verschmelzen, Abziehen.
- Der Riegel gegen doppelt gebuchte Leistungen, einschliesslich des Falls „Prüfung selbst
  fehlgeschlagen“.
- Die Prüfung der Konfiguration und das Lesen/Schreiben der Datei.
- Der Feiertagszwischenspeicher, einschliesslich „veralteter Kalender ist besser als keiner“.
- Die Eigenheiten des TANSS-Formats: `types` als Objekt **und** als Feld, `THRUSDAY`,
  `24:00`, Sekunden gegen Minuten.

**Gebaut, aber noch nicht gegen eine Instanz gemessen:**

- Sämtliche Routen dieses Werkzeugs. Die Beschreibung zu 10.10.0 ist die Quelle; die Tests in
  `Live.Tests` sind vorbereitet und benannt.
- Besonders offen: ob `GET /api/erp/v1/companies/employees` die eigene Firma im
  `meta`-Block nennt (daran hängt die Bundeslandbestimmung), und ob `/api/erp/v1` ohne
  `loggedInUserId` antwortet.
- Ob `POST /api/v1/supports/properties` die Initialisierer `TICKET` und `COMPANY` annimmt.
  Die Aufzählung des Servers führt beide; gemessen ist im Schwesterprojekt nur `TIMER`.

**Noch nicht gebaut:**

- Die Oberfläche der Sprachmodell-Einstellungen (Anbieter, Modell, Schlüssel, Einwilligung).
  Die Schicht darunter ist vollständig; die Werte lassen sich derzeit nur in `config.json`
  setzen.
- Das Herunterladen und Einspielen einer neuen Fassung aus der Anwendung heraus. Die Prüfung
  und der Hinweis stehen; der Knopf führt bisher zur Veröffentlichungsseite.

---

## Lizenz

[MIT](LICENSE) — ProNet Systems GmbH.
