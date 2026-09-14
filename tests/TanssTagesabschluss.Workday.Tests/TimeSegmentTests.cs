using TanssTagesabschluss.Workday.Model;
using Xunit;

namespace TanssTagesabschluss.Workday.Tests;

/// <summary>
/// Die Mengenlehre, auf der alles andere ruht.
/// </summary>
/// <remarks>
/// Jeder Fehler hier wird weiter oben zu einer Lücke, die es nicht gibt, oder zu einer, die
/// niemand sieht. Deshalb steht hier auch das, was selbstverständlich aussieht.
/// </remarks>
public sealed class TimeSegmentTests
{
    private static readonly DateTimeOffset Day = new(2026, 9, 14, 0, 0, 0, TimeSpan.FromHours(2));

    private static TimeSegment At(int fromHour, int fromMinute, int toHour, int toMinute) =>
        new(Day.AddHours(fromHour).AddMinutes(fromMinute),
            Day.AddHours(toHour).AddMinutes(toMinute));

    [Fact]
    public void Beruehrende_Abschnitte_ueberschneiden_sich_nicht()
    {
        // 9-12 und 12-15: Die Grenze gehoert dem spaeteren. Waere das anders, liesse sich
        // keine Leistung lueckenlos an die vorige anschliessen.
        Assert.False(At(9, 0, 12, 0).Overlaps(At(12, 0, 15, 0)));
    }

    [Fact]
    public void Eine_Minute_Ueberlappung_zaehlt()
    {
        Assert.True(At(9, 0, 12, 1).Overlaps(At(12, 0, 15, 0)));
    }

    [Fact]
    public void Merge_verschmilzt_beruehrende_Abschnitte()
    {
        // Ohne diese Regel bliebe zwischen 12:00 und 12:00 eine Luecke der Dauer null stehen,
        // und die stuende am Ende als Eintrag in der Liste.
        IReadOnlyList<TimeSegment> merged = TimeSegment.Merge([At(9, 0, 12, 0), At(12, 0, 15, 0)]);

        TimeSegment only = Assert.Single(merged);
        Assert.Equal(At(9, 0, 15, 0), only);
    }

    [Fact]
    public void Merge_ordnet_und_entflicht()
    {
        IReadOnlyList<TimeSegment> merged = TimeSegment.Merge(
            [At(13, 0, 14, 0), At(9, 0, 10, 0), At(9, 30, 11, 0)]);

        Assert.Equal([At(9, 0, 11, 0), At(13, 0, 14, 0)], merged);
    }

    [Fact]
    public void Merge_wirft_leere_Abschnitte_weg()
    {
        Assert.Empty(TimeSegment.Merge([At(9, 0, 9, 0), At(10, 0, 9, 0)]));
    }

    [Fact]
    public void Subtract_schneidet_ein_Loch_in_die_Mitte()
    {
        IReadOnlyList<TimeSegment> rest =
            TimeSegment.Subtract([At(8, 0, 17, 0)], [At(10, 0, 11, 0)]);

        Assert.Equal([At(8, 0, 10, 0), At(11, 0, 17, 0)], rest);
    }

    [Fact]
    public void Subtract_laesst_nichts_uebrig_wenn_alles_gedeckt_ist()
    {
        Assert.Empty(TimeSegment.Subtract([At(8, 0, 17, 0)], [At(7, 0, 18, 0)]));
    }

    [Fact]
    public void Subtract_kommt_mit_ueberlappenden_Abzuegen_zurecht()
    {
        // Zwei Leistungen, die sich ueberschneiden, duerfen nicht doppelt abgezogen werden -
        // sonst risse der Abzug ein Loch, das es nicht gibt.
        IReadOnlyList<TimeSegment> rest = TimeSegment.Subtract(
            [At(8, 0, 17, 0)], [At(9, 0, 12, 0), At(11, 0, 13, 0)]);

        Assert.Equal([At(8, 0, 9, 0), At(13, 0, 17, 0)], rest);
    }

    [Fact]
    public void Subtract_uebergeht_Abzuege_ausserhalb()
    {
        IReadOnlyList<TimeSegment> rest = TimeSegment.Subtract(
            [At(8, 0, 12, 0)], [At(5, 0, 6, 0), At(20, 0, 21, 0)]);

        Assert.Equal([At(8, 0, 12, 0)], rest);
    }

    [Fact]
    public void Subtract_ueber_mehrere_Grundabschnitte()
    {
        // Der Normalfall eines Tages mit Mittagspause: zwei Anwesenheitsabschnitte, eine
        // Leistung, die in den zweiten faellt.
        IReadOnlyList<TimeSegment> rest = TimeSegment.Subtract(
            [At(8, 0, 12, 0), At(13, 0, 17, 0)], [At(13, 0, 15, 0)]);

        Assert.Equal([At(8, 0, 12, 0), At(15, 0, 17, 0)], rest);
    }

    [Fact]
    public void TotalOf_zaehlt_Ueberschneidungen_nur_einmal()
    {
        TimeSpan total = TimeSegment.TotalOf([At(9, 0, 11, 0), At(10, 0, 12, 0)]);

        Assert.Equal(TimeSpan.FromHours(3), total);
    }

    [Fact]
    public void ClipTo_beschneidet_beidseitig()
    {
        TimeSegment clipped = At(6, 0, 20, 0).ClipTo(At(8, 0, 17, 0));

        Assert.Equal(At(8, 0, 17, 0), clipped);
    }

    [Fact]
    public void ClipTo_ergibt_einen_leeren_Abschnitt_wenn_nichts_uebrig_bleibt()
    {
        Assert.True(At(6, 0, 7, 0).ClipTo(At(8, 0, 17, 0)).IsEmpty);
    }

    [Fact]
    public void Ein_verkehrt_herum_gebauter_Abschnitt_hat_keine_negative_Dauer()
    {
        // Sonst ergaeben sich negative Summen, und die faende niemand wieder.
        Assert.Equal(TimeSpan.Zero, At(12, 0, 9, 0).Duration);
    }
}
