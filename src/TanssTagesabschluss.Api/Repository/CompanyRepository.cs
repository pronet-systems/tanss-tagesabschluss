using System.Globalization;
using System.Text.Json;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Diagnostics;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.Api.Repository;

/// <summary>
/// Die Firmensuche über <c>PUT /api/v1/search</c>.
/// </summary>
/// <remarks>
/// <para><b>Das ganze Verhalten dieser Klasse hängt an einer gemessenen Eigenheit:</b>
/// <c>configs.company.maxResults</c> ist eine Schwelle und keine Begrenzung. Übersteigt die
/// Trefferzahl den Wert, liefert TANSS eine <b>leere</b> Liste statt einer gekürzten
/// (nachgemessen: <c>Gmb</c> hat 540 Treffer, mit 539 kommen null, mit 540 kommen 540). Eine
/// leere Antwort heißt deshalb entweder „es gibt nichts“ oder „es sind zu viele“, und diese
/// beiden Fälle auseinanderzuhalten ist die einzige Aufgabe, die hier wirklich zählt.</para>
///
/// <para><b>Der Weg dorthin, in drei Stufen und mit Begründung.</b></para>
/// <para>1. Gefragt wird mit einer <b>hohen</b> Schwelle
/// (<see cref="CompanyRepositoryDefaults.Threshold"/>). Das kostet nichts: Die Schwelle ist keine
/// Seitengröße, TANSS überträgt so oder so genau die Treffer, die es gibt. Kommt etwas zurück,
/// ist die Antwort damit sogar <b>vollzählig</b> — eine nichtleere Antwort kann per Definition
/// die Schwelle nicht überschritten haben. Gekürzt wird erst hier, für die Anzeige, und die
/// Gesamtzahl bleibt in <see cref="CompanySearchResult.TotalFound"/> erhalten.</para>
///
/// <para>2. Bleibt die Liste leer, folgt die <b>Gegenprobe mit einer Obermenge</b>: dieselbe
/// Suche mit den ersten <see cref="CompanyRepositoryDefaults.MinimumQueryLength"/> Zeichen des
/// Suchbegriffs. Weil TANSS eine Teilzeichenkettensuche macht (gemessen: <c>roN</c> trifft
/// <c>ProNet</c>), gilt: jede Firma, die den vollen Begriff enthält, enthält auch dessen Anfang.
/// Die Treffermenge des kürzeren Begriffs ist also eine <b>Obermenge</b>. Kommt sie nichtleer
/// mit <c>n</c> Einträgen zurück, dann ist <c>n</c> ihre wahre Größe (sonst wäre auch sie leer),
/// und die gesuchte Menge hat höchstens <c>n</c> Einträge — also weniger als die Schwelle. Damit
/// ist bewiesen, dass die leere Antwort aus Stufe 1 <b>nicht</b> an der Schwelle lag: es gibt
/// wirklich keine Firma. Das ist <see cref="CompanySearchOutcome.NoMatch"/>, hergeleitet und
/// nicht geraten.</para>
///
/// <para>3. Bleibt auch die Gegenprobe leer — oder ist der Suchbegriff genau drei Zeichen lang,
/// sodass es keine erlaubte Obermenge gibt —, wird der ursprüngliche Begriff ein zweites Mal mit
/// einer <b>vervielfachten</b> Schwelle gefragt. Kommt jetzt etwas, war die erste Schwelle zu
/// niedrig. Kommt wieder nichts, ist die Lage <b>nicht ermittelt</b>
/// (<see cref="CompanySearchOutcome.Undetermined"/>) — und genau das wird gemeldet. Hausregel 2:
/// lieber „nicht ermittelt“ als geraten.</para>
///
/// <para><b>Warum nicht mit einer kürzeren Obermenge als drei Zeichen?</b> Wie TANSS auf ein oder
/// zwei Zeichen antwortet, ist <b>nicht gemessen</b>. Träte dort ein anderes Verhalten zutage —
/// etwa ein stilles Kürzen statt der Schwelle —, wäre der Beweis aus Stufe 2 wertlos, ohne dass
/// es jemandem auffiele. Ein Beweis, der auf einer Vermutung steht, ist keiner.</para>
///
/// <para><b>Hausregel 5.</b> Nur der erste Aufruf darf den Vorgang kosten. Scheitert eine der
/// Gegenproben an TANSS, wird das Ergebnis „nicht ermittelt“ — die Suche selbst stirbt daran
/// nicht.</para>
/// </remarks>
public sealed class CompanyRepository : ICompanyRepository
{
    /// <summary>
    /// Um so viel wird die Schwelle in Stufe 3 erhöht.
    /// </summary>
    /// <remarks>
    /// Kein weiterer Anlauf danach: Wer die Schwelle in Schritten hochtreibt, verwandelt eine
    /// Suche in eine Serie von Anfragen und bekommt am Ende doch keine Gewissheit. Zwei Anläufe
    /// klären den Fall oder er heißt „nicht ermittelt“.
    /// </remarks>
    private const int EscalationFactor = 20;

    /// <summary>Kurzform von <see cref="CompanyRepositoryDefaults.MinimumQueryLength"/>.</summary>
    private const int MinimumLength = CompanyRepositoryDefaults.MinimumQueryLength;

    private readonly ITanssClient _client;
    private readonly int _threshold;
    private readonly int _displayLimit;

    /// <summary>Baut das Repository mit den Vorgabewerten.</summary>
    /// <param name="client">Der HTTP-Zugang.</param>
    public CompanyRepository(ITanssClient client)
        : this(client, CompanyRepositoryDefaults.Threshold, CompanyRepositoryDefaults.DisplayLimit)
    {
    }

    /// <summary>
    /// Baut das Repository mit abweichender Schwelle und Anzeigegrenze.
    /// </summary>
    /// <remarks>
    /// Beides ist einstellbar, weil beides von der Instanz abhängt: Ob TANSS eine sehr hohe
    /// Schwelle annimmt, ist nicht gemessen, und wie viele Zeilen eine Auswahl verträgt,
    /// entscheidet die Oberfläche und nicht die Netzschicht.
    /// </remarks>
    /// <param name="client">Der HTTP-Zugang.</param>
    /// <param name="threshold">Die Schwelle für den ersten Aufruf. Muss größer als 0 sein.</param>
    /// <param name="displayLimit">Höchstzahl der zurückgegebenen Firmen. Muss größer als 0 sein.</param>
    public CompanyRepository(ITanssClient client, int threshold, int displayLimit)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threshold);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(displayLimit);

        _client = client;
        _threshold = threshold;
        _displayLimit = displayLimit;
    }

    /// <inheritdoc />
    public async Task<CompanySearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        string term = (query ?? string.Empty).Trim();
        if (term.Length < MinimumLength)
        {
            // Gar nicht erst fragen. Eine Teilzeichenkettensuche ueber zwei Zeichen traefe fast
            // jede Firma und liefe damit ohnehin in die Schwelle.
            return new CompanySearchResult
            {
                Outcome = CompanySearchOutcome.QueryTooShort,
                Threshold = _threshold,
                Explanation = $"Bitte mindestens {MinimumLength} Zeichen eingeben.",
            };
        }

        int requests = 0;

        // Stufe 1: hohe Schwelle. Kommt etwas zurueck, ist es vollzaehlig.
        IReadOnlyList<Company> hits = await FetchAsync(term, _threshold, ct).ConfigureAwait(false);
        requests++;

        if (hits.Count > 0)
        {
            return Present(term, hits, _threshold, requests);
        }

        // Stufe 2: Gegenprobe mit einer Obermenge - dem auf die Mindestlaenge gekuerzten Begriff.
        // Nur sinnvoll, wenn der Begriff laenger ist; sonst waere die "Obermenge" derselbe Begriff.
        if (term.Length > MinimumLength)
        {
            string shorter = term[..MinimumLength];
            (IReadOnlyList<Company> superset, bool answered) =
                await TryFetchAsync(shorter, _threshold, ct).ConfigureAwait(false);
            requests++;

            if (answered && superset.Count > 0)
            {
                // Die Obermenge passt unter die Schwelle, also passt die Teilmenge erst recht.
                // Die Leere aus Stufe 1 kann damit nicht von der Schwelle kommen.
                return new CompanySearchResult
                {
                    Outcome = CompanySearchOutcome.NoMatch,
                    Threshold = _threshold,
                    Requests = requests,
                    Explanation = $"Keine Firma enthält „{term}“. Gegenprobe: „{shorter}“ ergibt "
                        + $"{superset.Count} Firmen — die leere Antwort liegt also nicht an der "
                        + "Ergebnisgrenze.",
                };
            }
        }

        // Stufe 3: derselbe Begriff mit vervielfachter Schwelle.
        int raised = Raise(_threshold);
        (IReadOnlyList<Company> retry, bool reachable) =
            await TryFetchAsync(term, raised, ct).ConfigureAwait(false);
        requests++;

        if (reachable && retry.Count > 0)
        {
            return Present(term, retry, raised, requests);
        }

        return new CompanySearchResult
        {
            Outcome = CompanySearchOutcome.Undetermined,
            Threshold = raised,
            Requests = requests,
            Explanation = $"Zu „{term}“ hat TANSS nichts geliefert, auch nicht mit einer "
                + $"Ergebnisgrenze von {raised}. Ob es keine Firma gibt oder zu viele, ist damit "
                + "nicht ermittelt — bitte den Suchbegriff verlängern.",
        };
    }

    /// <summary>
    /// Baut das Ergebnis für einen Treffersatz: ordnen, Inaktive ausblenden, kürzen, beschreiben.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum das Ausblenden hier steht und nicht in der Oberfläche:</b> An dieser
    /// Stelle entstehen die Liste <b>und</b> der Satz darüber. Filterte die Oberfläche selbst,
    /// stünden sechs Zeilen unter einem Satz, der von elf redet — und die eine Stelle, an der
    /// „es gibt keine“ von „es sind zu viele“ unterschieden wird, hätte einen dritten Fall
    /// bekommen, von dem sie nichts weiß.</para>
    /// <para>Der Preis ist, dass dieses Lager eine Anzeigeentscheidung trifft. Es tut das
    /// ohnehin — es ordnet nach Passgenauigkeit, kürzt auf eine Anzeigegrenze und schreibt
    /// deutsche Sätze.</para>
    /// </remarks>
    private CompanySearchResult Present(string term, IReadOnlyList<Company> hits, int threshold,
                                        int requests)
    {
        List<Company> ordered = [.. Rank(term, hits)];

        // Inaktive fliegen raus, BEVOR gekuerzt wird. Andersherum verbrauchten aufgeloeste
        // Firmen Plaetze der Anzeigegrenze, und ein Suchbegriff mit vielen Altlasten haette die
        // gesuchte aktive Firma aus der Liste geschoben - unsichtbar und ohne Hinweis.
        List<Company> living = [.. ordered.Where(company => !company.Inactive)];
        int hidden = ordered.Count - living.Count;

        List<Company> shown = living.Count > _displayLimit
            ? living.GetRange(0, _displayLimit)
            : living;

        int locked = shown.Count(company => !company.Selectable);

        // Der Satz nennt die SICHTBARE Zahl. "11 Firmen gefunden" ueber einer Liste mit sechs
        // Zeilen liest sich wie ein Fehler des Werkzeugs; die uebrigen fuenf stehen im
        // Nachsatz, und dort stehen sie als das, was sie sind.
        string text = living.Count > shown.Count
            ? $"{living.Count} Firmen gefunden, die ersten {shown.Count} werden angezeigt. "
              + "Suchbegriff verlängern, um die Liste zu verkleinern."
            : $"{living.Count} Firmen gefunden.";

        if (living.Count == 0)
        {
            // Der Fall, der ohne eigenen Satz zur Luege wuerde: TANSS HAT geliefert, die Liste
            // ist trotzdem leer. Wer hier "keine Firma gefunden" liest, sucht weiter nach einer
            // Firma, die es sehr wohl gibt - sie ist nur nicht mehr in Betrieb.
            text = hidden == 1
                ? $"Zu „{term}“ gibt es nur einen inaktiven Treffer; er ist ausgeblendet. Eine "
                  + "Firma in Betrieb enthält diesen Begriff nicht."
                : $"Zu „{term}“ gibt es nur inaktive Treffer: {hidden} sind ausgeblendet. Eine "
                  + "Firma in Betrieb enthält diesen Begriff nicht.";
        }
        else if (hidden > 0)
        {
            text += hidden == 1
                ? " Ein inaktiver Treffer ist ausgeblendet."
                : $" {hidden} inaktive Treffer sind ausgeblendet.";
        }

        if (locked > 0)
        {
            text += $" Davon {locked} gesperrt und nicht wählbar.";
        }

        return new CompanySearchResult
        {
            Outcome = CompanySearchOutcome.Found,
            Companies = shown,
            TotalFound = ordered.Count,
            HiddenInactive = hidden,
            Threshold = threshold,
            Requests = requests,
            Explanation = text,
        };
    }

    /// <summary>
    /// Ordnet die Treffer so, dass ein Kürzen nicht das Gesuchte abschneidet.
    /// </summary>
    /// <remarks>
    /// TANSS liefert keine Bewertung mit, und alphabetisch zu kürzen hieße, bei „Gmb“ die Firmen
    /// ab dem Buchstaben H wegzuwerfen. Deshalb zuerst nach Passgenauigkeit des Namens, dann
    /// wählbar vor gesperrt und aktiv vor inaktiv, dann nach Name und Kennung — Letztere nur,
    /// damit die Reihenfolge bei Dubletten stabil bleibt.
    /// </remarks>
    private static IEnumerable<Company> Rank(string term, IReadOnlyList<Company> hits) =>
        hits.OrderBy(company => MatchRank(term, company.Name))
            .ThenBy(company => company.Selectable ? 0 : 1)
            .ThenBy(company => company.Inactive ? 1 : 0)
            .ThenBy(company => company.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(company => company.Id);

    /// <summary>Je kleiner, desto besser passt der Name zum Suchbegriff.</summary>
    /// <remarks>
    /// Rang 3 steht für einen Treffer, den TANSS über ein anderes Feld gefunden hat — etwa die
    /// Kundennummer oder den Ort. Welche Felder das genau sind, ist nicht ermittelt; solche
    /// Treffer werden deshalb behalten und nur hinten eingeordnet.
    /// </remarks>
    private static int MatchRank(string term, string name)
    {
        // Ordinal und nicht kulturabhaengig: TANSS selbst vergleicht Teilzeichenketten ohne
        // Ruecksicht auf Gross- und Kleinschreibung, und ein kulturabhaengiger Vergleich wuerde
        // hier eine Genauigkeit vortaeuschen, die der Server gar nicht anwendet. Sortiert wird
        // dagegen weiterhin kulturabhaengig - dort geht es um die Lesbarkeit der Liste.
        if (string.Equals(name, term, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    }

    /// <summary>Vervielfacht die Schwelle, ohne über den Zahlenbereich zu laufen.</summary>
    private static int Raise(int threshold) =>
        threshold > int.MaxValue / EscalationFactor ? int.MaxValue : threshold * EscalationFactor;

    /// <summary>
    /// Eine Gegenprobe, die den Vorgang nicht kosten darf.
    /// </summary>
    /// <remarks>
    /// Hausregel 5 in ihrer engsten Lesart: Der erste Aufruf trägt den Vorgang und darf werfen.
    /// Eine Gegenprobe ist ein Zusatz — scheitert sie, fehlt die Gewissheit, und genau das wird
    /// dann gemeldet. Sie deshalb werfen zu lassen, hieße eine beantwortbare Suche an einer
    /// Zusatzfrage scheitern zu lassen.
    /// </remarks>
    private async Task<(IReadOnlyList<Company> Companies, bool Answered)> TryFetchAsync(
        string term, int threshold, CancellationToken ct)
    {
        try
        {
            return (await FetchAsync(term, threshold, ct).ConfigureAwait(false), true);
        }
        catch (TanssException)
        {
            return ([], false);
        }
    }

    /// <summary>Der eigentliche Aufruf.</summary>
    /// <remarks>
    /// <b>PUT, obwohl es liest</b> — wie bei der Ticketsuche nimmt TANSS die Vorgabe im Rumpf
    /// entgegen. Der Aufruf ändert nichts; eine Wiederholung wäre unbedenklich, findet aber im
    /// Client nicht statt, weil dort nur GET wiederholt wird.
    /// </remarks>
    private async Task<IReadOnlyList<Company>> FetchAsync(string term, int threshold,
                                                          CancellationToken ct)
    {
        CompanySearchRequest body = new()
        {
            Query = term,
            Configs = new CompanySearchConfigs
            {
                Company = new CompanySearchConfig { MaxResults = threshold },
            },
        };

        CompanySearchResponse? response = await _client
            .PutAsync<CompanySearchResponse>(TanssRoutes.Search, body, ct: ct)
            .ConfigureAwait(false);

        return response?.Companies ?? [];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Zwei Schritte, weil ein einzelner nicht genügt.</b> Das Namensverzeichnis der
    /// Mitarbeiterabfrage nennt Kennung <i>und</i> Namen der eigenen Firma, aber keine
    /// Anschrift — und die Postleitzahl ist genau das, worauf es hier ankommt. Deshalb folgt
    /// auf die Kennung eine gewöhnliche Firmensuche über den Namen, aus der die Firma mit
    /// passender Kennung herausgegriffen wird.</para>
    /// <para><b>Warum über den Namen und nicht über die Kennung gesucht wird:</b> Eine Route
    /// <c>GET /api/v1/companies/{id}</c> gibt es in 10.10.0 nicht. Die Suche ist der einzige
    /// beschriebene Weg zu den Stammdaten einer Firma.</para>
    /// <para><b>Genau eine Firma muss es sein.</b> Nennt das Verzeichnis mehrere, ist keine
    /// davon belegbar die eigene — dann wird ausdrücklich nichts zurückgegeben, statt die
    /// erste zu nehmen. Eine falsch geratene Firma ergibt ein falsches Bundesland, falsche
    /// Feiertage und am Ende Mahnungen an Tagen, an denen niemand gearbeitet hat.</para>
    /// </remarks>
    public async Task<OwnCompanyResult> FindOwnAsync(CancellationToken ct = default)
    {
        if (_client is not ITanssMetaRead metaRead)
        {
            return new OwnCompanyResult(null, OwnCompanyOutcome.Undetermined,
                "Dieser HTTP-Zugang gibt den meta-Block nicht heraus, und nur dort steht die "
                + "eigene Firma. Das ist ein Programmierfehler und kein Bedienfehler — dem "
                + $"{nameof(CompanyRepository)} ist ein Zugang zu übergeben, der "
                + $"{nameof(ITanssMetaRead)} erfüllt.");
        }

        IReadOnlyDictionary<string, object?> meta;
        try
        {
            (_, meta) = await metaRead
                .GetWithMetaAsync<JsonElement>(TanssRoutes.OwnCompanyEmployees, ct: ct)
                .ConfigureAwait(false);
        }
        catch (TanssException ex)
        {
            return new OwnCompanyResult(null, OwnCompanyOutcome.Undetermined,
                "Die eigene Firma liess sich nicht abfragen. Solange sie unbekannt ist, steht "
                + "auch das Bundesland nicht fest und die Feiertage bleiben unberücksichtigt. "
                + "Sie lässt sich in den Einstellungen von Hand wählen. " + Redaction.Scrub(ex.Message));
        }

        IReadOnlyDictionary<string, string> companies =
            LinkedEntities.From(meta).Area(LinkedEntities.Companies);

        if (companies.Count != 1)
        {
            return new OwnCompanyResult(null, OwnCompanyOutcome.NotNamed,
                companies.Count == 0
                    ? "TANSS hat zu den eigenen Mitarbeitern keine Firma genannt. Die eigene "
                      + "Firma ist deshalb in den Einstellungen von Hand zu wählen; an ihrer "
                      + "Postleitzahl hängt das Bundesland und damit die Feiertage."
                    : $"TANSS hat {companies.Count} Firmen genannt statt einer. Welche davon die "
                      + "eigene ist, lässt sich von hier aus nicht entscheiden — sie ist in den "
                      + "Einstellungen von Hand zu wählen.");
        }

        KeyValuePair<string, string> only = companies.First();
        if (!int.TryParse(only.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            || id <= 0)
        {
            return new OwnCompanyResult(null, OwnCompanyOutcome.NotNamed,
                $"TANSS hat als eigene Firma „{only.Key}“ genannt, und das ist keine "
                + "Firmenkennung. Die eigene Firma ist in den Einstellungen von Hand zu wählen.");
        }

        string name = only.Value;
        Company fallback = new() { Id = id, Name = name };

        // Der zweite Schritt ist Beiwerk: Ohne ihn steht die Firma fest, nur eben ohne
        // Anschrift. Ein Fehlschlag darf deshalb nicht das ganze Ergebnis kosten.
        try
        {
            CompanySearchResult found = await SearchAsync(name, ct).ConfigureAwait(false);
            Company? exact = found.Companies.FirstOrDefault(company => company.Id == id);

            if (exact is not null)
            {
                return new OwnCompanyResult(exact, OwnCompanyOutcome.Found,
                    $"Eigene Firma: {exact.Name} ({exact.Distinguisher}).");
            }
        }
        catch (TanssException)
        {
            // Siehe Kommentar: die Kennung steht, die Anschrift fehlt.
        }

        return new OwnCompanyResult(fallback, OwnCompanyOutcome.FoundWithoutAddress,
            $"Eigene Firma: {name} (Kennung {id.ToString(CultureInfo.CurrentCulture)}). Die "
            + "Anschrift liess sich nicht nachschlagen, deshalb ist das Bundesland in den "
            + "Einstellungen von Hand zu setzen.");
    }
}
