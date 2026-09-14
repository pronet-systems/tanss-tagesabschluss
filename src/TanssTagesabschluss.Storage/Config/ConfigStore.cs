using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;

// ConfigException und ConfigValidationException liegen in TanssTagesabschluss.Storage
// (StorageErrors.cs) und nicht hier: Zwei gleichnamige Typen in Ober- und Unternamensraum
// sind eine Falle - welcher gemeint ist, entscheidet der Namensraum des Aufrufers, und der
// Uebersetzer sagt dazu nichts.

namespace TanssTagesabschluss.Storage.Config;

/// <summary>Findet, lädt und schreibt <c>config.json</c>.</summary>
/// <remarks>
/// Als Schnittstelle geführt, damit Oberfläche und Tests gegen dieselbe Zusage arbeiten — und
/// damit eine spätere Ablage in der Registrierung oder per Gruppenrichtlinie nichts oberhalb
/// dieser Schicht ändert.
/// </remarks>
public interface IConfigStore
{
    /// <summary>Pfad der Konfigurationsdatei.</summary>
    string Path { get; }

    /// <summary>Liegt bereits eine Konfiguration vor?</summary>
    /// <returns><c>true</c>, wenn die Datei existiert.</returns>
    bool Exists();

    /// <summary>Lädt und prüft die Konfiguration.</summary>
    /// <returns>Die geprüfte Konfiguration.</returns>
    /// <exception cref="ConfigException">Datei fehlt oder ist kein gültiges JSON.</exception>
    /// <exception cref="ConfigValidationException">Die Datei verletzt mindestens eine Regel.</exception>
    AppConfig Load();

    /// <summary>
    /// Prüft und schreibt die Konfiguration atomar.
    /// </summary>
    /// <remarks>
    /// Geprüft wird <b>vor</b> dem Schreiben. Eine fehlerhafte Konfiguration abzulegen hiesse,
    /// das Werkzeug beim nächsten Start mit einer Datei zu starten, die es selbst geschrieben
    /// hat und nicht laden kann.
    /// </remarks>
    /// <param name="config">Die zu schreibende Konfiguration.</param>
    /// <exception cref="ConfigValidationException">Sie verletzt mindestens eine Regel.</exception>
    void Save(AppConfig config);
}

/// <summary>Die Ablage von <c>config.json</c> im Profil des Benutzers.</summary>
[SupportedOSPlatform("windows")]
public sealed class ConfigStore : IConfigStore
{
    /// <summary>Legt die Ablage an einem beliebigen Pfad an — für Tests.</summary>
    /// <param name="path">Der Pfad der Konfigurationsdatei.</param>
    public ConfigStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
    }

    /// <summary>Legt die Ablage am vorgesehenen Ort im Profil an.</summary>
    /// <returns>Die Ablage.</returns>
    public static ConfigStore Default() => new(StoragePaths.ConfigFile);

    /// <inheritdoc />
    public string Path { get; }

    /// <inheritdoc />
    public bool Exists() => File.Exists(Path);

    /// <inheritdoc />
    public AppConfig Load()
    {
        if (!File.Exists(Path))
        {
            throw new ConfigException(
                $"Es liegt keine Konfiguration unter \"{Path}\" vor. Das Werkzeug muss einmalig "
                + "eingerichtet werden: Basisadresse der TANSS-Instanz, Anmeldung, und danach "
                + "prägt es sich selbst ein Arbeitstoken.");
        }

        string text;
        try
        {
            text = File.ReadAllText(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigException(
                $"Die Konfiguration unter \"{Path}\" liess sich nicht lesen: {ex.Message} Zu "
                + "prüfen sind die Zugriffsrechte auf das Profilverzeichnis.", ex);
        }

        AppConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(text, ConfigJson.Options);
        }
        catch (JsonException ex)
        {
            // Die Zeilennummer ist das Wertvollste an dieser Meldung: Die Datei wird von Hand
            // gepflegt, und ein fehlendes Komma ist der haeufigste Fehler ueberhaupt.
            string where = ex.LineNumber is { } line
                ? string.Create(CultureInfo.CurrentCulture, $" (Zeile {line + 1})")
                : string.Empty;

            throw new ConfigException(
                $"Die Konfiguration unter \"{Path}\" ist kein gültiges JSON{where}: {ex.Message} "
                + "Häufigste Ursachen sind ein fehlendes Komma, eine überzählige schliessende "
                + "Klammer oder ein Feldname, den diese Programmfassung nicht kennt.", ex);
        }

        if (config is null)
        {
            throw new ConfigException(
                $"Die Konfiguration unter \"{Path}\" ist leer. Sie muss mindestens den Abschnitt "
                + "\"tanss\" mit Basisadresse und Mitarbeiter-ID enthalten.");
        }

        if (config.Version > AppConfig.CurrentVersion)
        {
            throw new ConfigException(
                string.Create(CultureInfo.CurrentCulture,
                    $"Die Konfiguration hat Stand {config.Version}, diese Programmfassung kennt höchstens {AppConfig.CurrentVersion}.")
                + " Sie stammt vermutlich von einer neueren Fassung des Werkzeugs — dann ist "
                + "das Programm zu aktualisieren, nicht die Datei zu ändern.");
        }

        config.Validate(Path);
        return config;
    }

    /// <inheritdoc />
    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // ERST pruefen, DANN schreiben. Umgekehrt laege eine Datei auf der Platte, die das
        // Werkzeug selbst geschrieben hat und beim naechsten Start nicht laden kann.
        config.Validate(Path);

        string text = JsonSerializer.Serialize(config, ConfigJson.Options);
        AtomicFile.WriteText(Path, text);
    }
}
