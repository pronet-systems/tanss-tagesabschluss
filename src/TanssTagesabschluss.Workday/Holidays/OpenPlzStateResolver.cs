using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TanssTagesabschluss.Workday.Holidays;

/// <summary>
/// Bestimmt das Bundesland über <c>openplzapi.org</c>.
/// </summary>
/// <remarks>
/// <para><b>Warum dieser Dienst.</b> Er ist frei zugänglich, verlangt keinen Schlüssel und
/// beantwortet genau die gestellte Frage: <c>/de/Localities?postalCode=64380</c> liefert die
/// Orte einer Postleitzahl, jeden mit seinem <c>federalState</c>. Gemessen am 14.09.2026 ergab
/// <c>64380</c> den Ort Roßdorf mit <c>{"key":"06","name":"Hessen"}</c>.</para>
///
/// <para><b>Mehrere Orte je Postleitzahl sind der Normalfall</b> — gemessen hat <c>49824</c>
/// drei. Solange sie alle im selben Land liegen, ist das belanglos. Liegen sie in
/// verschiedenen, ist die Postleitzahl für diese Frage <b>nicht geeignet</b>, und das wird
/// gesagt statt geraten: siehe <see cref="FederalStateLookup.Ambiguous"/>.</para>
///
/// <para><b>Was hinausgeht, ist eine Postleitzahl.</b> Kein Firmenname, keine Anschrift, keine
/// Kennung — und die Abfrage geschieht einmal bei der Einrichtung, nicht laufend. Das Ergebnis
/// wird gespeichert; siehe die Einstellungen.</para>
/// </remarks>
public sealed class OpenPlzStateResolver : IFederalStateResolver
{
    /// <summary>Die Adresse des Dienstes, ohne Abfrageteil.</summary>
    public const string DefaultEndpoint = "https://openplzapi.org/de/Localities";

    private readonly HttpClient _http;
    private readonly string _endpoint;

    /// <summary>Baut den Zugang.</summary>
    /// <param name="http">Die Verbindungsschicht; sie gehört dem Aufrufer.</param>
    /// <param name="endpoint">Abweichende Adresse; ohne Angabe <see cref="DefaultEndpoint"/>.</param>
    public OpenPlzStateResolver(HttpClient http, string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint;
    }

    /// <inheritdoc />
    public async Task<FederalStateLookup> ResolveAsync(string postalCode,
                                                       CancellationToken ct = default)
    {
        string code = (postalCode ?? string.Empty).Trim();

        if (code.Length != 5 || !code.All(char.IsAsciiDigit))
        {
            // Gar nicht erst fragen. Eine nichtdeutsche oder unvollstaendige Postleitzahl
            // ergaebe eine leere Antwort, und die saehe aus wie ein Ausfall des Dienstes.
            return new FederalStateLookup(null, Ambiguous: false,
                $"„{code}“ ist keine deutsche Postleitzahl. Das Bundesland ist deshalb in den "
                + "Einstellungen von Hand zu wählen.");
        }

        string address = string.Create(CultureInfo.InvariantCulture,
            $"{_endpoint}?postalCode={code}");

        List<Locality>? localities;
        try
        {
            localities = await _http.GetFromJsonAsync<List<Locality>>(address, Json, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new FederalStateLookup(null, Ambiguous: false,
                "Die Postleitzahlenauskunft hat nicht rechtzeitig geantwortet. Das Bundesland "
                + "lässt sich in den Einstellungen von Hand wählen.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return new FederalStateLookup(null, Ambiguous: false,
                "Die Postleitzahlenauskunft ist nicht erreichbar (" + ex.Message + "). Das "
                + "Bundesland lässt sich in den Einstellungen von Hand wählen.");
        }

        if (localities is null or { Count: 0 })
        {
            return new FederalStateLookup(null, Ambiguous: false,
                $"Zur Postleitzahl {code} ist kein Ort bekannt. Das Bundesland ist in den "
                + "Einstellungen von Hand zu wählen.");
        }

        List<FederalState> states = [.. localities
            .Select(locality => FederalState.ByOfficialKey(locality.FederalState?.Key)
                                ?? FederalState.ByName(locality.FederalState?.Name))
            .Where(state => state is not null)
            .Select(state => state!)
            .Distinct()];

        if (states.Count == 0)
        {
            return new FederalStateLookup(null, Ambiguous: false,
                $"Zur Postleitzahl {code} nennt die Auskunft kein Bundesland, das sich zuordnen "
                + "lässt. Es ist in den Einstellungen von Hand zu wählen.");
        }

        if (states.Count > 1)
        {
            // Nicht die erste nehmen: An dieser Postleitzahl haengen die Feiertage, und die
            // unterscheiden sich zwischen zwei Laendern gerade dann, wenn es darauf ankommt.
            string names = string.Join(", ", states.Select(state => state.Name));
            return new FederalStateLookup(states[0], Ambiguous: true,
                $"Unter der Postleitzahl {code} liegen Orte in mehreren Bundesländern ({names}). "
                + "Welches gilt, ist in den Einstellungen zu wählen — davon hängen die Feiertage ab.");
        }

        FederalState only = states[0];
        return new FederalStateLookup(only, Ambiguous: false,
            $"Postleitzahl {code} liegt in {only.Name}.");
    }

    /// <summary>Die Leseeinstellungen; gleichgültig gegen Groß- und Kleinschreibung.</summary>
    private static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Ein Ort, wie die Auskunft ihn liefert.</summary>
    private sealed record Locality
    {
        /// <summary>Die Postleitzahl.</summary>
        [JsonPropertyName("postalCode")] public string? PostalCode { get; init; }

        /// <summary>Der Ortsname.</summary>
        [JsonPropertyName("name")] public string? Name { get; init; }

        /// <summary>Das Bundesland.</summary>
        [JsonPropertyName("federalState")] public FederalStateEntry? FederalState { get; init; }
    }

    /// <summary>Das Bundesland eines Ortes.</summary>
    private sealed record FederalStateEntry
    {
        /// <summary>Der amtliche Regionalschlüssel, zweistellig mit führender Null.</summary>
        [JsonPropertyName("key")] public string? Key { get; init; }

        /// <summary>Der ausgeschriebene Name.</summary>
        [JsonPropertyName("name")] public string? Name { get; init; }
    }
}
