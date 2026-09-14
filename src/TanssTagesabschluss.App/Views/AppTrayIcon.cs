using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace TanssTagesabschluss.App.Views;

/// <summary>
/// Das Symbol im Infobereich — der Grund, warum die Erinnerungen auch bei geschlossenem
/// Fenster laufen.
/// </summary>
/// <remarks>
/// <para><b>Es gehört der Anwendung und nicht dem Fenster.</b> Entstünde es im
/// <c>Loaded</c>-Ereignis des Hauptfensters, fiele dieses Ereignis bei einem Start ohne
/// sichtbares Fenster nicht — und es gäbe kein Symbol. Genau dieser Fehler hat das
/// Schwesterprojekt einmal gekostet.</para>
///
/// <para><b>Im Quelltext gebaut und nicht in XAML.</b> Ein <c>TaskbarIcon</c> in einer
/// eigenen XAML-Datei braucht eine Ressourcenwurzel, und die gibt es ohne Fenster nicht. Drei
/// Dutzend Zeilen hier sind der geradere Weg als ein Wörterbuch, das nur dieses eine Element
/// enthält.</para>
///
/// <para><b>Ohne eigenes <c>SupportedOSPlatform</c>.</b> Die Angabe des Projekts
/// (<c>10.0.19041.0</c>) gilt hier ohnehin; ein zusätzliches <c>[SupportedOSPlatform("windows")]</c>
/// ohne Versionsnummer wäre <b>breiter</b> als das Projektziel und machte aus der Zusage
/// wieder „alle Windows-Versionen“ — woraufhin der Übersetzer jeden Aufruf in die
/// Infobereichs-Bibliothek beanstandet, weil die erst ab Windows XP zugesagt ist.</para>
/// </remarks>
public sealed class AppTrayIcon : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly MainWindow _window;
    private bool _disposed;

    /// <summary>Baut das Symbol und hängt es in den Infobereich.</summary>
    /// <param name="window">Das Hauptfenster, das es zeigt und verbirgt.</param>
    public AppTrayIcon(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;

        ContextMenu menu = new();

        MenuItem open = new() { Header = "Öffnen" };
        open.Click += (_, _) => _window.Reveal();
        menu.Items.Add(open);

        menu.Items.Add(new Separator());

        MenuItem quit = new() { Header = "Beenden" };
        quit.Click += (_, _) => _window.ShutDown();
        menu.Items.Add(quit);

        _icon = new TaskbarIcon
        {
            // OHNE Symbol zeigt der Infobereich nichts an - und dann hat die Erinnerung
            // keinen sichtbaren Traeger mehr, ueber den sich das Fenster zurueckholen liesse.
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/tray.ico", UriKind.Absolute)),
            ToolTipText = "TANSS Tagesabschluss",
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
            Visibility = Visibility.Visible,
        };

        // Ein Doppelklick oeffnet - das erwartet jeder, und es erspart den Umweg ueber das
        // Kontextmenue.
        _icon.TrayMouseDoubleClick += (_, _) => _window.Reveal();

        _icon.ForceCreate();
    }

    /// <summary>
    /// Zeigt eine Meldung im Infobereich.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Ob Windows die Meldung anzeigt, entscheidet der Benutzer in seinen
    /// Einstellungen — und eine unterdrückte Meldung ist kein Grund, das Werkzeug anzuhalten.
    /// Die Lücken stehen ohnehin im Fenster.
    /// </remarks>
    /// <param name="title">Die Überschrift.</param>
    /// <param name="message">Der Text.</param>
    public void Notify(string title, string message)
    {
        try
        {
            _icon.ShowNotification(title, message, NotificationIcon.Info);
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            // Siehe Kommentar: Die Meldung ist Beiwerk, das Fenster ist der Bestand.
        }
    }

    /// <summary>Nimmt das Symbol aus dem Infobereich.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Dispose();
    }
}
