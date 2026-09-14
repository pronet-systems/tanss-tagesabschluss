using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TanssTagesabschluss.App.Design;

/// <summary>
/// Blendet ein Element aus, solange sein Wert <see langword="null"/> oder leer ist.
/// </summary>
/// <remarks>
/// <para><b>Gebraucht für alles, was nur manchmal etwas zu sagen hat</b> — die Fehlermeldung
/// einer Seite, der Status einer Lückenzeile. Ohne diesen Konverter stünde dort ein leeres
/// Element mit seinem Abstand darum, und die Karte darüber sähe aus, als fehlte ihr etwas.</para>
/// <para><b>Leerraum zählt als leer.</b> Eine Zeichenkette aus einem Leerzeichen kommt aus
/// einer Formatierung und nicht aus einer Aussage; sie sichtbar zu machen hiesse, eine leere
/// Zeile anzuzeigen.</para>
/// </remarks>
public sealed class NullToCollapsedConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => Visibility.Collapsed,
            string text when string.IsNullOrWhiteSpace(text) => Visibility.Collapsed,
            _ => Visibility.Visible,
        };

    /// <summary>Nicht vorgesehen.</summary>
    /// <remarks>
    /// Die Richtung ergibt keinen Sinn: Aus „unsichtbar“ liesse sich der ursprüngliche Wert
    /// nicht zurückgewinnen. Eine stillschweigend gelieferte Ersatzantwort wäre schlimmer als
    /// eine Ausnahme — sie schriebe irgendetwas in das gebundene Feld zurück.
    /// </remarks>
    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException(
            "Diese Umwandlung gibt es nur in eine Richtung. Eine Bindung, die sie rückwärts "
            + "braucht, ist falsch herum gebaut.");
}
