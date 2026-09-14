using System.Text.Json.Serialization;
using TanssTagesabschluss.Api;

namespace TanssTagesabschluss.Storage.Config;

/// <summary>
/// Die gesamte Konfiguration des Werkzeugs, so wie sie in <c>config.json</c> steht.
/// </summary>
/// <remarks>
/// <para><b>Unbekannte Felder werden abgelehnt</b> (<see cref="JsonUnmappedMemberHandling.Disallow"/>).
/// Eine vertippte Einstellung, die stillschweigend ignoriert wird, ist schlimmer als ein
/// Fehler: Wer <c>erinnerung</c> statt <c>reminder</c> schreibt, hält seine Erinnerung für
/// eingestellt, während in Wahrheit die Vorgabe gilt. Der Preis dafür ist, dass eine ältere
/// Programmfassung eine neuere Datei nicht liest — dafür gibt es <see cref="Version"/>.</para>
///
/// <para><b>Fehlende Abschnitte sind dagegen in Ordnung.</b> <c>Disallow</c> verbietet
/// unbekannte Felder, nicht ausgelassene: Eine Datei, die nur den TANSS-Abschnitt enthält,
/// lädt und bekommt für alles Übrige die Vorgaben.</para>
///
/// <para>Geheimnisse stehen hier <b>nicht</b> drin, nur Verweise darauf (<c>token_ref</c>,
/// <c>password_ref</c>). Das Token selbst liegt DPAPI-verschlüsselt im lokalen Profil.</para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppConfig
{
    /// <summary>Höchster Stand, den diese Programmfassung lesen kann.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Stand des Dateiaufbaus. Erlaubt später eine Überführung ohne Rätselraten.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Zugang zur TANSS-Instanz.</summary>
    public required TanssSection Tanss { get; init; }

    /// <summary>
    /// Die Sprachmodell-Unterstützung beim Erfassen einer Leistung.
    /// </summary>
    /// <remarks>
    /// Ausdrücklich ein eigener Abschnitt und nicht eine Handvoll Schalter anderswo: Hier
    /// verlassen zum ersten Mal Daten dieses Werkzeugs das Haus. Wer wissen will, ob das auf
    /// einem Rechner geschieht, soll genau eine Stelle nachsehen müssen.
    /// </remarks>
    public AiSection Ai { get; init; } = new();

    /// <summary>Die eigene Firma und das Bundesland, aus dem die Feiertage folgen.</summary>
    public CompanySection Company { get; init; } = new();

    /// <summary>Was als Lücke gilt.</summary>
    public GapSection Gaps { get; init; } = new();

    /// <summary>Wann geprüft und wann erinnert wird.</summary>
    public ReminderSection Reminder { get; init; } = new();

    /// <summary>Vorgeschalteter Proxy. Bleibt abgeschaltet, wenn keiner nötig ist.</summary>
    public ProxySection Proxy { get; init; } = new();

    /// <summary>Protokollierung auf dem Rechner des Technikers.</summary>
    public LoggingSection Logging { get; init; } = new();

    /// <summary>Übersetzt in die Netzoptionen der API-Schicht.</summary>
    /// <remarks>
    /// <para>Der Weg führt bewusst nur in diese Richtung: Die API-Schicht kennt weder Dateipfade
    /// noch Erinnerungszeiten und soll das auch nicht.</para>
    /// <para><b>Das Proxy-Kennwort muss von aussen kommen.</b> In der Konfiguration steht nur
    /// ein Verweis, und dieses Stück hat keinen Zugang zum Schlüsselspeicher. Ohne diesen
    /// Parameter ginge bei einem Proxy mit Anmeldung eine leere Zeichenkette als Kennwort über
    /// die Leitung — der Proxy wiese ab, und die Meldung sähe aus wie ein Problem mit TANSS.</para>
    /// </remarks>
    /// <param name="proxyPassword">Das aufgelöste Proxy-Kennwort, oder <see langword="null"/>.</param>
    /// <returns>Die Netzoptionen.</returns>
    public TanssOptions ToTanssOptions(string? proxyPassword = null) => new()
    {
        BaseUrl = Tanss.BaseUrl,
        EmployeeId = Tanss.EmployeeId,
        Timeout = TimeSpan.FromSeconds(Tanss.TimeoutSeconds),
        VerifyTls = Tanss.VerifyTls,
        Proxy = Proxy.Enabled
            ? new ProxyOptions
            {
                Address = Proxy.Address,
                Port = Proxy.Port,
                User = Proxy.User,
                Password = proxyPassword,
            }
            : null,
    };

    /// <summary>Prüft die geladene Konfiguration und wirft bei Verstößen.</summary>
    /// <param name="origin">Woher sie stammt — steht in der Fehlermeldung.</param>
    /// <exception cref="ConfigValidationException">Mindestens eine Regel ist verletzt.</exception>
    public void Validate(string? origin = null) => ConfigValidator.Validate(this, origin);
}

/// <summary>Zugang zur TANSS-Instanz.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TanssSection
{
    /// <summary>
    /// Basisadresse einschließlich <c>/backend</c>, ohne Schrägstrich am Ende.
    /// </summary>
    /// <remarks>
    /// Zeigt sie stattdessen auf die Weboberfläche, antwortet die PHP-Oberfläche auf
    /// <b>jede</b> Anfrage mit HTTP 400 — auch ohne Token. Der Fehler sieht dann wie ein
    /// kaputtes Werkzeug aus und nicht wie eine falsche Adresse, deshalb wird er schon beim
    /// Laden abgefangen.
    /// </remarks>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Mitarbeiter-ID des Technikers.
    /// </summary>
    /// <remarks>
    /// Sie entscheidet, <b>wessen</b> Arbeitszeiten gelesen und wessen Lücken gezeigt werden.
    /// Steht hier die falsche Kennung, prüft das Werkzeug den Tag eines Kollegen.
    /// </remarks>
    public required int EmployeeId { get; init; }

    /// <summary>Verweis auf das Arbeitstoken, nicht das Token selbst.</summary>
    public string TokenRef { get; init; } = "dpapi:credentials";

    /// <summary>
    /// Wie viele Tage vor Ablauf das Token erneuert wird.
    /// </summary>
    /// <remarks>
    /// Großzügig: Ein Techniker, der drei Wochen im Urlaub war, soll sein Werkzeug starten
    /// können, ohne sich neu anmelden zu müssen.
    /// </remarks>
    public int RotateBeforeDays { get; init; } = 60;

    /// <summary>TLS-Zertifikat prüfen. Nur für Testinstanzen abschaltbar.</summary>
    public bool VerifyTls { get; init; } = true;

    /// <summary>Zeitüberschreitung einzelner Aufrufe in Sekunden.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Die eigene Firma und das Bundesland.
/// </summary>
/// <remarks>
/// <b>Warum das gespeichert wird, statt es jedes Mal zu ermitteln.</b> Der Weg dorthin führt
/// über zwei Abfragen an TANSS und eine an einen fremden Dienst. Keine davon ändert ihr
/// Ergebnis von Woche zu Woche — die eigene Firma zieht nicht um. Einmal ermittelt, einmal
/// bestätigt, dann steht es hier; erneut gefragt wird nur auf Anweisung.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompanySection
{
    /// <summary>Die Kennung der eigenen Firma in TANSS; <c>0</c>, solange sie unbekannt ist.</summary>
    public int OwnCompanyId { get; init; }

    /// <summary>Der Name der eigenen Firma — nur zur Anzeige.</summary>
    public string? OwnCompanyName { get; init; }

    /// <summary>Die Postleitzahl, aus der das Bundesland bestimmt wurde.</summary>
    public string? PostalCode { get; init; }

    /// <summary>
    /// Das Kürzel des Bundeslands, etwa <c>HE</c>; <see langword="null"/>, solange keines feststeht.
    /// </summary>
    /// <remarks>
    /// <b>Ohne dieses Kürzel gibt es keine Feiertage</b>, und ohne Feiertage meldet das Werkzeug
    /// an Weihnachten einen fehlenden Arbeitstag. Steht es nicht fest, sagt die Oberfläche das
    /// ausdrücklich, statt still weiterzurechnen.
    /// </remarks>
    public string? FederalState { get; init; }

    /// <summary>
    /// Wurde das Bundesland von Hand gesetzt?
    /// </summary>
    /// <remarks>
    /// Dann wird es <b>nicht</b> mehr selbsttätig überschrieben. Wer eine Niederlassung in
    /// einem anderen Bundesland betreut als die, in der die Firma gemeldet ist, hat dafür einen
    /// Grund, und eine spätere Erkennung darf ihn nicht stillschweigend übergehen.
    /// </remarks>
    public bool FederalStateIsManual { get; init; }
}

/// <summary>Was als Lücke gilt.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GapSection
{
    /// <summary>
    /// Kürzer als dies wird nicht als Lücke gemeldet, in Minuten.
    /// </summary>
    /// <remarks>
    /// Fünfzehn Minuten, weil das in TANSS die übliche Abrechnungseinheit ist. Ohne Schwelle
    /// stünden dreissig Einträge von je vier Minuten in der Liste, und die eine Stunde, um die
    /// es wirklich geht, ginge darin unter.
    /// </remarks>
    public int MinimumMinutes { get; init; } = 15;

    /// <summary>
    /// Gilt ein nur regional geltender Feiertag als freier Tag?
    /// </summary>
    /// <remarks>
    /// Betrifft Fronleichnam in Sachsen und Thüringen, Mariä Himmelfahrt in Bayern und das
    /// Augsburger Friedensfest. Vorgabe ist ja — die Begründung steht bei
    /// <c>GapOptions.ConditionalHolidayCountsAsDayOff</c>.
    /// </remarks>
    public bool ConditionalHolidayIsDayOff { get; init; } = true;

    /// <summary>
    /// Die Wochentage, an denen gearbeitet wird — leer heißt „nicht gesetzt“.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum diese Angabe hier steht und nicht aus TANSS kommt.</b> Nachgemessen am
    /// 14.09.2026 gegen 10.10.0 nennt TANSS zu einem Arbeitszeitmodell Kennung und Namen, aber
    /// keinen Wochenplan; vielen Mitarbeitern ist ohnehin keines zugeordnet. Ohne diese Angabe
    /// muss das Werkzeug Montag bis Freitag annehmen — und sagt das dann auch an jedem Tag.</para>
    /// <para>Erlaubt sind die englischen Namen der Wochentage
    /// (<c>MONDAY</c> … <c>SUNDAY</c>), wie sie auch TANSS verwendet. Gross- und
    /// Kleinschreibung ist gleichgültig.</para>
    /// </remarks>
    /// <remarks>
    /// <b>Leer als Vorgabe, und das ist Absicht.</b> Eine hinterlegte Vorgabe wäre eine
    /// Vermutung, die niemand je bestätigt — und genau die soll es hier nicht geben. Die
    /// Konfiguration lädt ohne diese Angabe nicht; die Eingabemaske schlägt Montag bis Freitag
    /// vor, sodass das Bestätigen einen Klick kostet.
    /// </remarks>
    public IReadOnlyList<string> WorkDays { get; init; } = [];

    /// <summary>
    /// Der übliche Arbeitsbeginn, <c>HH:mm</c>; leer heißt „nicht angegeben“.
    /// </summary>
    /// <remarks>
    /// <para><b>Hieran hängt der teuerste blinde Fleck dieses Werkzeugs.</b> Lücken entstehen
    /// sonst nur <i>innerhalb</i> gestempelter Anwesenheit: Wer um 11:08 einstempelt, obwohl er
    /// um 8:00 da war, hat für die drei Stunden davor gar keinen Stempel — und damit auch keine
    /// Lücke. Mit einem Arbeitsbeginn wird dieser Zeitraum zum offenen Zeitfenster.</para>
    /// <para>Gilt für alle unter <see cref="WorkDays"/> gewählten Tage.</para>
    /// </remarks>
    public string WorkBegin { get; init; } = string.Empty;

    /// <summary>Das übliche Arbeitsende, <c>HH:mm</c>; leer heißt „nicht angegeben“.</summary>
    /// <remarks>
    /// <b>Ohne Ende kein Rahmen.</b> Ein Beginn allein ergäbe ein offenes Zeitfenster bis
    /// Mitternacht. Deshalb wirken beide Werte nur zusammen.
    /// </remarks>
    public string WorkEnd { get; init; } = string.Empty;

    /// <summary>Wie viele Tage rückwärts die Übersicht zeigt.</summary>
    /// <remarks>
    /// Vierzehn Tage decken zwei Wochen ab — genug, um nach einem Urlaub aufzuräumen, und wenig
    /// genug, dass die Abfrage schnell bleibt.
    /// </remarks>
    public int HistoryDays { get; init; } = 14;
}

/// <summary>Wann geprüft und wann erinnert wird.</summary>
/// <remarks>
/// <b>Zwei Zeitpunkte mit verschiedenen Fragen.</b> Morgens geht es um <i>gestern</i> — der Tag
/// ist vorbei, die Zeiten stehen fest, was fehlt, fehlt endgültig. Abends geht es um
/// <i>heute</i> — noch ist der Techniker da und kann es nachtragen, ohne sich zu erinnern, was
/// er vorgestern getan hat. Der zweite Zeitpunkt ist der wertvollere; der erste fängt auf, was
/// der zweite verpasst hat.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReminderSection
{
    /// <summary>Die morgendliche Prüfung des Vortags einschalten.</summary>
    public bool MorningEnabled { get; init; } = true;

    /// <summary>Uhrzeit der morgendlichen Prüfung, <c>HH:mm</c>.</summary>
    public string MorningTime { get; init; } = "08:30";

    /// <summary>Die abendliche Erinnerung einschalten.</summary>
    public bool EveningEnabled { get; init; } = true;

    /// <summary>
    /// Uhrzeit der abendlichen Erinnerung, <c>HH:mm</c>.
    /// </summary>
    /// <remarks>
    /// Kurz <b>vor</b> Feierabend und nicht danach: Eine Erinnerung, die kommt, während der
    /// Rechner schon heruntergefahren wird, erreicht niemanden.
    /// </remarks>
    public string EveningTime { get; init; } = "16:45";

    /// <summary>
    /// An Tagen ohne Lücke still bleiben.
    /// </summary>
    /// <remarks>
    /// <b>Vorgabe ist ja.</b> Eine Meldung „alles in Ordnung“, die jeden Tag erscheint, wird
    /// nach einer Woche weggeklickt, ohne gelesen zu werden — und dann auch die, bei der etwas
    /// dransteht.
    /// </remarks>
    public bool OnlyWhenSomethingIsOpen { get; init; } = true;
}

/// <summary>Vorgeschalteter Proxy.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProxySection
{
    /// <summary>Wird ein Proxy benutzt?</summary>
    public bool Enabled { get; init; }

    /// <summary>Adresse des Proxys.</summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>Port des Proxys.</summary>
    public int Port { get; init; } = 8080;

    /// <summary>Anmeldename, falls der Proxy einen verlangt.</summary>
    public string? User { get; init; }

    /// <summary>Verweis auf das Kennwort, nicht das Kennwort selbst.</summary>
    public string PasswordRef { get; init; } = "dpapi:proxy";
}

/// <summary>Protokollierung.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoggingSection
{
    /// <summary>Protokollstufe: <c>Trace</c>, <c>Debug</c>, <c>Information</c>, <c>Warning</c>, <c>Error</c>.</summary>
    public string Level { get; init; } = "Information";
}

/// <summary>
/// Die Sprachmodell-Unterstützung beim Erfassen einer Leistung.
/// </summary>
/// <remarks>
/// <para><b>Hier verlässt zum ersten Mal etwas das Haus.</b> Alles übrige in diesem Werkzeug
/// spricht ausschliesslich mit der TANSS-Instanz des Kunden. Wird diese Unterstützung
/// eingeschaltet, geht der Leistungstext an einen Anbieter in den Vereinigten Staaten —
/// mit allem, was darin steht: Zielsystem, Gerätename, Arbeitsplatz, Techniker.</para>
///
/// <para><b>Deshalb ist sie abgeschaltet und bleibt es ohne ausdrückliche Einwilligung.</b>
/// <see cref="Enabled"/> allein genügt nicht; ohne <see cref="ConsentGivenAt"/> sendet das
/// Werkzeug nichts. Die Einwilligung wird mit Zeitpunkt und Windows-Benutzer festgehalten,
/// damit später nachweisbar ist, wer sie wann erteilt hat — das ist der Zweck der beiden
/// Felder, nicht Buchhaltung.</para>
///
/// <para><b>Was das rechtlich bedeutet</b>, entscheidet nicht dieses Werkzeug: Eine Übermittlung
/// personenbezogener Daten in ein Drittland verlangt eine Rechtsgrundlage, einen
/// Auftragsverarbeitungsvertrag mit dem Anbieter und in aller Regel die Beteiligung von
/// Datenschutzbeauftragtem und Mitbestimmung. Die Einwilligungsmaske sagt das; entschieden
/// wird es ausserhalb.</para>
/// </remarks>
public sealed record AiSection
{
    /// <summary>Soll die Unterstützung überhaupt angeboten werden?</summary>
    public bool Enabled { get; init; }

    /// <summary>Der Anbieter: <c>anthropic</c> oder <c>openai</c>.</summary>
    public string Provider { get; init; } = "anthropic";

    /// <summary>
    /// Die Kennung des Modells, etwa <c>claude-opus-5</c>.
    /// </summary>
    /// <remarks>
    /// Leer, solange keines gewählt wurde. Die Auswahl kommt aus der Modellliste des Anbieters
    /// und steht bewusst nicht fest im Programm: Modelle kommen und gehen, und ein fest
    /// eingebauter Name wäre an dem Tag falsch, an dem der Anbieter ihn abkündigt.
    /// </remarks>
    public string Model { get; init; } = string.Empty;

    /// <summary>Verweis auf den Schlüssel; er selbst liegt DPAPI-verschlüsselt im Profil.</summary>
    public string KeyRef { get; init; } = "dpapi:ai";

    /// <summary>
    /// Wann die Einwilligung erteilt wurde; <c>null</c> heisst: gar nicht.
    /// </summary>
    /// <remarks>
    /// Als Zeichenkette in ISO-Schreibweise und nicht als Zeitstempeltyp, damit die Datei von
    /// Hand lesbar bleibt — sie ist im Zweifel ein Nachweis.
    /// </remarks>
    public string? ConsentGivenAt { get; init; }

    /// <summary>Der Windows-Benutzer, der die Einwilligung erteilt hat.</summary>
    public string? ConsentBy { get; init; }

    /// <summary>
    /// Namen von Zielsystemen und Arbeitsplätzen vor dem Senden unkenntlich machen.
    /// </summary>
    /// <remarks>
    /// Standardmässig an, und das ist mehr als Beiwerk: Für eine Rechtschreibprüfung muss kein
    /// Anbieter wissen, dass der Kunde einen Server namens <c>sap</c> betreibt. Ersetzt werden
    /// die bekannten Namen vor dem Senden durch Platzhalter und danach wieder zurück — was
    /// zurückkommt, liest sich unverändert.
    /// </remarks>
    public bool RedactIdentifiers { get; init; } = true;

    /// <summary>Liegt eine erteilte Einwilligung vor?</summary>
    /// <summary>
    /// Die Rolle, die dem Modell vorangestellt wird; leer heisst: der eingebaute Text.
    /// </summary>
    /// <remarks>
    /// <para><b>Bearbeitbar, weil die Berichte eures Hauses sind.</b> Wie ein Bericht klingen
    /// soll, weiss der Betrieb und nicht dieses Werkzeug — ob geduzt oder gesiezt wird, ob
    /// Fachbegriffe stehen bleiben, ob der Kunde ein Krankenhaus oder eine Werkstatt ist.</para>
    /// <para><b>Die vier unverhandelbaren Regeln bleiben trotzdem.</b> Sie werden beim Senden
    /// angehängt und lassen sich hier nicht entfernen: nichts erfinden, keine Zahlen und Namen
    /// ändern, die Sprache behalten, ausschliesslich mit dem Text antworten. Die ersten beiden
    /// sind die Zusage, auf die hin eingewilligt wurde; die letzte ist Technik, ohne sie
    /// landete eine Vorrede des Modells mitten im Bericht.</para>
    /// </remarks>
    public string? RolePrompt { get; init; }

    /// <summary>Der Auftrag für die Rechtschreibprüfung; leer heisst: der eingebaute Text.</summary>
    public string? ProofreadPrompt { get; init; }

    /// <summary>Der Auftrag für das Ausformulieren; leer heisst: der eingebaute Text.</summary>
    public string? ImprovePrompt { get; init; }

    [JsonIgnore]
    public bool HasConsent => !string.IsNullOrWhiteSpace(ConsentGivenAt);

    /// <summary>
    /// Darf tatsächlich gesendet werden?
    /// </summary>
    /// <remarks>
    /// Die einzige Stelle, an der diese Frage beantwortet wird. Drei Bedingungen, und alle drei
    /// müssen gelten: eingeschaltet, eingewilligt, ein Modell gewählt.
    /// </remarks>
    [JsonIgnore]
    public bool IsUsable => Enabled && HasConsent && !string.IsNullOrWhiteSpace(Model);
}
