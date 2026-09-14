using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Http;

namespace TanssTagesabschluss.Live.Tests;

/// <summary>
/// Der Zugang zu einer <b>echten</b> TANSS-Instanz — oder die Auskunft, dass keiner vorliegt.
/// </summary>
/// <remarks>
/// <para><b>Warum es dieses Testprojekt gibt.</b> Im Schwesterprojekt haben zwei Fehler
/// wochenlang alle übrigen Tests bestanden, weil sie mit Attrappen unsichtbar waren: Ein
/// Tokenspeicher gab das Präfix <c>Bearer </c> nicht mit, und die Einrichtung legte das
/// kurzlebige Sitzungstoken aus <c>/api/v1/login</c> als Arbeitstoken ab. Beide Male prüfte
/// die Testsuite eine Annahme über TANSS gegen die eigene Nachbildung dieser Annahme — und
/// bekam recht.</para>
///
/// <para><b>Was hier geprüft wird, ist deshalb ausschliesslich das Ungemessene.</b> Dieses
/// Werkzeug stützt sich an mehreren Stellen auf die Beschreibung zu 10.10.0 statt auf eine
/// Messung; die betreffenden Stellen sind im Quelltext als <i>ungemessen</i> vermerkt, und
/// hier stehen sie als Test.</para>
///
/// <para><b>Ohne Zugangsdaten überspringt sich jeder Test selbst.</b> Die Werkstrecke bei
/// GitHub hat keine Instanz und soll deswegen nicht scheitern. „Übersprungen“ ist dabei
/// ausdrücklich nicht „bestanden“: Der Testläufer weist es getrennt aus.</para>
///
/// <para><b>Dieses Projekt schreibt nichts.</b> Anders als im Schwesterprojekt, wo ein Timer
/// den schreibenden Umlauf trug, gibt es hier keinen Gegenstand, der sich restlos wieder
/// entfernen liesse: Eine Leistung lässt sich über die Schnittstelle nicht löschen, und ein
/// Zeitstempel ist die Arbeitszeit eines Menschen. Geprüft wird deshalb ausschliesslich
/// lesend.</para>
/// </remarks>
public static class LiveInstance
{
    /// <summary>Die Basisadresse aus der Umgebung, oder <see langword="null"/>.</summary>
    public static string? BaseUrl => Read("TANSS_BASE_URL");

    /// <summary>Der Anmeldename aus der Umgebung.</summary>
    public static string? User => Read("TANSS_USER");

    /// <summary>Das Kennwort aus der Umgebung.</summary>
    public static string? Password => Read("TANSS_PASSWORD");

    /// <summary>Liegen alle drei Angaben vor?</summary>
    public static bool IsConfigured =>
        BaseUrl is not null && User is not null && Password is not null;

    /// <summary>
    /// Der Satz, der beim Überspringen im Protokoll steht.
    /// </summary>
    /// <remarks>
    /// Er nennt die Variablen ausdrücklich: Ein „übersprungen“ ohne Begründung liest sich wie
    /// ein Mangel des Testlaufs und nicht wie eine bewusste Entscheidung.
    /// </remarks>
    public const string SkipReason =
        "Ohne TANSS_BASE_URL, TANSS_USER und TANSS_PASSWORD gibt es keine Instanz, gegen die "
        + "geprüft werden könnte. Übersprungen ist nicht bestanden.";

    /// <summary>
    /// Meldet sich an, prägt ein <b>kurzlebiges</b> Token und baut daraus einen Zugang.
    /// </summary>
    /// <remarks>
    /// <para><b>Geprägt wird ausschliesslich mit <c>isForTesting=true</c>.</b> Ein regulär
    /// geprägtes Token läuft ein Jahr und lässt sich in TANSS 10.10.0 <b>nicht widerrufen</b>.
    /// Eine Testreihe, die bei jedem Lauf eines ausstellt, hinterlässt nach einem Monat
    /// dreissig gültige Token, von denen niemand mehr weiss.</para>
    /// <para>Die Harmlosigkeit hängt nicht an einer Zusage des Servers, sondern an
    /// <c>TanssAuth.MintAsync</c>: Dort werden in diesem Fall 60 Sekunden angefragt.</para>
    /// </remarks>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Der Zugang und die Mitarbeiterkennung. Der Aufrufer gibt den Zugang frei.</returns>
    public static async Task<(TanssClient Client, int EmployeeId)> ConnectAsync(
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(SkipReason);
        }

        Api.Auth.LoginResult login = await Api.Auth.TanssAuth
            .LoginAsync(BaseUrl!, User!, Password!, ct: ct).ConfigureAwait(false);

        TanssOptions options = new()
        {
            BaseUrl = BaseUrl!,
            EmployeeId = login.EmployeeId,
        };

        using (TanssClient session = new(options, new Fixed("Bearer " + login.ApiKey)))
        {
            // isForTesting: 60 Sekunden Laufzeit. Siehe Kommentar oben.
            string minted = await Api.Auth.TanssAuth
                .MintAsync(session, durationDays: 1, info: "Tagesabschluss Live-Test",
                           forTesting: true, ct).ConfigureAwait(false);

            return (new TanssClient(options, new Fixed(minted)), login.EmployeeId);
        }
    }

    private static string? Read(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Ein Tokenspeicher mit festem Wert.</summary>
    private sealed class Fixed : ITokenStore
    {
        private readonly string _token;

        public Fixed(string token) => _token = token;

        public string Read() => _token;

        public void Write(string token)
        {
            // Ein Test schreibt kein Token in das Profil des Ausfuehrenden.
        }
    }
}

/// <summary>
/// Ein Test, der sich ohne Zugangsdaten selbst überspringt.
/// </summary>
/// <remarks>
/// Ein eigenes Attribut statt einer Abfrage am Anfang jeder Methode: So steht der Grund im
/// Protokoll des Testläufers, und der Test zählt als übersprungen und nicht als bestanden.
/// </remarks>
public sealed class LiveFactAttribute : Xunit.FactAttribute
{
    /// <summary>Baut das Attribut und setzt gegebenenfalls den Übersprunggrund.</summary>
    public LiveFactAttribute()
    {
        if (!LiveInstance.IsConfigured)
        {
            Skip = LiveInstance.SkipReason;
        }
    }
}
