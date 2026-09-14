using System.Globalization;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.Workday.Holidays;
using TanssTagesabschluss.Workday.Model;

namespace TanssTagesabschluss.Workday;

/// <summary>
/// Was als Lücke zählt und was nicht.
/// </summary>
/// <remarks>
/// Die Werte stehen hier und nicht verstreut im Rechenweg, weil sie die einzigen sind, über die
/// sich sinnvoll streiten lässt — und weil ein Betrieb sie anders setzen können muss als der
/// nächste.
/// </remarks>
public sealed record GapOptions
{
    /// <summary>
    /// Kürzer als dies wird nicht als Lücke gemeldet.
    /// </summary>
    /// <remarks>
    /// <para><b>Eine Schwelle ist nötig, sonst wird die Liste unbrauchbar.</b> Zwischen zwei
    /// Leistungen liegen fast immer ein paar Minuten — der Griff zum Kaffee, das Lesen der
    /// nächsten Mail. Ohne Schwelle stünden dreissig Einträge von je vier Minuten in der Liste,
    /// und die eine Stunde, um die es wirklich geht, ginge darin unter.</para>
    /// <para>Fünfzehn Minuten, weil das in TANSS die übliche Abrechnungseinheit ist: Was
    /// darunter liegt, liesse sich ohnehin nicht sinnvoll buchen.</para>
    /// </remarks>
    public TimeSpan MinimumGap { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gilt eine bedingte Feiertagsregelung (nur in Teilen des Landes) als freier Tag?
    /// </summary>
    /// <remarks>
    /// <para><b>Vorgabe ist ja, und das ist die vorsichtigere Richtung.</b> Ein bedingter
    /// Feiertag — Fronleichnam in Thüringen, Mariä Himmelfahrt in Bayern — betrifft nur einen
    /// Teil des Landes. Ihn als frei zu behandeln heisst schlimmstenfalls: An einem Tag, an dem
    /// jemand gearbeitet, aber <b>gar nichts</b> gestempelt hat, bleibt der Hinweis aus. Ihn
    /// als Arbeitstag zu behandeln heisst: Jeder, der ihn wirklich frei hat, wird jedes Jahr
    /// gemahnt — und hört nach dem zweiten Mal auf hinzusehen.</para>
    /// <para><b>Auf die Lücken selbst wirkt das ohnehin nicht.</b> Lücken entstehen aus
    /// gestempelter Anwesenheit; wer an einem bedingten Feiertag arbeitet und stempelt, bekommt
    /// seine Lücken wie an jedem anderen Tag.</para>
    /// </remarks>
    public bool ConditionalHolidayCountsAsDayOff { get; init; } = true;

    /// <summary>
    /// Die Vorgabewerte.
    /// </summary>
    public static GapOptions Default { get; } = new();
}

/// <summary>
/// Alles, was zur Beurteilung eines Tages gebraucht wird.
/// </summary>
/// <remarks>
/// Ein eigener Typ statt sieben Parametern: Die Liste wächst erfahrungsgemäß, und bei sieben
/// gleichartigen Argumenten vertauscht man irgendwann zwei — hier würde das den Übersetzer
/// kosten und nicht erst den Betrieb.
/// </remarks>
public sealed record DayInput
{
    /// <summary>Der Tag.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Die Zeiterfassung dieses Tages; <see langword="null"/>, wenn TANSS keine liefert.</summary>
    public TimestampDay? Timestamps { get; init; }

    /// <summary>Das Arbeitszeitmodell; <see langword="null"/>, wenn unbekannt.</summary>
    public WorkingTimeModel? Model { get; init; }

    /// <summary>Alle Einträge der Leistungsliste dieses Tages — auch Termine und Abwesenheiten.</summary>
    public IReadOnlyList<SupportEntry> Supports { get; init; } = [];

    /// <summary>Die Abwesenheitsanträge, die diesen Tag berühren.</summary>
    public IReadOnlyList<AbsenceRequest> Absences { get; init; } = [];

    /// <summary>Die Zusatzarten, um berechnete von unberechneten Abwesenheiten zu trennen.</summary>
    public IReadOnlyList<PlanningAdditionalType> AdditionalTypes { get; init; } = [];

    /// <summary>Die Feiertage des Jahres.</summary>
    public HolidaySet Holidays { get; init; } = HolidaySet.Empty;

    /// <summary>Der Bezugszeitpunkt — „jetzt“. Für Tests einsetzbar.</summary>
    public required DateTimeOffset Now { get; init; }
}

/// <summary>
/// Findet die Zeitfenster, in denen jemand da war und nichts erfasst hat.
/// </summary>
/// <remarks>
/// <para><b>Der ganze Rechenweg in einem Satz:</b> Anwesenheit minus erfasste Leistungen minus
/// genehmigte Abwesenheiten, alles über <see cref="TimeSegment.Subtract"/>. Was übrig bleibt und
/// länger ist als <see cref="GapOptions.MinimumGap"/>, ist eine Lücke.</para>
///
/// <para><b>Warum Pausen nirgends abgezogen werden müssen.</b> TANSS schneidet die Abschnitte
/// selbst: Ein <c>WORK</c>-Abschnitt endet bei <c>PAUSE_START</c>, der nächste beginnt bei
/// <c>PAUSE_END</c>. Die Mittagspause steht deshalb in <b>keinem</b> Anwesenheitsabschnitt und
/// kann folglich gar nicht als Lücke herauskommen. Das ist kein Zufall, auf den man sich
/// verlässt, sondern die Bauform der Route — und es ist der Grund, warum hier die Abschnitte
/// gelesen werden und nicht die rohen Stempel.</para>
///
/// <para><b>Diese Klasse geht nicht ins Netz und kennt keine Uhr.</b> Alles kommt über
/// <see cref="DayInput"/> herein, „jetzt“ eingeschlossen. Damit lässt sich jeder Grenzfall als
/// Test schreiben — und die Grenzfälle sind hier die Regel: der laufende Tag, der halbe
/// Urlaubstag, der Feiertag, der nur im Nachbarkreis gilt.</para>
/// </remarks>
public sealed class GapFinder
{
    private readonly GapOptions _options;

    /// <summary>Baut den Rechner.</summary>
    /// <param name="options">Die Schwellen; ohne Angabe die Vorgabewerte.</param>
    public GapFinder(GapOptions? options = null) => _options = options ?? GapOptions.Default;

    /// <summary>Beurteilt einen Tag.</summary>
    /// <param name="input">Alles, was dazu gebraucht wird.</param>
    /// <returns>Das Ergebnis; niemals <see langword="null"/>.</returns>
    public DayAnalysis Analyse(DayInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<string> notes = [];
        TimeSegment window = DayWindow(input.Date);

        // --- 1. Anwesenheit -------------------------------------------------------------
        IReadOnlyList<TimeSegment> attendance = Attendance(input, window, notes);

        // --- 2. Pausen: alles, was zwischen dem ersten Kommen und dem letzten Gehen liegt
        //        und in keinem Anwesenheitsabschnitt steht. Siehe Klassenkommentar.
        IReadOnlyList<TimeSegment> pauses = Pauses(attendance);

        // --- 3. Erfasste Leistungen -----------------------------------------------------
        List<SupportEntry> supports = [.. input.Supports
            .Where(entry => entry.IsSupport && entry.Begin is not null)
            .OrderBy(entry => entry.Date)];

        IReadOnlyList<TimeSegment> recorded = TimeSegment.Merge(
            supports.Select(entry => Cover(entry).ClipTo(window)));

        // --- 4. Abwesenheiten, die eine fehlende Leistung erklaeren ----------------------
        (IReadOnlyList<TimeSegment> absenceSpans, string? absenceLabel, AbsenceCoverage coverage) =
            Absences(input, window);

        // --- 5. Die Rechnung ------------------------------------------------------------
        IReadOnlyList<TimeSegment> open = TimeSegment.Subtract(
            attendance, [.. recorded, .. absenceSpans]);

        List<Gap> gaps = [.. open
            .Where(segment => segment.Duration >= _options.MinimumGap)
            .Select(segment => new Gap(segment, Before(supports, segment), After(supports, segment)))];

        int dropped = open.Count - gaps.Count;
        if (dropped > 0)
        {
            notes.Add(string.Create(CultureInfo.CurrentCulture,
                $"{dropped} Zeitfenster unter {_options.MinimumGap.TotalMinutes:0} Minuten sind "
                + $"nicht aufgeführt."));
        }

        // --- 6. Einordnung --------------------------------------------------------------
        Holiday? holiday = input.Holidays.On(input.Date);
        if (!input.Holidays.IsKnown && input.Holidays.Note is { } holidayNote)
        {
            notes.Add(holidayNote);
        }

        if (holiday is { IsConditional: true, Note: { } limitation })
        {
            notes.Add($"„{holiday.Name}“ gilt nicht im ganzen Bundesland: {Shorten(limitation)}");
        }

        DayKind kind = Classify(input, holiday, coverage, notes);
        DayVerdict verdict = Judge(input, window, kind, attendance, gaps, notes);

        return new DayAnalysis
        {
            Date = input.Date,
            Kind = kind,
            Verdict = verdict,
            HolidayName = holiday?.Name,
            AbsenceLabel = absenceLabel,
            Attendance = attendance,
            Recorded = recorded,
            Pauses = pauses,
            Gaps = gaps,
            Supports = supports,
            TargetTime = TargetTime(input),
            Notes = notes,
        };
    }

    /// <summary>Der Kalendertag als Abschnitt, in Ortszeit.</summary>
    /// <remarks>
    /// Ortszeit, weil ein Arbeitstag ein Begriff der Ortszeit ist. In UTC gerechnet begänne der
    /// deutsche Sommertag um 22 Uhr des Vortags.
    /// </remarks>
    /// <param name="date">Der Tag.</param>
    /// <returns>Der Abschnitt von 0:00 bis 0:00 des Folgetags.</returns>
    public static TimeSegment DayWindow(DateOnly date)
    {
        DateTime start = date.ToDateTime(TimeOnly.MinValue);
        DateTimeOffset from = new(start, TimeZoneInfo.Local.GetUtcOffset(start));
        return new TimeSegment(from, from.AddDays(1));
    }

    /// <summary>
    /// Die Anwesenheit des Tages — und der Schnitt bei „jetzt“.
    /// </summary>
    /// <remarks>
    /// <b>Der Schnitt ist der Unterschied zwischen brauchbar und lächerlich.</b> Ein noch
    /// laufender Abschnitt bekommt von TANSS als Ende das Tagesende. Ungeschnitten hätte, wer um
    /// zehn Uhr nachsieht, eine Lücke von vierzehn Stunden — nämlich den Rest des Tages, den er
    /// noch gar nicht gearbeitet hat.
    /// </remarks>
    private static IReadOnlyList<TimeSegment> Attendance(DayInput input, TimeSegment window,
                                                         List<string> notes)
    {
        if (input.Timestamps is not { } day)
        {
            return [];
        }

        List<TimeSegment> segments = [];
        bool clipped = false;

        foreach (TimestampPeriod period in day.AttendancePeriods())
        {
            DateTimeOffset? begin = TanssTime.FromUnixSeconds(period.Begin);
            DateTimeOffset? end = TanssTime.FromUnixSeconds(period.End);

            if (begin is null || end is null)
            {
                continue;
            }

            DateTimeOffset stop = end.Value;

            if (period.IsOngoing && stop > input.Now)
            {
                stop = input.Now;
                clipped = true;
            }

            TimeSegment segment = new TimeSegment(begin.Value, stop).ClipTo(window);
            if (!segment.IsEmpty)
            {
                segments.Add(segment);
            }
        }

        if (clipped)
        {
            notes.Add(string.Create(CultureInfo.CurrentCulture,
                $"Die Zeiterfassung läuft noch; gerechnet wurde bis {input.Now.ToLocalTime():HH:mm}."));
        }

        return TimeSegment.Merge(segments);
    }

    /// <summary>
    /// Die gestempelten Pausen: was zwischen dem ersten Kommen und dem letzten Gehen liegt und
    /// keine Anwesenheit ist.
    /// </summary>
    /// <remarks>
    /// Nur zur Anzeige. In die Lückenrechnung geht das Ergebnis nicht ein — es <b>kann</b> gar
    /// nicht eingehen, weil eine Pause definitionsgemäß ausserhalb jeder Anwesenheit liegt und
    /// damit ausserhalb dessen, wovon abgezogen wird.
    /// </remarks>
    private static IReadOnlyList<TimeSegment> Pauses(IReadOnlyList<TimeSegment> attendance)
    {
        if (attendance.Count < 2)
        {
            return [];
        }

        TimeSegment hull = new(attendance[0].Start, attendance[^1].End);
        return TimeSegment.Subtract([hull], attendance);
    }

    /// <summary>
    /// Der Zeitraum, den eine Leistung abdeckt.
    /// </summary>
    /// <remarks>
    /// <para><b>Die Pause innerhalb der Leistung wird aufgeschlagen, nicht abgezogen.</b> Ob
    /// <c>duration</c> eine eingetragene Pause bereits enthält, ist <b>nicht gemessen</b>.
    /// Enthält es sie nicht, wäre ohne diesen Aufschlag das Ende zu früh gesetzt, und das
    /// Stück dahinter erschiene als Lücke, obwohl dort eine Leistung steht. Enthält es sie
    /// doch, deckt die Leistung hier ein paar Minuten zu viel ab — das kostet höchstens eine
    /// kurze Lücke, die ohnehin unter der Schwelle läge.</para>
    /// <para><b>An- und Abfahrt bleiben aussen vor.</b> Sie sind in TANSS eigene Felder, und ob
    /// sie vor oder nach der Leistung liegen, sagt der Datensatz nicht. Sie zu unterstellen
    /// hiesse, eine Stunde Arbeitszeit abzudecken, über die nichts bekannt ist.</para>
    /// </remarks>
    private static TimeSegment Cover(SupportEntry entry)
    {
        DateTimeOffset begin = entry.Begin ?? DateTimeOffset.UnixEpoch;
        TimeSpan length = entry.Span + TimeSpan.FromMinutes(Math.Max(0, entry.DurationBreak));
        return new TimeSegment(begin, begin + length);
    }

    /// <summary>
    /// Die Abwesenheiten dieses Tages, soweit sie eine fehlende Leistung <b>erklären</b>.
    /// </summary>
    /// <remarks>
    /// <para><b>Nur genehmigte Anträge zählen.</b> Ein eingereichter, aber nicht entschiedener
    /// Urlaub ist ein Wunsch — und an einem Tag, an dem trotzdem gearbeitet wurde, wäre er die
    /// falsche Erklärung und liesse die vergessene Leistung unentdeckt.</para>
    /// <para><b>Und eine berechnete Abwesenheit erklärt gar nichts.</b> Home-Office ist in TANSS
    /// eine Abwesenheit der Planung, aber gearbeitete Zeit der Abrechnung; dort sind Leistungen
    /// sehr wohl zu erwarten. Entschieden wird das an
    /// <see cref="PlanningAdditionalType.Charged"/>.</para>
    /// </remarks>
    private static (IReadOnlyList<TimeSegment> Spans, string? Label, AbsenceCoverage Coverage)
        Absences(DayInput input, TimeSegment window)
    {
        List<TimeSegment> spans = [];
        string? label = null;
        AbsenceCoverage strongest = AbsenceCoverage.None;

        foreach (AbsenceRequest request in input.Absences)
        {
            if (!request.IsApproved || request.DayOn(input.Date) is not { } day)
            {
                continue;
            }

            if (IsChargedAbsence(input, request))
            {
                // Gearbeitete Zeit trotz Eintrag in der Abwesenheitsplanung. Nicht abziehen -
                // sonst verschwaende gerade der Home-Office-Tag aus der Pruefung.
                continue;
            }

            TimeSegment span = Span(day, window, input.Model);
            if (span.IsEmpty)
            {
                continue;
            }

            spans.Add(span);
            label ??= LabelOf(input, request);

            if (day.Coverage == AbsenceCoverage.FullDay
                || (strongest == AbsenceCoverage.None && day.Coverage != AbsenceCoverage.None))
            {
                strongest = day.Coverage;
            }
        }

        return (TimeSegment.Merge(spans), label, strongest);
    }

    /// <summary>Der Zeitraum, den ein Abwesenheitstag belegt.</summary>
    /// <remarks>
    /// Die Grenze zwischen Vor- und Nachmittag kommt aus dem Arbeitszeitmodell — die Mitte
    /// zwischen Tagesbeginn und Tagesende. Ohne Modell gilt 12:00 Uhr. Eine feste Mittagsgrenze
    /// wäre bei einem Arbeitstag von 7 bis 15 Uhr um eine Stunde daneben, und ein halber
    /// Urlaubstag deckte dann die falsche Hälfte ab.
    /// </remarks>
    private static TimeSegment Span(AbsenceDay day, TimeSegment window, WorkingTimeModel? model)
    {
        DateTimeOffset start = window.Start;
        DateTimeOffset end = window.End;

        switch (day.Coverage)
        {
            case AbsenceCoverage.FullDay:
                return window;

            case AbsenceCoverage.Forenoon:
                return new TimeSegment(start, Midpoint(window, model));

            case AbsenceCoverage.Afternoon:
                return new TimeSegment(Midpoint(window, model), end);

            case AbsenceCoverage.TimeSpan:
                return day.Start is { } from && day.End is { } to
                    ? new TimeSegment(start + from.ToTimeSpan(), start + to.ToTimeSpan())
                    : default;

            default:
                return default;
        }
    }

    /// <summary>Die Mitte des Arbeitstags; ohne Modell 12:00 Uhr.</summary>
    private static DateTimeOffset Midpoint(TimeSegment window, WorkingTimeModel? model)
    {
        WorkingDay? day = model?.DayFor(window.Start.LocalDateTime.DayOfWeek);

        if (day is { BeginOfDay: { } begin, EndOfDay: { } end }
            && end.TotalMinutes > begin.TotalMinutes)
        {
            int middle = (begin.TotalMinutes + end.TotalMinutes) / 2;
            return window.Start.AddMinutes(middle);
        }

        return window.Start.AddHours(12);
    }

    /// <summary>Ist diese Abwesenheit berechnete Zeit — etwa Home-Office?</summary>
    private static bool IsChargedAbsence(DayInput input, AbsenceRequest request)
    {
        if (request.PlanningAdditionalId <= 0)
        {
            return false;
        }

        PlanningAdditionalType? type = input.AdditionalTypes
            .FirstOrDefault(entry => entry.Id == request.PlanningAdditionalId);

        // Unbekannte Art gilt als nicht berechnet und erklaert damit die Luecke. Die
        // vorsichtigere Richtung: Sie erzeugt keine falsche Mahnung.
        return type?.Charged ?? false;
    }

    /// <summary>Die Bezeichnung einer Abwesenheit, wie sie in der Anzeige steht.</summary>
    private static string LabelOf(DayInput input, AbsenceRequest request)
    {
        if (request.PlanningAdditionalId > 0)
        {
            PlanningAdditionalType? type = input.AdditionalTypes
                .FirstOrDefault(entry => entry.Id == request.PlanningAdditionalId);

            if (!string.IsNullOrWhiteSpace(type?.Name))
            {
                return type.Name;
            }
        }

        return request.PlanningType switch
        {
            PlanningType.Vacation => "Urlaub",
            PlanningType.Illness => "Krankheit",
            PlanningType.Absence => "Abwesenheit",
            PlanningType.Overtime => "Überstundenausgleich",
            PlanningType.StandBy => "Rufbereitschaft",
            _ => "Abwesenheit",
        };
    }

    /// <summary>Was für ein Tag das ist.</summary>
    private DayKind Classify(DayInput input, Holiday? holiday, AbsenceCoverage coverage,
                             List<string> notes)
    {
        if (holiday is not null
            && (!holiday.IsConditional || _options.ConditionalHolidayCountsAsDayOff))
        {
            return DayKind.Holiday;
        }

        if (coverage == AbsenceCoverage.FullDay)
        {
            return DayKind.Absence;
        }

        if (coverage != AbsenceCoverage.None)
        {
            return DayKind.PartialAbsence;
        }

        WorkingDay? day = input.Model?.DayFor(input.Date.DayOfWeek);

        if (day is null)
        {
            notes.Add("Für diesen Tag liegt kein Arbeitszeitmodell vor. Ob er ein Arbeitstag "
                      + "war, ist deshalb angenommen und nicht belegt: Montag bis Freitag gelten "
                      + "als Arbeitstage.");
            return DayKind.Unknown;
        }

        return day.IsWorkingDay ? DayKind.WorkingDay : DayKind.Weekend;
    }

    /// <summary>Das Urteil über den Tag.</summary>
    /// <remarks>
    /// <b>Der Fall, den eine reine Lückenrechnung übersieht, steht hier:</b> Wo gar keine
    /// Anwesenheit gestempelt ist, gibt es auch keine Lücke — ein vollständig vergessener
    /// Arbeitstag fiele durch jedes Raster. Deshalb ist er ein eigenes Urteil und nicht das
    /// Fehlen eines Befunds.
    /// </remarks>
    private static DayVerdict Judge(DayInput input, TimeSegment window, DayKind kind,
                                    IReadOnlyList<TimeSegment> attendance,
                                    List<Gap> gaps, List<string> notes)
    {
        if (gaps.Count > 0)
        {
            return DayVerdict.HasGaps;
        }

        if (attendance.Count > 0)
        {
            bool stillRunning = input.Timestamps?.AttendancePeriods().Any(p => p.IsOngoing) == true;
            return stillRunning ? DayVerdict.Ongoing : DayVerdict.Complete;
        }

        // Ab hier: kein einziger Anwesenheitsabschnitt.
        if (kind is DayKind.Holiday or DayKind.Weekend or DayKind.Absence)
        {
            return DayVerdict.DayOff;
        }

        DateOnly today = DateOnly.FromDateTime(input.Now.LocalDateTime.Date);

        // Die Zukunft ist keine Luecke.
        if (input.Date > today)
        {
            return DayVerdict.Ongoing;
        }

        // HEUTE ist der Grenzfall, und er haengt nicht am Kalendertag, sondern am Feierabend.
        // Wer um halb acht noch nicht gestempelt hat, hat nichts versaeumt; wer es um elf Uhr
        // abends immer noch nicht hat, sehr wohl - und genau darauf zielt die abendliche
        // Erinnerung. Ein Schnitt bei Mitternacht haette den ganzen Tag als "laeuft noch"
        // ausgewiesen und die Erinnerung damit wertlos gemacht.
        if (input.Date == today && input.Now < EndOfWorkday(input, window))
        {
            return DayVerdict.Ongoing;
        }

        if (kind == DayKind.Unknown && input.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            notes.Add("Ohne Arbeitszeitmodell wird ein Wochenende als freier Tag behandelt.");
            return DayVerdict.DayOff;
        }

        return DayVerdict.NoTimeRecorded;
    }

    /// <summary>
    /// Wann der Arbeitstag endet — nach dem Arbeitszeitmodell, sonst um 17 Uhr.
    /// </summary>
    /// <remarks>
    /// Die Ersatzannahme 17 Uhr ist gegriffen, aber sie wirkt nur an <b>einer</b> Stelle: Sie
    /// entscheidet für den heutigen Tag ohne jeden Stempel, ob er als „läuft noch“ oder als
    /// „keine Zeit erfasst“ gilt. Auf die Lücken selbst hat sie keinen Einfluss.
    /// </remarks>
    private static DateTimeOffset EndOfWorkday(DayInput input, TimeSegment window)
    {
        WorkingDay? day = input.Model?.DayFor(input.Date.DayOfWeek);

        return day?.EndOfDay is { } end && end.TotalMinutes > 0
            ? window.Start.AddMinutes(end.TotalMinutes)
            : window.Start.AddHours(17);
    }

    /// <summary>Die Sollarbeitszeit des Tages; <see langword="null"/>, wenn unbekannt.</summary>
    private static TimeSpan? TargetTime(DayInput input) =>
        input.Model?.DayFor(input.Date.DayOfWeek) is { } day
            ? TimeSpan.FromMinutes(day.WorkTimeInMinutes)
            : null;

    /// <summary>Die letzte Leistung vor einer Lücke.</summary>
    private static SupportEntry? Before(IReadOnlyList<SupportEntry> supports, TimeSegment gap) =>
        supports.Where(entry => entry.End is { } end && end <= gap.Start)
                .OrderByDescending(entry => entry.End)
                .FirstOrDefault();

    /// <summary>Die nächste Leistung nach einer Lücke.</summary>
    private static SupportEntry? After(IReadOnlyList<SupportEntry> supports, TimeSegment gap) =>
        supports.Where(entry => entry.Begin is { } begin && begin >= gap.End)
                .OrderBy(entry => entry.Date)
                .FirstOrDefault();

    /// <summary>
    /// Kürzt einen Hinweis auf eine Länge, die in eine Zeile passt.
    /// </summary>
    /// <remarks>
    /// Die Einschränkungen des Feiertagsdienstes sind teils mehrere hundert Zeichen lang — die
    /// zu Mariä Himmelfahrt zählt die bayerischen Gemeinden aus. Ungekürzt sprengt das jede
    /// Anzeige; der volle Wortlaut steht weiterhin am <see cref="Holiday.Note"/>.
    /// </remarks>
    private static string Shorten(string text)
    {
        string flat = string.Join(' ', text.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return flat.Length <= 160 ? flat : flat[..159] + "…";
    }
}
