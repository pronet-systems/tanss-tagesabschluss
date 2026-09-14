using System.Globalization;
using System.Text.Json;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.Api.Repository;

/// <summary>
/// Die Zeiterfassung: Stempel, Abschnitte und Arbeitszeitmodelle.
/// </summary>
/// <remarks>
/// <para><b>Diese Klasse liest, und sie schreibt nie.</b> Es gibt in ihr keinen Weg, einen
/// Zeitstempel zu setzen oder einen Tag abzuschliessen, und das ist kein Versehen: Die
/// Arbeitszeit eines Menschen ist nichts, was ein Werkzeug zur Leistungskontrolle nebenbei
/// ändert. Wer hier einen Schreibweg ergänzt, ändert die Grundlage einer Lohnabrechnung.</para>
///
/// <para><b>Inhalt und Umschlag gehören zusammen.</b> Die Tage stehen im <c>content</c>, die
/// Arbeitszeitmodelle im <c>meta</c> — und ohne Modell ist ein leerer Tag nicht deutbar: Er kann
/// ein vergessener Arbeitstag sein oder ein freier Samstag. Deshalb geht beides in einem
/// <see cref="TimeRecording"/> heraus und nicht getrennt.</para>
/// </remarks>
public sealed class TimestampRepository : ITimestampRepository
{
    private const string ListProperties = "listProperties";
    private const string WorkingTimeModels = "workingTimeModels";
    private const string Extras = "extras";
    private const string Model = "model";

    private readonly ITanssClient _client;

    /// <summary>Baut das Repository.</summary>
    /// <param name="client">
    /// Der HTTP-Zugang. Er muss zusätzlich <see cref="ITanssMetaRead"/> erfüllen — ohne den
    /// <c>meta</c>-Block gibt es keine Arbeitszeitmodelle.
    /// </param>
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

        if (_client is not ITanssMetaRead metaRead)
        {
            // Kein Rueckfallweg: Ohne meta gibt es keine Arbeitszeitmodelle, und ohne die
            // waere jeder leere Samstag eine gemeldete Luecke.
            throw new InvalidOperationException(
                "Dieser HTTP-Zugang gibt den meta-Block nicht heraus, deshalb lassen sich die "
                + "Arbeitszeitmodelle nicht lesen. Das ist ein Programmierfehler, kein "
                + "Bedienfehler: Das Modell eines Tages steht ausschliesslich unter "
                + "meta.listProperties.workingTimeModels. Dem "
                + $"{nameof(TimestampRepository)} ist ein Zugang zu übergeben, der "
                + $"{nameof(ITanssMetaRead)} erfüllt — der mitgelieferte TanssClient tut das.");
        }

        Timeframe span = Span(from, until);
        Dictionary<string, string?> query = new(StringComparer.Ordinal)
        {
            ["from"] = span.From.ToString(CultureInfo.InvariantCulture),
            ["till"] = span.To.ToString(CultureInfo.InvariantCulture),
            ["employeeIds"] = employeeId.ToString(CultureInfo.InvariantCulture),
        };

        (List<TimestampDay>? days, IReadOnlyDictionary<string, object?> meta) = await metaRead
            .GetWithMetaAsync<List<TimestampDay>>(TanssRoutes.TimestampStatistics, query, ct)
            .ConfigureAwait(false);

        if (days is null or { Count: 0 })
        {
            return TimeRecording.Empty;
        }

        return new TimeRecording(
            [.. days.OrderBy(day => day.Date)],
            ReadModels(meta));
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

    /// <summary>
    /// Liest die Arbeitszeitmodelle aus <c>meta.listProperties.workingTimeModels</c>.
    /// </summary>
    /// <remarks>
    /// <para>Der Weg ist verschachtelt und in der Beschreibung nur als Beispiel belegt:
    /// <c>listProperties</c> → <c>workingTimeModels</c> → Kennung als Schlüssel →
    /// <c>extras</c> → <c>model</c>. Jede Stufe kann fehlen.</para>
    /// <para><b>Was sich nicht lesen lässt, fehlt einfach.</b> Geworfen wird hier nicht: Ein
    /// Tag ohne Modell wird von der Fachschicht als „Sollzeit unbekannt“ behandelt, und das ist
    /// allemal besser, als wegen eines geänderten Umschlags gar keine Zeiten anzuzeigen.</para>
    /// </remarks>
    private static Dictionary<int, WorkingTimeModel> ReadModels(
        IReadOnlyDictionary<string, object?> meta)
    {
        Dictionary<int, WorkingTimeModel> models = [];

        if (!meta.TryGetValue(ListProperties, out object? raw)
            || raw is not JsonElement properties
            || properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty(WorkingTimeModels, out JsonElement byId)
            || byId.ValueKind != JsonValueKind.Object)
        {
            return models;
        }

        foreach (JsonProperty entry in byId.EnumerateObject())
        {
            if (!int.TryParse(entry.Name, NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out int id))
            {
                continue;
            }

            if (ReadModel(entry.Value) is { } model)
            {
                // Die Kennung aus dem Schluessel hat Vorrang vor der im Modell: Sie ist die,
                // unter der der Tag sein Modell sucht.
                models[id] = model;
            }
        }

        return models;
    }

    /// <summary>Liest ein einzelnes Modell aus seinem <c>extras.model</c>-Block.</summary>
    private static WorkingTimeModel? ReadModel(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty(Extras, out JsonElement extras)
            || extras.ValueKind != JsonValueKind.Object
            || !extras.TryGetProperty(Model, out JsonElement model)
            || model.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return model.Deserialize<WorkingTimeModel>(Http.TanssJson.Options);
        }
        catch (JsonException)
        {
            // Siehe Kommentar an ReadModels: ein unlesbares Modell kostet die Sollzeit
            // dieses einen Modells, nicht die Zeiten des Mitarbeiters.
            return null;
        }
    }
}
