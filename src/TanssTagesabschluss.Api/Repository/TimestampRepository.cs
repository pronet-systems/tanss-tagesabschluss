using System.Globalization;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.Api.Repository;

/// <summary>
/// Die Zeiterfassung: Stempel und die Abschnitte, die TANSS daraus rechnet.
/// </summary>
/// <remarks>
/// <para><b>Diese Klasse liest, und sie schreibt nie.</b> Es gibt in ihr keinen Weg, einen
/// Zeitstempel zu setzen oder einen Tag abzuschliessen, und das ist kein Versehen: Die
/// Arbeitszeit eines Menschen ist nichts, was ein Werkzeug zur Leistungskontrolle nebenbei
/// ändert. Wer hier einen Schreibweg ergänzt, ändert die Grundlage einer Lohnabrechnung.</para>
///
/// <para><b>Das Arbeitszeitmodell wird hier <i>nicht</i> gelesen, und das ist gemessen und
/// nicht versehentlich.</b> TANSS 10.10.0 gibt zu einem Modell nur Kennung und Namen heraus
/// (<c>meta.linkedEntities.employeeWorkingTimeModels</c>, etwa <c>{"1":{"name":"Kernzeit"}}</c>);
/// der Wochenplan, den die Beschreibung unter <c>meta.listProperties.workingTimeModels</c>
/// verspricht, fehlt in der Antwort ganz — geprüft am 14.09.2026 mit und ohne
/// <c>employeeIds</c> und für einen Mitarbeiter, dem ein Modell zugeordnet ist. Welche Tage und
/// Uhrzeiten gelten, kommt deshalb aus der Konfiguration; siehe <c>GapOptions.WorkWeek</c>.</para>
/// </remarks>
public sealed class TimestampRepository : ITimestampRepository
{

    private readonly ITanssClient _client;

    /// <summary>Baut das Repository.</summary>
    /// <param name="client">Der HTTP-Zugang.</param>
    public TimestampRepository(ITanssClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<TimeRecording> ReadAsync(int employeeId, DateOnly from, DateOnly until,
                                               CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(employeeId);

        if (until < from)
        {
            throw new ArgumentOutOfRangeException(
                nameof(until),
                "Der letzte Tag liegt vor dem ersten. Ein umgekehrter Zeitraum liefert bei TANSS "
                + "keine Fehlermeldung, sondern eine leere Antwort — und die sähe aus wie ein "
                + "Mitarbeiter, der nie gearbeitet hat.");
        }

        Timeframe span = Span(from, until);
        Dictionary<string, string?> query = new(StringComparer.Ordinal)
        {
            ["from"] = span.From.ToString(CultureInfo.InvariantCulture),
            ["till"] = span.To.ToString(CultureInfo.InvariantCulture),
            ["employeeIds"] = employeeId.ToString(CultureInfo.InvariantCulture),
        };

        List<TimestampDay>? days = await _client
            .GetAsync<List<TimestampDay>>(TanssRoutes.TimestampStatistics, query, ct)
            .ConfigureAwait(false);

        if (days is null or { Count: 0 })
        {
            return TimeRecording.Empty;
        }

        return new TimeRecording([.. days.OrderBy(day => day.Date)]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PauseConfig>> ListPauseConfigsAsync(
        CancellationToken ct = default)
    {
        try
        {
            List<PauseConfig>? configs = await _client
                .GetAsync<List<PauseConfig>>(TanssRoutes.PauseConfigs, ct: ct)
                .ConfigureAwait(false);

            return configs ?? [];
        }
        catch (OperationCanceledException)
        {
            // Ein Abbruch des Aufrufers ist kein Fehlschlag der Instanz und geht durch.
            throw;
        }
        catch (TanssException)
        {
            // Verfeinerung, kein Fundament: Ohne die Regeln wird eine ungestempelte
            // Mittagslueke als Luecke ausgewiesen. Das ist die vorsichtigere Richtung -
            // sie erzeugt eine Nachfrage zu viel, nie eine vergessene Leistung zu wenig.
            return [];
        }
    }

    /// <summary>
    /// Der Zeitraum zweier Kalendertage in <b>Ortszeit</b>, von 0:00 des ersten bis 23:59:59
    /// des letzten.
    /// </summary>
    /// <remarks>
    /// Öffentlich, weil genau diese Umrechnung die stille Fehlerquelle dieser Route ist: In UTC
    /// gerechnet begänne ein deutscher Sommertag um 22 Uhr des Vortags, und der erste Stempel
    /// des Morgens fiele aus dem Zeitraum — ohne Fehlermeldung, nur mit einem Tag weniger.
    /// </remarks>
    /// <param name="from">Erster Tag, einschließlich.</param>
    /// <param name="until">Letzter Tag, einschließlich.</param>
    /// <returns>Der Zeitraum in Unix-Sekunden.</returns>
    public static Timeframe Span(DateOnly from, DateOnly until)
    {
        Timeframe first = Timeframe.Day(from);
        Timeframe last = Timeframe.Day(until);
        return new Timeframe { From = first.From, To = last.To };
    }

}
