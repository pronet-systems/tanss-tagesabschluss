using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TanssTagesabschluss.App.ViewModels;

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

    /// <summary>
    /// Die Eingabetaste im Suchfeld sucht die Firma.
    /// </summary>
    /// <remarks>
    /// <para>Wer einen Suchbegriff tippt, drückt danach die Eingabetaste — das ist eingeübt, und
    /// ohne diese Zeile geschieht dann nichts.</para>
    /// <para><b>Warum nicht bei jedem Tastendruck gesucht wird:</b> Die Suche geht über
    /// <c>PUT /api/v1/search</c> an die Produktivinstanz. Für ein Wort ergäbe das ein Dutzend
    /// Abfragen, und die Antwort auf „Mus“ ist ohnehin nicht die, die jemand sucht.</para>
    /// </remarks>
    private void OnCompanySearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not FrameworkElement { DataContext: GapRow row })
        {
            return;
        }

        e.Handled = true;

        if (row.SearchCompaniesCommand.CanExecute(null))
        {
            row.SearchCompaniesCommand.Execute(null);
        }
    }
}
