using System.Runtime.Versioning;

namespace TanssTagesabschluss.Storage;

/// <summary>
/// Die festen Ablageorte des Werkzeugs auf dem Rechner des Technikers.
/// </summary>
/// <remarks>
/// <para>Die Trennung ist beabsichtigt: Die Konfiguration liegt im <b>roaming</b>-Profil
/// (<c>%APPDATA%</c>), weil sie den Benutzer auf einen anderen Rechner begleiten darf. Das
/// Token liegt im <b>lokalen</b> Profil (<c>%LOCALAPPDATA%</c>): Das DPAPI-Geheimnis ist an
/// Benutzer <i>und</i> Rechner gebunden und ergäbe mitgereist nur eine unentschlüsselbare
/// Datei.</para>
///
/// <para><b>Der Feiertagszwischenspeicher liegt ebenfalls lokal</b>, obwohl er kein Geheimnis
/// enthält. Er ist eine Bequemlichkeit und kein Bestand: Wandert er mit, schleppt der Benutzer
/// den Kalender eines anderen Bundeslands mit sich herum, falls er den Arbeitsplatz wechselt.
/// Wegwerfen darf man ihn jederzeit — er füllt sich beim nächsten Start von selbst.</para>
///
/// <para><b>Eigene Ordner neben dem Schwesterprojekt.</b> Beide Werkzeuge stammen aus demselben
/// Haus und sprechen mit derselben Instanz, aber sie teilen sich nichts: Wer den Log-Watcher
/// entfernt, soll dem Tagesabschluss nicht das Token nehmen. Auch die zusätzliche Entropie der
/// Tokendatei trägt deshalb einen eigenen Produktnamen.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class StoragePaths
{
    /// <summary>Herstellerordner. Teil des Pfads, nicht des Dateinamens.</summary>
    public const string VendorFolder = "ProNet Systems";

    /// <summary>Produktordner.</summary>
    public const string ProductFolder = "TanssTagesabschluss";

    /// <summary>Dateiname der Konfiguration.</summary>
    public const string ConfigFileName = "config.json";

    /// <summary>Dateiname des verschlüsselten Tokens.</summary>
    public const string CredentialsFileName = "credentials.dat";

    /// <summary>Dateiname des Proxy-Kennworts.</summary>
    public const string ProxyPasswordFileName = "proxy.dat";

    /// <summary>Dateiname des Anbieterschlüssels für die Sprachmodell-Unterstützung.</summary>
    public const string AiKeyFileName = "ai.dat";

    /// <summary>Ordnername des Feiertagszwischenspeichers.</summary>
    public const string HolidayCacheFolderName = "feiertage";

    /// <summary><c>%APPDATA%\ProNet Systems\TanssTagesabschluss</c>.</summary>
    public static string ConfigDirectory => Combine(Environment.SpecialFolder.ApplicationData);

    /// <summary><c>%LOCALAPPDATA%\ProNet Systems\TanssTagesabschluss</c>.</summary>
    public static string StateDirectory => Combine(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Vollständiger Pfad der Konfigurationsdatei.</summary>
    public static string ConfigFile => Path.Combine(ConfigDirectory, ConfigFileName);

    /// <summary>Vollständiger Pfad der Token-Datei.</summary>
    public static string CredentialsFile => Path.Combine(StateDirectory, CredentialsFileName);

    /// <summary>
    /// Das Kennwort für den Proxy, DPAPI-verschlüsselt.
    /// </summary>
    /// <remarks>
    /// In <c>config.json</c> steht dafür nur ein Verweis. Das Geheimnis selbst liegt hier, an
    /// den Windows-Benutzer gebunden, und geht beim Weitergeben der Konfigurationsdatei nicht
    /// mit.
    /// </remarks>
    public static string ProxyPasswordFile => Path.Combine(StateDirectory, ProxyPasswordFileName);

    /// <summary>
    /// Der Schlüssel für den Sprachmodell-Anbieter, DPAPI-verschlüsselt.
    /// </summary>
    /// <remarks>
    /// Getrennt von <see cref="CredentialsFile"/>, obwohl beides Geheimnisse sind: Das eine ist
    /// der Zugang zur Instanz des Kunden, das andere der zu einem fremden Anbieter. Wer den
    /// einen zurückzieht, will selten den anderen mit zurückziehen — und zwei Dateien lassen
    /// sich einzeln löschen.
    /// </remarks>
    public static string AiKeyFile => Path.Combine(StateDirectory, AiKeyFileName);

    /// <summary>Der Ordner des Feiertagszwischenspeichers.</summary>
    public static string HolidayCacheDirectory =>
        Path.Combine(StateDirectory, HolidayCacheFolderName);

    private static string Combine(Environment.SpecialFolder root) =>
        Path.Combine(Environment.GetFolderPath(root, Environment.SpecialFolderOption.Create),
                     VendorFolder, ProductFolder);
}
