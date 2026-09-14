using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TanssTagesabschluss.Workday.Holidays;

/// <summary>
/// Feiertage von <c>feiertage-api.de</c>.
/// </summary>
/// <remarks>
/// <para><b>Warum dieser Dienst.</b> Er ist frei zugänglich, verlangt keinen Schlüssel und
/// beantwortet genau die Frage, um die es geht: <c>?jahr=2026&amp;nur_land=HE</c> liefert die
/// Feiertage eines Jahres <b>für ein Bundesland</b>. Gemessen am 14.09.2026 antwortete er auf
/// diese Anfrage mit den zehn hessischen Feiertagen des Jahres 2026.</para>
///
/// <para><b>Die Antwort ist ein Objekt, keine Liste.</b> Der Name des Feiertags ist der
/// Schlüssel: <c>{"Neujahrstag":{"datum":"2026-01-01","hinweis":""}}</c>. Ein Modell mit festen
/// Feldern wäre hier unbrauchbar — die Schlüssel sind die Daten.</para>
///
/// <para><b>Das Feld <c>hinweis</c> ist nicht Zierrat.</b> Es trägt die Einschränkung, dass ein
/// Tag nur in einem Teil des Landes gesetzlicher Feiertag ist. Siehe <see cref="Holiday"/>;
/// ein nichtleerer Hinweis ergibt hier einen bedingten Feiertag.</para>
///
/// <para><b>Dieser Dienst erfährt nichts über den Kunden.</b> Hinaus geht ein Jahr und ein
/// Länderkürzel — keine Firma, kein Name, keine Kennung. Das ist der Grund, warum die Abfrage
/// überhaupt ohne Rückfrage geschehen darf.</para>
/// </remarks>
public sealed class FeiertageApiProvider : IHolidayProvider
{
    /// <summary>Die Adresse des Dienstes, ohne Abfrageteil.</summary>
    public const string DefaultEndpoint = "https://feiertage-api.de/api/";

    private readonly HttpClient _http;
    private readonly string _endpoint;

    /// <summary>Baut den Zugang.</summary>
    /// <param name="http">
    /// Die Verbindungsschicht. Sie gehört dem Aufrufer und wird hier nicht freigegeben — dieser
    /// Dienst ist einer von mehreren, die sich einen <see cref="HttpClient"/> teilen sollen.
    /// </param>
    /// <param name="endpoint">Abweichende Adresse; ohne Angabe <see cref="DefaultEndpoint"/>.</param>
    public FeiertageApiProvider(HttpClient http, string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint;
    }

    /// <inheritdoc />
    public async Task<HolidaySet> GetAsync(int year, FederalState state,
                                           CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (year is < 1900 or > 2200)
        {
            // Der Dienst antwortet ausserhalb dieses Bereichs unvorhersehbar; ein sinnloses
            // Jahr kommt hier ohnehin nur aus einem Programmfehler.
            return HolidaySet.Unknown(string.Create(CultureInfo.CurrentCulture,
                $"Das Jahr {year} liegt ausserhalb dessen, wofür Feiertage abgefragt werden."));
        }

        string address = string.Create(CultureInfo.InvariantCulture,
            $"{_endpoint}?jahr={year}&nur_land={Uri.EscapeDataString(state.Code)}");

        Dictionary<string, FeiertagEntry>? payload;
        try
        {
            payload = await _http
                .GetFromJsonAsync<Dictionary<string, FeiertagEntry>>(address, Json, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Zeitgrenze des HttpClient - kein Abbruch durch den Aufrufer.
            return HolidaySet.Unknown(
                "Der Feiertagsdienst hat nicht rechtzeitig geantwortet. Ohne ihn werden "
                + "Feiertage wie gewöhnliche Arbeitstage behandelt.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return HolidaySet.Unknown(
                "Die Feiertage liessen sich nicht abrufen (" + ex.Message + "). Ohne sie werden "
                + "Feiertage wie gewöhnliche Arbeitstage behandelt — an einem Feiertag ohne "
                + "Zeiterfassung erscheint dann ein Hinweis, der keiner sein müsste.");
        }

        if (payload is null or { Count: 0 })
        {
            return HolidaySet.Unknown(string.Create(CultureInfo.CurrentCulture,
                $"Der Feiertagsdienst hat zu {state.Name} und {year} nichts geliefert."));
        }

        List<Holiday> holidays = [];

        foreach (KeyValuePair<string, FeiertagEntry> entry in payload)
        {
            if (!DateOnly.TryParseExact(entry.Value.Datum, "yyyy-MM-dd",
                                        CultureInfo.InvariantCulture, DateTimeStyles.None,
                                        out DateOnly date))
            {
                // Ein unlesbares Datum kostet diesen einen Feiertag und nicht den Kalender.
                continue;
            }

            string? hint = string.IsNullOrWhiteSpace(entry.Value.Hinweis)
                ? null
                : entry.Value.Hinweis.Trim();

            holidays.Add(new Holiday(date, entry.Key, hint is not null, hint));
        }

        return holidays.Count == 0
            ? HolidaySet.Unknown(
                string.Create(CultureInfo.CurrentCulture,
                    $"Der Feiertagsdienst hat zu {state.Name} und {year} nur unlesbare Angaben geliefert."))
            : HolidaySet.Known(holidays);
    }

    /// <summary>
    /// Die Leseeinstellungen.
    /// </summary>
    /// <remarks>
    /// Gleichgültig gegen Groß- und Kleinschreibung, damit eine geänderte Schreibweise der
    /// Felder den Kalender nicht leert. Eigene Einstellungen statt der voreingestellten: Dieser
    /// Dienst hat mit TANSS nichts zu tun und soll nicht dessen Regeln erben.
    /// </remarks>
    private static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Ein Eintrag der Antwort. Der Name des Feiertags ist der Schlüssel darüber.</summary>
    private sealed record FeiertagEntry
    {
        /// <summary>Das Datum als <c>YYYY-MM-DD</c>.</summary>
        [JsonPropertyName("datum")] public string Datum { get; init; } = string.Empty;

        /// <summary>
        /// Die Einschränkung, wenn der Tag nur in einem Teil des Landes gilt; sonst leer.
        /// </summary>
        [JsonPropertyName("hinweis")] public string Hinweis { get; init; } = string.Empty;
    }
}
