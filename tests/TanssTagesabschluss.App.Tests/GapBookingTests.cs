using System.Text.Json.Nodes;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.App.Services;
using Xunit;

namespace TanssTagesabschluss.App.Tests;

/// <summary>
/// Der Riegel gegen Dubletten — die Regel, deren Bruch am teuersten ist.
/// </summary>
/// <remarks>
/// <b>TANSS dedupliziert Leistungen nicht.</b> Ein zweiter Aufruf ergibt einen zweiten Posten
/// auf der Rechnung des Kunden, und eine gebuchte Leistung lässt sich von hier aus nicht
/// zurücknehmen. Jeder Test hier steht für einen Weg, auf dem eine Dublette entstehen könnte.
/// </remarks>
public sealed class GapBookingTests
{
    private static readonly DateTimeOffset Begin =
        new(2026, 9, 14, 9, 0, 0, TimeSpan.FromHours(2));

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private static BookingTarget OnTicket => BookingTarget.On(4711, 100);

    [Fact]
    public async Task Eine_Leistung_wird_angelegt_wenn_das_Fenster_frei_ist()
    {
        FakeSupports supports = new();
        GapBooking booking = new(supports, employeeId: 7);

        BookingResult result = await booking.BookAsync(Begin, Hour, "Drucker eingerichtet", OnTicket);

        Assert.Equal(BookingOutcome.Created, result.Outcome);
        Assert.Equal(1, supports.Created);
    }

    [Fact]
    public async Task Steht_schon_eine_Leistung_da_wird_NICHTS_gesendet()
    {
        FakeSupports supports = new() { Exists = true };
        GapBooking booking = new(supports, employeeId: 7);

        BookingResult result = await booking.BookAsync(Begin, Hour, "Text", OnTicket);

        Assert.Equal(BookingOutcome.AlreadyExists, result.Outcome);
        Assert.Equal(0, supports.Created);
        Assert.Equal(0, supports.Prepared);
    }

    [Fact]
    public async Task Scheitert_die_Pruefung_selbst_wird_NICHTS_gesendet()
    {
        // DER Umkehrschluss, den man leicht falsch macht: "unbekannt" heisst nicht "nicht
        // vorhanden". Wer hier im Zweifel sendet, erzeugt doppelt berechnete Arbeitszeit in
        // der Produktivinstanz eines Kunden.
        FakeSupports supports = new() { ExistsThrows = true };
        GapBooking booking = new(supports, employeeId: 7);

        BookingResult result = await booking.BookAsync(Begin, Hour, "Text", OnTicket);

        Assert.Equal(BookingOutcome.CheckFailed, result.Outcome);
        Assert.Equal(0, supports.Created);
    }

    [Fact]
    public async Task Ohne_Text_wird_nicht_gebucht()
    {
        // Was dort steht, liest der Kunde auf seiner Rechnung.
        FakeSupports supports = new();
        GapBooking booking = new(supports, employeeId: 7);

        BookingResult result = await booking.BookAsync(Begin, Hour, "   ", OnTicket);

        Assert.Equal(BookingOutcome.Rejected, result.Outcome);
        Assert.Equal(0, supports.Checked);
    }

    [Fact]
    public async Task Ohne_Ticket_und_ohne_Firma_wird_nicht_gebucht()
    {
        // Ohne beides weiss TANSS nicht, wem die Zeit zu berechnen ist - und kann auch
        // Stundensatz und Abrechnungsart nicht vorbelegen.
        FakeSupports supports = new();
        GapBooking booking = new(supports, employeeId: 7);

        BookingResult result = await booking.BookAsync(
            Begin, Hour, "Text", new BookingTarget(0, 0, 0, 0, Internal: false));

        Assert.Equal(BookingOutcome.Rejected, result.Outcome);
        Assert.Equal(0, supports.Checked);
    }

    [Fact]
    public async Task Ein_Fehlschlag_beim_Anlegen_hat_einen_OFFENEN_Ausgang()
    {
        // Eine Zeitueberschreitung heisst nicht, dass TANSS nichts getan hat. Deshalb wird
        // NICHT von selbst wiederholt.
        FakeSupports supports = new() { CreateThrows = true };
        GapBooking booking = new(supports, employeeId: 7);

        BookingResult result = await booking.BookAsync(Begin, Hour, "Text", OnTicket);

        Assert.Equal(BookingOutcome.Unknown, result.Outcome);
        Assert.Contains("nicht sagen", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vorbelegt_wird_bevorzugt_aus_dem_Ticket()
    {
        // Ueber das Ticket findet TANSS Vertrag, Stundensatz und Abrechnungsart. Die Firma
        // allein fuehrt zwar auch zu einem Stundensatz, aber nicht zum Vertrag.
        FakeSupports supports = new();
        GapBooking booking = new(supports, employeeId: 7);

        await booking.BookAsync(Begin, Hour, "Text", OnTicket);

        Assert.Equal("TICKET", supports.LastSource?.Type);
        Assert.Equal(4711, supports.LastSource?.Id);
    }

    [Fact]
    public async Task Ohne_Ticket_wird_aus_der_Firma_vorbelegt()
    {
        FakeSupports supports = new();
        GapBooking booking = new(supports, employeeId: 7);

        await booking.BookAsync(Begin, Hour, "Text", BookingTarget.On(0, 100));

        Assert.Equal("COMPANY", supports.LastSource?.Type);
        Assert.Equal(100, supports.LastSource?.Id);
    }

    [Fact]
    public async Task Das_interne_Kennzeichen_geht_mit_hinaus()
    {
        FakeSupports supports = new();
        GapBooking booking = new(supports, employeeId: 7);

        await booking.BookAsync(Begin, Hour, "Wartung am eigenen Server",
                                new BookingTarget(4711, 100, 0, 0, Internal: true));

        Assert.True(supports.LastDraft?.Internal);
    }

    [Fact]
    public async Task Eine_Geraetezuordnung_geht_mit_hinaus()
    {
        FakeSupports supports = new();
        GapBooking booking = new(supports, employeeId: 7);

        await booking.BookAsync(Begin, Hour, "Drucker getauscht",
                                new BookingTarget(4711, 100, LinkType.Periphery, 17,
                                                  Internal: false));

        Assert.Equal(LinkType.Periphery, supports.LastDraft?.LinkTypeId);
        Assert.Equal(17, supports.LastDraft?.LinkId);
    }

    [Fact]
    public async Task Ohne_gewaehltes_Geraet_bleibt_die_Vorbelegung_von_TANSS_stehen()
    {
        // Aus einem Ticket, das an einem Geraet haengt, kommt die Zuordnung bereits mit. Sie
        // auf 0 zu setzen, nur weil der Techniker nichts gewaehlt hat, waere ein Verlust.
        FakeSupports supports = new()
        {
            Prefilled = """{"linkTypeId":1,"linkId":99}""",
        };

        GapBooking booking = new(supports, employeeId: 7);

        await booking.BookAsync(Begin, Hour, "Text", OnTicket);

        Assert.Equal(LinkType.Pc, supports.LastDraft?.LinkTypeId);
        Assert.Equal(99, supports.LastDraft?.LinkId);
    }

    [Fact]
    public async Task Der_Mitarbeiter_kommt_aus_dem_Dienst_und_nicht_aus_der_Vorbelegung()
    {
        FakeSupports supports = new() { Prefilled = """{"employeeId":999}""" };
        GapBooking booking = new(supports, employeeId: 7);

        await booking.BookAsync(Begin, Hour, "Text", OnTicket);

        Assert.Equal(7, supports.LastDraft?.EmployeeId);
    }

    /// <summary>Eine Attrappe der Leistungen, die zählt und sich merkt.</summary>
    private sealed class FakeSupports : ISupportRepository
    {
        public bool Exists { get; init; }

        public bool ExistsThrows { get; init; }

        public bool CreateThrows { get; init; }

        public string Prefilled { get; init; } = "{}";

        public int Checked { get; private set; }

        public int Prepared { get; private set; }

        public int Created { get; private set; }

        public SupportInitializer? LastSource { get; private set; }

        public SupportDraft? LastDraft { get; private set; }

        public Task<IReadOnlyList<SupportEntry>> ListAsync(int employeeId, Timeframe timeframe,
                                                           CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SupportEntry>>([]);

        public Task<bool> ExistsAsync(int employeeId, DateTimeOffset begin, TimeSpan duration,
                                      CancellationToken ct = default)
        {
            Checked++;

            return ExistsThrows
                ? throw new TanssUnreachableException("Netz weg.")
                : Task.FromResult(Exists);
        }

        public Task<SupportDraft> PrepareAsync(SupportInitializer source,
                                               CancellationToken ct = default)
        {
            Prepared++;
            LastSource = source;
            return Task.FromResult(SupportDraft.From(JsonNode.Parse(Prefilled)!));
        }

        public Task<int> CreateAsync(SupportDraft draft, CancellationToken ct = default)
        {
            if (CreateThrows)
            {
                throw new TanssUnreachableException("Zeitüberschreitung.");
            }

            Created++;
            LastDraft = draft;
            return Task.FromResult(1234);
        }
    }
}
