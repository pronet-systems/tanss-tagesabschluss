using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;

namespace TanssTagesabschluss.App.Views.Pages;

/// <summary>Der Überblick über die letzten Tage.</summary>
/// <remarks>
/// Das Ansichtsmodell kommt vom Hauptfenster — Begründung siehe <see cref="DayPage"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class HistoryPage : Page
{
    /// <summary>Baut die Seite.</summary>
    public HistoryPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not null)
        {
            return;
        }

        if (Window.GetWindow(this) is MainWindow window)
        {
            DataContext = window.History;

            // Beim ersten Aufschlagen laden. Danach nur noch auf Anweisung: Zwei Wochen neu
            // zu rechnen kostet vier Abfragen an TANSS, und der Ueberblick aendert sich nicht
            // dadurch, dass jemand die Seite wechselt.
            if (window.History.Days.Count == 0)
            {
                _ = window.History.ReloadAsync();
            }
        }
    }
}
