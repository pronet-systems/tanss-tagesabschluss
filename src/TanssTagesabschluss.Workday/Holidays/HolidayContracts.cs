namespace TanssTagesabschluss.Workday.Holidays;

/// <summary>
/// Ein Feiertag — und die Frage, ob er im ganzen Bundesland gilt.
/// </summary>
/// <remarks>
/// <para><b>Die Einschränkung ist kein Sonderfall, sondern Alltag.</b> Der Feiertagsdienst
/// liefert zu einem Land auch Tage, die dort nur in bestimmten Gemeinden gesetzlicher Feiertag
/// sind, und vermerkt das im Feld <c>hinweis</c>. Gemessen am 14.09.2026 betrifft das unter
/// anderem Fronleichnam in Sachsen und Thüringen (nur im sorbischen Siedlungsgebiet
/// beziehungsweise im Eichsfeld), Mariä Himmelfahrt in Bayern (nur in überwiegend katholischen
/// Gemeinden) und das Augsburger Friedensfest (nur im Stadtgebiet Augsburg).</para>
/// <para><b>Wer den Hinweis übergeht, bekommt beides falsch:</b> Entweder gilt in ganz Bayern
/// Mariä Himmelfahrt als frei — dann verschwindet in zwei Dritteln des Landes ein ganzer
/// Arbeitstag aus der Prüfung — oder er gilt nirgends, und die Betroffenen werden an ihrem
/// Feiertag gemahnt. Deshalb steht hier ein <see cref="IsConditional"/> und kein blosser Name.</para>
/// </remarks>
/// <param name="Date">Der Tag.</param>
/// <param name="Name">Der Name, etwa <c>Fronleichnam</c>.</param>
/// <param name="IsConditional">
/// Gilt der Tag nur in einem Teil des Landes? Dann ist er hier zwar geführt, aber mit Vorbehalt.
/// </param>
/// <param name="Note">Der Wortlaut der Einschränkung; <see langword="null"/>, wenn es keine gibt.</param>
public sealed record Holiday(DateOnly Date, string Name, bool IsConditional, string? Note);

/// <summary>
/// Die Feiertage eines Jahres in einem Bundesland.
/// </summary>
/// <remarks>
/// <para>Eine Schnittstelle, weil dahinter ein <b>fremder Dienst</b> steht und weil die
/// Lückenrechnung sich gegen eine Attrappe prüfen lassen muss, ohne ins Netz zu gehen.</para>
/// <para><b>Wirft nicht.</b> Ein Feiertagskalender, der nicht erreichbar ist, darf den
/// Tagesabschluss nicht kosten: Ohne ihn wird ein Feiertag wie ein gewöhnlicher Arbeitstag
/// behandelt, und das Werkzeug sagt in seinen Hinweisen, dass es ihn nicht prüfen konnte.
/// Statt einer Ausnahme kommt deshalb ein <see cref="HolidaySet"/>, das über
/// <see cref="HolidaySet.IsKnown"/> Auskunft über sich selbst gibt.</para>
/// </remarks>
public interface IHolidayProvider
{
    /// <summary>Holt die Feiertage eines Jahres.</summary>
    /// <param name="year">Das Jahr.</param>
    /// <param name="state">Das Bundesland.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Feiertage; niemals <see langword="null"/>.</returns>
    Task<HolidaySet> GetAsync(int year, FederalState state, CancellationToken ct = default);
}

/// <summary>
/// Die Feiertage eines Jahres — samt der Aussage, ob sie überhaupt ermittelt werden konnten.
/// </summary>
/// <remarks>
/// <b>Die Unterscheidung ist der Zweck dieses Typs.</b> Eine leere Abbildung bedeutet sonst
/// zweierlei: „dieses Jahr hat keine Feiertage“ (gibt es nicht) und „wir konnten nicht
/// nachsehen“. Ohne <see cref="IsKnown"/> würde die Anzeige den zweiten Fall als ersten
/// darstellen — und an Weihnachten einen fehlenden Arbeitstag melden.
/// </remarks>
public sealed record HolidaySet
{
    private readonly IReadOnlyDictionary<DateOnly, Holiday> _byDate;

    private HolidaySet(IReadOnlyDictionary<DateOnly, Holiday> byDate, bool known, string? note)
    {
        _byDate = byDate;
        IsKnown = known;
        Note = note;
    }

    /// <summary>Konnten die Feiertage ermittelt werden?</summary>
    public bool IsKnown { get; }

    /// <summary>
    /// Warum sie nicht ermittelt werden konnten — ein Satz für die Anzeige; sonst
    /// <see langword="null"/>.
    /// </summary>
    public string? Note { get; }

    /// <summary>Die Feiertage nach Datum.</summary>
    public IReadOnlyDictionary<DateOnly, Holiday> ByDate => _byDate;

    /// <summary>Wie viele Feiertage ermittelt wurden.</summary>
    public int Count => _byDate.Count;

    /// <summary>Ein leerer, aber gültiger Satz — für Tests.</summary>
    public static HolidaySet Empty { get; } =
        new(new Dictionary<DateOnly, Holiday>(), known: true, note: null);

    /// <summary>Ein ermittelter Satz Feiertage.</summary>
    /// <param name="holidays">Die Feiertage. Mehrere am selben Tag: der erste gewinnt.</param>
    /// <returns>Der Satz.</returns>
    public static HolidaySet Known(IEnumerable<Holiday> holidays)
    {
        ArgumentNullException.ThrowIfNull(holidays);

        Dictionary<DateOnly, Holiday> byDate = [];
        foreach (Holiday holiday in holidays)
        {
            // Der erste gewinnt: Faellt ein unbedingter und ein eingeschraenkter Feiertag auf
            // denselben Tag, steht der unbedingte in der Liste zuerst - und er ist der
            // aussagekraeftigere.
            byDate.TryAdd(holiday.Date, holiday);
        }

        return new HolidaySet(byDate, known: true, note: null);
    }

    /// <summary>Ein Satz, der nicht ermittelt werden konnte.</summary>
    /// <param name="note">Warum — in einem Satz für die Anzeige.</param>
    /// <returns>Der Satz.</returns>
    public static HolidaySet Unknown(string note) =>
        new(new Dictionary<DateOnly, Holiday>(), known: false, note: note);

    /// <summary>Der Feiertag an diesem Tag; <see langword="null"/>, wenn keiner ist.</summary>
    /// <param name="day">Der Tag.</param>
    /// <returns>Der Feiertag, oder <see langword="null"/>.</returns>
    public Holiday? On(DateOnly day) =>
        _byDate.TryGetValue(day, out Holiday? holiday) ? holiday : null;
}

/// <summary>
/// Bestimmt das Bundesland zu einer Postleitzahl.
/// </summary>
/// <remarks>
/// <para><b>Warum eine Postleitzahl dafür nicht von allein genügt.</b> Postleitzahlenbereiche
/// folgen den Wegen der Post und nicht den Landesgrenzen; es gibt Postleitzahlen, unter denen
/// Orte zweier Bundesländer liegen. Eine feste Tabelle „erste Ziffer → Land“ wäre deshalb an
/// den Rändern falsch — und an den Rändern sitzen genau die Feiertage, die sich zwischen zwei
/// Ländern unterscheiden.</para>
/// <para>Gefragt wird deshalb ein Verzeichnis, das den Ort kennt. <b>Wirft nicht:</b> Ist es
/// nicht erreichbar, bleibt das Land unbekannt und wird in den Einstellungen von Hand gesetzt.</para>
/// </remarks>
public interface IFederalStateResolver
{
    /// <summary>Bestimmt das Bundesland zu einer deutschen Postleitzahl.</summary>
    /// <param name="postalCode">Die Postleitzahl, fünfstellig.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Das Ergebnis; niemals <see langword="null"/>.</returns>
    Task<FederalStateLookup> ResolveAsync(string postalCode, CancellationToken ct = default);
}

/// <summary>
/// Was bei der Suche nach dem Bundesland herauskam.
/// </summary>
/// <param name="State">
/// Das Land; <see langword="null"/>, wenn keines feststeht. Bei
/// <paramref name="Ambiguous"/> steht hier das erste gefundene — als Vorschlag, nicht als
/// Ergebnis.
/// </param>
/// <param name="Ambiguous">
/// Lagen unter dieser Postleitzahl Orte aus <b>mehreren</b> Bundesländern?
/// </param>
/// <param name="Message">Ein Satz, der sagt, was ist und was zu tun ist.</param>
public sealed record FederalStateLookup(FederalState? State, bool Ambiguous, string Message)
{
    /// <summary>Steht ein Land zweifelsfrei fest?</summary>
    public bool IsResolved => State is not null && !Ambiguous;
}
