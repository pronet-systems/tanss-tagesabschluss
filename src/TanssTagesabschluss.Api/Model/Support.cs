using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TanssTagesabschluss.Api.Model;

/// <summary>
/// Eine erfasste Leistung, so wie <c>PUT /api/v1/supports/list</c> sie liefert.
/// </summary>
/// <remarks>
/// <para><b>Nur die Felder, die dieses Werkzeug wirklich auswertet.</b> TANSS liefert über
/// achtzig; hier stehen die, aus denen sich der belegte Zeitraum, die Zuordnung und die Anzeige
/// ergeben. Wer ein weiteres braucht, ergänzt es — ungenutzte Felder mitzuschleppen heißt nur,
/// sie irgendwann falsch zu deuten.</para>
/// <para><b>Zum Lesen und nicht zum Zurückschreiben.</b> Eine Leistung, die über dieses Modell
/// liefe und dann wieder an TANSS ginge, verlöre die fünfundsiebzig Felder, die hier fehlen —
/// darunter Stundensatz und Abrechnungsart. Geschrieben wird ausschliesslich über
/// <see cref="SupportDraft"/>.</para>
/// </remarks>
public sealed record SupportEntry
{
    /// <summary>Die Kennung der Leistung.</summary>
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>Das zugehörige Ticket; <c>0</c> heißt „ohne Ticket“.</summary>
    [JsonPropertyName("ticketId")] public int TicketId { get; init; }

    /// <summary>Der <b>Beginn</b> der Leistung in Unix-Sekunden.</summary>
    [JsonPropertyName("date")] public long Date { get; init; }

    /// <summary>Die Dauer in <b>Minuten</b> — nicht in Sekunden wie die Zeitstempel.</summary>
    /// <remarks>
    /// Die beiden Einheiten stossen in diesem Werkzeug unmittelbar aufeinander: Der Zeitraum
    /// einer Leistung wird mit Abschnitten der Zeiterfassung verrechnet, und die zählen
    /// Sekunden. <see cref="Span"/> rechnet an genau einer Stelle um.
    /// </remarks>
    [JsonPropertyName("duration")] public int Duration { get; init; }

    /// <summary>Der Techniker, der die Leistung erbracht hat.</summary>
    [JsonPropertyName("employeeId")] public int EmployeeId { get; init; }

    /// <summary>Der Auftraggeber beim Kunden.</summary>
    [JsonPropertyName("remitterId")] public int RemitterId { get; init; }

    /// <summary>Die Firma, auf die gebucht wurde.</summary>
    [JsonPropertyName("companyId")] public int CompanyId { get; init; }

    /// <summary>Der Text, der auf der Rechnung steht.</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }

    /// <summary>Interne Bemerkung; der Kunde sieht sie nicht.</summary>
    [JsonPropertyName("textIntern")] public string? TextIntern { get; init; }

    /// <summary><c>OFFICE</c>, <c>CUSTOMER</c> oder <c>REMOTE</c>.</summary>
    [JsonPropertyName("location")] public string? Location { get; init; }

    /// <summary>
    /// Die Planungsart — <c>SUPPORT</c>, <c>APPOINTMENT_FIX</c>, <c>VACATION</c>, <c>ILLNESS</c> …
    /// </summary>
    /// <remarks>
    /// <b>Entscheidend für die Lückenrechnung.</b> Über dieselbe Route kommen auch Termine,
    /// Urlaube und Krankheiten zurück. Ein Terminvorschlag deckt keine Arbeitszeit ab; eine
    /// Abwesenheit erklärt sie. Siehe <see cref="PlanningType"/> und <see cref="IsSupport"/>.
    /// </remarks>
    [JsonPropertyName("planningType")] public string? PlanningType { get; init; }

    /// <summary>Die Art der Leistung in TANSS.</summary>
    [JsonPropertyName("typeId")] public int TypeId { get; init; }

    /// <summary>Der Zuordnungstyp (PC, Peripherie, …).</summary>
    [JsonPropertyName("linkTypeId")] public int LinkTypeId { get; init; }

    /// <summary>Die Kennung der Zuordnung.</summary>
    [JsonPropertyName("linkId")] public int LinkId { get; init; }

    /// <summary>Die nicht berechnete Dauer in Minuten.</summary>
    [JsonPropertyName("durationNotCharged")] public int DurationNotCharged { get; init; }

    /// <summary>Beginn einer Pause <b>innerhalb</b> der Leistung; <c>0</c>, wenn keine.</summary>
    [JsonPropertyName("startBreak")] public long StartBreak { get; init; }

    /// <summary>Dauer dieser Pause in Minuten.</summary>
    [JsonPropertyName("durationBreak")] public int DurationBreak { get; init; }

    /// <summary>Anfahrt in Minuten.</summary>
    [JsonPropertyName("durationApproach")] public int DurationApproach { get; init; }

    /// <summary>Abfahrt in Minuten.</summary>
    [JsonPropertyName("durationDeparture")] public int DurationDeparture { get; init; }

    /// <summary>Ist die Leistung als intern gekennzeichnet?</summary>
    [JsonPropertyName("internal")] public bool Internal { get; init; }

    /// <summary>Wurde sie bereits verbucht?</summary>
    [JsonPropertyName("booked")] public bool Booked { get; init; }

    /// <summary>Der Beginn als Zeitpunkt; <see langword="null"/>, wenn <c>date</c> 0 ist.</summary>
    [JsonIgnore] public DateTimeOffset? Begin => TanssTime.FromUnixSeconds(Date);

    /// <summary>Die Dauer als Zeitspanne.</summary>
    [JsonIgnore] public TimeSpan Span => TimeSpan.FromMinutes(Duration);

    /// <summary>Das Ende als Zeitpunkt; <see langword="null"/>, wenn <c>date</c> 0 ist.</summary>
    [JsonIgnore] public DateTimeOffset? End => Begin is { } begin ? begin + Span : null;

    /// <summary>
    /// Ist das eine Leistung — und nicht ein Termin oder eine Abwesenheit?
    /// </summary>
    /// <remarks>
    /// <para>Ein fehlendes <c>planningType</c> gilt als Leistung: Die älteste und häufigste
    /// Form ist die ohne ausdrückliche Planungsart, und sie als Termin zu behandeln machte
    /// jeden gewöhnlichen Tag zu einer einzigen Lücke.</para>
    /// <para><b>Ein Terminvorschlag deckt nichts ab.</b> <c>APPOINTMENT_PROPOSAL</c> ist eine
    /// Absicht; <c>APPOINTMENT_FIX</c> ist ein fester Termin, aber auch er ist keine erfasste
    /// Leistung. Beide zählen hier nicht.</para>
    /// </remarks>
    [JsonIgnore]
    public bool IsSupport =>
        string.IsNullOrWhiteSpace(PlanningType)
        || string.Equals(PlanningType, Model.PlanningType.Support, StringComparison.OrdinalIgnoreCase);

    /// <summary>Erklärt dieser Eintrag eine Abwesenheit?</summary>
    [JsonIgnore]
    public bool IsAbsence => Model.PlanningType.IsAbsence(PlanningType);

    /// <summary>Die Leistung in einer Zeile, für Anzeige und Protokoll.</summary>
    /// <returns>Uhrzeit, Dauer und Text.</returns>
    public override string ToString() => string.Create(CultureInfo.CurrentCulture,
        $"{Begin?.ToLocalTime():t}–{End?.ToLocalTime():t} ({Duration} min): {Text}");
}

/// <summary>Die Planungsarten, unter denen TANSS Einträge im Kalender führt.</summary>
/// <remarks>Zeichenketten statt Aufzählung — Begründung wie bei <see cref="TimestampType"/>.</remarks>
public static class PlanningType
{
    /// <summary>Eine erbrachte Leistung.</summary>
    public const string Support = "SUPPORT";

    /// <summary>Ein vorgeschlagener Termin.</summary>
    public const string AppointmentProposal = "APPOINTMENT_PROPOSAL";

    /// <summary>Ein fester Termin.</summary>
    public const string AppointmentFix = "APPOINTMENT_FIX";

    /// <summary>Ein privater Termin.</summary>
    public const string AppointmentPrivate = "APPOINTMENT_PRIVATE";

    /// <summary>Urlaub.</summary>
    public const string Vacation = "VACATION";

    /// <summary>Krankheit.</summary>
    public const string Illness = "ILLNESS";

    /// <summary>Sonstige Abwesenheit.</summary>
    public const string Absence = "ABSENCE";

    /// <summary>Rufbereitschaft.</summary>
    public const string StandBy = "STAND_BY";

    /// <summary>Überstundenausgleich.</summary>
    public const string Overtime = "OVERTIME";

    /// <summary>Erklärt diese Planungsart eine fehlende Leistung?</summary>
    /// <param name="planningType">Die Art, wie TANSS sie schreibt.</param>
    /// <returns><c>true</c> bei Urlaub, Krankheit, Abwesenheit und Überstundenausgleich.</returns>
    public static bool IsAbsence(string? planningType) =>
        string.Equals(planningType, Vacation, StringComparison.OrdinalIgnoreCase)
        || string.Equals(planningType, Illness, StringComparison.OrdinalIgnoreCase)
        || string.Equals(planningType, Absence, StringComparison.OrdinalIgnoreCase)
        || string.Equals(planningType, Overtime, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Woraus TANSS eine Leistung vorbelegen soll.
/// </summary>
/// <remarks>
/// <para><b>Die Aufzählung stammt aus dem Server.</b> <c>TnsSupportInitializerType</c> in
/// <c>TanssApi-10.10.0.jar</c> führt genau: <c>CALLBACK</c>, <c>CHECKLIST</c>, <c>COMPANY</c>,
/// <c>PHONECALL</c>, <c>REMOTE_MAINTENANCE</c>, <c>TASK</c>, <c>TICKET</c>, <c>TIMER</c>,
/// <c>TODO</c>. Ein erfundener Wert käme als Leistung ohne Vorbelegung zurück, und das fiele
/// erst auf der Rechnung auf.</para>
/// <para><b>Gemessen ist davon nur <see cref="Timer"/></b> — im Schwesterprojekt, gegen eine
/// Instanz der Fassung 10.10.0. <see cref="Ticket"/> und <see cref="Company"/> sind über die
/// Aufzählung des Servers <b>belegt</b>, aber noch nicht gemessen; siehe
/// <c>ISupportRepository.PrepareAsync</c>, das einen Fehlschlag deshalb als solchen meldet,
/// statt eine unvorbelegte Leistung zu buchen.</para>
/// </remarks>
/// <param name="Type">Die Quelle, etwa <c>TICKET</c>.</param>
/// <param name="Id">Deren Kennung.</param>
public sealed record SupportInitializer(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] int Id)
{
    /// <summary>Ein Timer als Quelle. Die einzige gemessene Art.</summary>
    /// <param name="timerId">Die Timerkennung.</param>
    /// <returns>Der Initialisierer.</returns>
    public static SupportInitializer Timer(int timerId) => new("TIMER", timerId);

    /// <summary>
    /// Ein Ticket als Quelle — der Weg für eine Lücke, zu der es keinen Timer gibt.
    /// </summary>
    /// <remarks>
    /// Über das Ticket findet TANSS Firma, Vertrag, Stundensatz und Abrechnungsart. Genau
    /// deswegen wird eine nachgetragene Leistung vorbelegt und nicht von Hand
    /// zusammengesetzt.
    /// </remarks>
    /// <param name="ticketId">Die Ticketkennung.</param>
    /// <returns>Der Initialisierer.</returns>
    public static SupportInitializer Ticket(int ticketId) => new("TICKET", ticketId);

    /// <summary>
    /// Eine Firma als Quelle — der Rückfall, wenn eine Lücke kein Ticket bekommt.
    /// </summary>
    /// <param name="companyId">Die Firmenkennung.</param>
    /// <returns>Der Initialisierer.</returns>
    public static SupportInitializer Company(int companyId) => new("COMPANY", companyId);
}

/// <summary>
/// Der Rumpf von <c>POST /api/v1/supports/properties</c>.
/// </summary>
/// <remarks>
/// <para><b>Der Rumpf ist eine Leistung, keine eigene Anfrageart.</b> Der Server nimmt dort
/// laut <c>TnsSupportController</c> ein <c>TnsSupport</c> entgegen, und <c>initializers</c> ist
/// ein Feld <i>dieser</i> Klasse. Ein Rumpf mit nur diesem einen Feld ist deshalb eine gültige,
/// ansonsten leere Leistung — genau das, was das Schwesterprojekt gemessen hat.</para>
/// <para>Daraus folgt eine Möglichkeit, die dieses Werkzeug nutzt: Neben den Quellen dürfen
/// gleich weitere Felder mitgegeben werden. Was TANSS nicht selbst vorbelegt, bleibt stehen.</para>
/// </remarks>
public sealed record SupportPropertiesRequest
{
    /// <summary>Die Quellen, aus denen TANSS die Leistung vorbelegen soll.</summary>
    [JsonPropertyName("initializers")]
    public required IReadOnlyList<SupportInitializer> Initializers { get; init; }
}

/// <summary>
/// Eine noch nicht gebuchte Leistung, so wie TANSS sie vorbelegt hat.
/// </summary>
/// <remarks>
/// <para><b>Warum hier ein <see cref="JsonObject"/> steht und kein Record mit Feldern.</b>
/// TANSS belegt beim Vorbereiten über achtzig Felder vor — Stundensatz, Abrechnungsart,
/// Fahrzeug, Zone, Kostenstelle, ein Dutzend Kennzeichen zur Fahrtabrechnung. Beim Anlegen
/// erwartet es sie zurück. Ein Record mit den fünf Feldern, die wir verstehen, hiesse: die
/// übrigen fünfundsiebzig fallen weg, und die Leistung wird mit Stundensatz 0 und ohne
/// Abrechnungsart gebucht. Der Fehler fiele in der Rechnungsstellung auf, nicht hier.</para>
///
/// <para>Deshalb bleibt der Block unangetastet, und geändert wird ausschliesslich, was zum
/// Schliessen einer Lücke nötig ist: Beginn, Dauer, Text und Ticket.</para>
///
/// <para>Veränderlich und kein <c>record</c>: Der Dialog schreibt hinein, während er offen ist.
/// Eine Kopie je Tastendruck wäre hier Zeremonie ohne Nutzen.</para>
/// </remarks>
public sealed class SupportDraft
{
    private const string TextField = "text";
    private const string TicketField = "ticketId";
    private const string DateField = "date";
    private const string DurationField = "duration";
    private const string EmployeeField = "employeeId";
    private const string CompanyField = "companyId";
    private const string InternalField = "internal";
    private const string LinkTypeField = "linkTypeId";
    private const string LinkField = "linkId";

    private readonly JsonObject _root;

    private SupportDraft(JsonObject root) => _root = root;

    /// <summary>Übernimmt, was TANSS vorbelegt hat.</summary>
    /// <param name="content">Der <c>content</c>-Block der Antwort.</param>
    /// <returns>Der Entwurf.</returns>
    /// <exception cref="ArgumentException">Der Block ist kein Objekt.</exception>
    public static SupportDraft From(JsonNode content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return content is JsonObject root
            ? new SupportDraft(root)
            : throw new ArgumentException(
                "TANSS hat auf die Vorbereitung der Leistung kein Objekt geantwortet. Ohne die "
                + "vorbelegten Felder liesse sich die Leistung nur unvollständig anlegen — sie "
                + "stünde dann ohne Stundensatz und ohne Abrechnungsart in TANSS.",
                nameof(content));
    }

    /// <summary>Der Text der Leistung — das, was auf der Rechnung steht.</summary>
    public string Text
    {
        get => (string?)_root[TextField] ?? string.Empty;
        set => _root[TextField] = value ?? string.Empty;
    }

    /// <summary>Das Ticket; <c>0</c> heisst „ohne Ticket“.</summary>
    public int TicketId
    {
        get => (int?)_root[TicketField] ?? 0;
        set => _root[TicketField] = value;
    }

    /// <summary>Die Firma, auf die gebucht wird.</summary>
    public int CompanyId
    {
        get => (int?)_root[CompanyField] ?? 0;
        set => _root[CompanyField] = value;
    }

    /// <summary>Der Mitarbeiter, dem die Leistung zugeschrieben wird.</summary>
    public int EmployeeId
    {
        get => (int?)_root[EmployeeField] ?? 0;
        set => _root[EmployeeField] = value;
    }

    /// <summary>Der Beginn. Wird als Unix-<b>Sekunden</b> geschrieben — siehe <see cref="TanssTime"/>.</summary>
    public DateTimeOffset? Begin
    {
        get => TanssTime.FromUnixSeconds((long?)_root[DateField] ?? 0);
        set => _root[DateField] = value is { } moment ? TanssTime.ToUnixSeconds(moment) : 0L;
    }

    /// <summary>
    /// Die Dauer. Wird als <b>Minuten</b> geschrieben.
    /// </summary>
    /// <remarks>
    /// <b>Auf ganze Minuten abgerundet, nicht gerundet.</b> Eine Lücke von 29,6 Minuten wird zu
    /// 29 und nicht zu 30: Aufrunden hiesse, dem Kunden eine Minute zu berechnen, die niemand
    /// gearbeitet hat. Die Rundungsregeln der Instanz greifen danach ohnehin noch.
    /// </remarks>
    public TimeSpan Duration
    {
        get => TimeSpan.FromMinutes((int?)_root[DurationField] ?? 0);
        set => _root[DurationField] = (int)Math.Floor(Math.Max(0, value.TotalMinutes));
    }

    /// <summary>
    /// Ist die Leistung als <b>intern</b> zu buchen?
    /// </summary>
    /// <remarks>
    /// <para>Eine interne Leistung sieht der Kunde nicht — sie ist Arbeit am eigenen Haus:
    /// Wartung der eigenen Systeme, Besprechungen, Schulung. Sie <b>gehört trotzdem erfasst</b>,
    /// und zwar genau deswegen: Was intern gebucht ist, verschwindet nicht aus der Auswertung,
    /// sondern erscheint dort als das, was es ist.</para>
    /// <para><b>Das Kennzeichen entscheidet nicht über die Berechnung.</b> Darüber entscheiden
    /// Abrechnungsart und Stundensatz, die TANSS beim Vorbereiten setzt. „Intern“ heisst
    /// „nicht für die Augen des Kunden“ — nicht „kostenlos“.</para>
    /// </remarks>
    public bool Internal
    {
        get => (bool?)_root[InternalField] ?? false;
        set => _root[InternalField] = value;
    }

    /// <summary>
    /// Der Zuordnungstyp — siehe <see cref="Model.LinkType"/>; <c>0</c> heisst „keine Zuordnung“.
    /// </summary>
    /// <remarks>
    /// <b>Typ und Kennung gehören zusammen</b> und werden deshalb über
    /// <see cref="AssignTo(int, int)"/> gemeinsam gesetzt. Ein Typ ohne Kennung zeigt auf nichts,
    /// eine Kennung ohne Typ ist mehrdeutig — die 17 ist ein PC <i>und</i> ein Drucker.
    /// </remarks>
    public int LinkTypeId => (int?)_root[LinkTypeField] ?? 0;

    /// <summary>Die Kennung der Zuordnung; <c>0</c> heisst „keine“.</summary>
    public int LinkId => (int?)_root[LinkField] ?? 0;

    /// <summary>
    /// Ordnet die Leistung einem Gerät, einer Firma oder einem Mitarbeiter zu.
    /// </summary>
    /// <remarks>
    /// Beide Werte zusammen oder gar keiner: <c>AssignTo(0, 0)</c> hebt die Zuordnung auf. Ein
    /// halb gesetztes Paar nähme serverseitig einen Zweig, den niemand gemeint hat.
    /// </remarks>
    /// <param name="linkTypeId">Der Zuordnungstyp; siehe <see cref="Model.LinkType"/>.</param>
    /// <param name="linkId">Die Kennung des zugeordneten Objekts.</param>
    public void AssignTo(int linkTypeId, int linkId)
    {
        bool complete = linkTypeId > 0 && linkId > 0;

        _root[LinkTypeField] = complete ? linkTypeId : 0;
        _root[LinkField] = complete ? linkId : 0;
    }

    /// <summary>Der Block, so wie er an TANSS zurückgeht.</summary>
    public JsonObject Payload => _root;

    /// <summary>Liest ein beliebiges vorbelegtes Feld — für Anzeige und Fehlersuche.</summary>
    /// <param name="name">Der Feldname, wie TANSS ihn schreibt.</param>
    /// <returns>Der Wert als Text, oder <see langword="null"/>.</returns>
    public string? Raw(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _root[name]?.ToString();
    }
}
