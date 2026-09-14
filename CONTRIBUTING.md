# Mitarbeiten

Kurz gehalten. Hier steht nur, was man wirklich wissen muss, bevor man etwas ändert — vor allem
die Stellen, an denen ein Fehler teuer wird.

> **Stand: in Entwicklung.** Was fertig ist und was nicht, steht im
> [README unter „Stand der Umsetzung"](README.md#stand-der-umsetzung).

---

## Voraussetzungen

| | |
|---|---|
| Betriebssystem | Windows 10 oder 11. Die Oberfläche ist WPF, das Token hängt an DPAPI. |
| SDK | .NET 10 |
| Für das Setup | Inno Setup 6 (`winget install JRSoftware.InnoSetup`) — nur, wer ein Setup packen will |
| TANSS-Instanz | **nicht nötig.** Die Testsuite läuft ohne Netz und ohne Zugangsdaten. |

## Bauen und testen

```bash
dotnet restore TanssTagesabschluss.slnx
dotnet build   TanssTagesabschluss.slnx -c Release -warnaserror
dotnet test    TanssTagesabschluss.slnx -c Release
```

Beim Bauen der Oberfläche darf die Anwendung nicht laufen — sonst sind `bin\` und `obj\`
gesperrt. Der CI-Lauf tut dasselbe; was lokal grün ist, ist dort grün.

---

## Die drei Regeln, deren Bruch am teuersten ist

### 1. Erst prüfen, dann senden — und im Zweifel nicht senden

**TANSS dedupliziert Leistungen nicht.** Ein zweiter Aufruf mit demselben Inhalt erzeugt einen
zweiten Datensatz, und der steht auf der Rechnung des Kunden. Eine gebuchte Leistung lässt sich
über die Schnittstelle **nicht** zurücknehmen.

> Vor jeder Wiederholung steht die Existenzprüfung. Eine Leistung mit unbekanntem Ausgang wird
> nie einfach noch einmal gesendet.

Und der Umkehrschluss, den man leicht falsch macht: **Scheitert die Prüfung selbst, heisst das
„unbekannt“ und nicht „nicht vorhanden“.** Dann wird nicht gesendet. Wer diese Fehlerbehandlung
„vereinfacht“ und im Zweifel sendet, erzeugt doppelt berechnete Arbeitszeit in der
Produktivinstanz eines Kunden.

Das steht in `GapBooking.BookAsync` und in `SupportRepository.ExistsAsync`. Wer daran arbeitet,
schreibt einen Test dazu — `GapBookingTests` deckt heute jeden dieser Wege ab.

### 2. Die Zeiterfassung wird gelesen und niemals geschrieben

`POST /api/v1/timestamps` stempelt einen Menschen ein oder aus.
`POST /api/v1/timestamps/dayClosing` schreibt seinen Tag fest. Beide Routen sind bekannt, beide
sind in `TanssRoutes` als Konstante hinterlegt — damit unmissverständlich ist, dass sie bewusst
ungenutzt sind.

**Wer hier einen Schreibweg ergänzt, ändert die Grundlage einer Lohnabrechnung.** Das ist keine
Erweiterung, sondern ein anderes Produkt.

### 3. Token prägen ist kein Lesevorgang

`GET /api/v1/jwts/tanss_app` stellt bei **jedem** Aufruf ein neues JWT aus, und TANSS 10.10.0
kann es **nicht widerrufen**. Eine automatische Wiederholung nach einer Zeitüberschreitung prägt
ein zweites Token, von dem niemand mehr erfährt — ein Jahr lang gültig.

`TanssRoutes.HasSideEffectOnGet` schützt das ganze Präfix `/api/v1/jwts` und nicht die eine
bekannte Route: Jede Tokenart, die dort später dazukommt, ist vom ersten Aufruf an geschützt.

Und für Tests gegen eine echte Instanz: **Prägen ausschliesslich mit `isForTesting=true`.** Die
Harmlosigkeit hängt dabei nicht an einer Zusage des Servers, sondern an einer Zeile in
`TanssAuth.MintAsync`, die dann 60 Sekunden anfragt.

---

## Was gemessen ist und was nicht

Ein erheblicher Teil dessen, was dieses Werkzeug über TANSS annimmt, stammt aus der
**Beschreibung** zu 10.10.0 und nicht aus einer Messung. Wo das so ist, steht es im Quelltext:
das Wort **ungemessen**.

> **Wer eine solche Annahme ändert, braucht eine Messung, keine Vermutung.** Ein Feld, das
> plausibel aussieht, ist noch lange nicht wirksam, und ein Verb sagt hier nichts über die
> Wirkung — die Hälfte der lesenden Routen ist `PUT`.

In den Pull Request gehört dann, gegen welche TANSS-Fassung gemessen wurde und was dabei
herauskam: Anfrage, Antwort, Statuscode.

### Tests gegen eine echte Instanz

`tests/TanssTagesabschluss.Live.Tests` läuft **nicht** gegen Attrappen. Ohne Zugangsdaten
überspringt es sich selbst — die Werkstrecke bei GitHub hat keine Instanz und soll deswegen
nicht scheitern. „Übersprungen“ ist dabei ausdrücklich nicht „bestanden“: Der Testläufer weist
es getrennt aus.

```bash
TANSS_BASE_URL=https://tanss.kunde.de/backend TANSS_USER=... TANSS_PASSWORD=... \
  dotnet test tests/TanssTagesabschluss.Live.Tests
```

**Warum es dieses Projekt gibt.** Im Schwesterprojekt haben zwei Fehler wochenlang alle übrigen
Tests bestanden, weil sie mit Attrappen unsichtbar waren: Ein Tokenspeicher gab das Präfix
`Bearer ` nicht mit, und die Einrichtung legte das kurzlebige Sitzungstoken als Arbeitstoken ab.

> **Wer eine Annahme über TANSS gegen seine eigene Nachbildung dieser Annahme prüft, bekommt
> immer recht.** Solche Annahmen gehören hierher.

**Dieses Projekt schreibt nichts.** Anders als im Schwesterprojekt gibt es hier keinen
Gegenstand, der sich restlos wieder entfernen liesse — eine Leistung lässt sich nicht löschen,
ein Zeitstempel ist die Arbeitszeit eines Menschen. Geprüft wird ausschliesslich lesend.

---

## Hausregeln

- **Deutsche Prosa, englische Bezeichner.** Dokumentationskommentare und alle Texte, die ein
  Benutzer zu sehen bekommt, sind deutsch. Klassen, Methoden, Felder und Parameter sind
  englisch.
- **Kommentare sagen *warum*, nicht *was*.** Was der Code tut, steht im Code. Ein Kommentar
  begründet eine Entscheidung, warnt vor einer Falle oder nennt eine Messung.
- **Null Warnungen sind Pflicht.** `TreatWarningsAsErrors` ist an. Eine Warnung wird behoben,
  nicht unterdrückt; wenn doch, dann gezielt in `GlobalSuppressions.cs` mit einer Begründung,
  die die Ausnahme trägt.
- **Jede Meldung sagt drei Dinge:** was ist, warum das passiert und was zu tun ist. Ein
  Stacktrace allein hilft am Freitagnachmittag niemandem.
- **Erst Status prüfen, dann Rumpf.** Ein leerer Rumpf ist nur bei Erfolg eine leere Antwort.
  Stünde die Leerprüfung vorher, wäre eine leere 403 ein leerer Erfolg.
- **Zeiten ausschliesslich über `TanssTime`.** TANSS rechnet Zeitstempel in **Sekunden**,
  Leistungsdauern in **Minuten**. Einen Millisekunden-Umrechner gibt es bewusst nicht: Ein
  Millisekundenwert wird nicht abgewiesen, sondern als Sekundenwert gelesen — daraus wird ein
  Datum im Jahr 51667.
- **Ein Fehlschlag kostet nie mehr, als er muss.** Ohne Feiertage rechnet das Werkzeug weiter
  und sagt es; ohne Zeitstempel gibt es keine Aussage und es wirft. Welche der beiden
  Richtungen richtig ist, steht an jeder Stelle im Kommentar.
- **Tests gehören dazu.** Jedes Fachmodul hat ein Testprojekt unter `tests/`. Die
  Lückenrechnung läuft ohne WPF und ohne Netz und ist deshalb vollständig testbar — sie ist der
  Kern dieses Werkzeugs und hat die meisten Tests.

---

## Änderungen einreichen

1. Einen Zweig anlegen, nicht auf `main` arbeiten.
2. Ändern, Tests dazu, `dotnet build -warnaserror` und `dotnet test` lokal grün.
3. Einen Eintrag in [`CHANGELOG.md`](CHANGELOG.md) unter **Unveröffentlicht** ergänzen — eine
   Zeile in der Sprache des Benutzers.
4. Pull Request aufmachen. Er beschreibt, **warum** die Änderung nötig ist, und bei allem, was
   TANSS berührt, **wie gemessen wurde**.
5. Der CI-Lauf muss grün sein.

Commit-Meldungen: erste Zeile im Imperativ und unter 72 Zeichen, danach eine Leerzeile und der
Grund.

## Was hier nicht hineingehört

- **Keine Zugangsdaten, keine Token, keine Kundendaten.** Nicht im Quelltext, nicht in Tests,
  nicht in einem Issue und nicht in einem Protokollauszug. `config.json`, `credentials.dat`,
  `ai.dat`, `proxy.dat` und `*.log` stehen deshalb in `.gitignore`.
- **Kein eigener Aktualisierungsmechanismus neben dem vorhandenen.** Aktualisiert wird über eine
  neue Fassung des Setups.

## Sicherheitslücken

Nicht als Issue. Eine Lücke in einem Werkzeug, das in fremde Ticketsysteme schreibt, wird
vertraulich gemeldet: per Security Advisory oder an ProNet Systems GmbH.

---

## Lizenz

Mit einem Beitrag stellst du ihn unter die [MIT-Lizenz](LICENSE) dieses Projekts.
