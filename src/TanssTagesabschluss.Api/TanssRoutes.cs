using System.Globalization;

namespace TanssTagesabschluss.Api;

/// <summary>
/// Die Routen, die dieses Werkzeug benutzt — und die Regel, welches Präfix
/// <c>loggedInUserId</c> verlangt und welches ihn nicht braucht.
/// </summary>
/// <remarks>
/// <para><b>Anders als im Schwesterprojekt ist hier fast alles dokumentiert.</b> Die
/// OpenAPI-Beschreibung zu TANSS 10.10.0 führt die Zeitstempel, die Leistungen und die
/// Abwesenheiten mitsamt Rumpf und Antwort. Belegt heißt aber nicht gemessen: Wo dieses
/// Werkzeug sich auf die Beschreibung verlässt und noch keine Messung vorliegt, steht es
/// ausdrücklich dabei.</para>
///
/// <para><b>Zwei Routen sind es nicht.</b> <c>GET /api/v1/jwts/tanss_app</c> steht in keiner
/// Beschreibung und ist aus dem Schwesterprojekt übernommen, wo sie gegen eine Produktivinstanz
/// der Fassung 10.10.0 ausgemessen wurde. Dasselbe gilt für <c>PUT /api/v1/search</c> in seiner
/// Eigenheit, dass <c>maxResults</c> eine Schwelle ist und keine Begrenzung.</para>
/// </remarks>
public static class TanssRoutes
{
    /// <summary>Integrationsschnittstelle. Hier ist <c>loggedInUserId</c> überflüssig.</summary>
    public const string TanssXPrefix = "/api/tanss.x/v1";

    /// <summary>Reguläre Schnittstelle. Hier ist <c>loggedInUserId</c> zwingend.</summary>
    public const string V1Prefix = "/api/v1";

    /// <summary>
    /// Die ERP-Schnittstelle.
    /// </summary>
    /// <remarks>
    /// <b>Ungemessen:</b> Ob sie <c>loggedInUserId</c> verlangt, ist nicht geprüft.
    /// <see cref="NeedsLoggedInUserId"/> hängt ihn nicht an, weil ihr Pfad nicht mit
    /// <see cref="V1Prefix"/> beginnt. Antwortet eine dieser Routen mit 403, ist das die erste
    /// Annahme, die zu prüfen ist.
    /// </remarks>
    public const string ErpPrefix = "/api/erp/v1";

    // --- Zeiterfassung -------------------------------------------------------------------

    /// <summary>
    /// Die Zeitstempel selbst: Kommen, Gehen, Pausenbeginn, Pausenende.
    /// </summary>
    /// <remarks>
    /// <para><c>GET</c> mit <c>from</c> und <c>till</c> in Unix-<b>Sekunden</b>. Ohne die beiden
    /// liefert TANSS den laufenden Tag.</para>
    /// <para><b>Diese Route wird nur gelesen.</b> Ein <c>POST</c> hierauf stempelt den
    /// Mitarbeiter ein oder aus — dieses Werkzeug ist eine Kontrolle der Erfassung und kein
    /// Ersatz für sie. Wer hier einen Schreibweg ergänzt, ändert die Arbeitszeit eines
    /// Menschen.
    /// </para>
    /// </remarks>
    public const string Timestamps = V1Prefix + "/timestamps";

    /// <summary>
    /// Zeitstempel <b>samt Auswertung</b>: je Tag die Abschnitte nach Art gruppiert.
    /// </summary>
    /// <remarks>
    /// Liefert zusätzlich zu den rohen Stempeln das Arbeitszeitmodell des Tages und die
    /// Abschnitte unter <c>types</c>. Für dieses Werkzeug die zweitwichtigste Route überhaupt;
    /// die wichtigste ist <see cref="TimestampStatistics"/>.
    /// </remarks>
    public const string TimestampInfo = V1Prefix + "/timestamps/info";

    /// <summary>
    /// <b>Die Route, um die es geht.</b> Zeitstempel, Arbeitszeitmodell und Auswertung je Tag.
    /// </summary>
    /// <remarks>
    /// <para>Beschrieben mit <c>from</c>, <c>till</c> (Unix-Sekunden) und <c>employeeIds</c>
    /// (kommagetrennt). Die Antwort trägt je Mitarbeiter und Tag:</para>
    /// <list type="bullet">
    ///   <item><c>workingTimeModelId</c> — und das Modell selbst unter
    ///   <c>meta.listProperties.workingTimeModels[ID].extras.model</c>, mit Beginn, Ende,
    ///   Pause und Sollminuten je Wochentag.</item>
    ///   <item><c>types</c> — die Abschnitte, nach <c>TimestampType</c> gruppiert:
    ///   <c>WORK</c>, <c>VACATION</c>, <c>ILLNESS</c>, <c>ABSENCE_PAID</c>,
    ///   <c>ABSENCE_UNPAID</c>, <c>OVERTIME</c>, <c>INHOUSE</c>, <c>ERRAND</c> — und
    ///   <c>DOCUMENTED_SUPPORT</c>.</item>
    /// </list>
    /// <para><b><c>DOCUMENTED_SUPPORT</c> ist die Abkürzung, die es zu prüfen gilt:</b> TANSS
    /// rechnet dort selbst aus, welche Zeitabschnitte des Tages durch erfasste Leistungen
    /// gedeckt sind. Dieses Werkzeug benutzt den Wert als <b>Gegenprobe</b> und nicht als
    /// Quelle — die Lücken werden aus den Leistungen selbst gerechnet, weil nur so zu jeder
    /// Lücke auch dasteht, was links und rechts von ihr liegt.</para>
    /// </remarks>
    public const string TimestampStatistics = V1Prefix + "/timestamps/statistics";

    /// <summary>
    /// Die Pausenregeln der Instanz: ab wie vielen Minuten Arbeit wie viel Pause zu nehmen ist.
    /// </summary>
    /// <remarks>
    /// Gebraucht, um eine Lücke, die genau einer vorgeschriebenen Pause entspricht, nicht als
    /// vergessene Leistung auszuweisen. Die tatsächlich gestempelte Pause steht dagegen in den
    /// Zeitstempeln selbst (<c>PAUSE_START</c> / <c>PAUSE_END</c>) und hat Vorrang.
    /// </remarks>
    public const string PauseConfigs = V1Prefix + "/timestamps/pauseConfigs";

    /// <summary>
    /// Der Tagesabschluss der Zeiterfassung. <b>Dieses Werkzeug schreibt ihn niemals.</b>
    /// </summary>
    /// <remarks>
    /// <para>Die Konstante steht hier, damit unmissverständlich ist, dass sie bekannt und
    /// bewusst ungenutzt ist. <c>POST</c> darauf <b>schliesst den Tag in TANSS ab</b>: Die
    /// gerechneten Werte werden festgeschrieben, und ein bestehender Abschluss lässt sich nur
    /// über <c>DELETE</c> und ein erneutes Anlegen berichtigen. Das ist die Entscheidung eines
    /// Menschen und niemals die eines Hintergrunddienstes.</para>
    /// <para>Der Name dieses Werkzeugs meint denselben Vorgang, nicht dieselbe Route: Es
    /// <i>bereitet</i> den Tagesabschluss vor, indem es die Lücken zeigt. Abgeschlossen wird in
    /// TANSS.</para>
    /// </remarks>
    public const string DayClosing = V1Prefix + "/timestamps/dayClosing";

    // --- Leistungen ----------------------------------------------------------------------

    /// <summary>
    /// Leistungen <b>lesen</b> — über <c>PUT</c> mit Filterrumpf.
    /// </summary>
    /// <remarks>
    /// <b>PUT, obwohl es liest</b>, wie die Ticket- und die Firmensuche: Der Filter geht im
    /// Rumpf, und dafür hat ein <c>GET</c> keinen Platz. Die für uns entscheidenden Felder des
    /// Filters sind <c>timeframe</c> (<c>from</c>/<c>to</c> in Unix-Sekunden) und
    /// <c>employees</c>.
    /// </remarks>
    public const string SupportList = V1Prefix + "/supports/list";

    /// <summary>
    /// Leistungen anlegen. <c>POST</c> ist das einzige Verb.
    /// </summary>
    /// <remarks>
    /// <b>TANSS dedupliziert nicht.</b> Zwei gleichlautende Aufrufe ergeben zwei Leistungen und
    /// damit zwei Rechnungsposten. Vor jeder Wiederholung steht deshalb die Existenzprüfung
    /// über <see cref="SupportList"/> — siehe <c>ISupportRepository</c>.
    /// </remarks>
    public const string Supports = V1Prefix + "/supports";

    /// <summary>Eine einzelne Leistung. Der Weg, eine angelegte Kennung gegenzuprüfen.</summary>
    /// <param name="supportId">Die Kennung der Leistung.</param>
    /// <returns>Der Pfad.</returns>
    public static string SupportById(int supportId) =>
        string.Create(CultureInfo.InvariantCulture, $"{Supports}/{supportId}");

    /// <summary>
    /// Bereitet eine Leistung aus einer Quelle vor, ohne sie anzulegen.
    /// </summary>
    /// <remarks>
    /// Mit <c>{"initializers":[{"type":"TIMER","id":&lt;id&gt;}]}</c> antwortet TANSS mit einer
    /// vollständig vorbelegten Leistung — Stundensatz, Abrechnungsart, Fahrzeug, Zone. Dieses
    /// Werkzeug benutzt den Weg, um eine <b>von Hand</b> nachgetragene Leistung mit denselben
    /// Abrechnungsfeldern zu versehen, die TANSS selbst gesetzt hätte. Ohne ihn entstünde eine
    /// Leistung mit Stundensatz 0.
    /// </remarks>
    public const string SupportProperties = V1Prefix + "/supports/properties";

    // --- Abwesenheiten -------------------------------------------------------------------

    /// <summary>
    /// Urlaub, Krankheit und sonstige Abwesenheiten — <c>PUT</c> mit Filterrumpf.
    /// </summary>
    /// <remarks>
    /// <para>Gefiltert wird über <c>year</c>, <c>month</c> und <c>employeeIds</c>. Jeder Antrag
    /// trägt seine Tage einzeln (<c>days</c>) mit <c>forenoon</c>, <c>afternoon</c> und
    /// wahlweise einer Uhrzeitspanne — ein halber Urlaubstag ist damit auch als halber zu
    /// erkennen.</para>
    /// <para><b>Der Zustand zählt.</b> Nur <c>APPROVED</c> ist eine Abwesenheit, die eine Lücke
    /// erklärt. Ein Antrag im Zustand <c>REQUESTED</c> ist ein Wunsch, und an einem Tag, an dem
    /// trotzdem gearbeitet wurde, wäre er die falsche Erklärung.</para>
    /// </remarks>
    public const string VacationRequestList = V1Prefix + "/vacationRequests/list";

    /// <summary>Die Zusatzarten zu einer Abwesenheit (Sonderurlaub, Home-Office, Kur).</summary>
    public const string PlanningAdditionalTypes =
        V1Prefix + "/vacationRequests/planningAdditionalTypes";

    // --- Tickets, Firmen, Personen -------------------------------------------------------

    /// <summary>Die eigenen Tickets des angemeldeten Technikers.</summary>
    public const string OwnTickets = V1Prefix + "/tickets/own";

    /// <summary>Ticketsuche mit Filterrumpf. <b>PUT, obwohl es liest.</b></summary>
    public const string TicketSearch = V1Prefix + "/tickets";

    /// <summary>
    /// Ein einzelnes Ticket. Antwortet mit 404, wenn es die Kennung nicht gibt.
    /// </summary>
    /// <param name="ticketId">Die zu prüfende Kennung.</param>
    /// <returns>Der Pfad.</returns>
    public static string TicketById(int ticketId) =>
        string.Create(CultureInfo.InvariantCulture, $"{TicketSearch}/{ticketId}");

    /// <summary>
    /// Die bereichsübergreifende Suche — der Weg von einem Firmennamen zur <c>companyId</c>.
    /// </summary>
    /// <remarks>
    /// <b>PUT, obwohl es liest.</b> Die Falle sitzt in <c>configs.company.maxResults</c>: Das
    /// ist eine Schwelle und keine Begrenzung — wird sie überschritten, kommt eine leere Liste
    /// statt einer gekürzten. Im Schwesterprojekt nachgemessen: <c>Gmb</c> hat 540 Treffer, mit
    /// 539 kommen null, mit 540 kommen alle.
    /// </remarks>
    public const string Search = V1Prefix + "/search";

    /// <summary>
    /// Die Mitarbeiter der <b>eigenen</b> Firma, wenn keine <c>companyId</c> mitgegeben wird.
    /// </summary>
    /// <remarks>
    /// <para>Der einzige Weg dieser Schnittstelle, der den Begriff „eigene Firma“ überhaupt
    /// kennt. In TANSS steht sie als Konfigurationswert <c>system.eigeneFirma.ID</c> in der
    /// Datenbank und wird von <b>keiner</b> beschriebenen Route unmittelbar herausgegeben.</para>
    /// <para><b>Nur noch der Rückfall.</b> Den Vortritt hat <see cref="OwnState"/>: dort steht
    /// die eigene Firma als Zahl, statt aus einem Namensverzeichnis erschlossen zu werden.
    /// Dieser Weg bleibt für Instanzen stehen, die <see cref="OwnState"/> nicht kennen.</para>
    /// <para><b>Nachgemessen am 14.09.2026</b> gegen 10.10.0: HTTP 403, obwohl dasselbe Token
    /// auf <see cref="OwnState"/> HTTP 200 bekommt. Das ERP-Präfix hängt an eigenen Rechten —
    /// ein Rückfall, auf den man sich nicht verlassen kann, und genau deshalb nicht der
    /// erste Weg.</para>
    /// </remarks>
    /// <summary>
    /// Der eigene Zustand: angemeldeter Benutzer, eigene Firma, Rechte, Module, API-Stand.
    /// </summary>
    /// <remarks>
    /// <para><b>Der unmittelbare Weg zur eigenen Firma.</b> Die Antwort trägt
    /// <c>content.ownCompanyId</c> — jenen Konfigurationswert <c>system.eigeneFirma.ID</c>, den
    /// sonst keine Route herausgibt — und dazu unter <c>content.defaultCompany</c> die ganze
    /// Firma samt <c>postcode</c>. Damit steht das Bundesland aus <b>einem</b> Aufruf fest,
    /// ohne Umweg über eine Namenssuche.</para>
    /// <para><b>Nachgemessen am 14.09.2026</b> gegen 10.10.0: mit <c>?loggedInUserId=…</c>
    /// HTTP 200, ohne den Parameter HTTP 403. Die Antwort nennt neben der Firma auch
    /// <c>content.loggedInUser.id</c> — die Mitarbeiterkennung ist damit aus dem Token allein
    /// herzuleiten und muss niemandem abverlangt werden.</para>
    /// <para>In <c>api-doc-10.10.0.yaml</c> ist diese Route <b>nicht</b> beschrieben; belegt ist
    /// sie durch <c>TnsEmployeeController</c> (<c>@GetMapping("/ownState")</c> auf
    /// <c>@RequestMapping("/api/v1/employees")</c>) und durch die Messung oben.</para>
    /// </remarks>
    public const string OwnState = V1Prefix + "/employees/ownState";

    public const string OwnCompanyEmployees = ErpPrefix + "/companies/employees";

    /// <summary>Die Techniker der Instanz. Liegt auf <c>tanss.x</c>.</summary>
    public const string Technicians = TanssXPrefix + "/technicians";

    // --- Geräte ---------------------------------------------------------------------------

    /// <summary>
    /// PCs und Server einer Firma. <b>PUT, obwohl es liest</b> — der Filter geht im Rumpf.
    /// </summary>
    /// <remarks>
    /// Gebraucht, um eine Leistung auf ein Gerät zu buchen: Die Kennung von hier wird zur
    /// <c>linkId</c> der Leistung, der Zuordnungstyp ist <see cref="Model.LinkType.Pc"/>.
    /// </remarks>
    public const string Pcs = V1Prefix + "/pcs";

    /// <summary>Peripherie einer Firma — Drucker, Bildschirme, Netzwerkgeräte.</summary>
    public const string Peripheries = V1Prefix + "/peripheries";

    /// <summary>Komponenten — was in einem Gerät steckt.</summary>
    public const string Components = V1Prefix + "/components";

    // --- Anmeldung und Token -------------------------------------------------------------

    /// <summary>Anmeldung mit Name und Kennwort. Der einzige Aufruf ohne Token.</summary>
    public const string Login = V1Prefix + "/login";

    /// <summary>
    /// Alles, was TANSS ein Token ausstellen lässt.
    /// </summary>
    /// <remarks>
    /// Der Riegel gegen Wiederholungen hängt am <b>Präfix</b> und nicht an einer einzelnen
    /// Route: Ein Pfad, der ein unwiderrufliches Token ausstellt, darf nicht erst dann
    /// geschützt werden, wenn jemand daran denkt.
    /// </remarks>
    public const string JwtPrefix = V1Prefix + "/jwts";

    /// <summary>Token prägen. <c>tanss_app</c> ist der gültige Wert — <c>tanss_x</c> gibt es nicht.</summary>
    public const string MintToken = JwtPrefix + "/tanss_app";

    /// <summary>
    /// Braucht der Pfad den Parameter <c>loggedInUserId</c>?
    /// </summary>
    /// <remarks>
    /// <para>Auf <c>/api/v1/**</c> ist er zwingend; ohne ihn antwortet TANSS mit 403. Auf
    /// <c>/api/tanss.x/v1/**</c> wird er ignoriert, und auf <c>/api/erp/v1/**</c> ist er
    /// ungemessen — beide beginnen nicht mit <see cref="V1Prefix"/> und bekommen ihn deshalb
    /// nicht.</para>
    /// <para>Die Prüfung auf das <c>tanss.x</c>-Präfix steht <b>zuerst</b>: Beide Präfixe
    /// beginnen zwar nicht gemeinsam, aber die Reihenfolge macht die Absicht lesbar und schützt
    /// vor einer späteren Umstellung auf einen gemeinsamen Stamm.</para>
    /// </remarks>
    /// <param name="path">Der Pfad mit Präfix.</param>
    /// <returns><c>true</c>, wenn der Parameter anzuhängen ist.</returns>
    public static bool NeedsLoggedInUserId(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return !path.StartsWith(TanssXPrefix, StringComparison.Ordinal)
            && path.StartsWith(V1Prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Hinterlässt ein <c>GET</c> auf diesen Pfad serverseitig eine Spur, die sich nicht
    /// zurücknehmen lässt?
    /// </summary>
    /// <remarks>
    /// <para><see cref="MintToken"/> ist trotz des Verbs <b>kein Lesevorgang</b>: TANSS stellt
    /// bei jedem Aufruf ein neues JWT aus, und 10.10.0 kennt keinen Widerruf. Ein zweiter
    /// Versuch nach einer Zeitüberschreitung prägt deshalb ein <b>zweites</b> Token, von dem
    /// niemand mehr erfährt. Solche Pfade laufen mit genau einem Versuch.</para>
    /// <para><b>Geprüft wird gegen <see cref="JwtPrefix"/> und nicht gegen die eine bekannte
    /// Route</b>, damit jede Tokenart, die dort später dazukommt, vom ersten Aufruf an
    /// geschützt ist.</para>
    /// </remarks>
    /// <param name="path">Der Pfad mit Präfix.</param>
    /// <returns><c>true</c>, wenn der Pfad nicht wiederholt werden darf.</returns>
    public static bool HasSideEffectOnGet(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.StartsWith(JwtPrefix, StringComparison.Ordinal);
    }
}
