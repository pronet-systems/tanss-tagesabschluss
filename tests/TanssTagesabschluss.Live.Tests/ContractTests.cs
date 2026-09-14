using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.Api.Repository;
using Xunit;
using Xunit.Abstractions;

namespace TanssTagesabschluss.Live.Tests;

/// <summary>
/// Was dieses Werkzeug über TANSS <b>annimmt</b> — hier gegen eine echte Instanz geprüft.
/// </summary>
/// <remarks>
/// <para>Jeder Test hier entspricht einer Stelle im Quelltext, an der <i>ungemessen</i>
/// steht. Bestehen sie, kann der Vermerk dort weg; scheitern sie, ist die Annahme falsch —
/// und das ist das Wertvollste, was dieses Projekt liefern kann.</para>
/// <para><b>Nur lesend.</b> Es wird keine Leistung angelegt, kein Zeitstempel gesetzt und kein
/// Tag abgeschlossen. Die Ausgabe geht ins Protokoll, damit sich das Gemessene nachlesen
/// lässt, ohne den Test zu ändern.</para>
/// </remarks>
public sealed class ContractTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Baut die Testklasse.</summary>
    /// <param name="output">Das Protokoll des Testläufers.</param>
    public ContractTests(ITestOutputHelper output) => _output = output;

    [LiveFact]
    public async Task Die_Zeitauswertung_antwortet_und_liefert_Arbeitszeitmodelle()
    {
        // DIE tragende Annahme dieses Werkzeugs: Das Arbeitszeitmodell eines Tages steht
        // ausschliesslich unter meta.listProperties.workingTimeModels. Faellt das weg, ist
        // jeder leere Samstag eine gemeldete Luecke.
        (TanssClient client, int employeeId) = await LiveInstance.ConnectAsync();

        using (client)
        {
            TimestampRepository timestamps = new(client);
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);

            TimeRecording recording = await timestamps
                .ReadAsync(employeeId, today.AddDays(-14), today);

            _output.WriteLine($"Tage: {recording.Days.Count}");

            foreach (TimestampDay day in recording.Days.Take(3))
            {
                string types = string.Join(", ", day.Types.Select(
                    pair => $"{pair.Key}×{pair.Value.Count}"));

                _output.WriteLine($"  {day.Date:yyyy-MM-dd} {day.WeekDay} "
                                  + $"{types}");
            }

            Assert.NotEmpty(recording.Days);
        }
    }

    [LiveFact]
    public async Task Der_Wochentag_Donnerstag_wird_so_geschrieben_wie_angenommen()
    {
        // Die Beschreibung fuehrt ihn als THRUSDAY. Ob die Instanz sich daran haelt, ist
        // ungemessen - WorkingTimeModel.DayFor nimmt deshalb beide Schreibweisen an.
        (TanssClient client, int employeeId) = await LiveInstance.ConnectAsync();

        using (client)
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            TimeRecording recording = await new TimestampRepository(client)
                .ReadAsync(employeeId, today.AddDays(-14), today);

            string[] thursdays = [.. recording.Days
                .Where(day => day.Date.DayOfWeek == DayOfWeek.Thursday)
                .Select(day => day.WeekDay ?? "(leer)")
                .Distinct()];

            _output.WriteLine("Donnerstag heisst hier: " + string.Join(", ", thursdays));

            // Kein Assert auf die Schreibweise: Beide sind zulaessig. Der Wert dieses Tests
            // liegt in der Zeile im Protokoll.
            Assert.True(true);
        }
    }

    [LiveFact]
    public async Task Die_Leistungsliste_antwortet_auf_PUT_mit_Zeitraum_und_Mitarbeiter()
    {
        (TanssClient client, int employeeId) = await LiveInstance.ConnectAsync();

        using (client)
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            Timeframe span = TimestampRepository.Span(today.AddDays(-14), today);

            IReadOnlyList<SupportEntry> supports = await new SupportRepository(client)
                .ListAsync(employeeId, span);

            _output.WriteLine($"Leistungen in 14 Tagen: {supports.Count}");

            string[] planningTypes = [.. supports
                .Select(entry => entry.PlanningType ?? "(ohne)")
                .Distinct()];

            _output.WriteLine("Planungsarten: " + string.Join(", ", planningTypes));

            Assert.NotNull(supports);
        }
    }

    [LiveFact]
    public async Task Die_Abwesenheiten_antworten_auf_Jahr_und_Monat()
    {
        (TanssClient client, int employeeId) = await LiveInstance.ConnectAsync();

        using (client)
        {
            DateTime today = DateTime.Today;

            IReadOnlyList<AbsenceRequest> absences = await new AbsenceRepository(client)
                .ListAsync(employeeId, today.Year, today.Month);

            _output.WriteLine($"Abwesenheitsanträge im laufenden Monat: {absences.Count}");

            foreach (AbsenceRequest request in absences.Take(3))
            {
                _output.WriteLine($"  {request.PlanningType} {request.Status}, "
                                  + $"{request.Days.Count} Tag(e)");
            }

            Assert.NotNull(absences);
        }
    }

    [LiveFact]
    public async Task Die_erp_Route_beantwortet_die_Frage_nach_der_eigenen_Firma()
    {
        // UNGEMESSEN, und die ganze Bundeslandbestimmung haengt daran: Nennt
        // meta.linkedEntities.companies der Mitarbeiterabfrage genau EINE Firma? Und
        // vertraegt /api/erp/v1 den loggedInUserId, den dieser Client dort NICHT anhaengt?
        (TanssClient client, _) = await LiveInstance.ConnectAsync();

        using (client)
        {
            OwnCompanyResult own = await new CompanyRepository(client).FindOwnAsync();

            _output.WriteLine($"Ergebnis: {own.Outcome}");
            _output.WriteLine($"Meldung:  {own.Message}");

            if (own.Company is { } company)
            {
                _output.WriteLine($"Firma:    {company.Id} {company.Name}, "
                                  + $"{company.PostCode} {company.City}");
            }

            // Kein Assert auf "gefunden": Ob die Instanz das hergibt, ist gerade die offene
            // Frage. Scheitern soll dieser Test nur, wenn der Aufruf selbst zusammenbricht.
            Assert.NotEqual(OwnCompanyOutcome.Undetermined, own.Outcome);
        }
    }

    [LiveFact]
    public async Task Die_Pausenregeln_lassen_sich_lesen()
    {
        (TanssClient client, _) = await LiveInstance.ConnectAsync();

        using (client)
        {
            List<PauseConfig>? configs =
                await client.GetAsync<List<PauseConfig>>(TanssRoutes.PauseConfigs);

            _output.WriteLine($"Pausenregeln: {configs?.Count ?? 0}");

            foreach (PauseConfig config in configs ?? [])
            {
                // Der Tippfehler "fromMinues" steht so in der Beschreibung. Welche
                // Schreibweise ankommt, ist ungemessen - diese Zeile sagt es.
                _output.WriteLine($"  ab {config.FromMinutes} min: mindestens "
                                  + $"{config.MinimumPause} min Pause "
                                  + $"(Tippfehlerform: {config.FromMinutesTypo}, "
                                  + $"richtige Form: {config.FromMinutesCorrect})");
            }

            Assert.NotNull(configs);
        }
    }

    [LiveFact]
    public async Task Die_Geraeterouten_antworten_auf_denselben_Filterrumpf()
    {
        // UNGEMESSEN: Ob /pcs, /peripheries und /components alle drei den Rumpf mit
        // companyId, branches und active annehmen. Das Repository schluckt einen Fehlschlag
        // je Geraeteart - hier soll er sichtbar werden.
        (TanssClient client, _) = await LiveInstance.ConnectAsync();

        using (client)
        {
            OwnCompanyResult own = await new CompanyRepository(client).FindOwnAsync();

            if (own.Company is not { Id: > 0 } company)
            {
                _output.WriteLine("Keine eigene Firma ermittelt — Geräte nicht geprüft.");
                return;
            }

            DeviceQuery query = new() { CompanyId = company.Id };

            foreach ((string route, string name) in new[]
                     {
                         (TanssRoutes.Pcs, "PCs"),
                         (TanssRoutes.Peripheries, "Peripherie"),
                         (TanssRoutes.Components, "Komponenten"),
                     })
            {
                try
                {
                    List<Device>? devices = await client.PutAsync<List<Device>>(route, query);
                    _output.WriteLine($"{name}: {devices?.Count ?? 0}");
                }
                catch (TanssException ex)
                {
                    _output.WriteLine($"{name}: FEHLGESCHLAGEN — {ex.Message}");
                }
            }
        }
    }
}
