using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.Workday.Model;
using Xunit;

namespace TanssTagesabschluss.Workday.Tests;

/// <summary>
/// Eine Lücke in zwei Tätigkeiten teilen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass.</b> Neun offene Stunden am Stück sind fast nie neun Stunden an
/// derselben Sache. Ohne Teilen bliebe nur, alles in <i>einen</i> Leistungstext zu schreiben
/// und auf <i>ein</i> Ticket zu buchen — erfasst wäre es, auf der Rechnung des Kunden aber
/// nicht mehr auseinanderzuhalten.</para>
/// <para><b>Was hier zählt, ist die Zeit.</b> Geht beim Teilen eine Minute verloren oder kommt
/// eine hinzu, steht sie am Ende zu wenig oder zu viel auf einer Rechnung. Deshalb prüft der
/// erste Test die Summe und nicht nur die Grenzen.</para>
/// </remarks>
public sealed class GapSplitTests
{
    private static readonly DateOnly Montag = new(2026, 9, 14);

    private static DateTimeOffset At(int hour, int minute = 0) =>
        GapFinder.DayWindow(Montag).Start.AddHours(hour).AddMinutes(minute);

    /// <summary>Eine Lücke von 8:00 bis 17:00 — neun Stunden.</summary>
    private static Gap Neun(SupportEntry? davor = null, SupportEntry? danach = null) =>
        new(new TimeSegment(At(8), At(17)), davor, danach);

    private static SupportEntry Leistung(int ticketId, int companyId) => new()
    {
        Id = ticketId,
        Date = TanssTime.ToUnixSeconds(At(7)),
        Duration = 60,
        TicketId = ticketId,
        CompanyId = companyId,
        PlanningType = PlanningType.Support,
    };

    [Fact]
    public void Die_Teile_grenzen_lueckenlos_aneinander_und_ergeben_zusammen_das_Ganze()
    {
        (Gap first, Gap second) = Neun().SplitAfter(TimeSpan.FromMinutes(150))!.Value;

        Assert.Equal(At(8), first.Start);
        Assert.Equal(At(10, 30), first.End);
        Assert.Equal(At(10, 30), second.Start);
        Assert.Equal(At(17), second.End);

        // Die Summe ist der Kern: Eine verlorene Minute ist eine Minute, die niemandem
        // berechnet wird; eine doppelte ist eine, die zweimal berechnet wird.
        Assert.Equal(TimeSpan.FromHours(9), first.Duration + second.Duration);
    }

    [Fact]
    public void Jede_Nachbarin_bleibt_auf_ihrer_Seite()
    {
        SupportEntry davor = Leistung(11, 100);
        SupportEntry danach = Leistung(22, 200);

        (Gap first, Gap second) = Neun(davor, danach).SplitAfter(TimeSpan.FromHours(4))!.Value;

        Assert.Same(davor, first.Before);
        Assert.Null(first.After);

        Assert.Null(second.Before);
        Assert.Same(danach, second.After);
    }

    [Fact]
    public void Nach_innen_wird_nichts_mehr_vorgeschlagen()
    {
        // Vorher trugen beide Nachbarinnen dasselbe Ticket, und die ganze Luecke bekam es
        // vorgeschlagen. Nach dem Teilen hat jeder Teil nur noch EINE Nachbarin -- und nach
        // innen steht die andere Haelfte, die noch gar nichts traegt. Ein Vorschlag von dort
        // waere aus der Luft gegriffen.
        Gap ganz = Neun(Leistung(11, 100), Leistung(11, 100));
        Assert.Equal(11, ganz.SuggestedTicketId);

        (Gap first, Gap second) = ganz.SplitAfter(TimeSpan.FromHours(4))!.Value;

        Assert.Equal(11, first.SuggestedTicketId);
        Assert.Equal(11, second.SuggestedTicketId);
        Assert.Null(first.After);
        Assert.Null(second.Before);
    }

    /// <param name="minuten">Die zu prüfende Dauer des ersten Teils.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(540)]
    [InlineData(541)]
    public void Wo_kein_zweiter_Teil_uebrig_bliebe_wird_nicht_geteilt(int minuten)
    {
        // Eine "Teilung", bei der ein Teil leer ist, waere keine -- sie haette nur die
        // Nachbarinnen weggeworfen und die Zeile grundlos ersetzt.
        Assert.Null(Neun().SplitAfter(TimeSpan.FromMinutes(minuten)));
    }

    [Fact]
    public void Ein_Teil_laesst_sich_wieder_teilen()
    {
        // Drei Taetigkeiten in einem Fenster sind nicht seltener als zwei.
        (Gap first, Gap rest) = Neun().SplitAfter(TimeSpan.FromHours(2))!.Value;
        (Gap second, Gap third) = rest.SplitAfter(TimeSpan.FromHours(3))!.Value;

        Assert.Equal(At(8), first.Start);
        Assert.Equal(At(10), second.Start);
        Assert.Equal(At(13), third.Start);
        Assert.Equal(At(17), third.End);

        Assert.Equal(TimeSpan.FromHours(9),
                     first.Duration + second.Duration + third.Duration);
    }

    [Fact]
    public void Die_Sekunde_geht_nicht_verloren()
    {
        // Krumme Grenzen sind der Normalfall: Wer um 11:08 einstempelt, hat eine Luecke von
        // 188 Minuten. Auch dort muss die Summe stimmen.
        Gap krumm = new(new TimeSegment(At(8), At(11, 8)), null, null);

        (Gap first, Gap second) = krumm.SplitAfter(TimeSpan.FromMinutes(47))!.Value;

        Assert.Equal(At(8, 47), first.End);
        Assert.Equal(At(8, 47), second.Start);
        Assert.Equal(krumm.Duration, first.Duration + second.Duration);
    }
}
