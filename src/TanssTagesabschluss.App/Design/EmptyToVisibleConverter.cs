using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TanssTagesabschluss.App.Design;

/// <summary>
/// Zeigt etwas an, <b>solange</b> ein Wert fehlt — für Platzhalter über einem Auswahlfeld.
/// </summary>
/// <remarks>
/// <para><b>Warum das ein eigener Umsetzer ist und kein Bedienelement.</b> WPF-UI kennt einen
/// Platzhalter nur am Textfeld, nicht am Auswahlfeld (<c>PlaceholderText</c> gibt es an
/// <c>TextBox</c> und <c>AutoSuggestBox</c>, an <c>ComboBox</c> nicht). Drei leere Kästchen
/// nebeneinander sagen aber niemandem, welches die Firma, welches das Ticket und welches das
/// Gerät ist — und wer auf gut Glück aufklappt, wählt irgendwann das Falsche.</para>
///
/// <para><b>Leere Zeichenkette zählt als fehlend.</b> Das beschreibbare Auswahlfeld der Firma
/// hält seinen Inhalt als Text; wäre nur <see langword="null"/> „fehlend“, bliebe der
/// Platzhalter dort für immer stehen.</para>
/// </remarks>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class EmptyToVisibleConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || (value is string text && string.IsNullOrWhiteSpace(text))
            ? Visibility.Visible
            : Visibility.Collapsed;

    /// <inheritdoc />
    /// <remarks>Ein Platzhalter wird angezeigt, nicht eingegeben — der Rückweg ergibt keinen Sinn.</remarks>
    public object ConvertBack(object? value, Type targetType, object? parameter,
                              CultureInfo culture) =>
        throw new NotSupportedException(
            "Aus der Sichtbarkeit eines Platzhalters lässt sich kein Wert zurückrechnen.");
}
