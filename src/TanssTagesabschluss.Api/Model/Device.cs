using System.Globalization;
using System.Text.Json.Serialization;

namespace TanssTagesabschluss.Api.Model;

/// <summary>
/// Die Zuordnungstypen von TANSS — die Zahlen hinter <c>linkTypeId</c>.
/// </summary>
/// <remarks>
/// <para><b>Woher diese Zahlen stammen.</b> Die OpenAPI-Beschreibung zu 10.10.0 führt die
/// Zuordnungstypen nur als <b>Namen</b> (<c>PC</c>, <c>COMPANY</c>, <c>PERIPHERY</c> …), während
/// eine Leistung sie als <b>Zahl</b> erwartet. Die Zuordnung von Name zu Zahl steht in keiner
/// Beschreibung. Sie ist aus <c>tns/core/linkType/TnsLinkTypes</c> in
/// <c>TanssApi-10.10.0.jar</c> ausgelesen — dort stehen sie als <c>static final int</c>.</para>
///
/// <para><b><c>SERVER</c> und <c>PC</c> tragen beide die 1.</b> Das ist kein Lesefehler,
/// sondern die Bauform: Ein Server ist in TANSS ein PC mit gesetztem Serverkennzeichen, und
/// beide hängen an derselben Tabelle. Für eine Leistung heißt das: Es gibt nur einen
/// Zuordnungstyp für beide.</para>
///
/// <para><b>Hier stehen nur die, die dieses Werkzeug wirklich benutzt.</b> Der Server kennt
/// neunundfünfzig; die übrigen sechsundfünfzig hier aufzuführen hiesse, Zahlen zu pflegen, die
/// niemand liest — und beim nächsten TANSS-Update stillschweigend falsch zu haben.</para>
/// </remarks>
public static class LinkType
{
    /// <summary>PC oder Server. In TANSS derselbe Typ.</summary>
    public const int Pc = 1;

    /// <summary>Firma.</summary>
    public const int Company = 2;

    /// <summary>Mitarbeiter.</summary>
    public const int Employee = 3;

    /// <summary>Peripherie — Drucker, Bildschirm, Netzwerkgerät.</summary>
    public const int Periphery = 4;

    /// <summary>Komponente — was in einem Gerät steckt.</summary>
    public const int Component = 5;

    /// <summary>Ticket.</summary>
    public const int Ticket = 11;

    /// <summary>Der deutsche Name eines Zuordnungstyps, für die Anzeige.</summary>
    /// <param name="linkTypeId">Die Kennung.</param>
    /// <returns>Der Name; bei einer unbekannten Kennung diese selbst.</returns>
    public static string NameOf(int linkTypeId) => linkTypeId switch
    {
        Pc => "PC / Server",
        Company => "Firma",
        Employee => "Mitarbeiter",
        Periphery => "Peripherie",
        Component => "Komponente",
        Ticket => "Ticket",
        _ => string.Create(CultureInfo.CurrentCulture, $"Zuordnung {linkTypeId}"),
    };
}

/// <summary>
/// Ein Gerät des Kunden — PC, Server, Peripherie oder Komponente.
/// </summary>
/// <remarks>
/// <para><b>Ein Modell für drei Routen.</b> <c>/api/v1/pcs</c>, <c>/api/v1/peripheries</c> und
/// <c>/api/v1/components</c> liefern Datensätze, die sich in vielem unterscheiden und in dem
/// übereinstimmen, worauf es hier ankommt: Kennung, Firma, ein Name und ein Aktivkennzeichen.
/// Drei eigene Modelle wären drei Stellen, an denen dasselbe steht.</para>
///
/// <para><b>Der Name ist nicht überall dasselbe Feld.</b> Ein PC trägt seinen Hostnamen unter
/// <c>name</c>, eine Peripherie führt daneben <c>description</c> und
/// <c>inventoryNumber</c> — und welches davon gefüllt ist, entscheidet der Kunde bei der
/// Pflege. <see cref="DisplayName"/> nimmt das erste, das etwas enthält, und fällt zuletzt auf
/// die Kennung zurück: Eine leere Zeile in einer Auswahlliste ist unbrauchbar.</para>
/// </remarks>
public sealed record Device
{
    /// <summary>Die Kennung. Wird beim Buchen zur <c>linkId</c>.</summary>
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>Die Firma, der das Gerät gehört.</summary>
    [JsonPropertyName("companyId")] public int CompanyId { get; init; }

    /// <summary>Der Name beziehungsweise Hostname.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>Die Beschreibung — bei Peripherie oft das einzige, was gepflegt ist.</summary>
    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>Die Inventarnummer.</summary>
    [JsonPropertyName("inventoryNumber")] public string? InventoryNumber { get; init; }

    /// <summary>Die Seriennummer.</summary>
    [JsonPropertyName("serialNumber")] public string? SerialNumber { get; init; }

    /// <summary>Ist das Gerät noch in Betrieb?</summary>
    [JsonPropertyName("active")] public bool Active { get; init; } = true;

    /// <summary>
    /// Der Zuordnungstyp, unter dem dieses Gerät gebucht wird.
    /// </summary>
    /// <remarks>
    /// Kommt <b>nicht</b> aus der Antwort, sondern aus der Route, über die das Gerät gelesen
    /// wurde — TANSS sagt einem PC nicht, dass er ein PC ist. Gesetzt wird er im Repository.
    /// </remarks>
    [JsonIgnore] public int LinkTypeId { get; init; }

    /// <summary>Der Name für eine Auswahlliste; niemals leer.</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            foreach (string? candidate in new[] { Name, Description, InventoryNumber, SerialNumber })
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate.Trim();
                }
            }

            return string.Create(CultureInfo.CurrentCulture,
                $"{LinkType.NameOf(LinkTypeId)} {Id}");
        }
    }

    /// <summary>Der Eintrag in einer Zeile — Art und Name.</summary>
    /// <returns>Etwa <c>PC / Server — SERVER-001</c>.</returns>
    public override string ToString() =>
        $"{LinkType.NameOf(LinkTypeId)} — {DisplayName}";
}

/// <summary>
/// Der Filterrumpf der Geräterouten.
/// </summary>
/// <remarks>
/// Alle drei Routen (<c>pcs</c>, <c>peripheries</c>, <c>components</c>) nehmen denselben Rumpf
/// entgegen — beschrieben für 10.10.0. <b>PUT, obwohl es liest</b>, wie überall dort, wo TANSS
/// den Filter im Rumpf erwartet.
/// </remarks>
public sealed record DeviceQuery
{
    /// <summary>Die Firma, deren Geräte gesucht werden.</summary>
    [JsonPropertyName("companyId")] public int CompanyId { get; init; }

    /// <summary>
    /// Ob Filialen mitgesucht werden.
    /// </summary>
    /// <remarks>
    /// <c>COMPANY_AND_BRANCHES</c> ist die richtige Vorgabe: Wer beim Kunden vor Ort war, war
    /// womöglich in einer Filiale, und das Gerät dort hängt nicht an der Zentrale.
    /// </remarks>
    [JsonPropertyName("branches")] public string Branches { get; init; } = "COMPANY_AND_BRANCHES";

    /// <summary>
    /// Ob ausgemusterte Geräte mitkommen.
    /// </summary>
    /// <remarks>
    /// <c>ACTIVE_ONLY</c>: Eine Leistung wird für ein Gerät erfasst, an dem gearbeitet wurde,
    /// und an einem ausgemusterten arbeitet niemand mehr. Die Auswahl bliebe sonst unübersichtlich.
    /// </remarks>
    [JsonPropertyName("active")] public string Active { get; init; } = "ACTIVE_ONLY";
}
