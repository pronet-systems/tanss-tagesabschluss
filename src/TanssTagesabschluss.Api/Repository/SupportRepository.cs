using System.Globalization;
using System.Text.Json.Nodes;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.Api.Repository;

/// <summary>
/// Leistungen lesen und anlegen.
/// </summary>
/// <remarks>
/// <para><b>Die Regel, deren Bruch am teuersten ist:</b> TANSS dedupliziert Leistungen nicht.
/// Zwei gleichlautende Aufrufe ergeben zwei Datensätze und damit zwei Posten auf der Rechnung
/// des Kunden. Eine Leistung, deren Ausgang unbekannt blieb — Zeitüberschreitung, abgerissene
/// Verbindung —, wird deshalb <b>niemals</b> blind wiederholt, sondern erst über
/// <see cref="ExistsAsync"/> gesucht.</para>
///
/// <para><b>Und der Umkehrschluss, den man leicht falsch macht:</b> Scheitert die
/// Existenzprüfung selbst, heißt das „unbekannt“ und nicht „nicht vorhanden“. Sie wirft dann,
/// statt <see langword="false"/> zu melden. Wer diese Fehlerbehandlung später „vereinfacht“ und
/// im Zweifel sendet, erzeugt doppelt berechnete Arbeitszeit in der Produktivinstanz eines
/// Kunden.</para>
///
/// <para><b>Angelegt wird in zwei Schritten.</b> Erst lässt <see cref="PrepareAsync"/> TANSS die
/// Leistung vorbelegen — Stundensatz, Abrechnungsart, Vertrag, Kostenstelle —, dann geht sie
/// über <see cref="CreateAsync"/> hinaus. Wer den zweiten Schritt ohne den ersten ginge, buchte
/// eine Leistung mit Stundensatz 0, und das fiele erst in der Rechnungsstellung auf.</para>
/// </remarks>
public sealed class SupportRepository : ISupportRepository
{
    private readonly ITanssClient _client;

    /// <summary>Baut das Repository.</summary>
    /// <param name="client">Der HTTP-Zugang.</param>
    public SupportRepository(ITanssClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupportEntry>> ListAsync(int employeeId, Timeframe timeframe,
                                                             CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(employeeId);
        ArgumentNullException.ThrowIfNull(timeframe);

        SupportListQuery query = new()
        {
            Timeframe = timeframe,
            Employees = [employeeId],
        };

        List<SupportEntry>? entries = await _client
            .PutAsync<List<SupportEntry>>(TanssRoutes.SupportList, query, ct: ct)
            .ConfigureAwait(false);

        // Nach Beginn geordnet: Die Fachschicht legt die Leistungen anschliessend ueber die
        // Anwesenheit, und das ist mit einer geordneten Liste ein Durchlauf statt einer Suche.
        return entries is null ? [] : [.. entries.OrderBy(entry => entry.Date)];
    }

    /// <inheritdoc />
    public async Task<SupportDraft> PrepareAsync(SupportInitializer source,
                                                 CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        SupportPropertiesRequest request = new() { Initializers = [source] };

        JsonNode? content = await _client
            .PostAsync<JsonNode>(TanssRoutes.SupportProperties, request, ct: ct)
            .ConfigureAwait(false);

        if (content is null)
        {
            throw new TanssException(
                string.Create(CultureInfo.CurrentCulture,
                    $"TANSS hat zu {source.Type} {source.Id} keine Leistung vorbereitet.")
                + " Üblichste Ursache: Es gibt die Quelle nicht mehr, oder sie gehört einem "
                + "anderen Techniker. Ohne Vorbereitung wird nichts angelegt — eine von Hand "
                + "zusammengesetzte Leistung stünde ohne Stundensatz in TANSS.");
        }

        return SupportDraft.From(content);
    }

    /// <inheritdoc />
    public async Task<int> CreateAsync(SupportDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        JsonNode? created = await _client
            .PostAsync<JsonNode>(TanssRoutes.Supports, draft.Payload, ct: ct)
            .ConfigureAwait(false);

        // Die Kennung ist Beiwerk: Angelegt ist angelegt, auch wenn die Antwort sie nicht
        // nennt. Eine Ausnahme an dieser Stelle liesse den Techniker glauben, es sei nichts
        // geschehen - und er traege ein zweites Mal nach.
        return created is JsonObject root && root["id"] is { } id
               && int.TryParse(id.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                               out int value)
            ? value
            : 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Gefragt wird nach dem ganzen Tag, verglichen wird auf Überschneidung.</b> Ein
    /// Fenster von genau der Länge der nachzutragenden Leistung würde eine bestehende Leistung
    /// übersehen, die eine Minute früher beginnt — und genau die ist der wahrscheinlichste
    /// Doppeleintrag, wenn jemand denselben Nachtrag zweimal abschickt.</para>
    /// <para><b>Termine und Abwesenheiten zählen nicht.</b> Ein fester Termin im selben
    /// Zeitraum ist keine erfasste Leistung; ihn als solche zu werten hiesse, das Nachtragen
    /// stillschweigend zu verweigern.</para>
    /// </remarks>
    public async Task<bool> ExistsAsync(int employeeId, DateTimeOffset begin, TimeSpan duration,
                                        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(employeeId);

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Eine Leistung ohne Dauer gibt es nicht, und für einen Zeitpunkt ohne Länge "
                + "lässt sich keine Überschneidung prüfen.");
        }

        DateTimeOffset end = begin + duration;

        // Der ganze Kalendertag des Beginns, grosszuegig bis zum Ende der Leistung erweitert:
        // Eine Leistung, die ueber Mitternacht laeuft, soll auch dann gefunden werden.
        DateOnly day = DateOnly.FromDateTime(begin.LocalDateTime);
        Timeframe window = Timeframe.Day(day);
        long endSeconds = TanssTime.ToUnixSeconds(end);
        if (endSeconds > window.To)
        {
            window = new Timeframe { From = window.From, To = endSeconds };
        }

        // KEIN Fangen von TanssException. Scheitert diese Pruefung, heisst das "unbekannt" -
        // und der Aufrufer darf dann gerade NICHT senden. Siehe Klassenkommentar.
        IReadOnlyList<SupportEntry> existing =
            await ListAsync(employeeId, window, ct).ConfigureAwait(false);

        return existing.Any(entry => entry.IsSupport && Overlaps(entry, begin, end));
    }

    /// <summary>Überschneidet sich eine bestehende Leistung mit dem gesuchten Zeitraum?</summary>
    /// <remarks>
    /// Halboffen verglichen: Eine Leistung, die genau dann endet, wenn die neue beginnt,
    /// überschneidet sich <b>nicht</b>. Anders wäre jeder lückenlos anschliessende Nachtrag
    /// blockiert — und lückenlos anschliessend ist gerade der Normalfall.
    /// </remarks>
    private static bool Overlaps(SupportEntry entry, DateTimeOffset begin, DateTimeOffset end) =>
        entry.Begin is { } entryBegin && entry.End is { } entryEnd
        && entryBegin < end && begin < entryEnd;
}
