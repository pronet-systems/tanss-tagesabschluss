using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.Api.Contract;

/// <summary>
/// Firmen suchen — und dabei zwischen „es gibt nichts“ und „es sind zu viele“ unterscheiden.
/// </summary>
/// <remarks>
/// <para><b>Warum diese Schnittstelle keine Liste zurückgibt.</b> Die Suche
/// <c>PUT /api/v1/search</c> nimmt unter <c>configs.company.maxResults</c> eine <b>Schwelle</b>
/// entgegen und keine Begrenzung: Übersteigt die Trefferzahl den Wert, antwortet TANSS mit einer
/// <b>leeren</b> Liste statt mit einer gekürzten. Nachgemessen gegen eine Instanz der Version
/// 10.10.0: <c>Gmb</c> hat 540 Treffer, mit <c>maxResults=539</c> kommen null, mit
/// <c>540</c> kommen 540.</para>
/// <para>Eine leere Liste bedeutet damit zweierlei, und eine Methode, die nur
/// <c>IReadOnlyList&lt;Company&gt;</c> liefert, wirft die Unterscheidung weg. Die Oberfläche
/// schriebe dann „keine Firma gefunden“, obwohl es 540 gibt. Deshalb ist der Rückgabewert
/// <see cref="CompanySearchResult"/> mit einem ausdrücklichen
/// <see cref="CompanySearchOutcome"/>.</para>
/// </remarks>
public interface ICompanyRepository
{
    /// <summary>
    /// Sucht Firmen zu einem Suchbegriff.
    /// </summary>
    /// <remarks>
    /// <para>Unter <see cref="CompanyRepositoryDefaults.MinimumQueryLength"/> Zeichen wird gar
    /// nicht erst gefragt; das Ergebnis ist dann
    /// <see cref="CompanySearchOutcome.QueryTooShort"/>. Nachgemessen genügen drei Zeichen für
    /// eine brauchbare Antwort (<c>Pro</c> ergab 37 Treffer), und weniger erzeugt nur Last ohne
    /// Nutzen.</para>
    /// <para>Die Suche ist eine Teilzeichenkettensuche ohne Rücksicht auf Groß- und
    /// Kleinschreibung: <c>roN</c> trifft <c>ProNet</c>.</para>
    /// <para>Wirft nur, wenn TANSS den <b>ersten</b> Aufruf abweist — eine fehlgeschlagene
    /// Gegenprobe kostet die Gewissheit, aber nicht den Vorgang; das Ergebnis heißt dann
    /// <see cref="CompanySearchOutcome.Undetermined"/>.</para>
    /// </remarks>
    /// <param name="query">Der Suchbegriff. Führende und folgende Leerzeichen fallen weg.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Treffer samt der Aussage, wie eine leere Liste zu lesen ist.</returns>
    Task<CompanySearchResult> SearchAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Ermittelt die <b>eigene</b> Firma — die des Betreibers dieser TANSS-Instanz.
    /// </summary>
    /// <remarks>
    /// <para><b>Warum das überhaupt eine Frage ist.</b> TANSS führt die eigene Firma als
    /// Konfigurationswert <c>system.eigeneFirma.ID</c> in der Datenbank. <b>Keine</b> Route
    /// dieser Schnittstelle gibt ihn heraus. Was es gibt, ist ein Umweg:
    /// <c>GET /api/erp/v1/companies/employees</c> ohne <c>companyId</c> liefert laut
    /// Beschreibung „all employees of the own company“ — und wenn TANSS dabei sein
    /// Namensverzeichnis mitschickt, steht die gesuchte Firma darin.</para>
    /// <para><b>Beides ist Annahme, nicht Messung</b>, und beides kann fehlschlagen. Deshalb
    /// wirft diese Methode nicht, sondern sagt mit <see cref="OwnCompanyOutcome"/>, woran es
    /// lag. Die Oberfläche bietet dann die Firmenauswahl an — von Hand gewählt ist besser als
    /// falsch geraten, denn an dieser Firma hängt das Bundesland und damit die Feiertage.</para>
    /// <para>Wozu das Ergebnis gebraucht wird: Aus der Postleitzahl der eigenen Firma wird das
    /// Bundesland bestimmt, und aus dem Bundesland die Feiertage. Ein Feiertag ist kein
    /// Arbeitstag, und an einem Feiertag ist eine fehlende Leistung keine Lücke.</para>
    /// </remarks>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Die Firma samt der Aussage, wie sicher sie ist.</returns>
    Task<OwnCompanyResult> FindOwnAsync(CancellationToken ct = default);
}

/// <summary>Die Vorgabewerte der Firmensuche, an einer Stelle und begründet.</summary>
public static class CompanyRepositoryDefaults
{
    /// <summary>
    /// So viele Zeichen muss ein Suchbegriff mindestens haben.
    /// </summary>
    /// <remarks>
    /// Nachgemessen reichen drei Zeichen aus, um eine Antwort in brauchbarer Größe zu bekommen
    /// (<c>Pro</c> 37, <c>Sys</c> 37, <c>roN</c> 41 Treffer). Zwei Zeichen träfen bei einer
    /// Teilzeichenkettensuche fast alles und kosteten nur Übertragung.
    /// </remarks>
    public const int MinimumQueryLength = 3;

    /// <summary>
    /// Die Schwelle, mit der gefragt wird.
    /// </summary>
    /// <remarks>
    /// <para>Bewusst weit über jeder erreichbaren Firmenzahl einer TANSS-Instanz. Die Schwelle
    /// ist <b>keine</b> Seitengröße: TANSS liefert ohnehin alle Treffer: die Menge der
    /// übertragenen Daten hängt an der Trefferzahl, nicht an diesem Wert. Ein hoher Wert kostet
    /// also nichts und verhindert den einzigen Fall, der wirklich schadet — die stille leere
    /// Antwort.</para>
    /// <para><b>Nicht ermittelt</b> ist, ob TANSS eine beliebig hohe Schwelle annimmt; deshalb
    /// ist sie am <c>CompanyRepository</c> einstellbar und nicht festverdrahtet.</para>
    /// </remarks>
    public const int Threshold = 100_000;

    /// <summary>
    /// So viele Firmen gehen höchstens an die Oberfläche.
    /// </summary>
    /// <remarks>
    /// Gekürzt wird <b>hier</b> und nicht von TANSS, weil nur ein clientseitiger Schnitt die
    /// Gesamtzahl kennt: <see cref="CompanySearchResult.TotalFound"/> bleibt erhalten, und die
    /// Oberfläche kann „540 gefunden, 200 gezeigt“ schreiben statt „keine gefunden“.
    /// </remarks>
    public const int DisplayLimit = 200;
}
