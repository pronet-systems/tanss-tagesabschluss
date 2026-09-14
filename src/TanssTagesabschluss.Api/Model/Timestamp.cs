using System.Text.Json;
using System.Text.Json.Serialization;

namespace TanssTagesabschluss.Api.Model;

/// <summary>
/// Die Zustände eines Zeitstempels — als Zeichenketten und nicht als Aufzählung.
/// </summary>
/// <remarks>
/// <b>Warum keine <c>enum</c>:</b> Ein unbekannter Wert aus einer späteren TANSS-Fassung brächte
/// beim Einlesen einer Aufzählung die <b>ganze</b> Antwort zu Fall — und damit den ganzen Tag,
/// nicht nur den einen Stempel. Ein Tag, der wegen eines neuen Zustands gar nicht mehr angezeigt
/// wird, ist schlechter als einer, in dem ein Abschnitt unbekannter Art steht.
/// </remarks>
public static class TimestampState
{
    /// <summary>Kommen.</summary>
    public const string On = "ON";

    /// <summary>Gehen.</summary>
    public const string Off = "OFF";

    /// <summary>Pausenbeginn.</summary>
    public const string PauseStart = "PAUSE_START";

    /// <summary>Pausenende.</summary>
    public const string PauseEnd = "PAUSE_END";
}

/// <summary>
/// Die Arten eines Zeitstempels beziehungsweise eines Abschnitts — als Zeichenketten.
/// </summary>
/// <remarks>Begründung wie bei <see cref="TimestampState"/>.</remarks>
public static class TimestampType
{
    /// <summary>Gewöhnliche Arbeitszeit. Der Abschnitt, in dem Lücken überhaupt entstehen.</summary>
    public const string Work = "WORK";

    /// <summary>Arbeit im Haus.</summary>
    public const string Inhouse = "INHOUSE";

    /// <summary>Dienstgang.</summary>
    public const string Errand = "ERRAND";

    /// <summary>Urlaub.</summary>
    public const string Vacation = "VACATION";

    /// <summary>Krankheit.</summary>
    public const string Illness = "ILLNESS";

    /// <summary>Bezahlte Abwesenheit.</summary>
    public const string AbsencePaid = "ABSENCE_PAID";

    /// <summary>Unbezahlte Abwesenheit.</summary>
    public const string AbsenceUnpaid = "ABSENCE_UNPAID";

    /// <summary>Überstundenausgleich.</summary>
    public const string Overtime = "OVERTIME";

    /// <summary>
    /// Die Zeit, die TANSS selbst als durch Leistungen gedeckt ausgerechnet hat.
    /// </summary>
    /// <remarks>
    /// Keine gestempelte Art, sondern ein Rechenergebnis des Servers. Dieses Werkzeug benutzt
    /// sie als <b>Gegenprobe</b> zur eigenen Rechnung — siehe <c>TanssRoutes.TimestampStatistics</c>.
    /// </remarks>
    public const string DocumentedSupport = "DOCUMENTED_SUPPORT";

    /// <summary>
    /// Zählt diese Art als Anwesenheit, in der eine Leistung zu erwarten wäre?
    /// </summary>
    /// <remarks>
    /// <para><see cref="Work"/>, <see cref="Inhouse"/> und <see cref="Errand"/> sind gearbeitete
    /// Zeit. Urlaub, Krankheit und Abwesenheit sind es nicht — dort ist eine „Lücke“ keine,
    /// sondern der Normalfall, und sie auszuweisen hiesse, den Techniker im Urlaub an seine
    /// Leistungserfassung zu erinnern.</para>
    /// <para><see cref="DocumentedSupport"/> ist ausdrücklich <b>keine</b> Anwesenheit: Die Art
    /// beschreibt die Deckung und nicht die Zeit, in der jemand da war. Sie hier mitzuzählen
    /// machte jede Lücke unsichtbar, die sie gerade aufdecken soll.</para>
    /// </remarks>
    /// <param name="type">Die Art, wie TANSS sie schreibt.</param>
    /// <returns><c>true</c>, wenn in diesem Abschnitt Leistungen zu erwarten sind.</returns>
    public static bool IsAttendance(string? type) =>
        string.Equals(type, Work, StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, Inhouse, StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, Errand, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Erklärt diese Art eine fehlende Leistung, ohne dass jemand etwas vergessen hätte?
    /// </summary>
    /// <param name="type">Die Art, wie TANSS sie schreibt.</param>
    /// <returns><c>true</c> bei Urlaub, Krankheit, Abwesenheit und Überstundenausgleich.</returns>
    public static bool IsAbsence(string? type) =>
        string.Equals(type, Vacation, StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, Illness, StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, AbsencePaid, StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, AbsenceUnpaid, StringComparison.OrdinalIgnoreCase)
        || string.Equals(type, Overtime, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Ein einzelner Zeitstempel, so wie TANSS ihn führt.</summary>
public sealed record TanssTimestamp
{
    /// <summary>Die Kennung in TANSS.</summary>
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>Der Mitarbeiter, dem dieser Stempel gehört.</summary>
    [JsonPropertyName("employeeId")] public int EmployeeId { get; init; }

    /// <summary>Der Zeitpunkt in Unix-<b>Sekunden</b>.</summary>
    [JsonPropertyName("date")] public long Date { get; init; }

    /// <summary>Kommen, Gehen, Pausenbeginn, Pausenende — siehe <see cref="TimestampState"/>.</summary>
    [JsonPropertyName("state")] public string? State { get; init; }

    /// <summary>Arbeit, Urlaub, Krankheit — siehe <see cref="TimestampType"/>.</summary>
    [JsonPropertyName("type")] public string? Type { get; init; }

    /// <summary>Der Zeitpunkt als Datum. Siehe <see cref="TanssTime"/>.</summary>
    [JsonIgnore] public DateTimeOffset? At => TanssTime.FromUnixSeconds(Date);
}

/// <summary>
/// Ein Abschnitt zwischen zwei Stempeln: von <c>ON</c> (oder <c>PAUSE_END</c>) bis <c>OFF</c>
/// (oder <c>PAUSE_START</c>).
/// </summary>
public sealed record TimestampPeriod
{
    /// <summary>Beginn in Unix-Sekunden.</summary>
    [JsonPropertyName("begin")] public long Begin { get; init; }

    /// <summary>Ende in Unix-Sekunden.</summary>
    [JsonPropertyName("end")] public long End { get; init; }

    /// <summary>Dauer in Sekunden, wie TANSS sie gerechnet hat.</summary>
    [JsonPropertyName("seconds")] public long Seconds { get; init; }

    /// <summary><c>ONGOING</c>, <c>COMPLETE</c> oder <c>PAUSED</c>.</summary>
    [JsonPropertyName("state")] public string? State { get; init; }

    /// <summary>Läuft dieser Abschnitt noch?</summary>
    /// <remarks>
    /// Ein laufender Abschnitt hat ein <c>end</c>, das TANSS auf das Tagesende setzt — er darf
    /// deshalb <b>nicht</b> bis dorthin auf Lücken geprüft werden. Wer um 10 Uhr nachsieht, hat
    /// bis 24 Uhr keine Leistung erfasst, und das ist keine Lücke, sondern die Zukunft.
    /// </remarks>
    [JsonIgnore]
    public bool IsOngoing => string.Equals(State, "ONGOING", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Ein Tag eines Mitarbeiters, so wie <c>/timestamps/statistics</c> und <c>/timestamps/info</c>
/// ihn liefern.
/// </summary>
public sealed record TimestampDay
{
    /// <summary>Der Mitarbeiter.</summary>
    [JsonPropertyName("employeeId")] public int EmployeeId { get; init; }

    /// <summary>Der Tag. TANSS schreibt ihn als <c>YYYY-mm-dd</c>.</summary>
    [JsonPropertyName("date")] public DateOnly Date { get; init; }

    /// <summary>Der Wochentag, wie TANSS ihn benennt — <c>THRUSDAY</c> eingeschlossen.</summary>
    /// <remarks>
    /// <b>Der Tippfehler ist echt.</b> Die Beschreibung zu 10.10.0 führt den Donnerstag als
    /// <c>THRUSDAY</c>. Wer hier <c>THURSDAY</c> erwartet, findet den Donnerstag nie — siehe
    /// <see cref="WorkingTimeModel.DayFor"/>, das beide Schreibweisen annimmt.
    /// </remarks>
    [JsonPropertyName("weekDay")] public string? WeekDay { get; init; }

    /// <summary>
    /// Das Arbeitszeitmodell dieses Tages. Das Modell selbst steht im <c>meta</c>-Block.
    /// </summary>
    /// <remarks>
    /// <c>0</c> ist eine gültige Kennung und bedeutet nicht „keines“ — das Beispiel der
    /// Beschreibung führt ein Modell mit der Kennung 0 und dem Namen <c>DEFAULT</c>.
    /// </remarks>
    [JsonPropertyName("workingTimeModelId")] public int WorkingTimeModelId { get; init; }

    /// <summary>Alle Stempel dieses Tages.</summary>
    [JsonPropertyName("timestamps")]
    public IReadOnlyList<TanssTimestamp> Timestamps { get; init; } = [];

    /// <summary>
    /// Die Abschnitte, nach Art gruppiert.
    /// </summary>
    /// <remarks>
    /// <para><b>Die Beschreibung widerspricht ihrem eigenen Beispiel.</b> Das Schema sagt zu
    /// jedem Eintrag <c>type: object</c> — also ein Abschnitt je Art —, das Beispiel derselben
    /// Datei zeigt ein Feld mit mehreren. Und mehrere sind der Normalfall: Wer mittags Pause
    /// macht, hat zwei <c>WORK</c>-Abschnitte.</para>
    /// <para>Gelesen werden deshalb <b>beide</b> Formen; siehe
    /// <see cref="PeriodMapConverter"/>. Ein einzelnes Objekt wird zur Liste mit einem
    /// Eintrag.</para>
    /// </remarks>
    [JsonPropertyName("types")]
    [JsonConverter(typeof(PeriodMapConverter))]
    public IReadOnlyDictionary<string, IReadOnlyList<TimestampPeriod>> Types { get; init; } =
        new Dictionary<string, IReadOnlyList<TimestampPeriod>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Die Abschnitte einer Art; eine leere Liste, wenn es keine gibt.</summary>
    /// <param name="type">Die Art, etwa <see cref="TimestampType.Work"/>.</param>
    /// <returns>Die Abschnitte; niemals <see langword="null"/>.</returns>
    public IReadOnlyList<TimestampPeriod> PeriodsOf(string type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Types.TryGetValue(type, out IReadOnlyList<TimestampPeriod>? periods)
            ? periods
            : [];
    }

    /// <summary>
    /// Alle Abschnitte, in denen jemand anwesend war — Arbeit, Haus, Dienstgang zusammen.
    /// </summary>
    /// <remarks>
    /// Ungeordnet zusammengetragen und <b>nicht</b> verschmolzen: Das Verschmelzen überlappender
    /// Abschnitte gehört in die Fachschicht, die auch die Pausen abzieht.
    /// </remarks>
    /// <returns>Die Abschnitte aller anwesenden Arten.</returns>
    public IReadOnlyList<TimestampPeriod> AttendancePeriods() =>
        [.. Types.Where(pair => TimestampType.IsAttendance(pair.Key)).SelectMany(pair => pair.Value)];

    /// <summary>Ist dieser Tag ganz oder teilweise durch eine Abwesenheit belegt?</summary>
    /// <returns><c>true</c>, wenn mindestens ein Abschnitt Urlaub, Krankheit oder Abwesenheit ist.</returns>
    public bool HasAbsence() =>
        Types.Any(pair => TimestampType.IsAbsence(pair.Key) && pair.Value.Count > 0);
}

/// <summary>
/// Liest <c>types</c>: eine Abbildung von der Art auf einen <b>oder mehrere</b> Abschnitte.
/// </summary>
/// <remarks>
/// Eigener Konverter statt eines <c>Dictionary&lt;string, List&lt;TimestampPeriod&gt;&gt;</c>,
/// weil TANSS je Art entweder ein Objekt oder ein Feld schreibt — siehe
/// <see cref="TimestampDay.Types"/>. Was sich nicht lesen lässt, wird übergangen und nicht
/// geworfen: Ein unbekannter Eintrag kostet dann eine Art, nicht den ganzen Tag.
/// </remarks>
public sealed class PeriodMapConverter
    : JsonConverter<IReadOnlyDictionary<string, IReadOnlyList<TimestampPeriod>>>
{
    /// <inheritdoc />
    public override IReadOnlyDictionary<string, IReadOnlyList<TimestampPeriod>> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Dictionary<string, IReadOnlyList<TimestampPeriod>> map =
            new(StringComparer.OrdinalIgnoreCase);

        if (reader.TokenType == JsonTokenType.Null)
        {
            return map;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                "Der Block \"types\" der Zeitauswertung ist kein Objekt. Erwartet wird eine "
                + "Abbildung von der Zeitstempel-Art auf ihre Abschnitte.");
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (ReadPeriods(property.Value, options) is { Count: > 0 } periods)
            {
                map[property.Name] = periods;
            }
        }

        return map;
    }

    /// <summary>
    /// Schreibt die Abbildung zurück — in der Feldform, die TANSS im Beispiel zeigt.
    /// </summary>
    /// <remarks>
    /// Gebraucht wird das Schreiben allein von den Tests und vom Zwischenspeicher; an TANSS
    /// geht dieser Block nie zurück, weil die Zeitauswertung eine reine Leseroute ist.
    /// </remarks>
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer,
                               IReadOnlyDictionary<string, IReadOnlyList<TimestampPeriod>> value,
                               JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();

        foreach (KeyValuePair<string, IReadOnlyList<TimestampPeriod>> pair in value)
        {
            writer.WritePropertyName(pair.Key);
            JsonSerializer.Serialize(writer, pair.Value, options);
        }

        writer.WriteEndObject();
    }

    private static List<TimestampPeriod> ReadPeriods(JsonElement element,
                                                     JsonSerializerOptions options)
    {
        List<TimestampPeriod> periods = [];

        try
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (JsonElement entry in element.EnumerateArray())
                    {
                        if (entry.Deserialize<TimestampPeriod>(options) is { } period)
                        {
                            periods.Add(period);
                        }
                    }

                    break;

                case JsonValueKind.Object:
                    if (element.Deserialize<TimestampPeriod>(options) is { } single)
                    {
                        periods.Add(single);
                    }

                    break;

                default:
                    // Weder Objekt noch Feld: eine Art, die dieses Werkzeug nicht kennt.
                    // Uebergangen und nicht geworfen - siehe Klassenkommentar.
                    break;
            }
        }
        catch (JsonException)
        {
            // Dieselbe Ueberlegung: der Verlust einer Art ist ertraeglich, der eines Tages nicht.
            return [];
        }

        return periods;
    }
}
