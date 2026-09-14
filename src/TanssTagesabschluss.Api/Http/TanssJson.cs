using System.Text.Json;
using System.Text.Json.Serialization;

namespace TanssTagesabschluss.Api.Http;

/// <summary>Die eine JSON-Einstellung, mit der dieses Werkzeug mit TANSS spricht.</summary>
/// <remarks>
/// <para><b>Nichts wird beim Schreiben weggelassen.</b> <see cref="JsonIgnoreCondition.Never"/>
/// ist hier kein Standardwert aus Bequemlichkeit, sondern Absicht: TANSS unterscheidet beim
/// Anlegen einer Leistung zwischen „Feld fehlt“ und „Feld ist 0 oder leer“. Ein weggelassenes
/// <c>ticketId</c> nimmt serverseitig einen anderen Zweig als <c>ticketId: 0</c>, und bei den
/// Abrechnungsfeldern (<c>hourlyRate</c>, <c>accountingTypeId</c>, die Fahrtkennzeichen) ist
/// ein fehlendes Feld nicht dasselbe wie eine gesetzte Null — dort entscheidet sich, ob die
/// Leistung auf der Rechnung des Kunden auftaucht.</para>
/// <para>Beim Lesen ist die Groß- und Kleinschreibung gleichgültig, weil TANSS je nach Route
/// unterschiedlich schreibt und ein stillschweigend leer gebliebenes Feld schlimmer ist als
/// eine großzügige Zuordnung.</para>
/// </remarks>
public static class TanssJson
{
    /// <summary>Die gemeinsamen Einstellungen für Lesen und Schreiben.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>Erzeugt einen unabhängigen Satz Einstellungen, etwa für Tests.</summary>
    public static JsonSerializerOptions Create() => new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Serialisiert einen Rumpf über seinen <b>Laufzeittyp</b>.
    /// </summary>
    /// <remarks>
    /// Der Umweg über <see cref="object.GetType"/> ist zwingend: <c>Serialize&lt;object&gt;</c>
    /// schreibt nur die Eigenschaften des statischen Typs und liefert bei einem als
    /// <c>object</c> übergebenen Datensatz ein leeres <c>{}</c> — eine Leistung ohne Inhalt,
    /// die TANSS anstandslos mit 201 quittieren würde.
    /// </remarks>
    public static string Serialize(object body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return JsonSerializer.Serialize(body, body.GetType(), Options);
    }
}
