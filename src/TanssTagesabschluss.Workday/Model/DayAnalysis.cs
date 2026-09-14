using System.Globalization;
using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.Workday.Model;

/// <summary>Was für ein Tag das ist — und damit, ob überhaupt etwas zu erwarten war.</summary>
public enum DayKind
{
    /// <summary>Ein gewöhnlicher Arbeitstag nach dem Arbeitszeitmodell.</summary>
    WorkingDay,

    /// <summary>Ein Tag ohne Sollzeit im Arbeitszeitmodell — üblicherweise Samstag oder Sonntag.</summary>
    Weekend,

    /// <summary>Ein gesetzlicher Feiertag des maßgeblichen Bundeslands.</summary>
    Holiday,

    /// <summary>Ganztägig abwesend: Urlaub, Krankheit, Überstundenausgleich.</summary>
    Absence,

    /// <summary>
    /// Teilweise abwesend — ein halber Urlaubstag oder eine stundenweise Abwesenheit.
    /// </summary>
    /// <remarks>
    /// Ausdrücklich von <see cref="Absence"/> getrennt: An einem halben Urlaubstag ist die
    /// andere Hälfte sehr wohl zu erfassen, und genau dort geht am ehesten etwas verloren.
    /// </remarks>
    PartialAbsence,

    /// <summary>
    /// Das Arbeitszeitmodell ist unbekannt.
    /// </summary>
    /// <remarks>
    /// Kein Fehler, sondern eine Wissenslücke: Ohne Modell lässt sich nicht sagen, ob ein Tag
    /// ohne Stempel frei war oder vergessen wurde. Das Werkzeug sagt das, statt zu raten.
    /// </remarks>
    Unknown,
}

/// <summary>Das Urteil über einen Tag — der Satz, der in der Liste steht.</summary>
public enum DayVerdict
{
    /// <summary>Anwesenheit vorhanden und vollständig durch Leistungen gedeckt.</summary>
    Complete,

    /// <summary>Es bleiben Zeitfenster ohne erfasste Leistung.</summary>
    HasGaps,

    /// <summary>
    /// Arbeitstag ohne <b>jede</b> Zeiterfassung — und ohne Feiertag oder Abwesenheit, die das
    /// erklären würde.
    /// </summary>
    /// <remarks>
    /// Der Fall, den eine reine Lückenrechnung übersieht: Wo keine Anwesenheit steht, gibt es
    /// auch keine Lücke — und ein ganzer vergessener Tag fiele durch. Deshalb ist er ein
    /// eigenes Urteil.
    /// </remarks>
    NoTimeRecorded,

    /// <summary>Ein freier Tag: Wochenende, Feiertag oder ganztägige Abwesenheit.</summary>
    DayOff,

    /// <summary>
    /// Der Tag läuft noch.
    /// </summary>
    /// <remarks>
    /// Für heute wird nur bis <b>jetzt</b> gerechnet. Alles danach ist Zukunft und keine Lücke;
    /// wer um zehn Uhr nachsieht, hat nicht vierzehn Stunden versäumt.
    /// </remarks>
    Ongoing,
}

/// <summary>
/// Eine Lücke: ein Zeitfenster, in dem der Techniker anwesend war und nichts erfasst ist.
/// </summary>
/// <param name="Segment">Das Zeitfenster.</param>
/// <param name="Before">Die letzte Leistung davor; <see langword="null"/>, wenn es keine gibt.</param>
/// <param name="After">Die nächste Leistung danach; <see langword="null"/>, wenn es keine gibt.</param>
public sealed record Gap(TimeSegment Segment, SupportEntry? Before, SupportEntry? After)
{
    /// <summary>Die Dauer der Lücke.</summary>
    public TimeSpan Duration => Segment.Duration;

    /// <summary>Der Beginn — der Wert, der beim Nachtragen als <c>date</c> gesetzt wird.</summary>
    public DateTimeOffset Start => Segment.Start;

    /// <summary>Das Ende.</summary>
    public DateTimeOffset End => Segment.End;

    /// <summary>
    /// Das Ticket, auf das diese Lücke am ehesten gehört — oder <c>0</c>, wenn unklar.
    /// </summary>
    /// <remarks>
    /// <para><b>Nur bei Einigkeit.</b> Tragen die Leistung davor und die danach dasselbe Ticket,
    /// ist die Lücke mit grosser Wahrscheinlichkeit eine Unterbrechung derselben Arbeit — dann
    /// wird das Ticket vorgeschlagen. Weichen sie ab, wird <b>nichts</b> vorgeschlagen: Ein
    /// geratenes Ticket, das der Techniker unbesehen bestätigt, bucht Arbeitszeit beim falschen
    /// Kunden, und das fällt niemandem auf.</para>
    /// <para>Gibt es nur eine Nachbarin, zählt deren Ticket — mit einer Nachbarin ist die
    /// Vermutung schwächer, aber es gibt keine widersprechende.</para>
    /// </remarks>
    public int SuggestedTicketId =>
        Before?.TicketId is { } before and > 0 && After?.TicketId is { } after and > 0
            ? before == after ? before : 0
            : Before?.TicketId is { } onlyBefore and > 0
                ? onlyBefore
                : After?.TicketId is { } onlyAfter and > 0
                    ? onlyAfter
                    : 0;

    /// <summary>Die Firma, auf die diese Lücke am ehesten gehört; <c>0</c>, wenn unklar.</summary>
    /// <remarks>Dieselbe Regel wie bei <see cref="SuggestedTicketId"/>.</remarks>
    public int SuggestedCompanyId =>
        Before?.CompanyId is { } before and > 0 && After?.CompanyId is { } after and > 0
            ? before == after ? before : 0
            : Before?.CompanyId is { } onlyBefore and > 0
                ? onlyBefore
                : After?.CompanyId is { } onlyAfter and > 0
                    ? onlyAfter
                    : 0;

    /// <summary>
    /// Teilt die Lücke an einer Stelle in zwei.
    /// </summary>
    /// <remarks>
    /// <para><b>Ein Zeitfenster ist selten eine einzige Tätigkeit.</b> Neun offene Stunden am
    /// Stück sind fast nie neun Stunden an derselben Sache — dazwischen lagen ein Anruf, ein
    /// Kunde, eine Stunde am eigenen Server. Ohne Teilen bliebe nur, das alles in <i>einen</i>
    /// Leistungstext zu schreiben und auf <i>ein</i> Ticket zu buchen. Das ist zwar erfasst,
    /// aber es ist auf der Rechnung des Kunden nicht mehr auseinanderzuhalten.</para>
    ///
    /// <para><b>Die Nachbarinnen wandern mit, und zwar jede zu ihrer Seite.</b> Der ersten
    /// Hälfte bleibt die Leistung davor, der zweiten die danach; nach innen hat keine der
    /// beiden eine Nachbarin, denn dort steht die jeweils andere Hälfte und trägt noch nichts.
    /// Damit fällt auch der Ticketvorschlag weg, wo er nicht mehr trägt — geraten wird beim
    /// Kunden nicht.</para>
    /// </remarks>
    /// <param name="first">Die Dauer des ersten Teils.</param>
    /// <returns>
    /// Die beiden Teile, oder <see langword="null"/>, wenn sich dort nicht teilen lässt —
    /// bei null, bei negativer Dauer und bei allem, was nicht kürzer als die Lücke ist.
    /// </returns>
    public (Gap First, Gap Second)? SplitAfter(TimeSpan first)
    {
        if (first <= TimeSpan.Zero || first >= Duration)
        {
            return null;
        }

        DateTimeOffset cut = Start + first;

        return (new Gap(new TimeSegment(Start, cut), Before, After: null),
                new Gap(new TimeSegment(cut, End), Before: null, After));
    }

    /// <summary>
    /// Fügt diese Lücke mit der unmittelbar folgenden wieder zusammen.
    /// </summary>
    /// <remarks>
    /// <para><b>Das Gegenstück zu <see cref="SplitAfter"/>, und es wird gebraucht.</b> Wer an
    /// der falschen Stelle geteilt hat, soll das zurücknehmen können, ohne den Tag neu zu laden
    /// und die bereits geschriebenen Texte zu verlieren.</para>
    ///
    /// <para><b>Nur lückenlos aneinandergrenzend.</b> Zwei Fenster mit etwas dazwischen zu
    /// einem zu verschmelzen hiesse, die Zeit dazwischen stillschweigend mitzubuchen — und die
    /// gehört dorthin nicht: Zwischen zwei offenen Fenstern liegt entweder eine erfasste
    /// Leistung oder eine gestempelte Pause, und beides wäre dann doppelt erfasst.</para>
    ///
    /// <para><b>Die Nachbarinnen kommen von aussen.</b> Die Leistung vor dem ersten und die
    /// nach dem zweiten Fenster; was innen lag, gab es nie — dort stand die Naht.</para>
    /// </remarks>
    /// <param name="next">Die folgende Lücke.</param>
    /// <returns>
    /// Die zusammengefügte Lücke, oder <see langword="null"/>, wenn die beiden nicht
    /// aneinandergrenzen.
    /// </returns>
    public Gap? MergeWith(Gap next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return End == next.Start
            ? new Gap(new TimeSegment(Start, next.End), Before, next.After)
            : null;
    }

    /// <summary>Die Lücke in einer Zeile.</summary>
    /// <returns>Etwa <c>09:15–10:00 (45 min)</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.CurrentCulture,
        $"{Segment} ({Duration.TotalMinutes:0} min)");
}

/// <summary>
/// Das Ergebnis für einen Tag: was gestempelt, was erfasst und was offen ist.
/// </summary>
/// <remarks>
/// Ein Datensatz und kein Zustand: Er entsteht aus einem Satz Eingaben und ändert sich danach
/// nicht mehr. Wer neu rechnen will, rechnet neu — so kann die Anzeige nie einen halb
/// fortgeschriebenen Tag zeigen.
/// </remarks>
public sealed record DayAnalysis
{
    /// <summary>Der Tag.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Was für ein Tag das ist.</summary>
    public required DayKind Kind { get; init; }

    /// <summary>Das Urteil.</summary>
    public required DayVerdict Verdict { get; init; }

    /// <summary>Der Name des Feiertags, wenn es einer ist.</summary>
    public string? HolidayName { get; init; }

    /// <summary>Die Bezeichnung der Abwesenheit, wenn es eine gibt — etwa „Urlaub“.</summary>
    public string? AbsenceLabel { get; init; }

    /// <summary>Die Zeitfenster, in denen der Techniker anwesend war — Pausen bereits abgezogen.</summary>
    public IReadOnlyList<TimeSegment> Attendance { get; init; } = [];

    /// <summary>Die Zeitfenster, die durch erfasste Leistungen gedeckt sind.</summary>
    public IReadOnlyList<TimeSegment> Recorded { get; init; } = [];

    /// <summary>Die gestempelten Pausen — ausdrücklich <b>keine</b> Lücken.</summary>
    public IReadOnlyList<TimeSegment> Pauses { get; init; } = [];

    /// <summary>Die Lücken, nach Beginn geordnet.</summary>
    public IReadOnlyList<Gap> Gaps { get; init; } = [];

    /// <summary>Die Leistungen dieses Tages, nach Beginn geordnet.</summary>
    public IReadOnlyList<SupportEntry> Supports { get; init; } = [];

    /// <summary>Die Sollarbeitszeit nach dem Arbeitszeitmodell; <see langword="null"/>, wenn unbekannt.</summary>
    public TimeSpan? TargetTime { get; init; }

    /// <summary>
    /// Hinweise zur Rechnung selbst — was nicht ermittelt werden konnte und was das bedeutet.
    /// </summary>
    /// <remarks>
    /// Hausregel: Was nicht feststeht, wird gesagt und nicht verschwiegen. Ein Tag ohne
    /// Arbeitszeitmodell, ein Feiertagsdienst, der nicht erreichbar war, ein noch laufender
    /// Abschnitt — alles das steht hier und ist in der Anzeige lesbar.
    /// </remarks>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Die anwesende Zeit insgesamt.</summary>
    public TimeSpan AttendanceTotal => TimeSegment.TotalOf(Attendance);

    /// <summary>Die durch Leistungen gedeckte Zeit — nur innerhalb der Anwesenheit gezählt.</summary>
    public TimeSpan RecordedTotal =>
        TimeSegment.TotalOf(TimeSegment.Subtract(Attendance, Gaps.Select(gap => gap.Segment)));

    /// <summary>Die Summe aller Lücken.</summary>
    public TimeSpan GapTotal => TimeSegment.TotalOf(Gaps.Select(gap => gap.Segment));

    /// <summary>Gibt es an diesem Tag etwas zu tun?</summary>
    public bool NeedsAttention =>
        Verdict is DayVerdict.HasGaps or DayVerdict.NoTimeRecorded;

    /// <summary>
    /// Wie viel der Anwesenheit gedeckt ist, von 0 bis 1; <c>1</c>, wenn niemand anwesend war.
    /// </summary>
    /// <remarks>
    /// Ein Tag ohne Anwesenheit gilt als vollständig und nicht als „0 Prozent erfasst“ — sonst
    /// stünde jeder Sonntag als vollständiges Versäumnis in der Auswertung.
    /// </remarks>
    public double Coverage
    {
        get
        {
            double attendance = AttendanceTotal.TotalMinutes;
            return attendance <= 0 ? 1d : Math.Clamp(1d - (GapTotal.TotalMinutes / attendance), 0d, 1d);
        }
    }
}
