using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;

namespace TanssTagesabschluss.App.Views.Pages;

/// <summary>Version, Ablageorte, Aktualisierung.</summary>
[SupportedOSPlatform("windows")]
public partial class AboutPage : Page
{
    /// <summary>Baut die Seite.</summary>
    public AboutPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is null && Window.GetWindow(this) is MainWindow window)
        {
            DataContext = window.About;
        }
    }
}
