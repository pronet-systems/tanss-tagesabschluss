using System.Runtime.Versioning;
using System.Windows;
using TanssTagesabschluss.App.ViewModels;

namespace TanssTagesabschluss.App.Views;

/// <summary>
/// Fragt, welcher Leistungstext das Zusammenfügen zweier Zeitfenster überlebt.
/// </summary>
/// <remarks>
/// <para><b>Ohne Ansichtsmodell.</b> Das Fenster hat einen Zustand — drei Möglichkeiten, von
/// denen eine gewählt ist — und keine Aufgabe darüber hinaus: kein Laden, kein Speichern, kein
/// Netz. Ein eigenes Ansichtsmodell dafür wäre eine Schicht, die nichts trägt.</para>
///
/// <para><b>Alle drei Texte stehen sichtbar da.</b> Wer sich zwischen Texten entscheidet, muss
/// sie lesen können, ohne erst zu wählen — und was hier entsteht, liest anschliessend der
/// Kunde auf seiner Rechnung.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public partial class MergeTextWindow
{
    private readonly MergeTextRequest _question;

    /// <summary>Baut das Fenster zu einer Frage.</summary>
    /// <param name="question">Die beiden Texte.</param>
    public MergeTextWindow(MergeTextRequest question)
    {
        ArgumentNullException.ThrowIfNull(question);

        _question = question;
        InitializeComponent();

        BothPreview.Text = question.Both;

        FirstHeadline.Text = "Nur der Text des ersten Zeitfensters";
        FirstPreview.Text = question.First.Trim();

        SecondHeadline.Text = "Nur der Text des zweiten Zeitfensters";
        SecondPreview.Text = question.Second.Trim();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        _question.Chosen = FirstOption.IsChecked == true
            ? _question.First.Trim()
            : SecondOption.IsChecked == true
                ? _question.Second.Trim()
                : _question.Both;

        DialogResult = true;
    }

    /// <summary>Abbrechen heisst: gar nicht zusammenfügen.</summary>
    /// <remarks>
    /// <b>Nicht „dann eben beide“.</b> Wer die Frage wegklickt, hat sich gegen den Eingriff
    /// entschieden und nicht für einen der Texte. Die beiden Zeitfenster bleiben, wie sie
    /// waren.
    /// </remarks>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _question.Chosen = null;
        DialogResult = false;
    }
}
