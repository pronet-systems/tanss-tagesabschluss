# Änderungen

Alle bemerkenswerten Änderungen an diesem Projekt. Eine Zeile in der Sprache des Benutzers,
nicht in der des Compilers.

Das Format folgt lose [Keep a Changelog](https://keepachangelog.com/de/1.1.0/); die Versionen
folgen [Semantic Versioning](https://semver.org/lang/de/).

## Unveröffentlicht

## 0.1.0 — erste Fassung

### Neu

- **Lückenerkennung.** Legt die Zeiterfassung und die erfassten Leistungen eines Tages
  übereinander und zeigt, was dazwischen offen bleibt. Gestempelte Pausen können dabei
  strukturell nicht als Lücke erscheinen.
- **Tagesansicht.** Ein Tag auf einmal, mit Kalender zum Blättern, den offenen Zeitfenstern und
  den bereits erfassten Leistungen. Zu jeder Lücke steht, welche Leistung davor und danach lag.
- **Überblick.** Die letzten vierzehn Tage auf einen Blick — welcher Tag verdient überhaupt
  einen Blick. Ein Klick führt in den Tag.
- **Leistung nachtragen.** Text, Ticket, Gerät und das Kennzeichen „intern“, direkt aus der
  Liste. Vorbelegt wird über TANSS, damit Stundensatz und Abrechnungsart stimmen.
- **Zwei Erinnerungen.** Morgens der Vortag, abends vor Feierabend der laufende Tag. Läuft auch
  bei geschlossenem Fenster; die Anwendung liegt im Infobereich.
- **Feiertage nach Bundesland.** Bestimmt aus der Postleitzahl der eigenen Firma, mit Auswahl
  von Hand als Rückfall. Regional begrenzte Feiertage (Fronleichnam in Sachsen und Thüringen,
  Mariä Himmelfahrt in Bayern) werden als solche gekennzeichnet.
- **Urlaub, Krankheit und Abwesenheit** erklären eine fehlende Leistung — auch halbtags und
  stundenweise. Home-Office ausdrücklich nicht: Das ist gearbeitete Zeit.
- **Sprachmodell-Unterstützung** für Korrektur und Ausformulierung des Leistungstextes.
  Standardmässig abgeschaltet, mit ausdrücklicher Einwilligung und eigenem Schlüssel.
- **Aktualisierung und Setup.** Prüfung auf neue Fassungen, Hinweis in der Fußzeile, Setup pro
  Benutzer ohne Administratorrechte.

### Sicherheit

- Das Arbeitstoken liegt DPAPI-verschlüsselt im lokalen Profil, an Benutzer **und** Rechner
  gebunden. Kennwort und Sitzungsschlüssel werden nicht gespeichert.
- Das Token wird vor Ablauf erneuert und erst nach bestandener Probe übernommen. Die Probe
  läuft über den HTTP-Zugang und nicht über das Repository — jenes fängt Fehler ab, und ein
  Token, auf das TANSS mit 403 antwortet, bestünde sie sonst klaglos.
- Geheimnisse werden vor jeder Ausgabe geschwärzt (`Redaction`).

### Bekannte Einschränkungen

- Die benutzten TANSS-Routen sind aus der Beschreibung zu 10.10.0 übernommen und noch nicht
  gegen eine Instanz gemessen. Die Tests dafür liegen in `TanssTagesabschluss.Live.Tests`.
- Die Oberfläche für die Sprachmodell-Einstellungen fehlt noch; die Werte lassen sich nur in
  `config.json` setzen.
