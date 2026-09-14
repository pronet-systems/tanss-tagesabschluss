using System.Text.Json;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.Api.Repository;
using Xunit;

namespace TanssTagesabschluss.Api.Tests;

/// <summary>
/// Der unmittelbare Weg zur eigenen Firma: <c>GET /api/v1/employees/ownState</c>.
/// </summary>
/// <remarks>
/// <para>Die Antwortform stammt aus einer Messung gegen 10.10.0 am 14.09.2026 (HTTP 200 mit
/// <c>?loggedInUserId=…</c>, HTTP 403 ohne). Die Werte hier sind erfunden — geprüft wird die
/// <b>Form</b>, und Kundendaten gehören nicht in ein Verzeichnis.</para>
/// <para>In <c>api-doc-10.10.0.yaml</c> ist die Route nicht beschrieben. Belegt ist sie durch
/// <c>TnsEmployeeController</c> im ausgelieferten Archiv: <c>@GetMapping("/ownState")</c> auf
/// <c>@RequestMapping("/api/v1/employees")</c>, Rückgabetyp <c>TnsEmployeeOwnState</c>.</para>
/// </remarks>
public sealed class OwnStateRouteTests
{
    [Fact]
    public void Die_Route_steht_wortgetreu()
    {
        // Wortgetreu und nicht zusammengesetzt: Ein Tippfehler im Pfad ergibt eine 404, die
        // sich von "diese Instanz kennt die Route nicht" nicht unterscheiden laesst - und das
        // Werkzeug fiele still auf den ERP-Weg zurueck, der auf dieser Instanz 403 gibt.
        Assert.Equal("/api/v1/employees/ownState", TanssRoutes.OwnState);
    }

    [Fact]
    public void Ohne_loggedInUserId_antwortet_TANSS_mit_403_also_muss_der_Parameter_mit()
    {
        // Nachgemessen: derselbe Aufruf ohne den Parameter gibt 403. Die Route liegt auf dem
        // v1-Praefix und bekommt ihn deshalb - hier festgenagelt, damit eine spaetere
        // Umstellung der Praefixregel diesen Fall nicht still verliert.
        Assert.True(TanssRoutes.NeedsLoggedInUserId(TanssRoutes.OwnState));
    }

    [Fact]
    public void Der_eigene_Zustand_ist_ein_Lesevorgang()
    {
        Assert.False(TanssRoutes.HasSideEffectOnGet(TanssRoutes.OwnState));
    }
}

/// <summary>Was aus der gemessenen Antwortform gelesen wird.</summary>
public sealed class OwnStateModelTests
{
    [Fact]
    public void Kennung_Firma_und_Anschrift_kommen_aus_einer_einzigen_Antwort()
    {
        OwnState state = Fixtures.Parse(Fixtures.FullState);

        Assert.Equal(4711, state.OwnCompanyId);
        Assert.Equal(4711, state.DefaultCompany?.Id);
        Assert.Equal("59757", state.DefaultCompany?.PostCode);
        Assert.Equal("Musterstadt", state.DefaultCompany?.City);
        Assert.Equal(7, state.LoggedInUser?.Id);
        Assert.Equal("Muster, Erika", state.LoggedInUser?.Name);
        Assert.Equal("TECHNICIAN", state.UserType);
        Assert.Equal("10.10.0", state.ApiVersion);
    }

    [Fact]
    public void Die_Postleitzahl_kommt_auch_klein_geschrieben_an()
    {
        // TANSS schreibt "postcode", das Modell "postCode". Das traegt nur, solange die
        // Netzschicht ohne Ruecksicht auf Gross- und Kleinschreibung liest - und genau das
        // wird hier festgehalten, weil es sonst beim naechsten Umbau der Leseoptionen
        // lautlos verschwindet.
        OwnState state = Fixtures.Parse(Fixtures.FullState);

        Assert.False(string.IsNullOrWhiteSpace(state.DefaultCompany?.PostCode));
    }
}

/// <summary>
/// Wie sich <c>FindOwnAsync</c> entscheidet — und wann es sich ausdrücklich nicht entscheidet.
/// </summary>
public sealed class FindOwnCompanyTests
{
    [Fact]
    public async Task Stimmen_Kennung_und_Firma_ueberein_steht_die_Anschrift_fest()
    {
        FakeClient client = new();
        client.OnGet(TanssRoutes.OwnState, Fixtures.FullState);

        OwnCompanyResult result = await new CompanyRepository(client).FindOwnAsync();

        Assert.Equal(OwnCompanyOutcome.Found, result.Outcome);
        Assert.True(result.HasAddress);
        Assert.Equal(4711, result.Company?.Id);
        Assert.Equal("59757", result.Company?.PostCode);

        // Ein Aufruf, nicht zwei: Der ganze Sinn dieser Route ist, dass die Namenssuche
        // entfaellt.
        Assert.Equal([TanssRoutes.OwnState], client.Calls);
    }

    [Fact]
    public async Task Eine_abweichende_Firma_wird_genannt_aber_nicht_uebernommen()
    {
        // Der Fall des Technikers einer Tochtergesellschaft: ownCompanyId ist die Einstellung
        // des Systems, defaultCompany die Zuordnung des Benutzers. Die Anschrift der falschen
        // Firma ergaebe ein falsches Bundesland und damit falsche Feiertage.
        FakeClient client = new();
        client.OnGet(TanssRoutes.OwnState, Fixtures.MismatchedState);

        OwnCompanyResult result = await new CompanyRepository(client).FindOwnAsync();

        Assert.Equal(OwnCompanyOutcome.FoundWithoutAddress, result.Outcome);
        Assert.False(result.HasAddress);
        Assert.Equal(4711, result.Company?.Id);
        Assert.Null(result.Company?.PostCode);
        Assert.Contains("99999", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_hinterlegte_eigene_Firma_wird_nicht_geraten()
    {
        FakeClient client = new();
        client.OnGet(TanssRoutes.OwnState, Fixtures.UnsetState);

        OwnCompanyResult result = await new CompanyRepository(client).FindOwnAsync();

        Assert.Equal(OwnCompanyOutcome.NotNamed, result.Outcome);
        Assert.Null(result.Company);

        // Die zugeordnete Firma steht in der Meldung als Vorschlag - ohne sie muesste jemand
        // raten, welche Firma gemeint sein koennte.
        Assert.Contains("Musterfirma GmbH", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kennt_die_Instanz_die_Route_nicht_greift_der_Rueckfall()
    {
        FakeClient client = new();
        client.OnGetThrow(TanssRoutes.OwnState, new TanssException("HTTP 404", 404));
        client.OnGetWithMeta(TanssRoutes.OwnCompanyEmployees, Fixtures.EmployeeListMeta);
        client.OnPut(TanssRoutes.Search, Fixtures.SearchResult);

        OwnCompanyResult result = await new CompanyRepository(client).FindOwnAsync();

        Assert.Equal(OwnCompanyOutcome.Found, result.Outcome);
        Assert.Equal(4711, result.Company?.Id);
        Assert.Contains(TanssRoutes.OwnCompanyEmployees, client.Calls, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Scheitern_beide_Wege_stehen_beide_Gruende_in_der_Meldung()
    {
        // Sonst sucht jemand am ERP-Recht, waehrend die Ursache am ersten Weg liegt. Eine
        // Meldung, die nur den letzten Fehlschlag nennt, schickt den Suchenden in die
        // falsche Richtung - und das kostet mehr Zeit als der ganze Fehler wert ist.
        FakeClient client = new();
        client.OnGetThrow(TanssRoutes.OwnState, new TanssException("403 auf ownState", 403));
        client.OnGetWithMetaThrow(TanssRoutes.OwnCompanyEmployees,
            new TanssException("403 auf das ERP-Praefix", 403));

        OwnCompanyResult result = await new CompanyRepository(client).FindOwnAsync();

        Assert.Equal(OwnCompanyOutcome.Undetermined, result.Outcome);
        Assert.Contains("403 auf ownState", result.Message, StringComparison.Ordinal);
        Assert.Contains("403 auf das ERP-Praefix", result.Message, StringComparison.Ordinal);
    }
}

/// <summary>Die Antwortformen, gegen die geprüft wird. Form gemessen, Werte erfunden.</summary>
internal static class Fixtures
{
    /// <summary>Der Regelfall: Einstellung und Zuordnung sind dieselbe Firma.</summary>
    public const string FullState = """
        {
          "loggedInUser": { "id": 7, "name": "Muster, Erika", "active": true },
          "userType": "TECHNICIAN",
          "defaultCompany": {
            "id": 4711, "displayId": "4711", "name": "Musterfirma GmbH",
            "street": "Musterweg 1", "postcode": "59757", "city": "Musterstadt",
            "country": "Deutschland", "inactive": false, "lockout": false
          },
          "companyAccess": [-1, 4711],
          "apiVersion": "10.10.0",
          "ownCompanyId": 4711,
          "unseenEvents": 3
        }
        """;

    /// <summary>Einstellung und Zuordnung laufen auseinander.</summary>
    public const string MismatchedState = """
        {
          "loggedInUser": { "id": 7, "name": "Muster, Erika", "active": true },
          "defaultCompany": {
            "id": 99999, "name": "Tochtergesellschaft GmbH",
            "postcode": "10115", "city": "Berlin"
          },
          "apiVersion": "10.10.0",
          "ownCompanyId": 4711
        }
        """;

    /// <summary>Die Instanz führt keine eigene Firma.</summary>
    public const string UnsetState = """
        {
          "loggedInUser": { "id": 7, "name": "Muster, Erika", "active": true },
          "defaultCompany": { "id": 4711, "name": "Musterfirma GmbH", "postcode": "59757" },
          "apiVersion": "10.10.0",
          "ownCompanyId": 0
        }
        """;

    /// <summary>Der ganze Umschlag der Mitarbeiterabfrage — der Rückfall liest nur den Kopf.</summary>
    public const string EmployeeListMeta = """
        {
          "meta": { "linkedEntities": { "companies": { "4711": { "name": "Musterfirma GmbH" } } } },
          "content": []
        }
        """;

    /// <summary>Ein Treffersatz der Firmensuche.</summary>
    public const string SearchResult = """
        {
          "companies": [
            { "id": 4711, "name": "Musterfirma GmbH", "postcode": "59757", "city": "Musterstadt" }
          ]
        }
        """;

    public static OwnState Parse(string json) =>
        JsonSerializer.Deserialize<OwnState>(json, TanssJson.Options)
        ?? throw new InvalidOperationException("Die Vorlage liess sich nicht lesen.");
}

/// <summary>
/// Ein Zugang, der Pfade auf vorbereitete Antworten abbildet.
/// </summary>
/// <remarks>
/// Keine Attrappenbibliothek: Was hier geprüft wird, ist die <b>Entscheidung</b> des Lagers
/// zwischen zwei Wegen, und dafür muss sichtbar sein, welcher Pfad tatsächlich gefragt wurde.
/// <see cref="Calls"/> hält das fest.
/// </remarks>
internal sealed class FakeClient : ITanssClient, ITanssMetaRead
{
    private readonly Dictionary<string, string> _get = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _getMeta = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _put = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TanssException> _throws = new(StringComparer.Ordinal);
    private readonly List<string> _calls = [];

    /// <summary>Die Pfade in der Reihenfolge, in der sie gefragt wurden.</summary>
    public IReadOnlyList<string> Calls => _calls;

    public void OnGet(string path, string json) => _get[path] = json;

    public void OnGetWithMeta(string path, string json) => _getMeta[path] = json;

    public void OnPut(string path, string json) => _put[path] = json;

    public void OnGetThrow(string path, TanssException exception) => _throws[path] = exception;

    public void OnGetWithMetaThrow(string path, TanssException exception) =>
        _throws[path] = exception;

    public Task<T?> GetAsync<T>(string path, IDictionary<string, string?>? query = null,
                                CancellationToken ct = default)
    {
        _calls.Add(path);
        return _throws.TryGetValue(path, out TanssException? boom)
            ? throw boom
            : Task.FromResult(Read<T>(_get, path));
    }

    public Task<(T? Content, IReadOnlyDictionary<string, object?> Meta)> GetWithMetaAsync<T>(
        string path, IDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        _calls.Add(path);
        if (_throws.TryGetValue(path, out TanssException? boom))
        {
            throw boom;
        }

        using JsonDocument document = JsonDocument.Parse(_getMeta[path]);
        Dictionary<string, object?> meta = new(StringComparer.OrdinalIgnoreCase);

        if (document.RootElement.TryGetProperty("meta", out JsonElement metaElement))
        {
            foreach (JsonProperty property in metaElement.EnumerateObject())
            {
                meta[property.Name] = JsonSerializer.Deserialize<object?>(
                    property.Value.GetRawText(), TanssJson.Options);
            }
        }

        T? content = document.RootElement.TryGetProperty("content", out JsonElement contentElement)
            ? JsonSerializer.Deserialize<T>(contentElement.GetRawText(), TanssJson.Options)
            : default;

        return Task.FromResult<(T?, IReadOnlyDictionary<string, object?>)>((content, meta));
    }

    public Task<T?> PutAsync<T>(string path, object? body = null,
                                IDictionary<string, string?>? query = null,
                                CancellationToken ct = default)
    {
        _calls.Add(path);
        return Task.FromResult(Read<T>(_put, path));
    }

    public Task<(T? Content, IReadOnlyDictionary<string, object?> Meta)> PutWithMetaAsync<T>(
        string path, object? body = null, IDictionary<string, string?>? query = null,
        CancellationToken ct = default) => throw new NotSupportedException();

    public Task<T?> PostAsync<T>(string path, object? body = null,
                                 IDictionary<string, string?>? query = null,
                                 CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<(T? Content, IReadOnlyDictionary<string, object?> Meta)> PostWithMetaAsync<T>(
        string path, object? body = null, IDictionary<string, string?>? query = null,
        CancellationToken ct = default) => throw new NotSupportedException();

    public Task DeleteAsync(string path, IDictionary<string, string?>? query = null,
                            CancellationToken ct = default) => throw new NotSupportedException();

    public void Dispose()
    {
    }

    private static T? Read<T>(Dictionary<string, string> source, string path) =>
        source.TryGetValue(path, out string? json)
            ? JsonSerializer.Deserialize<T>(json, TanssJson.Options)
            : default;
}
