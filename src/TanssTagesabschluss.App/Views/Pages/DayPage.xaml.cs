using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;

namespace TanssTagesabschluss.App.Views.Pages;

/// <summary>Die Tagesansicht.</summary>
/// <remarks>
/// <b>Das Ansichtsmodell kommt vom Hauptfenster und entsteht nicht hier.</b> Der
/// Navigationsbereich legt Seiten nicht ab — jeder Seitenwechsel baut sie neu. Entstünde das
/// Ansichtsmodell mit der Seite, verlöre jeder Wechsel den gewählten Tag und lüde von vorn.
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class DayPage : Page
{
    /// <summary>Baut die Seite.</summary>
    public DayPage()
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
            DataContext = window.Day;

            // Beim ersten Aufschlagen laden: Der Tag steht schon fest (heute oder der vom
            // Ueberblick gewaehlte), aber niemand hat ihn bisher geholt.
            if (window.Day.Analysis is null)
            {
                _ = window.Day.ReloadAsync();
            }
        }
    }
}
