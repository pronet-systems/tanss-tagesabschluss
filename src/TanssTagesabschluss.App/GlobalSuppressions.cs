using System.Diagnostics.CodeAnalysis;

// CA1001 verlangt, dass ein Typ mit verwerfbaren Feldern selbst verwerfbar ist. Fuer die
// beiden WPF-Typen hier ist das die falsche Bauform, und zwar aus demselben Grund: Ihre
// Lebensdauer gehoert nicht uns, sondern WPF. Niemand ruft auf einer Application oder einem
// Window Dispose auf - das Rahmenwerk tut es nicht, und ein Aufrufer kaeme gar nicht an sie
// heran. Ein IDisposable an dieser Stelle waere eine Zusage, die niemand einloest.
//
// Freigegeben wird statt dessen dort, wo WPF das Ende ausdruecklich meldet: App.OnExit und
// MainWindow.OnClosing. Beide Stellen sind angefasst und tun es nachweislich.

[assembly: SuppressMessage(
    "Design", "CA1001:Typen mit verwerfbaren Feldern müssen verwerfbar sein",
    Justification = "App gibt Wirt und Infobereichssymbol in OnExit frei; die Lebensdauer "
                  + "einer Application gehoert WPF, und Dispose ruft dort niemand auf.",
    Scope = "type", Target = "~T:TanssTagesabschluss.App.App")]

[assembly: SuppressMessage(
    "Design", "CA1001:Typen mit verwerfbaren Feldern müssen verwerfbar sein",
    Justification = "MainWindow gibt den Aktualisierungsdienst in OnClosing frei; die "
                  + "Lebensdauer eines Window gehoert WPF.",
    Scope = "type", Target = "~T:TanssTagesabschluss.App.Views.MainWindow")]
