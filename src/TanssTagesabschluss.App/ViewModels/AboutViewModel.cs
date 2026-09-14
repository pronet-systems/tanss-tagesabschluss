using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TanssTagesabschluss.App.Runtime;
using TanssTagesabschluss.Storage;

namespace TanssTagesabschluss.App.ViewModels;

/// <summary>
/// „Über“: Fassung, Ablageorte, Aktualisierung.
/// </summary>
/// <remarks>
/// <b>Die Ablageorte stehen hier, weil sie im Fehlerfall gebraucht werden.</b> Wer einer
/// Fehlermeldung nachgeht, sucht als Erstes die Konfigurationsdatei — und findet sie ohne
/// diese Seite nur, wenn er den Pfad auswendig weiß.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class AboutViewModel : ObservableObject
{
    private readonly UpdateService _updates;

    [ObservableProperty]
    private UpdateCheckState _state = UpdateCheckState.Unknown;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private AvailableUpdate? _update;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Baut das Ansichtsmodell.</summary>
    /// <param name="updates">Der Dienst, der nach neuen Fassungen sieht.</param>
    public AboutViewModel(UpdateService updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        _updates = updates;
    }

    /// <summary>Die laufende Fassung.</summary>
    public static string VersionText => Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        is { Length: > 0 } version
        ? version.Split('+')[0]
        : "unbekannt";

    /// <summary>Der Produktname.</summary>
    public static string Product => "TANSS Tagesabschluss";

    /// <summary>Der Hersteller.</summary>
    public static string Company => "ProNet Systems GmbH";

    /// <summary>Der Urheberrechtsvermerk.</summary>
    public static string Copyright =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
        ?? "Copyright (c) 2026 ProNet Systems GmbH";

    /// <summary>
    /// Die .NET-Laufzeit, mit der das Werkzeug gerade läuft.
    /// </summary>
    /// <remarks>
    /// Steht hier, weil es bei einer Rückfrage die zweite Frage ist — und weil dieses Werkzeug
    /// in sich geschlossen ausgeliefert wird: Was hier steht, ist die mitgelieferte Laufzeit
    /// und nicht die des Rechners.
    /// </remarks>
    public static string Runtime => System.Runtime.InteropServices.RuntimeInformation
        .FrameworkDescription;

    /// <summary>Das Betriebssystem.</summary>
    public static string OperatingSystemName =>
        System.Runtime.InteropServices.RuntimeInformation.OSDescription
        + " (" + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture + ")";

    /// <summary>Die Anschrift des Hauses.</summary>
    private const string Website = "https://www.pronet-systems.de";

    /// <summary>Öffnet die Anschrift im eingestellten Browser.</summary>
    [RelayCommand]
    private static void OpenWebsite() => Open(Website);

    /// <summary>Wo die Konfiguration liegt.</summary>
    public static string ConfigPath => StoragePaths.ConfigFile;

    /// <summary>Wo das verschlüsselte Token liegt.</summary>
    public static string TokenPath => StoragePaths.CredentialsFile;

    /// <summary>Wo der Feiertagszwischenspeicher liegt.</summary>
    public static string HolidayCachePath => StoragePaths.HolidayCacheDirectory;

    /// <summary>Die Veröffentlichungen auf GitHub.</summary>
    public static string ReleasesUrl =>
        $"https://github.com/{UpdateService.Repository}/releases";

    /// <summary>Steht eine neuere Fassung bereit?</summary>
    public bool HasUpdate => Update is not null;

    /// <summary>Sieht nach, ob es eine neuere Fassung gibt.</summary>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    public async Task CheckAsync()
    {
        IsBusy = true;
        State = UpdateCheckState.Checking;
        Message = "Sieht nach …";

        try
        {
            // Der Dienst fuehrt den Zustand selbst - er lebt laenger als diese Seite und
            // ueberdauert jeden Seitenwechsel. Gelesen wird deshalb von ihm und nicht aus
            // einem Rueckgabewert, den diese Seite sich merken muesste.
            State = await _updates.CheckAsync().ConfigureAwait(true);
            Update = _updates.Available;
            Message = _updates.Problem;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasUpdate));
        }
    }

    /// <summary>
    /// Öffnet die Seite der Veröffentlichung im Browser.
    /// </summary>
    /// <remarks>
    /// <b>Bewusst der Browser und kein Herunterladen im Fenster.</b> Wer eine neue Fassung
    /// einspielt, soll vorher lesen, was sich geändert hat — und das steht auf dieser Seite.
    /// Das Herunterladen und Einspielen selbst übernimmt der Dienst auf Wunsch trotzdem.
    /// </remarks>
    [RelayCommand]
    private void OpenReleases() => Open(Update?.ReleaseUrl.ToString() ?? ReleasesUrl);

    /// <summary>Öffnet den Ordner der Konfiguration im Explorer.</summary>
    [RelayCommand]
    private static void OpenConfigFolder() => Open(StoragePaths.ConfigDirectory);

    /// <summary>
    /// Öffnet eine Adresse oder einen Ordner mit dem eingestellten Programm.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Ein Rechner ohne eingetragenen Browser ist selten, aber er ist kein
    /// Grund, das Fenster zu schliessen.
    /// </remarks>
    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                      or FileNotFoundException
                                      or InvalidOperationException)
        {
            // Kein eingetragenes Programm fuer diese Art von Ziel. Der Pfad steht auf der
            // Seite und laesst sich von Hand kopieren.
        }
    }

    /// <summary>Der Zustand der Aktualisierungsprüfung als Satz.</summary>
    public string StateText => State switch
    {
        UpdateCheckState.Checking => "Sieht nach …",
        UpdateCheckState.UpToDate => string.Create(CultureInfo.CurrentCulture,
            $"Fassung {VersionText} ist die neueste."),
        UpdateCheckState.UpdateAvailable => Update is { } update
            ? string.Create(CultureInfo.CurrentCulture,
                $"Fassung {update.Version} steht bereit ({update.SizeText}).")
            : "Eine neuere Fassung steht bereit.",
        UpdateCheckState.Failed => Message ?? "Die Prüfung ist fehlgeschlagen.",
        _ => "Noch nicht nachgesehen.",
    };

    partial void OnStateChanged(UpdateCheckState value) => OnPropertyChanged(nameof(StateText));

    partial void OnUpdateChanged(AvailableUpdate? value)
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(StateText));
    }
}
