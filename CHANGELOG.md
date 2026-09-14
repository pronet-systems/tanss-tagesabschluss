# Änderungen

Alle bemerkenswerten Änderungen an diesem Projekt. Eine Zeile in der Sprache des Benutzers,
nicht in der des Compilers.

Das Format folgt lose [Keep a Changelog](https://keepachangelog.com/de/1.1.0/); die Versionen
folgen [Semantic Versioning](https://semver.org/lang/de/).

## Unveröffentlicht

### Neu

- **Ein Zeitfenster lässt sich teilen.** Neun offene Stunden am Stück sind fast nie neun
  Stunden an derselben Sache — dazwischen lagen ein Anruf, ein Kunde, eine Stunde am eigenen
  Server. Bisher blieb nur, alles in *einen* Leistungstext zu schreiben und auf *ein* Ticket zu
  buchen; erfasst war es dann, auf der Rechnung des Kunden aber nicht mehr auseinanderzuhalten.
  Neben jeder Lücke steht jetzt die Dauer des ersten Teils und ein „Teilen". Jeder Teil bekommt
  eigenen Text, eigene Firma, eigenes Ticket und eigenes Gerät; ein Teil lässt sich wieder
  teilen.
  Der erste Teil erbt, was schon getippt oder gewählt war — wer teilt, hat den Anfang im Sinn.
  Die Leistung davor bleibt beim ersten Teil, die danach beim zweiten; nach innen schlägt keiner
  von beiden mehr ein Ticket vor, denn dort steht die andere Hälfte und trägt noch nichts.
  Geteilt wird an Ort und Stelle, ohne den Tag neu zu laden — die Lücke ist eine Rechnung dieses
  Werkzeugs und steht so in TANSS gar nicht.

## 0.1.0 — 2026-09-14

Die erste Version. Sie ist nie zuvor veröffentlicht worden; was während der Entwicklung
geändert oder berichtigt wurde, steht deshalb nicht als eigene Zeile hier, sondern ist in die
Beschreibung eingeflossen. Was gegen eine echte TANSS-Instanz nachgemessen ist und was nicht,
steht unten.

### Neu

- **Lückenerkennung.** Legt die Zeiterfassung und die erfassten Leistungen eines Tages
  übereinander und zeigt, was dazwischen offen bleibt. Gestempelte Pausen können dabei
  strukturell nicht als Lücke erscheinen.
- **Auch die Zeit vor dem Einstempeln.** Wer um 11:08 stempelt, obwohl der Arbeitstag um 8:00
  beginnt, hat für die drei Stunden davor keinen Zeitstempel — und damit hätte eine reine
  Lückenrechnung dort auch keine Lücke gesehen. Der erwartete Arbeitsrahmen wird deshalb
  mitdurchsucht. Am laufenden Tag reicht er nur bis jetzt, an freien Tagen gilt er nicht, und
  gestempelte Pausen sind ausgenommen.
- **Tagesansicht.** Ein Tag auf einmal, mit Kalender zum Blättern, den offenen Zeitfenstern und
  den bereits erfassten Leistungen. Zu jeder Lücke steht, welche Leistung davor und danach lag.
- **Überblick.** Die letzten Tage auf einen Blick — welcher Tag verdient überhaupt einen Blick.
  Der Rückblick reicht von sieben Tagen bis zu einem Jahr und lässt sich dort oben rechts
  umstellen. Ein Klick führt in den Tag.
- **Leistung nachtragen.** Text, Firma, Ticket, Gerät und das Kennzeichen „intern", direkt aus
  der Liste. Vorbelegt wird über TANSS, damit Stundensatz und Abrechnungsart stimmen.
- **Firmensuche.** Das Firmenfeld sucht ab drei Zeichen von selbst und zeigt Kundennummer,
  Namen und — wo TANSS es führt — Zentrale oder Filiale. Ist eine Firma gewählt, stehen daneben
  nur noch **deren** offene Tickets und **deren** Geräte; wer für einen Kollegen einspringt,
  findet dessen Ticket sonst nicht. Ohne Firma bleibt es bei den eigenen offenen Tickets.
- **Zwei Erinnerungen.** Morgens der Vortag, abends vor Feierabend der laufende Tag. Läuft auch
  bei geschlossenem Fenster; die Anwendung liegt im Infobereich.
- **Feiertage nach Bundesland.** Bestimmt aus der Postleitzahl der eigenen Firma, mit Auswahl
  von Hand als Rückfall. Regional begrenzte Feiertage (Fronleichnam in Sachsen und Thüringen,
  Mariä Himmelfahrt in Bayern) werden als solche gekennzeichnet.
- **Urlaub, Krankheit und Abwesenheit** erklären eine fehlende Leistung — auch halbtags und
  stundenweise. Home-Office ausdrücklich nicht: Das ist gearbeitete Zeit.
- **Verbindungsprüfung.** Zehn lesende Stichproben in den Einstellungen, jede mit ihrer Route
  und ihrem Befund. Jede Zeile erscheint, sobald sie feststeht.
- **Sprachmodell-Unterstützung** für Korrektur und Ausformulierung des Leistungstextes, mit
  eigenem Fenster und einer Plakette, die den Zustand in einem Wort nennt. Standardmässig
  abgeschaltet, mit ausdrücklicher Einwilligung und eigenem Schlüssel.
- **Aktualisierung und Setup.** Prüfung auf neue Versionen, Hinweis in der Fußzeile, Setup pro
  Benutzer ohne Administratorrechte.

### Einzurichten, bevor es losgeht

- **Verbindung und Anmeldung.** Basisadresse eintragen, einmal anmelden — die
  Mitarbeiterkennung kommt aus der Anmeldung und ist keine Eingabe.
- **Eigene Firma.** „Ermitteln" liest sie über `GET /api/v1/employees/ownState`; aus ihrer
  Postleitzahl folgt das Bundesland und daraus die Feiertage.
- **Arbeitswoche — Pflichtangabe.** Wochentage und übliche Arbeitszeit. Ohne sie lädt die
  Konfiguration nicht. Der Grund steht unten: TANSS gibt den Wochenplan nicht heraus, und
  geraten wird hier nicht.

### Gegen eine Instanz nachgemessen

Alles Folgende ist gegen TANSS 10.10.0 geprüft, nicht aus der Beschreibung übernommen.

- **Das Arbeitszeitmodell ist über die Schnittstelle nicht zu bekommen.** TANSS nennt zu einem
  Modell Kennung und Namen (`meta.linkedEntities.employeeWorkingTimeModels`), aber keinen
  Wochenplan; der Ort, den die Beschreibung dafür nennt
  (`meta.listProperties.workingTimeModels`), fehlt in der Antwort ganz — geprüft mit und ohne
  `employeeIds` und für einen Mitarbeiter, dem ein Modell zugeordnet ist. Die Route
  `/api/v1/workingHours/client` hilft nicht: Es gibt sie in 10.10.0 nicht, und in 10.15 liefert
  sie die Servicezeiten der *Kunden*. Deshalb die Pflichtangabe in den Einstellungen.
- **Eine Leistung braucht eine Zuordnung.** TANSS bereitet mit `linkTypeId = 0` vor und lehnt
  das Anlegen dann mit HTTP 404 und `SupportMissingAssignmentException` ab. Ohne gewähltes
  Gerät wird deshalb die Firma zugeordnet.
- **Die eigene Firma steht in `ownState`.** `GET /api/v1/employees/ownState` liefert
  `ownCompanyId` und die ganze Firma samt Postleitzahl — mit `loggedInUserId` HTTP 200, ohne
  ihn HTTP 403. Der ERP-Weg über `/api/erp/v1/companies/employees` antwortet mit 403 und bleibt
  nur als Rückfall.
- **Abwesenheiten kommen anders als beschrieben.** `PUT /api/v1/vacationRequests/list` liefert
  kein Feld, sondern ein Objekt mit `vacationRequests`. Gelesen werden beide Formen.
- **`timestamps/statistics` braucht rund 13 Sekunden**, unabhängig vom angefragten Zeitraum
  (mit 1, 2, 7 und 30 Tagen geprüft). Die schnellere Route `timestamps/info` antwortet in rund
  100 Millisekunden, liefert an einem Urlaubstag aber leere Tage — ohne `types`, ohne
  `weekDay`. Sie wird deshalb nicht benutzt.

### Sicherheit

- Das Arbeitstoken liegt DPAPI-verschlüsselt im lokalen Profil, an Benutzer **und** Rechner
  gebunden. Kennwort und Sitzungsschlüssel werden nicht gespeichert.
- Das Token wird vor Ablauf erneuert und erst nach bestandener Probe übernommen. Die Probe
  läuft über den HTTP-Zugang und nicht über das Repository — jenes fängt Fehler ab, und ein
  Token, auf das TANSS mit 403 antwortet, bestünde sie sonst klaglos.
- Geheimnisse werden vor jeder Ausgabe geschwärzt (`Redaction`).

### Was dieses Werkzeug nicht tut

- Es setzt **keine Zeitstempel**. Kommen, Gehen und Pausen werden gelesen, niemals geschrieben.
- Es schliesst **keinen Tag** in der Zeiterfassung ab.
- Es bucht **nichts von selbst**. Jede Leistung entsteht, weil jemand sie geschrieben und
  bestätigt hat.
- Es übermittelt nichts nach draussen, solange die Sprachmodell-Unterstützung aus ist.
  Feiertage und Postleitzahl sind die Ausnahme — dorthin gehen ein Jahr, ein Länderkürzel und
  eine Postleitzahl, sonst nichts.

### Bekannte Einschränkungen

- **Die Tagesansicht braucht rund 13 Sekunden je Tag.** Das ist die Route und nicht die Menge;
  siehe oben.
- **Das Setup ist nicht signiert.** SmartScreen meldet einen unbekannten Herausgeber. Die
  Veröffentlichungsnotiz nennt den SHA256 zum Vergleich.
- **Eine neue Version wird gemeldet, aber nicht eingespielt.** „Veröffentlichung öffnen" führt
  in den Browser; heruntergeladen und installiert wird von Hand.
- Die Live-Tests unter `TanssTagesabschluss.Live.Tests` laufen nur mit gesetzten
  Umgebungsvariablen und werden sonst übersprungen.
