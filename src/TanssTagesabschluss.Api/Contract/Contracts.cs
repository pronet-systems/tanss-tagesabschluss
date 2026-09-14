using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.Api.Contract;

/// <summary>
/// Woher das Arbeitstoken kommt und wohin das erneuerte geht.
/// </summary>
/// <remarks>
/// Bewusst eine Schnittstelle: die Netzschicht soll nichts über DPAPI, Dateipfade oder Windows
/// wissen. Die Umsetzung liegt in <c>TanssTagesabschluss.Storage</c>.
/// </remarks>
public interface ITokenStore
{
    /// <summary>Liefert das Token einschließlich des Präfixes <c>Bearer </c>.</summary>
    /// <returns>Das Token, oder eine leere Zeichenkette, wenn keines hinterlegt ist.</returns>
    string Read();

    /// <summary>
    /// Ersetzt das Token. Die Umsetzung legt vorher eine Sicherung des bisherigen an — ein
    /// misslungener Wechsel darf nicht bedeuten, dass gar kein Token mehr da ist.
    /// </summary>
    /// <param name="token">Das neue Token.</param>
    void Write(string token);
}

/// <summary>Reiner HTTP-Zugriff auf TANSS. Kennt keine Fachlogik.</summary>
/// <remarks>
/// <b>Zwei Präfixe, eine Pflicht.</b> Auf <c>/api/v1/**</c> ist <c>loggedInUserId</c> zwingend —
/// ohne ihn antwortet TANSS mit 403. Auf <c>/api/tanss.x/v1/**</c> wird er ignoriert. Die
/// Umsetzung setzt ihn selbsttätig; ein Aufrufer setzt ihn nie von Hand.
/// </remarks>
public interface ITanssClient : IDisposable
{
    /// <summary>
    /// Liest. Wird bei Netzfehlern und 5xx wiederholt — <b>außer</b> auf den Pfaden, die
    /// <see cref="TanssRoutes.HasSideEffectOnGet"/> als seiteneffektbehaftet kennt.
    /// </summary>
    /// <typeparam name="T">Das erwartete Modell des <c>content</c>.</typeparam>
    /// <param name="path">Der Pfad mit Präfix.</param>
    /// <param name="query">Abfrageparameter.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Der Inhalt, oder <see langword="null"/> bei leerer Antwort.</returns>
    Task<T?> GetAsync<T>(string path, IDictionary<string, string?>? query = null,
                         CancellationToken ct = default);

    /// <summary>Schickt ein PUT. Läuft mit genau einem Versuch.</summary>
    /// <typeparam name="T">Das erwartete Modell des <c>content</c>.</typeparam>
    /// <param name="path">Der Pfad mit Präfix.</param>
    /// <param name="body">Der Rumpf.</param>
    /// <param name="query">Abfrageparameter.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Der Inhalt, oder <see langword="null"/>.</returns>
    Task<T?> PutAsync<T>(string path, object? body = null,
                         IDictionary<string, string?>? query = null,
                         CancellationToken ct = default);

    /// <summary>Schickt ein POST. Läuft mit genau einem Versuch.</summary>
    /// <typeparam name="T">Das erwartete Modell des <c>content</c>.</typeparam>
    /// <param name="path">Der Pfad mit Präfix.</param>
    /// <param name="body">Der Rumpf.</param>
    /// <param name="query">Abfrageparameter.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Der Inhalt, oder <see langword="null"/>.</returns>
    Task<T?> PostAsync<T>(string path, object? body = null,
                          IDictionary<string, string?>? query = null,
                          CancellationToken ct = default);

    /// <summary>Schickt ein DELETE. Läuft mit genau einem Versuch.</summary>
    /// <param name="path">Der Pfad mit Präfix.</param>
    /// <param name="query">Abfrageparameter.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    Task DeleteAsync(string path, IDictionary<string, string?>? query = null,
                     CancellationToken ct = default);

    /// <summary>
    /// Wie <see cref="PostAsync{T}"/>, liefert zusätzlich den <c>meta</c>-Block.
    /// </summary>
    /// <typeparam name="T">Das erwartete Modell des <c>content</c>.</typeparam>
    /// <param name="path">Der Pfad mit Präfix.</param>
    /// <param name="body">Der Rumpf.</param>
    /// <param name="query">Abfrageparameter.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Inhalt und <c>meta</c>; der <c>meta</c>-Teil ist nie <see langword="null"/>.</returns>
    Task<(T? Content, IReadOnlyDictionary<string, object?> Meta)> PostWithMetaAsync<T>(
        string path, object? body = null, IDictionary<string, string?>? query = null,
        CancellationToken ct = default);
}

/// <summary>
/// Lesen <b>mit</b> dem <c>meta</c>-Block.
/// </summary>
/// <remarks>
/// <para><b>Für dieses Werkzeug ist der Umschlag keine Zugabe, sondern Pflicht.</b> Das
/// Arbeitszeitmodell eines Tages steht ausschliesslich dort: Der Inhalt der Zeitauswertung
/// nennt nur die <c>workingTimeModelId</c>, das Modell selbst liegt unter
/// <c>meta.listProperties.workingTimeModels</c>. Ohne diesen Weg wüsste das Werkzeug nicht,
/// ob ein leerer Tag ein Versäumnis oder ein freier Tag ist.</para>
/// <para><b>Warum eine eigene Schnittstelle und keine neuen Glieder auf
/// <see cref="ITanssClient"/>:</b> Der Grundvertrag bleibt schmal, damit ihn eine Attrappe in
/// wenigen Zeilen erfüllt. Wer den Umschlag braucht, prüft auf diese Schnittstelle.</para>
/// <para><b>Der <c>meta</c>-Block bleibt roh.</b> Seine Werte kommen als <c>JsonElement</c>
/// durch. Jede feste Modellierung wäre nach dem nächsten TANSS-Update falsch.</para>
/// </remarks>
public interface ITanssMetaRead
{
    /// <summary>Liest wie <see cref="ITanssClient.GetAsync{T}"/> und gibt zusätzlich <c>meta</c> heraus.</summary>
    /// <typeparam name="T">Das erwartete Modell des <c>content</c>.</typeparam>
    /// <param name="path">Der Pfad mit Präfix.</param>
    /// <param name="query">Abfrageparameter.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Inhalt und <c>meta</c>; der <c>meta</c>-Teil ist nie <see langword="null"/>.</returns>
    Task<(T? Content, IReadOnlyDictionary<string, object?> Meta)> GetWithMetaAsync<T>(
        string path, IDictionary<string, string?>? query = null, CancellationToken ct = default);

    /// <summary>
    /// Schickt wie <see cref="ITanssClient.PutAsync{T}"/> und gibt zusätzlich <c>meta</c> heraus.
    /// </summary>
    /// <remarks>
    /// Für die lesenden Abfragen, die TANSS als PUT verlangt — die Leistungsliste, die
    /// Firmensuche und die Ticketsuche. Läuft mit genau einem Versuch.
    /// </remarks>
    /// <typeparam name="T">Das erwartete Modell des <c>content</c>.</typeparam>
    /// <param name="path">Der Pfad mit Präfix.</param>
    /// <param name="body">Der Rumpf.</param>
    /// <param name="query">Abfrageparameter.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Inhalt und <c>meta</c>; der <c>meta</c>-Teil ist nie <see langword="null"/>.</returns>
    Task<(T? Content, IReadOnlyDictionary<string, object?> Meta)> PutWithMetaAsync<T>(
        string path, object? body = null, IDictionary<string, string?>? query = null,
        CancellationToken ct = default);
}

/// <summary>
/// Die Zeiterfassung eines Mitarbeiters: Stempel, Abschnitte, Arbeitszeitmodell.
/// </summary>
/// <remarks>
/// <b>Ausschliesslich lesend.</b> Diese Schnittstelle kennt keinen Weg, einen Zeitstempel zu
/// setzen oder einen Tag abzuschliessen — siehe <c>TanssRoutes.Timestamps</c> und
/// <c>TanssRoutes.DayClosing</c>. Die Arbeitszeit eines Menschen ändert dieses Werkzeug nicht.
/// </remarks>
public interface ITimestampRepository
{
    /// <summary>
    /// Liest die Tage eines Zeitraums samt Auswertung und Arbeitszeitmodellen.
    /// </summary>
    /// <param name="employeeId">Der Mitarbeiter.</param>
    /// <param name="from">Erster Tag, einschließlich.</param>
    /// <param name="until">Letzter Tag, <b>einschließlich</b>. Heißt bei TANSS <c>till</c>.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Tage und die dazugehörigen Modelle.</returns>
    Task<TimeRecording> ReadAsync(int employeeId, DateOnly from, DateOnly until,
                                  CancellationToken ct = default);

    /// <summary>Die Pausenregeln der Instanz.</summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Die Regeln sind eine Verfeinerung: Ohne sie wird eine ungestempelte
    /// Mittagslücke als Lücke ausgewiesen, und das ist die vorsichtigere Richtung. Ein
    /// Fehlschlag ergibt eine leere Liste.
    /// </remarks>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Regeln; leer, wenn keine vorliegen oder das Lesen misslang.</returns>
    Task<IReadOnlyList<PauseConfig>> ListPauseConfigsAsync(CancellationToken ct = default);
}

/// <summary>
/// Was bei einer Abfrage der Zeiterfassung herauskommt: die Tage und ihre Modelle.
/// </summary>
/// <remarks>
/// Beides gehört zusammen, weil es aus <b>einer</b> Antwort stammt — die Tage aus dem Inhalt,
/// die Modelle aus dem Umschlag. Sie getrennt herauszugeben hiesse, den Aufrufer zu zwingen,
/// sie wieder zusammenzusuchen, und liesse ihn dabei einen Tag ohne Modell übersehen.
/// </remarks>
/// <param name="Days">Die Tage, aufsteigend nach Datum.</param>
/// <param name="Models">Die Arbeitszeitmodelle, geschlüsselt nach ihrer Kennung.</param>
public sealed record TimeRecording(
    IReadOnlyList<TimestampDay> Days,
    IReadOnlyDictionary<int, WorkingTimeModel> Models)
{
    /// <summary>Ein leeres Ergebnis.</summary>
    public static TimeRecording Empty { get; } = new([], new Dictionary<int, WorkingTimeModel>());

    /// <summary>Der Tag mit diesem Datum; <see langword="null"/>, wenn TANSS keinen liefert.</summary>
    /// <param name="day">Das gesuchte Datum.</param>
    /// <returns>Der Tag, oder <see langword="null"/>.</returns>
    public TimestampDay? DayOn(DateOnly day) => Days.FirstOrDefault(entry => entry.Date == day);

    /// <summary>Das Arbeitszeitmodell eines Tages; <see langword="null"/>, wenn keines vorliegt.</summary>
    /// <param name="day">Der Tag.</param>
    /// <returns>Das Modell, oder <see langword="null"/>.</returns>
    public WorkingTimeModel? ModelFor(TimestampDay day)
    {
        ArgumentNullException.ThrowIfNull(day);

        return Models.TryGetValue(day.WorkingTimeModelId, out WorkingTimeModel? model)
            ? model
            : null;
    }
}

/// <summary>Leistungen lesen und anlegen.</summary>
public interface ISupportRepository
{
    /// <summary>
    /// Liest die Leistungen eines Mitarbeiters in einem Zeitraum.
    /// </summary>
    /// <remarks>
    /// Liefert <b>alles</b>, was auf dieser Route kommt — auch Termine und Abwesenheiten. Das
    /// Trennen übernimmt <see cref="SupportEntry.IsSupport"/>; siehe
    /// <see cref="SupportListQuery"/>.
    /// </remarks>
    /// <param name="employeeId">Der Mitarbeiter.</param>
    /// <param name="timeframe">Der Zeitraum.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Einträge; leer, wenn es keine gibt.</returns>
    Task<IReadOnlyList<SupportEntry>> ListAsync(int employeeId, Timeframe timeframe,
                                                CancellationToken ct = default);

    /// <summary>
    /// Lässt TANSS eine Leistung vorbereiten. <b>Legt nichts an.</b>
    /// </summary>
    /// <remarks>
    /// Der erste von zwei Schritten, und das ist keine Umständlichkeit: TANSS belegt dabei über
    /// achtzig Felder vor — darunter Stundensatz und Abrechnungsart, die sich aus Kunde,
    /// Vertrag und Mitarbeiter ergeben. Wer den zweiten Schritt ohne den ersten ginge, buchte
    /// eine Leistung, die rechnerisch nichts wert ist.
    /// </remarks>
    /// <param name="source">Die Quelle, aus der vorbelegt wird.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die vorbelegte Leistung.</returns>
    Task<SupportDraft> PrepareAsync(SupportInitializer source, CancellationToken ct = default);

    /// <summary>
    /// Legt die Leistung an.
    /// </summary>
    /// <remarks>
    /// <b>Es gibt kein Zurück.</b> Eine gebuchte Leistung ist in TANSS zu korrigieren, nicht
    /// von hier aus. Und <b>TANSS dedupliziert nicht</b>: Vor jeder Wiederholung steht
    /// <see cref="ExistsAsync"/>.
    /// </remarks>
    /// <param name="draft">Die vorbereitete und ausgefüllte Leistung.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Kennung der angelegten Leistung; <c>0</c>, wenn TANSS keine genannt hat.</returns>
    Task<int> CreateAsync(SupportDraft draft, CancellationToken ct = default);

    /// <summary>
    /// Steht in diesem Zeitraum bereits eine Leistung dieses Mitarbeiters?
    /// </summary>
    /// <remarks>
    /// <para><b>Der Riegel gegen Dubletten.</b> Ein Anlegen, dessen Ausgang unbekannt blieb —
    /// Zeitüberschreitung, abgerissene Verbindung —, wird niemals blind wiederholt.</para>
    /// <para><b>Und der Umkehrschluss, den man leicht falsch macht:</b> Scheitert diese Prüfung
    /// selbst, heisst das „unbekannt“ und nicht „nicht vorhanden“. Die Umsetzung wirft dann,
    /// statt <see langword="false"/> zu melden — wer hier im Zweifel sendet, erzeugt eine
    /// doppelt berechnete Arbeitsstunde beim Kunden.</para>
    /// </remarks>
    /// <param name="employeeId">Der Mitarbeiter.</param>
    /// <param name="begin">Beginn der nachzutragenden Leistung.</param>
    /// <param name="duration">Deren Dauer.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns><see langword="true"/>, wenn der Zeitraum bereits belegt ist.</returns>
    Task<bool> ExistsAsync(int employeeId, DateTimeOffset begin, TimeSpan duration,
                           CancellationToken ct = default);
}

/// <summary>Urlaub, Krankheit und sonstige Abwesenheiten.</summary>
public interface IAbsenceRepository
{
    /// <summary>
    /// Liest die Abwesenheitsanträge eines Mitarbeiters für einen Monat.
    /// </summary>
    /// <remarks>
    /// TANSS filtert auf dieser Route nach Jahr und Monat, nicht nach einem freien Zeitraum.
    /// Wer einen Zeitraum über eine Monatsgrenze braucht, fragt beide Monate — siehe
    /// <see cref="ListRangeAsync"/>.
    /// </remarks>
    /// <param name="employeeId">Der Mitarbeiter.</param>
    /// <param name="year">Das Jahr.</param>
    /// <param name="month">Der Monat, 1 bis 12.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Anträge; leer, wenn es keine gibt.</returns>
    Task<IReadOnlyList<AbsenceRequest>> ListAsync(int employeeId, int year, int month,
                                                  CancellationToken ct = default);

    /// <summary>Liest die Anträge über einen Zeitraum hinweg, Monat für Monat.</summary>
    /// <param name="employeeId">Der Mitarbeiter.</param>
    /// <param name="from">Erster Tag.</param>
    /// <param name="until">Letzter Tag, einschließlich.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Anträge aller berührten Monate, ohne Dubletten.</returns>
    Task<IReadOnlyList<AbsenceRequest>> ListRangeAsync(int employeeId, DateOnly from,
                                                       DateOnly until,
                                                       CancellationToken ct = default);

    /// <summary>
    /// Die Zusatzarten zu einer Abwesenheit.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Ohne sie weiß das Werkzeug bei einer Abwesenheit der Art
    /// <c>ABSENCE</c> nicht, ob sie berechnet wird (Home-Office) oder nicht (Sonderurlaub) —
    /// und behandelt sie dann als nicht berechnet, also als Erklärung für die Lücke. Das ist
    /// die vorsichtigere Richtung: Sie erzeugt keine falsche Mahnung.
    /// </remarks>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Arten; leer, wenn keine vorliegen oder das Lesen misslang.</returns>
    Task<IReadOnlyList<PlanningAdditionalType>> ListAdditionalTypesAsync(
        CancellationToken ct = default);
}

/// <summary>Tickets des angemeldeten Technikers.</summary>
public interface ITicketRepository
{
    Task<IReadOnlyList<Ticket>> ListOwnAsync(CancellationToken ct = default);

    /// <summary>
    /// Sucht die <b>offenen</b> Tickets eines Mitarbeiters.
    /// </summary>
    /// <remarks>
    /// <para>Der Weg zu einer Auswahl statt eines Zahlenfelds. Ohne sie müsste der Techniker die
    /// Ticketnummer auswendig wissen, und eingetippt würde jede Zahl angenommen — auch eine, zu
    /// der es kein Ticket gibt. Eine Leistung auf eine erfundene Nummer zu buchen ist
    /// schlimmer als gar kein Ticket: Sie taucht in keiner Auswertung auf und fällt niemandem
    /// auf.</para>
    /// <para>Erledigte Tickets bleiben aussen vor. Nachgemessen ergab das 20 statt 572
    /// Einträge — auf ein abgeschlossenes Ticket zu buchen ist fast immer ein Versehen.</para>
    /// </remarks>
    /// <param name="employeeId">Der Mitarbeiter, dessen Tickets gesucht werden.</param>
    /// <param name="ct">Abbruchmarke.</param>
    Task<IReadOnlyList<Ticket>> SearchOpenAsync(int employeeId, CancellationToken ct = default);

    /// <summary>
    /// Liest ein einzelnes Ticket — der Weg, eine eingetippte Nummer zu prüfen.
    /// </summary>
    /// <remarks>
    /// Gibt <see langword="null"/> zurück, wenn es die Kennung nicht gibt; TANSS antwortet dann
    /// mit 404. Das ist kein Fehler, sondern die Antwort auf die Frage.
    /// </remarks>
    /// <param name="ticketId">Die zu prüfende Kennung.</param>
    /// <param name="ct">Abbruchmarke.</param>
    Task<Ticket?> FindAsync(int ticketId, CancellationToken ct = default);

    /// <summary>
    /// Sucht die <b>offenen</b> Tickets einer Firma — und nennt dabei deren Namen.
    /// </summary>
    /// <remarks>
    /// <para>Der Weg, der beim Nachtragen einer Lücke aus einer erkannten Firma eine Ticketauswahl macht.
    /// Gefiltert wird über <c>companies</c> und ausdrücklich <b>nicht</b> zusätzlich über
    /// <c>staff</c>: Wer für einen Kollegen einspringt, bucht auf dessen Ticket,
    /// und mit beiden Filtern zugleich stünde genau dieses nicht in der Liste.</para>
    /// <para><b>Wirft nicht.</b> Die Ticketauswahl ist eine Bequemlichkeit, die Buchung ist
    /// Arbeitszeit; ein Aussetzer der Leitung darf den Dialog nicht kosten. Ein
    /// Fehlschlag kommt als <see cref="CompanyTicketOutcome.Undetermined"/> samt fertigem Satz
    /// zurück — <see cref="SearchOpenAsync"/> wirft dagegen weiterhin, denn dort hängt kein
    /// Fenster daran.</para>
    /// </remarks>
    /// <param name="companyId">Die Firma. Muss grösser als 0 sein.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Tickets, Firmenname und die Aussage, wie eine leere Liste zu lesen ist.</returns>
    Task<CompanyTickets> ListForCompanyAsync(int companyId, CancellationToken ct = default);
}

/// <summary>
/// Die Geräte einer Firma — für die Zuordnung einer nachgetragenen Leistung.
/// </summary>
/// <remarks>
/// <b>Warum eine Leistung überhaupt ein Gerät tragen sollte.</b> Ein Ticket sagt, <i>worum</i>
/// es ging; das Gerät sagt, <i>woran</i> gearbeitet wurde. In der Gerätehistorie des Kunden
/// steht später genau das — und eine Leistung ohne Zuordnung taucht dort nicht auf. Für die
/// Abrechnung ist sie belanglos, für die Nachvollziehbarkeit nicht.
/// </remarks>
public interface IDeviceRepository
{
    /// <summary>
    /// Alle Geräte einer Firma: PCs und Server, Peripherie, Komponenten.
    /// </summary>
    /// <remarks>
    /// Geordnet nach Art und Name. <b>Wirft nicht wegen einer einzelnen Geräteart</b> — siehe
    /// <c>DeviceRepository</c>: Eine Instanz, die keine Peripherie pflegt, soll trotzdem ihre
    /// PCs zeigen.
    /// </remarks>
    /// <param name="companyId">Die Firma.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Geräte; leer, wenn es keine gibt.</returns>
    Task<IReadOnlyList<Device>> ListAsync(int companyId, CancellationToken ct = default);
}

/// <summary>Techniker nachschlagen — für die Einrichtung und die Eigenidentifikation.</summary>
public interface ITechnicianRepository
{
    /// <summary>Alle Techniker der Instanz.</summary>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Techniker; leer, wenn keine kommen.</returns>
    Task<IReadOnlyList<Technician>> ListAsync(CancellationToken ct = default);
}
