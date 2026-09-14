using System.Globalization;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Diagnostics;
using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.App.Services;

/// <summary>Wie eine Buchung ausgegangen ist.</summary>
public enum BookingOutcome
{
    /// <summary>Die Leistung steht in TANSS.</summary>
    Created,

    /// <summary>
    /// In diesem Zeitfenster steht bereits eine Leistung. Es wurde <b>nichts</b> gesendet.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// Die Eingabe taugt nicht — kein Text, kein Ticket, keine Dauer.
    /// </summary>
    Rejected,

    /// <summary>
    /// Es liess sich nicht feststellen, ob die Leistung schon dasteht. Es wurde nichts gesendet.
    /// </summary>
    /// <remarks>
    /// <b>Ausdrücklich etwas anderes als ein Fehlschlag beim Anlegen.</b> Hier ist nichts
    /// passiert und der Versuch gefahrlos zu wiederholen. Beim Anlegen selbst ist der Ausgang
    /// womöglich offen — siehe <see cref="Unknown"/>.
    /// </remarks>
    CheckFailed,

    /// <summary>
    /// Das Anlegen ist fehlgeschlagen, aber der Ausgang ist <b>offen</b>.
    /// </summary>
    /// <remarks>
    /// Eine Zeitüberschreitung heißt nicht, dass TANSS nichts getan hat. Vor einer Wiederholung
    /// steht deshalb wieder die Existenzprüfung — die dieser Dienst von sich aus ausführt,
    /// sobald jemand es erneut versucht.
    /// </remarks>
    Unknown,
}

/// <summary>Das Ergebnis einer Buchung.</summary>
/// <param name="Outcome">Wie es ausgegangen ist.</param>
/// <param name="SupportId">Die Kennung der angelegten Leistung; <c>0</c>, wenn keine genannt wurde.</param>
/// <param name="Message">Ein Satz, der sagt, was ist und was zu tun ist.</param>
public sealed record BookingResult(BookingOutcome Outcome, int SupportId, string Message)
{
    /// <summary>Steht die Leistung jetzt in TANSS?</summary>
    public bool Succeeded => Outcome == BookingOutcome.Created;
}

/// <summary>
/// Worauf eine Leistung gebucht wird.
/// </summary>
/// <remarks>
/// <para><b>Ein eigener Typ statt vier Zahlen in der Parameterliste.</b> Ticket, Firma,
/// Zuordnungstyp und Zuordnungskennung gehören zusammen und sind allesamt <c>int</c> — vier
/// gleichartige Argumente hintereinander werden irgendwann vertauscht, und eine vertauschte
/// Buchung landet beim falschen Kunden.</para>
///
/// <para><b>Ticket und Gerät schliessen einander nicht aus.</b> Der Regelfall ist beides: das
/// Ticket sagt, worum es ging, das Gerät, woran gearbeitet wurde. Wer nur eines hat, gibt nur
/// eines an — aber ohne Ticket <i>und</i> ohne Firma weiß TANSS nicht, wem die Zeit zu
/// berechnen ist.</para>
/// </remarks>
/// <param name="TicketId">Das Ticket; <c>0</c>, wenn keines gewählt wurde.</param>
/// <param name="CompanyId">Die Firma; <c>0</c>, wenn sie sich aus dem Ticket ergibt.</param>
/// <param name="LinkTypeId">
/// Der Zuordnungstyp des Geräts — siehe <see cref="LinkType"/>; <c>0</c> für keine Zuordnung.
/// </param>
/// <param name="LinkId">Die Kennung des Geräts; <c>0</c> für keine Zuordnung.</param>
/// <param name="Internal">
/// Als <b>intern</b> buchen — Arbeit am eigenen Haus, die der Kunde nicht sieht.
/// </param>
public sealed record BookingTarget(int TicketId, int CompanyId, int LinkTypeId, int LinkId,
                                   bool Internal)
{
    /// <summary>Eine Buchung ohne Zuordnung und ohne internes Kennzeichen.</summary>
    /// <param name="ticketId">Das Ticket.</param>
    /// <param name="companyId">Die Firma.</param>
    /// <returns>Das Ziel.</returns>
    public static BookingTarget On(int ticketId, int companyId) =>
        new(ticketId, companyId, 0, 0, Internal: false);

    /// <summary>Weiß TANSS, wem die Zeit zu berechnen ist?</summary>
    public bool HasBillingAnchor => TicketId > 0 || CompanyId > 0;

    /// <summary>Ist ein Gerät vollständig angegeben?</summary>
    /// <remarks>
    /// Beides oder nichts: Ein Typ ohne Kennung zeigt auf nichts, eine Kennung ohne Typ ist
    /// mehrdeutig — die 17 ist ein PC <i>und</i> ein Drucker.
    /// </remarks>
    public bool HasAssignment => LinkTypeId > 0 && LinkId > 0;
}

/// <summary>
/// Trägt eine Leistung für eine Lücke nach.
/// </summary>
/// <remarks>
/// <para><b>Die Regel, deren Bruch am teuersten ist, steht hier:</b> Vor jedem Anlegen wird
/// geprüft, ob in diesem Zeitfenster schon eine Leistung steht. TANSS dedupliziert nicht — ein
/// zweiter Versuch ergibt einen zweiten Posten auf der Rechnung des Kunden, und eine Leistung
/// lässt sich von hier aus nicht zurücknehmen.</para>
///
/// <para><b>Und der Umkehrschluss:</b> Scheitert die Prüfung selbst, heisst das „unbekannt“ und
/// nicht „nicht vorhanden“. Dann wird <b>nicht</b> gesendet. Wer diese Stelle später
/// „vereinfacht“ und im Zweifel sendet, erzeugt doppelt berechnete Arbeitszeit in der
/// Produktivinstanz eines Kunden.</para>
///
/// <para><b>Vorbelegen und dann erst anlegen.</b> TANSS setzt beim Vorbereiten über achtzig
/// Felder — Stundensatz, Abrechnungsart, Vertrag, Kostenstelle. Eine von Hand zusammengesetzte
/// Leistung stünde mit Stundensatz 0 in TANSS und fiele erst in der Rechnungsstellung auf.
/// Deshalb gibt es hier keinen Weg, der die Vorbereitung überspringt.</para>
/// </remarks>
public sealed class GapBooking
{
    private readonly ISupportRepository _supports;
    private readonly int _employeeId;

    /// <summary>Baut den Dienst.</summary>
    /// <param name="supports">Die Leistungen.</param>
    /// <param name="employeeId">Der Mitarbeiter, auf den gebucht wird.</param>
    public GapBooking(ISupportRepository supports, int employeeId)
    {
        ArgumentNullException.ThrowIfNull(supports);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(employeeId);

        _supports = supports;
        _employeeId = employeeId;
    }

    /// <summary>
    /// Trägt eine Leistung nach.
    /// </summary>
    /// <param name="begin">Beginn der Leistung.</param>
    /// <param name="duration">Deren Dauer.</param>
    /// <param name="text">Was getan wurde — das, was auf der Rechnung steht.</param>
    /// <param name="target">Worauf gebucht wird: Ticket, Firma, Gerät, intern.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Das Ergebnis; niemals <see langword="null"/>.</returns>
    public async Task<BookingResult> BookAsync(DateTimeOffset begin, TimeSpan duration,
                                               string text, BookingTarget target,
                                               CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new BookingResult(BookingOutcome.Rejected, 0,
                "Ohne Text keine Leistung. Was dort steht, liest der Kunde auf seiner Rechnung — "
                + "eine Leistung ohne Beschreibung ist für ihn nicht nachvollziehbar und wird "
                + "erfahrungsgemäß beanstandet.");
        }

        if (duration < TimeSpan.FromMinutes(1))
        {
            return new BookingResult(BookingOutcome.Rejected, 0,
                "Die Dauer muss mindestens eine Minute betragen.");
        }

        if (!target.HasBillingAnchor)
        {
            return new BookingResult(BookingOutcome.Rejected, 0,
                "Es fehlt das Ticket. Ohne Ticket oder wenigstens eine Firma weiß TANSS nicht, "
                + "wem die Zeit zu berechnen ist — und kann auch Stundensatz und Abrechnungsart "
                + "nicht vorbelegen. Die Leistung stünde dann ohne Wert in der Auswertung.");
        }

        // --- Der Riegel gegen Dubletten. Er steht VOR allem anderen. -----------------------
        try
        {
            if (await _supports.ExistsAsync(_employeeId, begin, duration, ct).ConfigureAwait(false))
            {
                return new BookingResult(BookingOutcome.AlreadyExists, 0,
                    string.Create(CultureInfo.CurrentCulture,
                        $"Für {begin.ToLocalTime():t} steht in TANSS bereits eine Leistung.")
                    + " Es wurde nichts gesendet. Die Übersicht ist neu zu laden — vermutlich "
                    + "wurde die Lücke inzwischen anderswo geschlossen.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException ex)
        {
            // "Unbekannt" heisst NICHT "nicht vorhanden". Hier wird nicht gesendet.
            return new BookingResult(BookingOutcome.CheckFailed, 0,
                "Ob für dieses Zeitfenster schon eine Leistung besteht, liess sich nicht "
                + "feststellen — deshalb wurde nichts gesendet. TANSS legt Leistungen doppelt "
                + "an, wenn man es zweimal bittet, und eine gebuchte Leistung lässt sich von "
                + "hier aus nicht zurücknehmen. Der Versuch ist gefahrlos zu wiederholen. "
                + Redaction.Scrub(ex.Message));
        }

        // --- Vorbelegen lassen ------------------------------------------------------------
        // Vorbelegt wird bevorzugt aus dem Ticket: Ueber das Ticket findet TANSS Vertrag,
        // Stundensatz und Abrechnungsart des Kunden. Die Firma allein fuehrt zwar auch zu
        // einem Stundensatz, aber nicht zum Vertrag - und der entscheidet ueber den Preis.
        SupportInitializer source = target.TicketId > 0
            ? SupportInitializer.Ticket(target.TicketId)
            : SupportInitializer.Company(target.CompanyId);

        SupportDraft draft;
        try
        {
            draft = await _supports.PrepareAsync(source, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException ex)
        {
            // Hier ist noch nichts angelegt: PrepareAsync legt nichts an. Gefahrlos.
            return new BookingResult(BookingOutcome.CheckFailed, 0,
                "TANSS konnte die Leistung nicht vorbereiten; es wurde nichts angelegt. Ohne "
                + "Vorbereitung fehlten Stundensatz und Abrechnungsart, und die Leistung wäre "
                + "auf der Rechnung nichts wert. " + Redaction.Scrub(ex.Message));
        }

        draft.Text = text.Trim();
        draft.Begin = begin;
        draft.Duration = duration;
        draft.EmployeeId = _employeeId;
        draft.Internal = target.Internal;

        if (target.TicketId > 0)
        {
            draft.TicketId = target.TicketId;
        }

        if (target.CompanyId > 0 && draft.CompanyId <= 0)
        {
            // Nur setzen, wenn TANSS selbst keine gefunden hat: Die aus dem Ticket abgeleitete
            // Firma ist die richtige, unsere geratene hoechstens die wahrscheinliche.
            draft.CompanyId = target.CompanyId;
        }

        if (target.HasAssignment)
        {
            // Ein gewaehltes Geraet ist die genaueste Zuordnung: Nur mit ihr taucht die
            // Leistung in der Geraetehistorie auf.
            draft.AssignTo(target.LinkTypeId, target.LinkId);
        }
        else if (draft.LinkTypeId <= 0 || draft.LinkId <= 0)
        {
            // --- Ohne Zuordnung nimmt TANSS die Leistung nicht an. ------------------------
            // Nachgemessen am 14.09.2026: POST /api/v1/supports/properties bereitet sowohl aus
            // einem Ticket als auch aus einer Firma mit linkTypeId = 0 und linkId = 0 vor. Wer
            // diesen Entwurf unveraendert anlegt, bekommt HTTP 404 mit
            // SupportMissingAssignmentException und dem Satz "Sie muessen eine gueltige
            // Zuweisung auswaehlen!" -- eine Pruefung, die TANSS hinter einem 404 versteckt.
            //
            // Die Firma ist die Zuordnung, die immer trägt: Sie steht im vorbereiteten
            // Entwurf, und ohne sie gaebe es ohnehin keinen Stundensatz. Ein Geraet waere
            // genauer, aber keines ist gewaehlt -- und raten waere hier die Historie eines
            // fremden Geraets.
            int companyId = draft.CompanyId > 0 ? draft.CompanyId : target.CompanyId;

            if (companyId > 0)
            {
                draft.AssignTo(LinkType.Company, companyId);
            }
        }

        // --- Anlegen. Genau ein Versuch. --------------------------------------------------
        try
        {
            int id = await _supports.CreateAsync(draft, ct).ConfigureAwait(false);

            return new BookingResult(BookingOutcome.Created, id,
                id > 0
                    ? string.Create(CultureInfo.CurrentCulture,
                        $"Leistung {id} angelegt: {begin.ToLocalTime():t}, {duration.TotalMinutes:0} Minuten.")
                    : string.Create(CultureInfo.CurrentCulture,
                        $"Leistung angelegt: {begin.ToLocalTime():t}, {duration.TotalMinutes:0} Minuten.")
                      + " TANSS hat keine Kennung genannt — angelegt ist sie trotzdem.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException ex)
        {
            // Der einzige Fall mit offenem Ausgang: Die Anfrage kann angekommen und nur die
            // Antwort verloren gegangen sein. Deshalb NICHT von selbst wiederholen.
            return new BookingResult(BookingOutcome.Unknown, 0,
                "Das Anlegen ist fehlgeschlagen. Ob die Leistung bei TANSS angekommen ist, lässt "
                + "sich von hier aus nicht sagen — deshalb wird nicht von selbst wiederholt. Ein "
                + "erneuter Versuch prüft zuerst, ob sie schon dasteht. "
                + Redaction.Scrub(ex.Message));
        }
    }
}
