using System.ComponentModel;
using System.Runtime.Versioning;
using System.Windows;
using TanssTagesabschluss.App.Runtime;
using TanssTagesabschluss.App.Services;
using TanssTagesabschluss.App.ViewModels;
using TanssTagesabschluss.App.Views.Pages;
using TanssTagesabschluss.Storage.Config;

namespace TanssTagesabschluss.App.Views;

/// <summary>
/// Das Hauptfenster: Navigation, Fußzeile, und die Seiten dazwischen.
/// </summary>
/// <remarks>
/// <para><b>Die Ansichtsmodelle der Seiten entstehen hier und leben so lange wie das
/// Fenster.</b> Würde jede Seite ihres beim Aufschlagen bauen, ginge beim Seitenwechsel der
/// gewählte Tag verloren — und der Ladevorgang begänne von vorn. Der Navigationsbereich legt
/// die Seiten ohnehin nicht ab, wohl aber deren <c>DataContext</c>.</para>
///
/// <para><b>Das Schliessen beendet die Anwendung nicht.</b> Die Erinnerungen sind der Zweck
/// dieses Werkzeugs, und sie laufen im Hintergrund. Beendet wird über das Symbol im
/// Infobereich — dort steht es auch.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppHost _host;
    private readonly UpdateService _updates;

    /// <summary>Baut das Fenster.</summary>
    /// <param name="host">Der Wirt.</param>
    public MainWindow(AppHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        _updates = new UpdateService();

        InitializeComponent();

        Shell = new ShellViewModel(host);
        DataContext = Shell;

        Day = new DayViewModel(host);
        History = new HistoryViewModel(host);
        Settings = new SettingsViewModel(host, ConfigStore.Default(), host.Reload);
        About = new AboutViewModel(_updates);

        // Vom Überblick in den Tag: Der Überblick sagt, welcher Tag einen Blick verdient,
        // und der Tag ist der Ort, an dem etwas geschieht. Ohne diesen Weg müsste der
        // Techniker das Datum abtippen.
        History.DayChosen += (_, day) =>
        {
            Day.SelectedDate = day.ToDateTime(TimeOnly.MinValue);
            Navigation.Navigate(typeof(DayPage));
        };

        // Der Aktualisierungshinweis in der Fusszeile haengt am Dienst und nicht an der Seite
        // "Ueber": Diese Seite oeffnet im Betrieb niemand, und eine Aktualisierung, von der
        // man nur erfaehrt, wenn man ohnehin nachsieht, ist keine Benachrichtigung.
        _updates.CheckCompleted += (_, _) =>
            App.OnUi(() => Shell.Update = _updates.Available);

        Loaded += (_, _) => Navigation.Navigate(typeof(DayPage));
    }

    /// <summary>Die Fußzeile.</summary>
    public ShellViewModel Shell { get; }

    /// <summary>Die Tagesansicht.</summary>
    public DayViewModel Day { get; }

    /// <summary>Der Überblick.</summary>
    public HistoryViewModel History { get; }

    /// <summary>Die Einstellungen.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Die Seite „Über“.</summary>
    public AboutViewModel About { get; }

    /// <summary>
    /// Zeigt das Fenster wieder an, auch wenn es zugeklappt oder verborgen war.
    /// </summary>
    /// <remarks>
    /// Die drei Schritte sind alle nötig: <see cref="Window.Show"/> holt ein verborgenes
    /// Fenster zurück, <see cref="WindowState"/> ein zum Symbol verkleinertes, und
    /// <see cref="Window.Activate"/> bringt es nach vorn. Wer einen davon auslässt, bekommt in
    /// genau einem der drei Fälle nichts zu sehen.
    /// </remarks>
    public void Reveal()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>
    /// Das Schliessen verbirgt das Fenster, statt die Anwendung zu beenden.
    /// </summary>
    /// <remarks>
    /// <b>Sonst hört die Erinnerung mit dem ersten Schliessen auf zu erinnern.</b> Beendet wird
    /// über das Symbol im Infobereich; dort steht der Eintrag „Beenden“.
    /// </remarks>
    /// <inheritdoc />
    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (!_isShuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _updates.Dispose();
        base.OnClosing(e);
    }

    /// <summary>Beendet die Anwendung wirklich — der Weg aus dem Infobereich.</summary>
    public void ShutDown()
    {
        _isShuttingDown = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private bool _isShuttingDown;

    /// <summary>Führt zur Seite „Über“, wo sich die neue Fassung holen lässt.</summary>
    private void OnUpdateHintClick(object sender, RoutedEventArgs e) =>
        Navigation.Navigate(typeof(AboutPage));

    /// <summary>
    /// Ein Befund der Erinnerung: auf den betroffenen Tag springen.
    /// </summary>
    /// <remarks>
    /// <para><b>Auf den Tag springen und nicht bloss melden.</b> Eine Meldung, nach der man
    /// erst das Datum suchen muss, wird weggeklickt; eine, die den Tag schon aufgeschlagen
    /// hat, wird bearbeitet.</para>
    /// <para><b>Das Fenster kommt nur nach vorn, wenn wirklich etwas offen ist.</b> Ein
    /// Werkzeug, das zweimal am Tag ungefragt das Fenster über die Arbeit legt und dann
    /// „alles in Ordnung“ sagt, wird nach einer Woche abgeschaltet — und dann meldet es auch
    /// das nicht mehr, worauf es ankäme.</para>
    /// </remarks>
    /// <param name="finding">Der Befund.</param>
    public void ShowFinding(ReminderFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        Day.SelectedDate = finding.Day.ToDateTime(TimeOnly.MinValue);
        Navigation.Navigate(typeof(DayPage));

        if (finding.Analysis.NeedsAttention)
        {
            Reveal();
        }
    }
}
