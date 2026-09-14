using System.Globalization;
using System.Net.Http;
using System.Runtime.Versioning;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.Api.Repository;
using TanssTagesabschluss.App.Services;
using TanssTagesabschluss.Storage.Cache;
using TanssTagesabschluss.Storage.Config;
using TanssTagesabschluss.Storage.Secrets;
using TanssTagesabschluss.Workday;
using TanssTagesabschluss.Workday.Holidays;

namespace TanssTagesabschluss.App.Runtime;

/// <summary>
/// Der Zusammenbau: aus einer geprüften <see cref="AppConfig"/> entstehen hier alle Bausteine.
/// </summary>
/// <remarks>
/// <para><b>Es gibt genau diese eine Stelle, an der verdrahtet wird.</b> Jeder zweite Ort, an
/// dem ein Baustein entsteht, ist ein Ort, an dem eine Einstellung fehlen kann — und die
/// gefährlichste davon ist die Mitarbeiterkennung: Steht sie falsch, prüft das Werkzeug
/// klaglos die Tage eines Kollegen.</para>
///
/// <para><b>Zwei Verbindungsschichten, und das ist Absicht.</b> Der
/// <see cref="TanssClient"/> spricht mit der Instanz des Kunden und trägt deren Token; der
/// <see cref="HttpClient"/> für Feiertage und Postleitzahlen spricht mit zwei öffentlichen
/// Diensten und trägt <b>nichts</b>. Sie zu teilen hiesse, ein Kundentoken an einem Zugang zu
/// führen, der nach draussen geht.</para>
///
/// <para><b>Die Feiertage laufen über den Zwischenspeicher und nicht unmittelbar über den
/// Dienst.</b> Die Übersicht rechnet beim Blättern jeden Tag neu; ohne Zwischenspeicher ginge
/// bei jedem Tastendruck eine Anfrage nach draussen.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class RuntimeComposition : IDisposable
{
    private readonly HttpClient _public;
    private bool _disposed;

    /// <summary>Baut alles aus der geprüften Konfiguration.</summary>
    /// <param name="config">Die geladene und geprüfte Konfiguration.</param>
    /// <param name="clock">Die Uhr; für Tests einsetzbar.</param>
    /// <param name="holidayCacheDirectory">
    /// Abweichender Ordner des Feiertagszwischenspeichers. Nur für Tests gedacht.
    /// </param>
    public RuntimeComposition(AppConfig config, TimeProvider? clock = null,
                              string? holidayCacheDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        Config = config;
        Clock = clock ?? TimeProvider.System;

        Tokens = DpapiTokenStore.Default();

        // Das Proxy-Kennwort wird HIER aufgeloest und nicht in ToTanssOptions: Jenes Stueck ist
        // eine reine Abbildung und kennt keinen Schluesselspeicher. Ohne diese Zeile ginge bei
        // einem Proxy mit Anmeldung eine leere Zeichenkette hinaus, und die Abweisung saehe aus
        // wie ein Problem mit TANSS.
        string? proxyPassword = config.Proxy is { Enabled: true, User: not null }
            ? DpapiSecretStore.ForProxy().Read()
            : null;

        Client = new TanssClient(config.ToTanssOptions(proxyPassword), Tokens);

        Timestamps = new TimestampRepository(Client);
        Supports = new SupportRepository(Client);
        Absences = new AbsenceRepository(Client);
        Tickets = new TicketRepository(Client);
        Companies = new CompanyRepository(Client);
        Devices = new DeviceRepository(Client);
        Technicians = new TechnicianRepository(Client);

        // Eine eigene Verbindungsschicht fuer die oeffentlichen Dienste - siehe Klassenkommentar.
        // Die Zeitgrenze ist knapper als die zu TANSS: Ohne Feiertage laesst sich weiterarbeiten,
        // und niemand soll dreissig Sekunden auf einen Kalender warten.
        _public = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        Holidays = new HolidayCache(new FeiertageApiProvider(_public), holidayCacheDirectory, Clock);
        FederalStates = new OpenPlzStateResolver(_public);

        Days = new DayService(Timestamps, Supports, Absences, Holidays, Clock);
        Booking = new GapBooking(Supports, config.Tanss.EmployeeId);
    }

    /// <summary>Die Konfiguration, aus der alles gebaut wurde.</summary>
    public AppConfig Config { get; }

    /// <summary>Die Uhr, mit der dieser Zusammenbau rechnet.</summary>
    public TimeProvider Clock { get; }

    /// <summary>Der Tokenspeicher im lokalen Profil.</summary>
    public ITokenStore Tokens { get; }

    /// <summary>Der HTTP-Zugang zu TANSS.</summary>
    public TanssClient Client { get; }

    /// <summary>Die Zeiterfassung — ausschließlich lesend.</summary>
    public ITimestampRepository Timestamps { get; }

    /// <summary>Leistungen lesen und anlegen.</summary>
    public ISupportRepository Supports { get; }

    /// <summary>Urlaub, Krankheit und sonstige Abwesenheiten.</summary>
    public IAbsenceRepository Absences { get; }

    /// <summary>Die Tickets — für die Auswahl beim Nachtragen.</summary>
    public ITicketRepository Tickets { get; }

    /// <summary>Die Firmensuche und die Erkennung der eigenen Firma.</summary>
    public ICompanyRepository Companies { get; }

    /// <summary>Die Techniker der Instanz — für die Einrichtung.</summary>
    public ITechnicianRepository Technicians { get; }

    /// <summary>
    /// Die Geräte einer Firma — für die Zuordnung einer nachgetragenen Leistung.
    /// </summary>
    /// <remarks>
    /// Ein Ticket sagt, worum es ging; das Gerät sagt, woran gearbeitet wurde. Nur mit
    /// Zuordnung taucht die Leistung später in der Gerätehistorie des Kunden auf.
    /// </remarks>
    public IDeviceRepository Devices { get; }

    /// <summary>Die Feiertage, über den Zwischenspeicher.</summary>
    public IHolidayProvider Holidays { get; }

    /// <summary>Die Bestimmung des Bundeslands aus einer Postleitzahl.</summary>
    public IFederalStateResolver FederalStates { get; }

    /// <summary>Der Dienst, der einen Zeitraum beurteilt.</summary>
    public DayService Days { get; }

    /// <summary>Der Dienst, der eine Lücke schliesst.</summary>
    public GapBooking Booking { get; }

    /// <summary>Das Bundesland aus der Konfiguration; <see langword="null"/>, wenn keines feststeht.</summary>
    /// <remarks>
    /// Aufgelöst und nicht als Zeichenkette durchgereicht: Ein unbekanntes Kürzel soll hier
    /// auffallen und nicht erst dort, wo die Feiertage gebraucht werden. Die Prüfung beim Laden
    /// fängt das ab — dies ist der zweite Riegel.
    /// </remarks>
    public FederalState? State => FederalState.ByCode(Config.Company.FederalState);

    /// <summary>Die Lückenregeln aus der Konfiguration.</summary>
    public GapOptions GapOptions => new()
    {
        MinimumGap = TimeSpan.FromMinutes(Config.Gaps.MinimumMinutes),
        ConditionalHolidayCountsAsDayOff = Config.Gaps.ConditionalHolidayIsDayOff,
        WorkWeek = ReadWorkWeek(Config.Gaps),
    };

    /// <summary>
    /// Liest die Arbeitswoche aus der Konfiguration.
    /// </summary>
    /// <remarks>
    /// <b>Zweiter Riegel.</b> Die Prüfung beim Laden verlangt Tage und Uhrzeiten und lässt eine
    /// Konfiguration ohne sie gar nicht durch. Bleibt hier trotzdem nichts Erkennbares übrig,
    /// gilt <see cref="WorkWeek.Default"/> — eine Ausnahme an dieser Stelle kostete die ganze
    /// Tagesansicht für einen Tippfehler, den die Einstellungen längst beanstandet haben.
    /// </remarks>
    private static WorkWeek ReadWorkWeek(GapSection gaps)
    {
        List<DayOfWeek> days = [];

        foreach (string name in gaps.WorkDays)
        {
            if (Enum.TryParse(name, ignoreCase: true, out DayOfWeek day))
            {
                days.Add(day);
            }
        }

        return TimeOnly.TryParse(gaps.WorkBegin, CultureInfo.InvariantCulture, out TimeOnly begin)
               && TimeOnly.TryParse(gaps.WorkEnd, CultureInfo.InvariantCulture, out TimeOnly end)
            ? WorkWeek.Of(days, begin, end)
            : WorkWeek.Default;
    }

    /// <summary>
    /// Baut einen zweiten Zugang mit einem <b>anderen</b> Token.
    /// </summary>
    /// <remarks>
    /// Der einzige Grund dafür ist die Probe beim Tokenwechsel: Das frisch geprägte Token muss
    /// sich bewähren, <b>bevor</b> es das bisherige ablöst. Gelingt die Probe nicht, soll alles
    /// bleiben, wie es war.
    /// </remarks>
    /// <param name="token">Das zu prüfende Token, mit oder ohne <c>Bearer </c>.</param>
    /// <returns>Der Zugang. Der Aufrufer gibt ihn frei.</returns>
    public TanssClient CreateClientWith(string token) =>
        new(Config.ToTanssOptions(), new FixedTokenStore(token));

    /// <summary>Gibt beide Verbindungsschichten frei.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Client.Dispose();
        _public.Dispose();
    }

    /// <summary>Ein Tokenspeicher mit festem Wert — für die Probe beim Tokenwechsel.</summary>
    /// <remarks>
    /// <see cref="Write"/> tut nichts und wirft auch nicht: Dieser Speicher ist eine Attrappe
    /// für genau einen Aufruf, und ein Schreibversuch darauf wäre ein Programmfehler, der die
    /// Probe nicht wert ist, abzubrechen.
    /// </remarks>
    private sealed class FixedTokenStore : ITokenStore
    {
        private readonly string _token;

        public FixedTokenStore(string token) => _token = token;

        public string Read() => _token;

        public void Write(string token)
        {
            // Absichtlich leer - siehe Klassenkommentar.
        }
    }
}
