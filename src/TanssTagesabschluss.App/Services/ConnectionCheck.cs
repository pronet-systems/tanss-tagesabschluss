using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.Versioning;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Auth;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Diagnostics;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.Api.Repository;
using TanssTagesabschluss.App.ViewModels;
using TanssTagesabschluss.Workday.Holidays;

namespace TanssTagesabschluss.App.Services;

/// <summary>
/// Fragt TANSS nacheinander alles, was dieses Werkzeug im Betrieb braucht — und sagt zu jeder
/// Route, was tatsächlich zurückkam.
/// </summary>
/// <remarks>
/// <para><b>Das ist mehr als eine Verbindungsanzeige.</b> Keine der benutzten Routen ist bisher
/// gegen eine Instanz gemessen; alles stammt aus der Beschreibung zu 10.10.0. Diese Prüfung
/// misst sie — und zwar dort, wo der Techniker sowieso steht, statt in einem Testprojekt, das
/// niemand aufruft. Jede Zeile nennt deshalb ihre <b>Route</b> und nicht nur ein Ergebnis.</para>
///
/// <para><b>„Nicht geprüft“ ist eine eigene Stufe.</b> Wer sie wie „in Ordnung“ anzeigt,
/// behauptet Befunde, die niemand erhoben hat — und beendet die Fehlersuche, bevor sie
/// anfängt.</para>
///
/// <para><b>Es wird ausschliesslich gelesen.</b> Keine Leistung, kein Zeitstempel, kein
/// Tagesabschluss — und ausdrücklich auch <b>kein Token geprägt</b>: Ob der Mitarbeiter prägen
/// darf, liesse sich nur durch einen Prägeversuch feststellen, und TANSS 10.10.0 kann ein
/// ausgestelltes Token nicht widerrufen. Eine Prüfanzeige, die bei jedem Öffnen Token
/// ausstellt, wäre teurer als ihr Erkenntniswert.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ConnectionCheck
{
    private readonly TanssClient _client;
    private readonly int _employeeId;
    private readonly bool _verifyTls;
    private readonly IFederalStateResolver? _states;
    private readonly FederalState? _state;

    /// <summary>Baut die Prüfung.</summary>
    /// <param name="client">Der Zugang zu TANSS.</param>
    /// <param name="employeeId">Der Mitarbeiter, dessen Zeiten geprüft werden.</param>
    /// <param name="verifyTls">Ist die Zertifikatsprüfung eingeschaltet?</param>
    /// <param name="states">Die Postleitzahlenauskunft; <see langword="null"/>, wenn keine vorliegt.</param>
    /// <param name="state">Das eingestellte Bundesland; <see langword="null"/>, wenn keines feststeht.</param>
    public ConnectionCheck(TanssClient client, int employeeId, bool verifyTls,
                           IFederalStateResolver? states = null, FederalState? state = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _employeeId = employeeId;
        _verifyTls = verifyTls;
        _states = states;
        _state = state;
    }

    /// <summary>
    /// Läuft alle Prüfungen durch.
    /// </summary>
    /// <remarks>
    /// <b>Keine einzelne Prüfung darf die übrigen kosten.</b> Antwortet die Abwesenheitsroute
    /// nicht, soll trotzdem dastehen, dass die Zeiterfassung trägt — das ist die Auskunft, mit
    /// der jemand etwas anfangen kann.
    /// </remarks>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Ein Befund je geprüfter Sache, in der Reihenfolge des Aufbaus.</returns>
    public async Task<IReadOnlyList<CheckRow>> RunAsync(CancellationToken ct = default)
    {
        List<CheckRow> rows = [];

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);

        // 1. Erreichbarkeit. Die kleinste lesende Route, die es gibt - sie beantwortet
        //    zugleich, ob Basisadresse, Token und loggedInUserId zusammen tragen.
        (CheckLevel reach, string reachDetail) = await ProbeAsync(
            "GET " + TanssRoutes.PauseConfigs,
            async token => (await _client
                .GetAsync<List<PauseConfig>>(TanssRoutes.PauseConfigs, ct: token)
                .ConfigureAwait(false))?.Count ?? 0,
            count => count == 0
                ? "keine Pausenregel hinterlegt — die Route hat aber geantwortet"
                : string.Create(CultureInfo.CurrentCulture, $"{count} Pausenregel(n) gelesen"),
            ct).ConfigureAwait(false);

        rows.Add(new CheckRow("TANSS erreichbar", reach, reachDetail));

        bool reachable = reach == CheckLevel.Ok;

        // 2. Das Token. Ohne beantworteten Aufruf ist darueber NICHTS bekannt - dann steht
        //    hier "nicht geprueft" und nicht "traegt".
        rows.Add(TokenRow(reachable));

        // 3. Die Zeiterfassung. Die tragende Route dieses Werkzeugs: Ohne sie gibt es keine
        //    Anwesenheit und damit ueberhaupt keine Aussage.
        rows.Add(await ProbeRowAsync(
            "Zeiterfassung lesbar",
            "GET " + TanssRoutes.TimestampStatistics,
            reachable,
            async token =>
            {
                TimeRecording recording = await new TimestampRepository(_client)
                    .ReadAsync(_employeeId, today.AddDays(-7), today, token).ConfigureAwait(false);

                return string.Create(CultureInfo.CurrentCulture,
                    $"{recording.Days.Count} Tag(e) und {recording.Models.Count} Arbeitszeitmodell(e) "
                    + $"für Mitarbeiter {_employeeId} gelesen");
            },
            ct).ConfigureAwait(false));

        // 4. Die Arbeitszeitmodelle stehen im meta-Block. Faellt das weg, ist jeder leere
        //    Samstag eine gemeldete Luecke - deshalb eine eigene Zeile.
        rows.Add(await ModelRowAsync(reachable, today, ct).ConfigureAwait(false));

        // 5. Die Leistungen. Ohne sie waere jeder Tag eine einzige Luecke.
        rows.Add(await ProbeRowAsync(
            "Leistungen lesbar",
            "PUT " + TanssRoutes.SupportList,
            reachable,
            async token =>
            {
                IReadOnlyList<SupportEntry> supports = await new SupportRepository(_client)
                    .ListAsync(_employeeId, TimestampRepository.Span(today.AddDays(-7), today), token)
                    .ConfigureAwait(false);

                return string.Create(CultureInfo.CurrentCulture,
                    $"{supports.Count} Eintrag/Einträge der letzten sieben Tage gelesen");
            },
            ct).ConfigureAwait(false));

        // 6. Abwesenheiten. Erklaeren einen leeren Tag - ohne sie steht dort faelschlich
        //    "keine Zeit erfasst", wo in Wahrheit Urlaub war.
        rows.Add(await ProbeRowAsync(
            "Urlaub und Krankheit lesbar",
            "PUT " + TanssRoutes.VacationRequestList,
            reachable,
            async token =>
            {
                IReadOnlyList<AbsenceRequest> absences = await new AbsenceRepository(_client)
                    .ListAsync(_employeeId, today.Year, today.Month, token).ConfigureAwait(false);

                return string.Create(CultureInfo.CurrentCulture,
                    $"{absences.Count} Antrag/Anträge im laufenden Monat gelesen");
            },
            ct).ConfigureAwait(false));

        // 7. Die eigene Firma. DIE ungemessene Annahme dieses Werkzeugs: Nennt der meta-Block
        //    der Mitarbeiterabfrage genau eine Firma? Daran haengt das Bundesland.
        rows.Add(await OwnCompanyRowAsync(reachable, ct).ConfigureAwait(false));

        // 8. Tickets. Ohne sie bleibt die Auswahl beim Nachtragen leer - laestig, aber kein
        //    Hindernis: Gebucht werden kann auch ueber die Firma.
        rows.Add(await ProbeRowAsync(
            "Tickets lesbar",
            "PUT " + TanssRoutes.TicketSearch,
            reachable,
            async token =>
            {
                IReadOnlyList<Ticket> tickets = await new TicketRepository(_client)
                    .SearchOpenAsync(_employeeId, token).ConfigureAwait(false);

                return string.Create(CultureInfo.CurrentCulture,
                    $"{tickets.Count} offene(s) Ticket(s) gelesen");
            },
            ct,
            failLevel: CheckLevel.Warn).ConfigureAwait(false));

        // 9. Feiertage. Der einzige fremde Dienst, der im Betrieb laufend gefragt wird.
        rows.Add(await HolidayRowAsync(today, ct).ConfigureAwait(false));

        // 10. Die Zertifikatspruefung. Keine Messung, sondern eine Einstellung - aber die
        //     folgenschwerste, die es hier gibt.
        rows.Add(new CheckRow("Zertifikatsprüfung",
            _verifyTls ? CheckLevel.Ok : CheckLevel.Fail,
            _verifyTls
                ? "Eingeschaltet (tanss.verify_tls = true)."
                : "ABGESCHALTET. Das Arbeitstoken geht bei jedem Aufruf ungeprüft über diese "
                  + "Leitung, und TANSS kann ein ausgestelltes Token nicht widerrufen."));

        return rows;
    }

    /// <summary>Der Befund zum Arbeitstoken.</summary>
    /// <remarks>
    /// <b>Ohne beantworteten Aufruf steht hier „nicht geprüft“.</b> Die Restlaufzeit aus dem
    /// Token sagt nur, wann es abläuft — nicht, ob TANSS es annimmt. Genau diese Verwechslung
    /// liess im Schwesterprojekt ein kaputtes Token als „trägt“ erscheinen.
    /// </remarks>
    private static CheckRow TokenRow(bool reachable)
    {
        string token = Storage.Secrets.DpapiTokenStore.Default().Read();

        if (string.IsNullOrWhiteSpace(token))
        {
            return new CheckRow("Arbeitstoken", CheckLevel.Fail,
                "Es ist keines hinterlegt. Ohne Token nimmt TANSS keinen Aufruf an.");
        }

        try
        {
            double days = TanssAuth.DaysRemaining(token);

            if (days < 0)
            {
                return new CheckRow("Arbeitstoken", CheckLevel.Fail,
                    "Abgelaufen. Eine neue Anmeldung ist nötig.");
            }

            string expiry = string.Create(CultureInfo.CurrentCulture, $"noch {days:0} Tage gültig");

            return reachable
                ? new CheckRow("Arbeitstoken", days < 30 ? CheckLevel.Warn : CheckLevel.Ok,
                    $"Der Aufruf oben wurde angenommen; das Token trägt — {expiry}.")
                : new CheckRow("Arbeitstoken", CheckLevel.Unknown,
                    $"Nicht geprüft — ohne beantworteten Aufruf ist darüber nichts bekannt. "
                    + $"Laut Token selbst: {expiry}.");
        }
        catch (TanssAuthException ex)
        {
            return new CheckRow("Arbeitstoken", CheckLevel.Fail,
                "Das hinterlegte Token ist nicht lesbar: " + Redaction.Scrub(ex.Message));
        }
    }

    /// <summary>Der Befund zu den Arbeitszeitmodellen aus dem <c>meta</c>-Block.</summary>
    private async Task<CheckRow> ModelRowAsync(bool reachable, DateOnly today, CancellationToken ct)
    {
        if (!reachable)
        {
            return new CheckRow("Arbeitszeitmodell", CheckLevel.Unknown, NotProbed);
        }

        try
        {
            TimeRecording recording = await new TimestampRepository(_client)
                .ReadAsync(_employeeId, today.AddDays(-7), today, ct).ConfigureAwait(false);

            if (recording.Models.Count > 0)
            {
                string names = string.Join(", ", recording.Models.Values
                    .Select(model => model.Name ?? "(ohne Namen)").Distinct());

                return new CheckRow("Arbeitszeitmodell", CheckLevel.Ok,
                    $"meta.listProperties.workingTimeModels gelesen: {names}. Damit lässt sich "
                    + "ein freier Samstag von einem vergessenen Arbeitstag unterscheiden.");
            }

            return new CheckRow("Arbeitszeitmodell", CheckLevel.Warn,
                "Die Zeitauswertung hat geantwortet, aber kein Modell im meta-Block genannt. "
                + "Ohne Modell gilt Montag bis Freitag als Arbeitstag — das steht dann auch als "
                + "Hinweis am jeweiligen Tag.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException ex)
        {
            return new CheckRow("Arbeitszeitmodell", CheckLevel.Fail, Redaction.Scrub(ex.Message));
        }
    }

    /// <summary>Der Befund zur eigenen Firma — die Annahme, an der das Bundesland hängt.</summary>
    private async Task<CheckRow> OwnCompanyRowAsync(bool reachable, CancellationToken ct)
    {
        if (!reachable)
        {
            return new CheckRow("Eigene Firma", CheckLevel.Unknown, NotProbed);
        }

        try
        {
            OwnCompanyResult own = await new CompanyRepository(_client).FindOwnAsync(ct)
                .ConfigureAwait(false);

            CheckLevel level = own.Outcome switch
            {
                OwnCompanyOutcome.Found => CheckLevel.Ok,
                OwnCompanyOutcome.FoundWithoutAddress => CheckLevel.Warn,
                OwnCompanyOutcome.NotNamed => CheckLevel.Warn,
                _ => CheckLevel.Fail,
            };

            // Genannt wird der Weg, der den Vortritt hat. Greift er nicht, nennt die Meldung des
            // Ergebnisses selbst den Rueckfall - eine feste Route hier waere in genau dem Fall
            // die falsche.
            return new CheckRow("Eigene Firma", level,
                "GET " + TanssRoutes.OwnState + " — " + own.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException ex)
        {
            return new CheckRow("Eigene Firma", CheckLevel.Fail, Redaction.Scrub(ex.Message));
        }
    }

    /// <summary>Der Befund zum Feiertagsdienst.</summary>
    /// <remarks>
    /// Steht kein Bundesland fest, wird der Dienst gar nicht erst gefragt — und das ist der
    /// Befund: Ohne Bundesland gibt es keine Feiertage, und an Weihnachten erschiene der
    /// Hinweis „keine Zeit erfasst“.
    /// </remarks>
    private async Task<CheckRow> HolidayRowAsync(DateOnly today, CancellationToken ct)
    {
        if (_state is null)
        {
            return new CheckRow("Feiertage", CheckLevel.Warn,
                "Kein Bundesland gesetzt — deshalb keine Feiertage. An einem Feiertag ohne "
                + "Zeiterfassung erscheint dann ein Hinweis, der keiner sein müsste.");
        }

        if (_states is null)
        {
            return new CheckRow("Feiertage", CheckLevel.Unknown, NotProbed);
        }

        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };

        HolidaySet holidays = await new FeiertageApiProvider(http)
            .GetAsync(today.Year, _state, ct).ConfigureAwait(false);

        return holidays.IsKnown
            ? new CheckRow("Feiertage", CheckLevel.Ok, string.Create(CultureInfo.CurrentCulture,
                $"feiertage-api.de: {holidays.Count} Feiertage für {_state.Name} im Jahr {today.Year}."))
            : new CheckRow("Feiertage", CheckLevel.Warn,
                holidays.Note ?? "Nicht abrufbar.");
    }

    /// <summary>Eine Prüfzeile aus einem Aufruf, der eine Beschreibung zurückgibt.</summary>
    private static async Task<CheckRow> ProbeRowAsync(
        string name, string route, bool reachable,
        Func<CancellationToken, Task<string>> probe, CancellationToken ct,
        CheckLevel failLevel = CheckLevel.Fail)
    {
        if (!reachable)
        {
            return new CheckRow(name, CheckLevel.Unknown, NotProbed);
        }

        long started = Stopwatch.GetTimestamp();

        try
        {
            string detail = await probe(ct).ConfigureAwait(false);
            TimeSpan took = Stopwatch.GetElapsedTime(started);

            return new CheckRow(name, CheckLevel.Ok, string.Create(CultureInfo.CurrentCulture,
                $"{route} — {detail} ({took.TotalMilliseconds:0} ms)."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException ex)
        {
            return new CheckRow(name, failLevel, route + " — " + Redaction.Scrub(ex.Message));
        }
    }

    /// <summary>Die Erreichbarkeitsprüfung mit eigener Formulierung.</summary>
    private static async Task<(CheckLevel Level, string Detail)> ProbeAsync<T>(
        string route, Func<CancellationToken, Task<T>> probe, Func<T, string> describe,
        CancellationToken ct)
    {
        long started = Stopwatch.GetTimestamp();

        try
        {
            T result = await probe(ct).ConfigureAwait(false);
            TimeSpan took = Stopwatch.GetElapsedTime(started);

            return (CheckLevel.Ok, string.Create(CultureInfo.CurrentCulture,
                $"{route} — {describe(result)} ({took.TotalMilliseconds:0} ms)."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException ex)
        {
            return (CheckLevel.Fail, route + " — " + Redaction.Scrub(ex.Message));
        }
    }

    private const string NotProbed =
        "Nicht geprüft — solange TANSS nicht antwortet, sagt diese Zeile nichts aus.";
}
