# Änderungen

Alle bemerkenswerten Änderungen an diesem Projekt. Eine Zeile in der Sprache des Benutzers,
nicht in der des Compilers.

Das Format folgt lose [Keep a Changelog](https://keepachangelog.com/de/1.1.0/); die Versionen
folgen [Semantic Versioning](https://semver.org/lang/de/).

## Unveröffentlicht

### Behoben

- **Die eigene Firma wird jetzt gefunden.** Bisher lief die Erkennung über
  `GET /api/erp/v1/companies/employees`; auf unserer Instanz antwortet dieser Weg mit 403, und
  „Ermitteln“ blieb ohne Ergebnis. Den Vortritt hat nun
  `GET /api/v1/employees/ownState` — dort steht die eigene Firma als Zahl, samt Anschrift, und
  ein Aufruf genügt. Nachgemessen gegen 10.10.0: HTTP 200 mit `loggedInUserId`, HTTP 403 ohne.
  Der alte Weg bleibt als Rückfall; scheitern beide, nennt die Meldung beide Gründe.
- **Das Firmenlogo ist auf der Seite „Über“ wieder zu sehen.** Der Datei fehlte im Kopf ein
  einzelnes Byte (`0D`), abhandengekommen bei einer Umwandlung von Zeilenenden. WPF zeichnet ein
  unlesbares Bild ohne jede Meldung einfach nicht — ein Fehler, den nur bemerkt, wer weiss, dass
  dort ein Logo stehen soll. Ein Test prüft die Kennfolge jetzt bei jedem Bau.
- **Die Mitarbeiterkennung wird gegen das Token geprüft.** Weicht die hinterlegte Kennung von
  der des angemeldeten Benutzers ab, gilt die des Tokens und die Einstellungen sagen es. Mit der
  falschen Kennung hätte das Werkzeug fremde Zeiten gelesen und zu fremden Leistungen gemahnt.
- **Vier Fehlermeldungen verwiesen auf das Schwesterwerkzeug.** Wer kein Token hatte, wurde
  gebeten, `tanss-logwatch setup` auszuführen — ein Befehl, den es hier nicht gibt. Sie
  verweisen jetzt auf die Anmeldung in den Einstellungen.

### Neu

- **Verbindungsprüfung in den Einstellungen.** Zehn lesende Stichproben, jede mit ihrer Route
  und ihrem Befund: Erreichbarkeit, Token, Zeiterfassung, Arbeitszeitmodell, Leistungen,
  Abwesenheiten, eigene Firma, Tickets, Feiertage, Zertifikatsprüfung.
- **Speichern und Prüfen stehen oben** auf der Seite Einstellungen, und jede Karte trägt ihren
  eigenen Befund. Bisher landete jede Meldung unten am Rand — weit weg von der Schaltfläche, die
  sie ausgelöst hatte, und damit leicht zu übersehen.
- **Eigenes Fenster für die Sprachmodell-Unterstützung**, wie im Schwesterprojekt: Anbieter,
  Modell, Schlüssel und die ausdrückliche Einwilligung an einer Stelle.
- **Die Seite „Über“** zeigt Logo, Version, Laufzeit, Betriebssystem, die Ablageorte und
  ausdrücklich, was dieses Werkzeug **nicht** tut.

### Geändert

- Die Mitarbeiterkennung ist keine Eingabe mehr, sondern eine Anzeige mit aufgelöstem Namen.
  Sie kommt aus der Anmeldung; sie von Hand zu ändern konnte nur schaden.

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
