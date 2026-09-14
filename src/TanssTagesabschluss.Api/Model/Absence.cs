using System.Text.Json;
using System.Text.Json.Serialization;

namespace TanssTagesabschluss.Api.Model;

/// <summary>Die Zustände, die ein Abwesenheitsantrag durchläuft.</summary>
/// <remarks>Zeichenketten statt Aufzählung — Begründung wie bei <see cref="TimestampType"/>.</remarks>
public static class AbsenceStatus
{
    /// <summary>Angelegt, aber noch nicht eingereicht.</summary>
    public const string New = "NEW";

    /// <summary>Eingereicht und noch nicht entschieden.</summary>
    public const string Requested = "REQUESTED";

    /// <summary>Genehmigt. <b>Nur dieser Zustand erklärt eine Lücke.</b></summary>
    public const string Approved = "APPROVED";

    /// <summary>Abgelehnt.</summary>
    public const string Declined = "DECLINED";
}

/// <summary>
/// Ein Abwesenheitsantrag: Urlaub, Krankheit, Sonderurlaub, Überstundenausgleich.
/// </summary>
/// <remarks>
/// <para><b>Warum das Werkzeug sie überhaupt liest, obwohl die Zeiterfassung sie auch kennt.</b>
/// Ein genehmigter Urlaub steht im Antrag ab dem Tag der Genehmigung; als Zeitstempel steht er
/// erst da, wenn der Tag vorbei und gestempelt ist. Für die abendliche Erinnerung und für einen
/// Blick auf den morgigen Tag ist der Antrag die einzige Quelle.</para>
/// <para><b>Ein halber Tag ist ein halber Tag.</b> <see cref="AbsenceDay"/> trägt
/// <c>forenoon</c>, <c>afternoon</c> und wahlweise eine Uhrzeitspanne. Wer einen Antrag als
/// ganztägig behandelt, verliert genau die Nachmittage, an denen tatsächlich gearbeitet und
/// nichts erfasst wurde.</para>
/// </remarks>
public sealed record AbsenceRequest
{
    /// <summary>Die Kennung des Antrags.</summary>
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>Die Art — <c>VACATION</c>, <c>ILLNESS</c>, <c>ABSENCE</c>, <c>OVERTIME</c>, …</summary>
    [JsonPropertyName("planningType")] public string? PlanningType { get; init; }

    /// <summary>Der Zustand; siehe <see cref="AbsenceStatus"/>.</summary>
    [JsonPropertyName("status")] public string? Status { get; init; }

    /// <summary>Beginn in Unix-Sekunden.</summary>
    [JsonPropertyName("startDate")] public long StartDate { get; init; }

    /// <summary>Ende in Unix-Sekunden.</summary>
    [JsonPropertyName("endDate")] public long EndDate { get; init; }

    /// <summary>Der Antragsteller.</summary>
    [JsonPropertyName("requesterId")] public int RequesterId { get; init; }

    /// <summary>Die Begründung des Antragstellers.</summary>
    [JsonPropertyName("requestReason")] public string? RequestReason { get; init; }

    /// <summary>Der Vorgesetzte, der entschieden hat.</summary>
    [JsonPropertyName("supervisorId")] public int SupervisorId { get; init; }

    /// <summary>Die Zusatzart bei <c>ABSENCE</c> — Sonderurlaub, Kur, Home-Office.</summary>
    [JsonPropertyName("planningAdditionalId")] public int PlanningAdditionalId { get; init; }

    /// <summary>Die einzelnen Tage des Antrags.</summary>
    [JsonPropertyName("days")] public IReadOnlyList<AbsenceDay> Days { get; init; } = [];

    /// <summary>Ist dieser Antrag genehmigt?</summary>
    /// <remarks>
    /// <b>Nur ein genehmigter Antrag erklärt eine Lücke.</b> Ein Antrag im Zustand
    /// <see cref="AbsenceStatus.Requested"/> ist ein Wunsch — und an einem Tag, an dem trotzdem
    /// gearbeitet wurde, wäre er die falsche Erklärung und liesse die vergessene Leistung
    /// unentdeckt.
    /// </remarks>
    [JsonIgnore]
    public bool IsApproved =>
        string.Equals(Status, AbsenceStatus.Approved, StringComparison.OrdinalIgnoreCase);

    /// <summary>Der Eintrag zu einem Tag; <see langword="null"/>, wenn der Antrag ihn nicht führt.</summary>
    /// <param name="day">Der gesuchte Tag.</param>
    /// <returns>Der Tageseintrag, oder <see langword="null"/>.</returns>
    public AbsenceDay? DayOn(DateOnly day) => Days.FirstOrDefault(entry => entry.Day == day);
}

/// <summary>Ein einzelner Tag eines Abwesenheitsantrags.</summary>
/// <remarks>
/// <b>Die Kombination der Felder ist nicht offensichtlich.</b> Ein ganzer Tag trägt
/// <c>forenoon</c> und <c>afternoon</c>; ein halber nur eines von beiden; ein stundenweiser
/// Antrag statt dessen eine Uhrzeitspanne. Die Beschreibung sagt zu den Stundenfeldern
/// ausdrücklich „if only a certain timeframe is used“ — sie sind also nur dann gesetzt.
/// <see cref="Coverage"/> fasst die drei Fälle zusammen, damit die Unterscheidung an genau
/// einer Stelle steht.
/// </remarks>
public sealed record AbsenceDay
{
    /// <summary>Der Antrag, zu dem dieser Tag gehört.</summary>
    [JsonPropertyName("vacationRequestId")] public int VacationRequestId { get; init; }

    /// <summary>Der Tag in Unix-Sekunden; TANSS setzt ihn auf 0:00 Uhr.</summary>
    [JsonPropertyName("date")] public long Date { get; init; }

    /// <summary>Gilt der Antrag für den ganzen Vormittag?</summary>
    [JsonPropertyName("forenoon")] public bool Forenoon { get; init; }

    /// <summary>Gilt der Antrag für den ganzen Nachmittag?</summary>
    [JsonPropertyName("afternoon")] public bool Afternoon { get; init; }

    /// <summary>Beginn der Uhrzeitspanne, falls eine benutzt wird.</summary>
    [JsonPropertyName("startHour")] public int StartHour { get; init; }

    /// <summary>Beginnminute der Uhrzeitspanne.</summary>
    [JsonPropertyName("startMinute")] public int StartMinute { get; init; }

    /// <summary>Ende der Uhrzeitspanne.</summary>
    [JsonPropertyName("endHour")] public int EndHour { get; init; }

    /// <summary>Endminute der Uhrzeitspanne.</summary>
    [JsonPropertyName("endMinute")] public int EndMinute { get; init; }

    /// <summary>Pause innerhalb der Uhrzeitspanne, in Minuten.</summary>
    [JsonPropertyName("pause")] public int Pause { get; init; }

    /// <summary>
    /// Der Tag als Datum — in <b>Ortszeit</b> gelesen.
    /// </summary>
    /// <remarks>
    /// TANSS setzt den Zeitstempel auf Mitternacht der Serverzeitzone. Würde er als UTC
    /// gelesen, verschöbe sich ein Urlaubstag in der deutschen Sommerzeit auf den Vortag
    /// 22 Uhr — und damit auf das falsche Datum.
    /// </remarks>
    [JsonIgnore]
    public DateOnly Day => DateOnly.FromDateTime(
        (TanssTime.FromUnixSeconds(Date) ?? DateTimeOffset.UnixEpoch).ToLocalTime().DateTime);

    /// <summary>Wie viel dieses Tages die Abwesenheit belegt.</summary>
    [JsonIgnore]
    public AbsenceCoverage Coverage
    {
        get
        {
            if (Forenoon && Afternoon)
            {
                return AbsenceCoverage.FullDay;
            }

            if (Forenoon)
            {
                return AbsenceCoverage.Forenoon;
            }

            if (Afternoon)
            {
                return AbsenceCoverage.Afternoon;
            }

            // Weder Vor- noch Nachmittag: Dann traegt der Tag eine Uhrzeitspanne - oder gar
            // nichts, und dann belegt er nichts.
            return HasTimeSpan ? AbsenceCoverage.TimeSpan : AbsenceCoverage.None;
        }
    }

    /// <summary>Trägt dieser Tag eine auswertbare Uhrzeitspanne?</summary>
    /// <remarks>
    /// Verlangt wird ein <b>Ende nach dem Beginn</b>. Vier Nullen sind keine Spanne, sondern
    /// der Normalfall eines ganztägigen Antrags — und als Spanne gelesen ergäben sie eine
    /// Abwesenheit von 0:00 bis 0:00.
    /// </remarks>
    [JsonIgnore]
    public bool HasTimeSpan =>
        ((EndHour * 60) + EndMinute) > ((StartHour * 60) + StartMinute);

    /// <summary>Der Beginn der Uhrzeitspanne; <see langword="null"/>, wenn es keine gibt.</summary>
    [JsonIgnore]
    public TimeOnly? Start =>
        HasTimeSpan && StartHour is >= 0 and < 24 && StartMinute is >= 0 and < 60
            ? new TimeOnly(StartHour, StartMinute)
            : null;

    /// <summary>Das Ende der Uhrzeitspanne; <see langword="null"/>, wenn es keine gibt.</summary>
    [JsonIgnore]
    public TimeOnly? End =>
        HasTimeSpan && EndHour is >= 0 and < 24 && EndMinute is >= 0 and < 60
            ? new TimeOnly(EndHour, EndMinute)
            : null;
}

/// <summary>Wie viel eines Tages eine Abwesenheit belegt.</summary>
public enum AbsenceCoverage
{
    /// <summary>Gar nichts — der Tag ist im Antrag, belegt aber keine Zeit.</summary>
    None,

    /// <summary>Der ganze Tag.</summary>
    FullDay,

    /// <summary>Nur der Vormittag.</summary>
    Forenoon,

    /// <summary>Nur der Nachmittag.</summary>
    Afternoon,

    /// <summary>Eine ausdrückliche Uhrzeitspanne.</summary>
    TimeSpan,
}

/// <summary>
/// Die Antwort von <c>PUT /api/v1/vacationRequests/list</c>.
/// </summary>
/// <remarks>
/// <para><b>Die Beschreibung und die Instanz sind sich hier nicht einig, und das ist kein
/// Schönheitsfehler gewesen.</b> <c>api-doc-10.10.0.yaml</c> sagt zu dieser Route
/// <c>content: type: array</c> — eine blanke Liste von Anträgen. Nachgemessen am 14.09.2026
/// gegen 10.10.0 kommt statt dessen ein Objekt:</para>
/// <code>
/// "content": {
///   "vacationRequests": [ … ],
///   "employeeSummaries": { "1": { "employeeId": 1, "vacationDaysForYear": { … } } }
/// }
/// </code>
/// <para>Gegen <c>List&lt;AbsenceRequest&gt;</c> gelesen ergab das keinen leeren Urlaub,
/// sondern einen Lesefehler — und der ist die freundlichere der beiden Möglichkeiten. Wäre er
/// irgendwo verschluckt worden, hätte das Werkzeug jeden Urlaubstag als Lücke gemeldet, und
/// jemand hätte Leistungen für Tage gesucht, an denen er am Strand lag.</para>
/// <para><b>Gelesen werden deshalb beide Formen</b> — die beschriebene und die gemessene. Auf
/// welcher Version die Instanz des Kunden steht, entscheidet nicht dieses Werkzeug.</para>
/// </remarks>
[JsonConverter(typeof(AbsenceListConverter))]
public sealed record AbsenceList
{
    /// <summary>Die Anträge; niemals <see langword="null"/>.</summary>
    public IReadOnlyList<AbsenceRequest> Requests { get; init; } = [];
}

/// <summary>
/// Liest die Antwort der Abwesenheitsliste in ihren beiden bekannten Formen.
/// </summary>
/// <remarks>
/// <b>Was nicht zu erkennen ist, wird geworfen und nicht übergangen.</b> Anders als bei
/// <see cref="PeriodMapConverter"/>, wo ein unlesbarer Eintrag eine Art kostet: Hier ginge
/// stillschweigend der ganze Urlaub verloren, und das Ergebnis wäre eine Mahnung an einem
/// Urlaubstag. Eine Ausnahme führt statt dessen zu einer Zeile in der Verbindungsprüfung, die
/// sagt, was ankam.
/// </remarks>
public sealed class AbsenceListConverter : JsonConverter<AbsenceList>
{
    /// <summary>Der Name des Feldes, unter dem die gemessene Form die Anträge führt.</summary>
    private const string Field = "vacationRequests";

    /// <inheritdoc />
    public override AbsenceList Read(ref Utf8JsonReader reader, Type typeToConvert,
                                     JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Die beschriebene Form: eine blanke Liste.
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return new AbsenceList
            {
                Requests = JsonSerializer.Deserialize<List<AbsenceRequest>>(ref reader, options) ?? [],
            };
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return new AbsenceList();
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                "Die Abwesenheitsliste kam weder als Feld noch als Objekt an, sondern als "
                + $"{reader.TokenType}. Damit lässt sich nicht entscheiden, ob jemand Urlaub "
                + "hatte — und ein geratener Urlaub wäre schlimmer als gar keine Antwort.");
        }

        // Die gemessene Form: ein Objekt, in dem die Antraege unter "vacationRequests"
        // stehen. Daneben liegt "employeeSummaries" mit Jahresurlaubskonten; die liest dieses
        // Werkzeug nicht, weil es sie nicht auswertet.
        using JsonDocument document = JsonDocument.ParseValue(ref reader);

        if (!document.RootElement.TryGetProperty(Field, out JsonElement list))
        {
            throw new JsonException(
                $"Die Abwesenheitsliste kam als Objekt ohne das Feld „{Field}“ an. Gemessen "
                + "gegen 10.10.0 steht dort die Liste der Anträge; ohne sie ist unbekannt, ob "
                + "jemand Urlaub hatte.");
        }

        return new AbsenceList
        {
            Requests = list.Deserialize<List<AbsenceRequest>>(options) ?? [],
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Dieses Werkzeug schreibt keine Abwesenheiten — es liest sie nur, um eine Lücke zu
    /// erklären. Ein Schreibweg hier wäre eine Einladung, das später zu ändern.
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, AbsenceList value,
                               JsonSerializerOptions options) =>
        throw new NotSupportedException(
            "Abwesenheiten werden von diesem Werkzeug ausschliesslich gelesen.");
}

/// <summary>Der Filterrumpf von <c>PUT /api/v1/vacationRequests/list</c>.</summary>
/// <remarks>
/// Es müssen nicht alle Felder gesetzt werden; TANSS wertet aus, was da ist. Dieses Werkzeug
/// fragt nach Jahr, Monat und dem eigenen Mitarbeiter.
/// </remarks>
public sealed record AbsenceQuery
{
    /// <summary>Das Jahr, etwa 2026.</summary>
    [JsonPropertyName("year")] public int Year { get; init; }

    /// <summary>Der Monat, 1 bis 12.</summary>
    [JsonPropertyName("month")] public int Month { get; init; }

    /// <summary>Die Mitarbeiter, nach denen gefiltert wird.</summary>
    [JsonPropertyName("employeeIds")]
    public IReadOnlyList<int> EmployeeIds { get; init; } = [];
}

/// <summary>Eine Zusatzart zu einer Abwesenheit — Sonderurlaub, Kur, Home-Office.</summary>
public sealed record PlanningAdditionalType
{
    /// <summary>Die Kennung.</summary>
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>Der angezeigte Name, etwa <c>Home-Office</c>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>
    /// Soll diese Art berechnet werden?
    /// </summary>
    /// <remarks>
    /// <b>Der entscheidende Schalter für dieses Werkzeug.</b> Home-Office ist eine Abwesenheit
    /// im Sinne der Planung, aber gearbeitete Zeit im Sinne der Abrechnung — dort sind
    /// Leistungen sehr wohl zu erwarten. Eine Art mit <c>charged = true</c> entschuldigt eine
    /// Lücke deshalb <b>nicht</b>.
    /// </remarks>
    [JsonPropertyName("charged")] public bool Charged { get; init; }

    /// <summary><c>ABSENCE</c> oder <c>CUSTOM</c>.</summary>
    [JsonPropertyName("type")] public string? Type { get; init; }

    /// <summary>Ist die Art noch in Gebrauch?</summary>
    [JsonPropertyName("active")] public bool Active { get; init; }
}
