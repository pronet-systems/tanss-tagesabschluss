using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TanssTagesabschluss.App.ViewModels;

namespace TanssTagesabschluss.App.Views.Pages;

/// <summary>Einrichtung und Einstellungen.</summary>
/// <remarks>
/// <b>Die Anmeldung läuft über den Quelltext dahinter und nicht über einen Befehl.</b> Der
/// Grund ist das Kennwort: Ein gebundenes Kennwortfeld hielte es im Speicher des
/// Ansichtsmodells, so lange die Seite offen ist — und in einem Speicherabbild stünde es dort
/// im Klartext. Hier wird es aus dem Bedienelement gelesen, weitergereicht und danach gelöscht;
/// es lebt nur für die Dauer eines Aufrufs.
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class SettingsPage : Page
{
    /// <summary>Baut die Seite.</summary>
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not null || Window.GetWindow(this) is not MainWindow window)
        {
            return;
        }

        DataContext = window.Settings;

        // Nur einmal anmelden, auch wenn die Seite mehrfach aufgeschlagen wird: Der
        // Navigationsbereich baut sie bei jedem Wechsel neu, das Ansichtsmodell lebt weiter.
        window.Settings.AiSettingsRequested -= window.OpenAiSettings;
        window.Settings.AiSettingsRequested += window.OpenAiSettings;
    }

    /// <summary>
    /// Die Eingabetaste im Kennwortfeld meldet an.
    /// </summary>
    /// <remarks>
    /// Wer ein Kennwort tippt, drückt danach die Eingabetaste — das ist eingeübt, und ohne
    /// diese Zeile geschieht dann nichts.
    /// </remarks>
    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OnSignInClick(sender, new RoutedEventArgs());
        }
    }

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel model)
        {
            return;
        }

        string password = Password.Password;

        try
        {
            await model.SignInAsync(password).ConfigureAwait(true);
        }
        finally
        {
            // Das Feld leeren, sobald der Aufruf durch ist. Ein stehengebliebenes Kennwort
            // waere sichtbar, sobald jemand anders an den Rechner tritt - und es laege im
            // Speicher des Bedienelements, bis die Seite verworfen wird.
            Password.Clear();
        }
    }
}
