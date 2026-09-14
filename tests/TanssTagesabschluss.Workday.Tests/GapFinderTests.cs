using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.Workday.Holidays;
using TanssTagesabschluss.Workday.Model;
using Xunit;

namespace TanssTagesabschluss.Workday.Tests;

/// <summary>
/// Die Lückenrechnung — und zwar an den Fällen, die im Alltag Geld kosten.
/// </summary>
/// <remarks>
/// Jeder Test hier steht für eine Verwechslung, die einmal teuer werden kann: eine Pause, die
/// als Lücke gemeldet wird; ein Urlaubstag, an dem gemahnt wird; ein laufender Tag, der als
/// vierzehn Stunden Versäumnis erscheint; und der vergessene Tag, den eine reine Lückenrechnung
/// gar nicht sehen kann.
/// </remarks>
public sealed class GapFinderTests
{
    private static readonly DateOnly Montag = new(2026, 9, 14);

    /// <summary>Der Tagesbeginn in Ortszeit — dieselbe Rechnung wie im Prüfling.</summary>
    private static DateTimeOffset Start => GapFinder.DayWindow(Montag).Start;

    private static DateTimeOffset At(int hour, int minute = 0) =>
        Start.AddHours(hour).AddMinutes(minute);

    /// <summary>Ein Arbeitszeitmodell mit Montag bis Freitag 8 bis 17 Uhr.</summary>
    private static WorkingTimeModel Modell { get; } = new()
    {
        Id = 0,
        Name = "TEST",
        Days = new Dictionary<string, WorkingDay>(StringComparer.OrdinalIgnoreCase)
        {
            ["MONDAY"] = Arbeitstag,
            ["TUESDAY"] = Arbeitstag,
            ["WEDNESDAY"] = Arbeitstag,
            ["THRUSDAY"] = Arbeitstag,
            ["FRIDAY"] = Arbeitstag,
            ["SATURDAY"] = Freitag,
            ["SUNDAY"] = Freitag,
        },
    };

    private static WorkingDay Arbeitstag => new()
    {
        Pause = 60,
        BeginOfDay = new ClockTime(8, 0),
        EndOfDay = new ClockTime(17, 0),
        WorkTimeInMinutes = 480,
    };

    private static WorkingDay Freitag => new() { Pause = 0, WorkTimeInMinutes = 0 };

    private static TimestampPeriod Abschnitt(int fromHour, int fromMinute,
                                             int toHour, int toMinute,
                                             string state = "COMPLETE") => new()
                                             {
                                                 Begin = TanssTime.ToUnixSeconds(At(fromHour, fromMinute)),
                                                 End = TanssTime.ToUnixSeconds(At(toHour, toMinute)),
                                                 State = state,
                                             };

    private static TimestampDay Tag(params TimestampPeriod[] arbeit) => new()
    {
        EmployeeId = 1,
        Date = Montag,
        WeekDay = "MONDAY",
        WorkingTimeModelId = 0,
        Types = new Dictionary<string, IReadOnlyList<TimestampPeriod>>(StringComparer.OrdinalIgnoreCase)
        {
            [TimestampType.Work] = arbeit,
        },
    };

    private static SupportEntry Leistung(int fromHour, int fromMinute, int minutes,
                                         int ticketId = 0, int companyId = 0) => new()
                                         {
                                             Id = (fromHour * 100) + fromMinute,
                                             Date = TanssTime.ToUnixSeconds(At(fromHour, fromMinute)),
                                             Duration = minutes,
                                             TicketId = ticketId,
                                             CompanyId = companyId,
                                             EmployeeId = 1,
                                             PlanningType = PlanningType.Support,
                                         };

    private static DayInput Eingabe(TimestampDay? tag, IEnumerable<SupportEntry>? leistungen = null,
                                    IEnumerable<AbsenceRequest>? abwesenheiten = null,
                                    HolidaySet? feiertage = null,
                                    DateTimeOffset? jetzt = null,
                                    WorkingTimeModel? modell = null) => new()
                                    {
                                        Date = Montag,
                                        Timestamps = tag,
                                        Model = modell ?? Modell,
                                        Supports = [.. leistungen ?? []],
                                        Absences = [.. abwesenheiten ?? []],
                                        Holidays = feiertage ?? HolidaySet.Empty,
                                        // Weit nach Feierabend, damit der Tag als abgeschlossen gilt.
                                        Now = jetzt ?? At(23, 0),
                                    };

    // --- Der Kern -----------------------------------------------------------------------

    [Fact]
    public void Ein_vollstaendig_erfasster_Tag_hat_keine_Luecke()
    {
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0), Abschnitt(13, 0, 17, 0)),
                    [Leistung(8, 0, 240), Leistung(13, 0, 240)]));

        Assert.Empty(result.Gaps);
        Assert.Equal(DayVerdict.Complete, result.Verdict);
        Assert.Equal(1d, result.Coverage);
    }

    [Fact]
    public void Eine_Stunde_ohne_Leistung_wird_gefunden()
    {
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)), [Leistung(8, 0, 180)]));

        Gap gap = Assert.Single(result.Gaps);
        Assert.Equal(At(11, 0), gap.Start);
        Assert.Equal(At(12, 0), gap.End);
        Assert.Equal(TimeSpan.FromHours(1), gap.Duration);
        Assert.Equal(DayVerdict.HasGaps, result.Verdict);
    }

    [Fact]
    public void Die_gestempelte_Mittagspause_ist_keine_Luecke()
    {
        // DER Fall, um den es ausdruecklich geht. TANSS schneidet den WORK-Abschnitt bei
        // PAUSE_START; die Pause liegt damit in keinem Anwesenheitsabschnitt und kann gar
        // nicht als Luecke herauskommen.
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0), Abschnitt(13, 0, 17, 0)),
                    [Leistung(8, 0, 240), Leistung(13, 0, 240)]));

        Assert.Empty(result.Gaps);

        TimeSegment pause = Assert.Single(result.Pauses);
        Assert.Equal(At(12, 0), pause.Start);
        Assert.Equal(At(13, 0), pause.End);
    }

    [Fact]
    public void Kurze_Zeitfenster_unter_der_Schwelle_stehen_nicht_in_der_Liste()
    {
        // Zehn Minuten zwischen zwei Leistungen: der Griff zum Kaffee, keine vergessene
        // Leistung. Ohne Schwelle waere die Liste voll davon.
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)), [Leistung(8, 0, 110), Leistung(10, 0, 120)]));

        Assert.Empty(result.Gaps);
        Assert.Contains(result.Notes, note => note.Contains("unter 15 Minuten", StringComparison.Ordinal));
    }

    [Fact]
    public void Die_Schwelle_laesst_sich_heruntersetzen()
    {
        DayAnalysis result = new GapFinder(new GapOptions { MinimumGap = TimeSpan.FromMinutes(5) })
            .Analyse(Eingabe(Tag(Abschnitt(8, 0, 12, 0)),
                             [Leistung(8, 0, 110), Leistung(10, 0, 120)]));

        Gap gap = Assert.Single(result.Gaps);
        Assert.Equal(TimeSpan.FromMinutes(10), gap.Duration);
    }

    // --- Der laufende Tag ---------------------------------------------------------------

    [Fact]
    public void Ein_laufender_Tag_wird_nur_bis_jetzt_gerechnet()
    {
        // TANSS setzt bei einem laufenden Abschnitt als Ende das Tagesende. Ungeschnitten
        // haette, wer um 10 Uhr nachsieht, eine Luecke von vierzehn Stunden.
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 23, 59, state: "ONGOING")),
                    [Leistung(8, 0, 60)],
                    jetzt: At(10, 0)));

        Gap gap = Assert.Single(result.Gaps);
        Assert.Equal(At(9, 0), gap.Start);
        Assert.Equal(At(10, 0), gap.End);
        Assert.Contains(result.Notes, note => note.Contains("läuft noch", StringComparison.Ordinal));
    }

    [Fact]
    public void Ein_laufender_Tag_ohne_Luecke_heisst_laeuft_noch()
    {
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 23, 59, state: "ONGOING")),
                    [Leistung(8, 0, 120)],
                    jetzt: At(10, 0)));

        Assert.Equal(DayVerdict.Ongoing, result.Verdict);
    }

    // --- Freie Tage ---------------------------------------------------------------------

    [Fact]
    public void Ein_ganzer_Tag_Urlaub_erzeugt_keine_Mahnung()
    {
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(tag: null, abwesenheiten: [Urlaub(ganztags: true)]));

        Assert.Equal(DayKind.Absence, result.Kind);
        Assert.Equal(DayVerdict.DayOff, result.Verdict);
        Assert.Empty(result.Gaps);
        Assert.Equal("Urlaub", result.AbsenceLabel);
    }

    [Fact]
    public void Ein_halber_Urlaubstag_deckt_nur_den_Vormittag()
    {
        // Der Fall, in dem am ehesten etwas verlorengeht: Vormittag Urlaub, nachmittags
        // gearbeitet - und die Leistung am Nachmittag vergessen.
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 17, 0)),
                    leistungen: [],
                    abwesenheiten: [Urlaub(ganztags: false)]));

        Gap gap = Assert.Single(result.Gaps);

        // Die Mitte kommt aus dem Arbeitszeitmodell: 8 bis 17 Uhr ergibt 12:30.
        Assert.Equal(At(12, 30), gap.Start);
        Assert.Equal(At(17, 0), gap.End);
        Assert.Equal(DayKind.PartialAbsence, result.Kind);
    }

    [Fact]
    public void Ein_nicht_genehmigter_Urlaub_erklaert_nichts()
    {
        // Ein beantragter, aber nicht entschiedener Urlaub ist ein Wunsch. Wer an dem Tag
        // trotzdem gearbeitet hat, soll seine Luecke sehen.
        AbsenceRequest beantragt = Urlaub(ganztags: true) with { Status = AbsenceStatus.Requested };

        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)), abwesenheiten: [beantragt]));

        Gap gap = Assert.Single(result.Gaps);
        Assert.Equal(TimeSpan.FromHours(4), gap.Duration);
    }

    [Fact]
    public void Home_Office_ist_berechnete_Zeit_und_erklaert_keine_Luecke()
    {
        // Home-Office steht in TANSS in der Abwesenheitsplanung, ist aber gearbeitete Zeit.
        // Wer es als Abwesenheit behandelt, verliert genau die Tage aus der Pruefung, an
        // denen am meisten allein gearbeitet wird.
        AbsenceRequest homeOffice = new()
        {
            Id = 7,
            PlanningType = PlanningType.Absence,
            Status = AbsenceStatus.Approved,
            PlanningAdditionalId = 3,
            Days = [Tagesangabe(forenoon: true, afternoon: true)],
        };

        DayInput input = Eingabe(Tag(Abschnitt(8, 0, 12, 0)), abwesenheiten: [homeOffice]) with
        {
            AdditionalTypes = [new PlanningAdditionalType
            {
                Id = 3, Name = "Home-Office", Charged = true, Type = "ABSENCE", Active = true,
            }],
        };

        DayAnalysis result = new GapFinder().Analyse(input);

        Gap gap = Assert.Single(result.Gaps);
        Assert.Equal(TimeSpan.FromHours(4), gap.Duration);
    }

    [Fact]
    public void Ein_Feiertag_ohne_Zeiterfassung_erzeugt_keine_Mahnung()
    {
        HolidaySet feiertage = HolidaySet.Known(
            [new Holiday(Montag, "Tag der Deutschen Einheit", IsConditional: false, Note: null)]);

        DayAnalysis result = new GapFinder().Analyse(Eingabe(tag: null, feiertage: feiertage));

        Assert.Equal(DayKind.Holiday, result.Kind);
        Assert.Equal(DayVerdict.DayOff, result.Verdict);
        Assert.Equal("Tag der Deutschen Einheit", result.HolidayName);
    }

    [Fact]
    public void Wer_am_Feiertag_arbeitet_bekommt_seine_Luecken_trotzdem()
    {
        // Der Feiertag entscheidet nur ueber den Tag ohne jede Erfassung. Wer gestempelt
        // hat, hat gearbeitet - und was er gearbeitet hat, gehoert erfasst.
        HolidaySet feiertage = HolidaySet.Known(
            [new Holiday(Montag, "Karfreitag", IsConditional: false, Note: null)]);

        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(9, 0, 13, 0)), feiertage: feiertage));

        Gap gap = Assert.Single(result.Gaps);
        Assert.Equal(TimeSpan.FromHours(4), gap.Duration);
        Assert.Equal(DayVerdict.HasGaps, result.Verdict);
    }

    [Fact]
    public void Ein_bedingter_Feiertag_wird_als_frei_behandelt_und_gesagt()
    {
        HolidaySet feiertage = HolidaySet.Known(
        [
            new Holiday(Montag, "Fronleichnam", IsConditional: true,
                        Note: "Fronleichnam ist kein gesetzlicher Feiertag ausser im Eichsfeld."),
        ]);

        DayAnalysis result = new GapFinder().Analyse(Eingabe(tag: null, feiertage: feiertage));

        Assert.Equal(DayKind.Holiday, result.Kind);
        Assert.Contains(result.Notes,
            note => note.Contains("gilt nicht im ganzen Bundesland", StringComparison.Ordinal));
    }

    [Fact]
    public void Ein_Wochenende_ohne_Erfassung_ist_frei()
    {
        DayInput input = Eingabe(tag: null) with { Date = new DateOnly(2026, 9, 19) };

        DayAnalysis result = new GapFinder().Analyse(input);

        Assert.Equal(DayKind.Weekend, result.Kind);
        Assert.Equal(DayVerdict.DayOff, result.Verdict);
    }

    // --- Der vergessene Tag -------------------------------------------------------------

    [Fact]
    public void Ein_Arbeitstag_ganz_ohne_Zeiterfassung_ist_ein_eigener_Befund()
    {
        // Wo keine Anwesenheit steht, gibt es auch keine Luecke - ein vollstaendig
        // vergessener Tag fiele durch jede reine Lueckenrechnung.
        DayAnalysis result = new GapFinder().Analyse(Eingabe(tag: null));

        Assert.Empty(result.Gaps);
        Assert.Equal(DayVerdict.NoTimeRecorded, result.Verdict);
        Assert.True(result.NeedsAttention);
    }

    [Fact]
    public void Heute_ohne_Stempel_ist_kein_Versaeumnis()
    {
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(tag: null, jetzt: At(7, 30)));

        Assert.Equal(DayVerdict.Ongoing, result.Verdict);
        Assert.False(result.NeedsAttention);
    }

    [Fact]
    public void Ohne_Arbeitszeitmodell_wird_die_Annahme_ausgesprochen()
    {
        DayAnalysis result = new GapFinder().Analyse(Eingabe(tag: null, modell: LeeresModell));

        Assert.Equal(DayKind.Unknown, result.Kind);
        Assert.Equal(DayVerdict.NoTimeRecorded, result.Verdict);
        Assert.Contains(result.Notes,
            note => note.Contains("kein Arbeitszeitmodell", StringComparison.Ordinal));
    }

    // --- Vorschlaege --------------------------------------------------------------------

    [Fact]
    public void Bei_gleichem_Ticket_links_und_rechts_wird_es_vorgeschlagen()
    {
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)),
                    [Leistung(8, 0, 60, ticketId: 4711, companyId: 100),
                     Leistung(11, 0, 60, ticketId: 4711, companyId: 100)]));

        Gap gap = Assert.Single(result.Gaps);
        Assert.Equal(4711, gap.SuggestedTicketId);
        Assert.Equal(100, gap.SuggestedCompanyId);
    }

    [Fact]
    public void Bei_verschiedenen_Tickets_wird_nichts_vorgeschlagen()
    {
        // Ein geratenes Ticket, das jemand unbesehen bestaetigt, bucht Arbeitszeit beim
        // falschen Kunden - und das faellt niemandem auf.
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)),
                    [Leistung(8, 0, 60, ticketId: 4711), Leistung(11, 0, 60, ticketId: 4712)]));

        Gap gap = Assert.Single(result.Gaps);
        Assert.Equal(0, gap.SuggestedTicketId);
    }

    [Fact]
    public void Mit_nur_einer_Nachbarin_zaehlt_deren_Ticket()
    {
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)), [Leistung(8, 0, 60, ticketId: 4711)]));

        Gap gap = Assert.Single(result.Gaps);
        Assert.Equal(4711, gap.SuggestedTicketId);
    }

    // --- Eigenheiten der Leistungen -----------------------------------------------------

    [Fact]
    public void Eine_Pause_innerhalb_einer_Leistung_reisst_keine_Luecke()
    {
        // Ob duration eine eingetragene Pause enthaelt, ist nicht gemessen. Der Aufschlag
        // verhindert, dass das Stueck dahinter als Luecke erscheint.
        SupportEntry mitPause = Leistung(8, 0, 180) with { DurationBreak = 60 };

        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)), [mitPause]));

        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void Ein_Termin_deckt_keine_Arbeitszeit_ab()
    {
        SupportEntry termin = Leistung(8, 0, 240) with
        {
            PlanningType = PlanningType.AppointmentFix,
        };

        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)), [termin]));

        Gap gap = Assert.Single(result.Gaps);
        Assert.Equal(TimeSpan.FromHours(4), gap.Duration);
    }

    [Fact]
    public void Eine_Leistung_ohne_Planungsart_gilt_als_Leistung()
    {
        // Die aelteste und haeufigste Form traegt keine ausdrueckliche Planungsart. Sie als
        // Termin zu behandeln machte jeden gewoehnlichen Tag zu einer einzigen Luecke.
        SupportEntry ohne = Leistung(8, 0, 240) with { PlanningType = null };

        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)), [ohne]));

        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void Ueberlappende_Leistungen_reissen_kein_Loch()
    {
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)), [Leistung(8, 0, 180), Leistung(10, 0, 120)]));

        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void Die_Deckung_wird_als_Anteil_ausgewiesen()
    {
        DayAnalysis result = new GapFinder().Analyse(
            Eingabe(Tag(Abschnitt(8, 0, 12, 0)), [Leistung(8, 0, 120)]));

        Assert.Equal(0.5d, result.Coverage, precision: 3);
        Assert.Equal(TimeSpan.FromHours(2), result.GapTotal);
        Assert.Equal(TimeSpan.FromHours(4), result.AttendanceTotal);
    }

    // --- Hilfsmittel --------------------------------------------------------------------

    private static WorkingTimeModel LeeresModell { get; } = new()
    {
        Id = 99,
        Name = "OHNE TAGE",
        Days = new Dictionary<string, WorkingDay>(StringComparer.OrdinalIgnoreCase),
    };

    private static AbsenceRequest Urlaub(bool ganztags) => new()
    {
        Id = 42,
        PlanningType = PlanningType.Vacation,
        Status = AbsenceStatus.Approved,
        Days = [Tagesangabe(forenoon: true, afternoon: ganztags)],
    };

    private static AbsenceDay Tagesangabe(bool forenoon, bool afternoon) => new()
    {
        VacationRequestId = 42,
        // TANSS setzt den Tag auf Mitternacht der Serverzeitzone; AbsenceDay.Day liest ihn
        // in Ortszeit zurueck, und genau das wird hier mitgeprueft.
        Date = TanssTime.ToUnixSeconds(Start),
        Forenoon = forenoon,
        Afternoon = afternoon,
    };
}
