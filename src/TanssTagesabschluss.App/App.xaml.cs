using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using TanssTagesabschluss.App.Design;
using TanssTagesabschluss.App.Runtime;
using TanssTagesabschluss.App.Services;
using TanssTagesabschluss.App.Views;
using TanssTagesabschluss.Storage.Config;
using Wpf.Ui.Appearance;

namespace TanssTagesabschluss.App;

/// <summary>
/// Der Einstieg: Darstellung setzen, Wirt bauen, Dienste starten, Fenster zeigen.
/// </summary>
/// <remarks>
/// <para><b>Die Anwendung startet auch ohne Konfiguration.</b> Ein frisch aufgesetzter Rechner
/// hat keine, und das ist der erste Start und kein Fehlerfall: Das Fenster geht auf, die
/// Fußzeile sagt „nicht eingerichtet“, und die Seite „Einstellungen“ führt hindurch.</para>
///
/// <para><b>Die Erinnerungen laufen auch bei geschlossenem Fenster.</b> Das ist ihr Zweck —
/// wer das Fenster offen hätte, bräuchte keine Erinnerung. Deshalb liegt die Anwendung im
/// Infobereich und beendet sich nicht mit dem Fenster.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private AppHost? _host;
    private AppTrayIcon? _tray;
    private SingleInstance? _instance;

    /// <summary>Der Wirt; <see langword="null"/>, solange der Start nicht durch ist.</summary>
    internal AppHost? Host => _host;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // MUSS vor dem ersten Fenster stehen. In der Voreinstellung endet eine
        // WPF-Anwendung, sobald ihr letztes Fenster zugeht - und beim Autostart mit
        // --minimized gibt es gar kein Fenster. Beendet wird ab hier nur noch
        // ausdruecklich: ueber "Beenden" am Symbol im Infobereich.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // GENAU EINMAL. Zwei laufende Fassungen hiessen zwei Symbole im Infobereich, zwei
        // Tokendienste und zweimal dieselbe Erinnerung - bei einem Werkzeug, dessen ganzer
        // Zweck eine Meldung am Tag ist, waere das der schnellste Weg, es abzuschalten.
        //
        // Und das Setup haengt daran: installer\TanssTagesabschluss.iss traegt unter
        // AppMutex denselben Namen. Haelt ihn niemand, bemerkt der Assistent die laufende
        // Anwendung nicht und scheitert beim Ersetzen offener Dateien.
        _instance = SingleInstance.Acquire();

        if (!_instance.IsFirst)
        {
            // Die laufende Fassung nach vorn bitten und still enden. Eine Meldung "laeuft
            // bereits" waere hier die schlechtere Antwort: Wer das Werkzeug startet, will es
            // sehen und nicht darueber belehrt werden, dass es schon da ist.
            SingleInstance.AskRunningInstanceToShow();

            // Ueber den Dispatcher und nicht unmittelbar. Shutdown() mitten in OnStartup
            // verpufft: Die Schleife pumpt noch nicht, die Bitte wird verworfen, und uebrig
            // bleibt ein Prozess ohne Fenster, der nie endet. Nachgemessen: Wer die Anwendung
            // beendet und binnen zwei Sekunden neu startet, hatte genau das -- das Werkzeug
            // schien nicht mehr zu starten, lief aber im Verborgenen weiter.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Shutdown));
            return;
        }

        base.OnStartup(e);

        ApplyTheme(e.Args);

        // Der Notifier MUSS auf diesem Strang entstehen: Er merkt sich den
        // SynchronizationContext, und zur Zeit eines Ereignisses waere das der Strang des
        // Vorrats und damit gerade nicht der gesuchte.
        _host = new AppHost(ConfigStore.Default());
        _host.Add(new ReminderService(_host));
        _host.Add(new TokenRotationService(_host));

        _host.Reload();

        MainWindow window = new(_host);
        MainWindow = window;

        // Das Symbol im Infobereich gehoert der ANWENDUNG und nicht dem Fenster. Haenge es am
        // Loaded-Ereignis des Fensters, faende beim Start ohne Fenster kein Ereignis statt -
        // und es gaebe kein Symbol.
        _tray = new AppTrayIcon(window);

        // Die Erinnerung wird HIER verdrahtet und nicht im Fenster: Sie hat zwei Empfaenger -
        // das Symbol im Infobereich und das Fenster -, und keiner der beiden gehoert dem
        // anderen. Im Fenster verdrahtet, haette das Symbol nichts zu melden, sobald jemand
        // das Fenster schliesst; genau dann ist die Erinnerung aber am noetigsten.
        foreach (ReminderService reminder in _host.Services.OfType<ReminderService>())
        {
            reminder.Found += (_, finding) =>
            {
                if (finding.Analysis.NeedsAttention)
                {
                    _tray?.Notify(finding.Headline, finding.Detail);
                }

                window.ShowFinding(finding);
            };
        }

        // --minimized: Der Autostart traegt dieses Argument. Ohne es legte sich bei jeder
        // Anmeldung ein Fenster ueber den Bildschirm, das niemand aufgerufen hat - und das
        // waere der schnellste Weg, den Autostart wieder abzuschalten. Die Dienste laufen
        // trotzdem, und genau darauf kommt es an: Die Erinnerung braucht kein Fenster.
        if (!StartsMinimized(e.Args))
        {
            window.Show();
        }

        _ = _host.StartAsync();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        _tray?.Dispose();
        _instance?.Dispose();

        if (_host is { } host)
        {
            // Mit Frist: Ein Dienst, der gerade an einer Zeitgrenze haengt, darf das Beenden
            // nicht um dreissig Sekunden verzoegern. Verloren geht dabei nichts - dieses
            // Werkzeug haelt keine unabgeschlossenen Vorgaenge.
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(3));
            host.StopAsync(deadline.Token).GetAwaiter().GetResult();
            host.Dispose();
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Setzt die Darstellung: dem System folgend, oder erzwungen über ein Startargument.
    /// </summary>
    /// <remarks>
    /// <para><c>--theme=light</c> und <c>--theme=dark</c> erzwingen sie. Gedacht für den Fall,
    /// dass die Erkennung auf einem Rechner nicht greift — und für Bildschirmfotos.</para>
    /// <para><b>Ein Wechsel der Systemeinstellung zur Laufzeit wird nicht nachgezogen.</b> Das
    /// ist bewusst so: Die Nachführung verlangt einen Fensterhaken, und der ist mehr Aufwand,
    /// als der Fall wert ist.</para>
    /// </remarks>
    private static void ApplyTheme(IReadOnlyList<string> args)
    {
        ApplicationTheme theme = ApplicationTheme.Dark;

        foreach (string argument in args)
        {
            if (argument.Equals("--theme=light", StringComparison.OrdinalIgnoreCase))
            {
                theme = ApplicationTheme.Light;
            }
            else if (argument.Equals("--theme=dark", StringComparison.OrdinalIgnoreCase))
            {
                theme = ApplicationTheme.Dark;
            }
            else if (argument.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            else
            {
                continue;
            }

            ApplicationThemeManager.Apply(theme);
            ThemeTokens.Track();
            return;
        }

        ApplicationThemeManager.ApplySystemTheme();
        ThemeTokens.Track();
    }

    /// <summary>
    /// Soll die Anwendung ohne sichtbares Fenster starten?
    /// </summary>
    /// <remarks>
    /// Das Argument setzt das Setup auf die Autostart-Verknüpfung. Beides wird angenommen,
    /// mit und ohne führende Bindestriche — eine von Hand angelegte Verknüpfung schreibt
    /// erfahrungsgemäß das eine oder das andere.
    /// </remarks>
    /// <param name="args">Die Startargumente.</param>
    /// <returns><c>true</c>, wenn das Fenster zunächst verborgen bleibt.</returns>
    private static bool StartsMinimized(IReadOnlyList<string> args) =>
        args.Any(argument =>
            argument.Trim().TrimStart('-', '/')
                    .Equals("minimized", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Führt etwas auf dem Strang der Oberfläche aus.
    /// </summary>
    /// <param name="action">Was zu tun ist.</param>
    internal static void OnUi(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
            return;
        }

        action();
    }
}
