namespace TanssTagesabschluss.Workday.Holidays;

/// <summary>
/// Ein deutsches Bundesland — mit beiden Schlüsseln, die dieses Werkzeug braucht.
/// </summary>
/// <remarks>
/// <para><b>Warum zwei Schlüssel.</b> Die Postleitzahlenauskunft nennt das Land über den
/// <see cref="OfficialKey"/> (der amtliche Regionalschlüssel, <c>06</c> für Hessen), der
/// Feiertagsdienst über den <see cref="Code"/> (<c>HE</c>). Beide führen dasselbe Land, und
/// beide sind hier hinterlegt, damit die Umschlüsselung an genau einer Stelle steht.</para>
/// <para><b>Warum die Liste fest im Programm steht.</b> Sechzehn Bundesländer sind kein
/// Datenbestand, der sich ändert — das letzte Mal 1990. Sie über das Netz zu holen hiesse, den
/// Feiertagskalender von einem zweiten Dienst abhängig zu machen, ohne etwas zu gewinnen.</para>
/// </remarks>
/// <param name="Code">Das Kürzel, wie der Feiertagsdienst es erwartet — etwa <c>HE</c>.</param>
/// <param name="Name">Der ausgeschriebene Name — etwa <c>Hessen</c>.</param>
/// <param name="OfficialKey">Der amtliche Regionalschlüssel, zweistellig — etwa <c>06</c>.</param>
public sealed record FederalState(string Code, string Name, string OfficialKey)
{
    /// <summary>Baden-Württemberg.</summary>
    public static FederalState BadenWuerttemberg { get; } = new("BW", "Baden-Württemberg", "08");

    /// <summary>Bayern.</summary>
    public static FederalState Bayern { get; } = new("BY", "Bayern", "09");

    /// <summary>Berlin.</summary>
    public static FederalState Berlin { get; } = new("BE", "Berlin", "11");

    /// <summary>Brandenburg.</summary>
    public static FederalState Brandenburg { get; } = new("BB", "Brandenburg", "12");

    /// <summary>Bremen.</summary>
    public static FederalState Bremen { get; } = new("HB", "Bremen", "04");

    /// <summary>Hamburg.</summary>
    public static FederalState Hamburg { get; } = new("HH", "Hamburg", "02");

    /// <summary>Hessen.</summary>
    public static FederalState Hessen { get; } = new("HE", "Hessen", "06");

    /// <summary>Mecklenburg-Vorpommern.</summary>
    public static FederalState MecklenburgVorpommern { get; } =
        new("MV", "Mecklenburg-Vorpommern", "13");

    /// <summary>Niedersachsen.</summary>
    public static FederalState Niedersachsen { get; } = new("NI", "Niedersachsen", "03");

    /// <summary>Nordrhein-Westfalen.</summary>
    public static FederalState NordrheinWestfalen { get; } = new("NW", "Nordrhein-Westfalen", "05");

    /// <summary>Rheinland-Pfalz.</summary>
    public static FederalState RheinlandPfalz { get; } = new("RP", "Rheinland-Pfalz", "07");

    /// <summary>Saarland.</summary>
    public static FederalState Saarland { get; } = new("SL", "Saarland", "10");

    /// <summary>Sachsen.</summary>
    public static FederalState Sachsen { get; } = new("SN", "Sachsen", "14");

    /// <summary>Sachsen-Anhalt.</summary>
    public static FederalState SachsenAnhalt { get; } = new("ST", "Sachsen-Anhalt", "15");

    /// <summary>Schleswig-Holstein.</summary>
    public static FederalState SchleswigHolstein { get; } = new("SH", "Schleswig-Holstein", "01");

    /// <summary>Thüringen.</summary>
    public static FederalState Thueringen { get; } = new("TH", "Thüringen", "16");

    /// <summary>Alle sechzehn, alphabetisch nach Namen — die Reihenfolge für eine Auswahlliste.</summary>
    public static IReadOnlyList<FederalState> All { get; } =
    [
        BadenWuerttemberg, Bayern, Berlin, Brandenburg, Bremen, Hamburg, Hessen,
        MecklenburgVorpommern, Niedersachsen, NordrheinWestfalen, RheinlandPfalz, Saarland,
        Sachsen, SachsenAnhalt, SchleswigHolstein, Thueringen,
    ];

    /// <summary>Sucht ein Land über sein Kürzel, etwa <c>HE</c>.</summary>
    /// <param name="code">Das Kürzel; Groß- und Kleinschreibung gleichgültig.</param>
    /// <returns>Das Land, oder <see langword="null"/>.</returns>
    public static FederalState? ByCode(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(state =>
                string.Equals(state.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sucht ein Land über den amtlichen Regionalschlüssel, etwa <c>06</c>.
    /// </summary>
    /// <remarks>
    /// <b>Die führende Null gehört dazu und geht leicht verloren.</b> Wer den Schlüssel
    /// unterwegs durch eine Zahl schickt, hat aus <c>06</c> eine <c>6</c> gemacht; deshalb wird
    /// hier beides angenommen.
    /// </remarks>
    /// <param name="key">Der Schlüssel, mit oder ohne führende Null.</param>
    /// <returns>Das Land, oder <see langword="null"/>.</returns>
    public static FederalState? ByOfficialKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string normalized = key.Trim().PadLeft(2, '0');
        return All.FirstOrDefault(state =>
            string.Equals(state.OfficialKey, normalized, StringComparison.Ordinal));
    }

    /// <summary>Sucht ein Land über seinen ausgeschriebenen Namen.</summary>
    /// <param name="name">Der Name, etwa <c>Hessen</c>.</param>
    /// <returns>Das Land, oder <see langword="null"/>.</returns>
    public static FederalState? ByName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : All.FirstOrDefault(state =>
                string.Equals(state.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Der Name, für Anzeige und Protokoll.</summary>
    /// <returns>Der ausgeschriebene Name.</returns>
    public override string ToString() => Name;
}
