using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.Workday.Holidays;
using TanssTagesabschluss.Workday.Model;
using Xunit;

namespace TanssTagesabschluss.Workday.Tests;

/// <summary>
/// Der erwartete Arbeitsrahmen — und der teuerste blinde Fleck, den er schliesst.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass ist ein gemeldeter Fall.</b> Am Montag, 14.09.2026 wurde erst um 11:08
/// eingestempelt, obwohl der Arbeitstag um 8:00 beginnt. Die drei Stunden davor tragen keinen
/// Zeitstempel — und eine Lückenrechnung, die nur <i>innerhalb</i> der gestempelten Anwesenheit
/// sucht, sieht dort nichts. Genau diese Stunden fielen durch jedes Raster.</para>
/// <para><b>Warum der Rahmen nicht aus TANSS kommt:</b> Nachgemessen gegen 10.10.0 nennt die
/// Schnittstelle zu einem Arbeitszeitmodell Kennung und Namen, aber keinen Wochenplan; vielen
/// Mitarbeitern ist überdies gar keines zugeordnet.</para>
/// </remarks>
public sealed class WorkWeekTests
{
    private static readonly DateOnly Montag = new(2026, 9, 14);
    private static readonly DateOnly Samstag = new(2026, 9, 12);

    private static DateTimeOffset Start => GapFinder.DayWindow(Montag).Start;

    private static DateTimeOffset At(int hour, int minute = 0) =>
        Start.AddHours(hour).AddMinutes(minute);

    /// <summary>Mo–Fr, 8 bis 17 Uhr — gesetzt, nicht angenommen.</summary>
    private static WorkWeek Gesetzt => WorkWeek.Of(
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
         DayOfWeek.Friday],
        new TimeOnly(8, 0),
        new TimeOnly(17, 0));

    private static GapOptions MitRahmen => new() { WorkWeek = Gesetzt };

    private static TimestampPeriod Abschnitt(int fromHour, int fromMinute, int toHour,
                                             int toMinute) => new()
    {
        Begin = TanssTime.ToUnixSeconds(At(fromHour, fromMinute)),
        End = TanssTime.ToUnixSeconds(At(toHour, toMinute)),
        State = "COMPLETE",
    };

    private static TimestampDay Tag(DateOnly datum, params TimestampPeriod[] arbeit) => new()
    {
        EmployeeId = 1,
        Date = datum,
        WeekDay = datum.DayOfWeek.ToString().ToUpperInvariant(),
        Types = new Dictionary<string, IReadOnlyList<TimestampPeriod>>(StringComparer.OrdinalIgnoreCase)
        {
            [TimestampType.Work] = arbeit,
        },
    };

    private static DayInput Eingabe(DateOnly datum, TimestampDay? tag,
                                    DateTimeOffset? jetzt = null,
                                    params SupportEntry[] leistungen) => new()
    {
        Date = datum,
        Timestamps = tag,
        Supports = leistungen,
        Holidays = HolidaySet.Empty,
        Now = jetzt ?? At(23, 0),
    };

    private static SupportEntry Leistung(int fromHour, int fromMinute, int minutes) => new()
    {
        Id = (fromHour * 100) + fromMinute,
        Date = TanssTime.ToUnixSeconds(At(fromHour, fromMinute)),
        Duration = minutes,
        EmployeeId = 1,
        PlanningType = PlanningType.Support,
    };

    [Fact]
    public void Wer_zu_spaet_einstempelt_bekommt_die_Zeit_davor_als_offenes_Fenster()
    {
        // Der gemeldete Fall: eingestempelt um 11:08, erwartet ab 8:00 -- und die Zeit ab
        // 11:08 ist lueckenlos mit Leistungen belegt. Uebrig bleibt genau das, was ohne
        // Arbeitsrahmen unsichtbar gewesen waere.
        DayAnalysis result = new GapFinder(MitRahmen).Analyse(
            Eingabe(Montag, Tag(Montag, Abschnitt(11, 8, 17, 0)), null, Leistung(11, 8, 352)));

        Gap only = Assert.Single(result.Gaps);

        Assert.Equal(At(8, 0), only.Segment.Start);
        Assert.Equal(At(11, 8), only.Segment.End);
        Assert.Equal(188, (int)only.Duration.TotalMinutes);
    }

    [Fact]
    public void Ohne_jede_Leistung_ist_der_ganze_Rahmen_offen()
    {
        // Anwesenheit und Rahmen verschmelzen zu einem Fenster: Wer von 8 bis 17 erwartet wird
        // und nichts erfasst hat, hat neun offene Stunden und nicht zwei Fenster.
        DayAnalysis result = new GapFinder(MitRahmen)
            .Analyse(Eingabe(Montag, Tag(Montag, Abschnitt(11, 8, 17, 0))));

        Gap only = Assert.Single(result.Gaps);

        Assert.Equal(At(8, 0), only.Segment.Start);
        Assert.Equal(At(17, 0), only.Segment.End);
    }

    [Fact]
    public void Der_Hinweis_nennt_beide_Uhrzeiten()
    {
        // "Eingestempelt wurde erst um 11:08, erwartet war 08:00." -- ohne die zweite Zahl
        // muesste jemand raten, woher das Fenster kommt.
        DayAnalysis result = new GapFinder(MitRahmen)
            .Analyse(Eingabe(Montag, Tag(Montag, Abschnitt(11, 8, 17, 0))));

        Assert.Contains(result.Notes,
            note => note.Contains("Eingestempelt wurde erst um", StringComparison.Ordinal));
    }

    [Fact]
    public void An_einem_freien_Tag_gilt_kein_Rahmen()
    {
        // Sonst mahnte das Werkzeug jeden Samstag -- und wer zweimal grundlos gemahnt wird,
        // sieht beim dritten Mal nicht mehr hin.
        DayAnalysis result = new GapFinder(MitRahmen).Analyse(
            Eingabe(Samstag, null, GapFinder.DayWindow(Samstag).Start.AddHours(23)));

        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void Am_laufenden_Tag_reicht_der_Rahmen_nur_bis_jetzt()
    {
        // Um zehn Uhr ist der Nachmittag noch nicht versaeumt. Ohne diesen Schnitt staende
        // dort eine Luecke bis Feierabend.
        DayAnalysis result = new GapFinder(MitRahmen)
            .Analyse(Eingabe(Montag, null, At(10, 0)));

        Assert.All(result.Gaps, gap => Assert.True(gap.Segment.End <= At(10, 0)));
    }

    [Fact]
    public void Der_Rahmen_gilt_nur_an_den_gewaehlten_Tagen()
    {
        WorkWeek nurDienstags = WorkWeek.Of([DayOfWeek.Tuesday], new TimeOnly(8, 0),
                                            new TimeOnly(17, 0));

        Assert.Null(nurDienstags.FrameOn(Montag));
        Assert.NotNull(nurDienstags.FrameOn(new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public void Sieben_abgewaehlte_Tage_ergeben_die_Vorgabe()
    {
        // Sonst meldete das Werkzeug nie etwas, und niemand kaeme auf die Einstellung. Die
        // Pruefung beim Laden faengt das ab; dies ist der zweite Riegel.
        Assert.Equal(WorkWeek.Default, WorkWeek.Of([], new TimeOnly(8, 0), new TimeOnly(17, 0)));
    }

    [Fact]
    public void Ein_verkehrter_Rahmen_ergibt_die_Vorgabe()
    {
        // Ende vor Beginn hiesse eine negative Sollzeit -- und ein Fenster, das rueckwaerts
        // laeuft, ergibt an jeder weiteren Stelle Unsinn.
        Assert.Equal(WorkWeek.Default,
            WorkWeek.Of([DayOfWeek.Monday], new TimeOnly(17, 0), new TimeOnly(8, 0)));
    }

    [Fact]
    public void Die_Sollzeit_ist_die_Dauer_des_Rahmens()
    {
        Assert.Equal(TimeSpan.FromHours(9), Gesetzt.TargetOn(Montag));
        Assert.Null(Gesetzt.TargetOn(Samstag));
    }

    [Fact]
    public void Die_Arbeitstage_werden_in_der_Reihenfolge_der_Woche_genannt()
    {
        Assert.Equal("Mo, Di, Mi, Do, Fr", WorkWeek.Default.Describe());
        Assert.Equal("Sa, So",
            WorkWeek.Of([DayOfWeek.Sunday, DayOfWeek.Saturday], new TimeOnly(8, 0),
                        new TimeOnly(17, 0)).Describe());
    }
}
