using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TanssTagesabschluss.App.Runtime;

namespace TanssTagesabschluss.App.ViewModels;

/// <summary>
/// Die Fußzeile des Hauptfensters: Zustand, Instanz, Aktualisierungshinweis.
/// </summary>
/// <remarks>
/// <b>Was hier steht, steht hier, weil man es im Blick behalten will, ohne eine Seite zu
/// öffnen.</b> Alles andere gehört auf eine Seite. Die Zustandsplakette ist anklickbar: Sie
/// sagte sonst „mit Hinweis“, und es gäbe nirgends einen Ort, an dem stünde, welcher — ein
/// Hinweis, den niemand nachlesen kann, beunruhigt nur.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IRuntimeContext _context;

    [ObservableProperty]
    private AppStatus _status;

    [ObservableProperty]
    private bool _isWarningsOpen;

    [ObservableProperty]
    private AvailableUpdate? _update;

    /// <summary>Baut das Ansichtsmodell.</summary>
    /// <param name="context">Der Zugang zu Zustand und Zusammenbau.</param>
    public ShellViewModel(IRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _status = context.Status;

        context.StatusChanged += (_, status) =>
        {
            Status = status;
            Warnings.Clear();

            foreach (AppWarning warning in status.Warnings)
            {
                Warnings.Add(warning);
            }

            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(BaseUrlText));
            OnPropertyChanged(nameof(EmployeeText));
            OnPropertyChanged(nameof(HasWarnings));
            OnPropertyChanged(nameof(NoWarningText));
        };
    }

    /// <summary>Die Hinweise, die die Plakette aufklappt.</summary>
    public ObservableCollection<AppWarning> Warnings { get; } = [];

    /// <summary>Der Zustand in einem Wort.</summary>
    public string StateText => Status.State switch
    {
        AppState.Connected => Status.HasWarnings ? "verbunden, mit Hinweis" : "verbunden",
        AppState.Degraded => "gestört",
        _ => "nicht eingerichtet",
    };

    /// <summary>Die Basisadresse der Instanz.</summary>
    public string BaseUrlText => _context.Composition?.Config.Tanss.BaseUrl ?? Status.Message;

    /// <summary>Der Mitarbeiter, dessen Zeiten geprüft werden.</summary>
    /// <remarks>
    /// <b>Steht ausdrücklich in der Fußzeile</b> und nicht nur in den Einstellungen: An dieser
    /// Kennung hängt alles. Wer sie beim Einrichten vertippt hat, sieht klaglos die Lücken
    /// eines Kollegen — und das fällt nur auf, wenn die Zahl im Blick ist.
    /// </remarks>
    public string EmployeeText => _context.Composition?.Config.Tanss.EmployeeId is { } id and > 0
        ? string.Create(CultureInfo.CurrentCulture, $"Mitarbeiter {id}")
        : string.Empty;

    /// <summary>Gibt es Hinweise?</summary>
    public bool HasWarnings => Status.HasWarnings;

    /// <summary>Der Satz, der ohne Hinweis in der Auskunft steht.</summary>
    /// <remarks>
    /// Damit der Klick auf die Plakette nie ins Leere geht. Eine Auskunft, die sich öffnet und
    /// leer ist, sieht nach einem Fehler aus.
    /// </remarks>
    public string NoWarningText => Status.State == AppState.Connected
        ? "Nichts zu beachten. Die Verbindung zu TANSS steht."
        : Status.Message;

    /// <summary>Steht eine neuere Fassung bereit?</summary>
    public bool UpdateAvailable => Update is not null;

    /// <summary>Der Hinweis auf die neue Fassung.</summary>
    public string UpdateText => Update is { } update
        ? string.Create(CultureInfo.CurrentCulture, $"Fassung {update.Version} verfügbar")
        : string.Empty;

    /// <summary>Klappt die Auskunft zur Zustandsplakette auf und zu.</summary>
    [RelayCommand]
    private void ToggleWarnings() => IsWarningsOpen = !IsWarningsOpen;

    partial void OnUpdateChanged(AvailableUpdate? value)
    {
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(UpdateText));
    }
}
