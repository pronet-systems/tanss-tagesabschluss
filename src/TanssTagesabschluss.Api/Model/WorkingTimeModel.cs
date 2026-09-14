using System.Text.Json.Serialization;

namespace TanssTagesabschluss.Api.Model;

/// <summary>
/// Das Arbeitszeitmodell eines Mitarbeiters: je Wochentag Beginn, Ende, Pause und Sollzeit.
/// </summary>
/// <remarks>
/// <para><b>Es kommt nicht im Inhalt, sondern im Umschlag.</b> Die Zeitauswertung nennt im
/// Inhalt nur die <c>workingTimeModelId</c>; das Modell selbst steht unter
/// <c>meta.listProperties.workingTimeModels[ID].extras.model</c>. Der Weg dorthin liegt in
/// <c>TimestampRepository</c>.</para>
///
/// <para><b>Wozu es gebraucht wird — und wozu nicht.</b> Die tatsächliche Anwesenheit steht in
/// den Zeitstempeln, und aus ihr entstehen die Lücken. Das Modell beantwortet die andere Frage:
/// Ist an diesem Tag überhaupt zu arbeiten? Ein Samstag mit <see cref="WorkingDay.WorkTimeInMinutes"/>
/// gleich 0 ist kein Arbeitstag, und ein Tag ohne jeden Stempel ist dort keine vergessene
/// Erfassung, sondern ein freier Tag.</para>
/// </remarks>
public sealed record WorkingTimeModel
{
    /// <summary>Die Kennung. <c>0</c> ist gültig und heißt nicht „keines“.</summary>
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>Der Name, etwa <c>DEFAULT</c>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>Die Wochentage, geschlüsselt wie TANSS sie schreibt (<c>MONDAY</c> …).</summary>
    [JsonPropertyName("days")]
    public IReadOnlyDictionary<string, WorkingDay> Days { get; init; } =
        new Dictionary<string, WorkingDay>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Der Eintrag zu einem Wochentag; <see langword="null"/>, wenn das Modell keinen führt.
    /// </summary>
    /// <remarks>
    /// <b>Nimmt <c>THRUSDAY</c> genauso an wie <c>THURSDAY</c>.</b> Die Beschreibung zu 10.10.0
    /// schreibt den Donnerstag mit vertauschten Buchstaben; ob die Instanz sich an ihre eigene
    /// Beschreibung hält, ist ungemessen. Beide Schreibweisen zu prüfen kostet nichts — die
    /// falsche Annahme kostete jeden Donnerstag.
    /// </remarks>
    /// <param name="day">Der gesuchte Wochentag.</param>
    /// <returns>Der Eintrag, oder <see langword="null"/>.</returns>
    public WorkingDay? DayFor(DayOfWeek day)
    {
        foreach (string name in NamesOf(day))
        {
            if (Days.TryGetValue(name, out WorkingDay? entry))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>Die Schreibweisen, unter denen TANSS einen Wochentag führen kann.</summary>
    /// <param name="day">Der Wochentag.</param>
    /// <returns>Eine oder zwei Schreibweisen, die wahrscheinlichste zuerst.</returns>
    public static IReadOnlyList<string> NamesOf(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => ["MONDAY"],
        DayOfWeek.Tuesday => ["TUESDAY"],
        DayOfWeek.Wednesday => ["WEDNESDAY"],
        // Der Tippfehler steht so in der Beschreibung und deshalb hier zuerst.
        DayOfWeek.Thursday => ["THRUSDAY", "THURSDAY"],
        DayOfWeek.Friday => ["FRIDAY"],
        DayOfWeek.Saturday => ["SATURDAY"],
        DayOfWeek.Sunday => ["SUNDAY"],
        _ => [],
    };
}

/// <summary>Ein Wochentag im Arbeitszeitmodell.</summary>
public sealed record WorkingDay
{
    /// <summary>Die vorgesehene Pause in Minuten.</summary>
    [JsonPropertyName("pause")] public int Pause { get; init; }

    /// <summary>
    /// Die Zeitfenster mit ihrem Faktor.
    /// </summary>
    /// <remarks>
    /// <b>Das sind nicht die Arbeitszeiten.</b> Im Beispiel der Beschreibung stehen hier
    /// <c>0:00–08:00</c> und <c>17:00–24:00</c> — also gerade die Fenster <i>ausserhalb</i> des
    /// Arbeitstags, der von <see cref="BeginOfDay"/> bis <see cref="EndOfDay"/> reicht. Wer
    /// diese Spannen für die Arbeitszeit hielte, bekäme den Tag verkehrt herum. Dieses Werkzeug
    /// liest sie mit, wertet sie aber nicht aus.
    /// </remarks>
    [JsonPropertyName("hours")]
    public IReadOnlyList<WorkingHourSpan> Hours { get; init; } = [];

    /// <summary>Beginn des Arbeitstags; fehlt an Tagen ohne Sollzeit.</summary>
    [JsonPropertyName("beginOfDay")] public ClockTime? BeginOfDay { get; init; }

    /// <summary>Ende des Arbeitstags; fehlt an Tagen ohne Sollzeit.</summary>
    [JsonPropertyName("endOfDay")] public ClockTime? EndOfDay { get; init; }

    /// <summary>Die Sollarbeitszeit dieses Tages in Minuten.</summary>
    [JsonPropertyName("workTimeInMinutes")] public int WorkTimeInMinutes { get; init; }

    /// <summary>
    /// Ist an diesem Tag überhaupt zu arbeiten?
    /// </summary>
    /// <remarks>
    /// Entschieden an der Sollzeit und nicht daran, ob <see cref="BeginOfDay"/> gesetzt ist:
    /// Im Beispiel der Beschreibung tragen Samstag und Sonntag zwar Zeitfenster, aber
    /// <see cref="WorkTimeInMinutes"/> gleich 0 und keinen Tagesbeginn. Die Sollzeit ist die
    /// Angabe, die in beiden Fällen dasteht.
    /// </remarks>
    [JsonIgnore] public bool IsWorkingDay => WorkTimeInMinutes > 0;
}

/// <summary>Ein Zeitfenster des Arbeitszeitmodells mit seinem Faktor.</summary>
public sealed record WorkingHourSpan
{
    /// <summary>Die Kennung der Angabe in TANSS.</summary>
    [JsonPropertyName("workingHourInfoId")] public int WorkingHourInfoId { get; init; }

    /// <summary>Die laufende Nummer innerhalb des Tages.</summary>
    [JsonPropertyName("number")] public int Number { get; init; }

    /// <summary>Beginn als Uhrzeittext, etwa <c>0:00</c> — mit und ohne führende Null.</summary>
    [JsonPropertyName("begin")] public string? Begin { get; init; }

    /// <summary>Ende als Uhrzeittext, etwa <c>08:00</c> oder <c>24:00</c>.</summary>
    [JsonPropertyName("end")] public string? End { get; init; }

    /// <summary>Der Faktor, mit dem diese Zeit gewertet wird.</summary>
    [JsonPropertyName("factor")] public double Factor { get; init; }
}

/// <summary>Eine Uhrzeit, wie das Arbeitszeitmodell sie schreibt: Stunden und Minuten getrennt.</summary>
/// <param name="Hrs">Die Stunde.</param>
/// <param name="Mins">Die Minute.</param>
public sealed record ClockTime(
    [property: JsonPropertyName("hrs")] int Hrs,
    [property: JsonPropertyName("mins")] int Mins)
{
    /// <summary>
    /// Die Uhrzeit als <see cref="TimeOnly"/>; <see langword="null"/>, wenn sie ausserhalb des
    /// Tages liegt.
    /// </summary>
    /// <remarks>
    /// <c>24:00</c> ist in TANSS eine gültige Schreibweise für das Tagesende, für
    /// <see cref="TimeOnly"/> aber keine gültige Uhrzeit. Der Aufrufer bekommt hier
    /// <see langword="null"/> und entscheidet selbst, ob er daraus Mitternacht des Folgetags
    /// macht — eine stillschweigende Wandlung in 00:00 ergäbe einen Tag von 8 Uhr bis 0 Uhr und
    /// damit eine negative Dauer.
    /// </remarks>
    [JsonIgnore]
    public TimeOnly? AsTimeOnly =>
        Hrs is >= 0 and < 24 && Mins is >= 0 and < 60 ? new TimeOnly(Hrs, Mins) : null;

    /// <summary>Die Uhrzeit in Minuten seit Mitternacht — auch für <c>24:00</c>.</summary>
    [JsonIgnore] public int TotalMinutes => (Hrs * 60) + Mins;
}
