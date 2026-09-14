using TanssTagesabschluss.App.Runtime;
using Xunit;

namespace TanssTagesabschluss.App.Tests;

/// <summary>
/// Der Wächter, der dafür sorgt, dass das Werkzeug genau einmal läuft.
/// </summary>
public sealed class SingleInstanceTests
{
    [Fact]
    public void Die_erste_Instanz_bekommt_den_Platz_die_zweite_nicht()
    {
        using SingleInstance first = SingleInstance.Acquire();
        using SingleInstance second = SingleInstance.Acquire();

        Assert.True(first.IsFirst);
        Assert.False(second.IsFirst);
    }

    [Fact]
    public void Nach_dem_Freigeben_ist_der_Platz_wieder_frei()
    {
        // Sonst liesse sich das Werkzeug nach einem Neustart nicht mehr starten.
        using (SingleInstance first = SingleInstance.Acquire())
        {
            Assert.True(first.IsFirst);
        }

        using SingleInstance next = SingleInstance.Acquire();
        Assert.True(next.IsFirst);
    }

    [Fact]
    public void Ohne_laufende_Instanz_geht_die_Bitte_ins_Leere_und_wirft_nicht()
    {
        // Gibt es das Signal nicht - etwa weil die andere Fassung gerade beendet wird -,
        // endet der zweite Start still. Eine Fehlermeldung waere hier die schlechtere
        // Antwort.
        Assert.False(SingleInstance.AskRunningInstanceToShow());
    }

    [Fact]
    public void Eine_zweite_Instanz_erreicht_die_erste()
    {
        using ManualResetEventSlim asked = new(initialState: false);

        using (SingleInstance first = SingleInstance.Acquire())
        {
            first.ListenForSecondStart(() => asked.Set());

            Assert.True(SingleInstance.AskRunningInstanceToShow());
            Assert.True(asked.Wait(TimeSpan.FromSeconds(5)));
        }

        // HIER STAND EINMAL EINE ZUSAGE ZU VIEL: dass das benannte Ereignis nach dem
        // Freigeben sofort verschwunden sei. Das gibt die Laufzeit nicht her -- der
        // Strangvorrat haelt seine Kopie noch einen Moment, und auf einem Bauserver mit zwei
        // Kernen faellt genau das auf. Schlimm ist es nicht: Ob eine Instanz laeuft,
        // entscheidet der MUTEX und nicht das Ereignis (siehe App.OnStartup). Ein
        // nachhallendes Ereignis kostet hoechstens ein Signal ins Leere.
    }

    [Fact]
    public void Der_Mutexname_ist_der_aus_dem_Setup()
    {
        // DIESER TEST IST DIE KLAMMER ZWISCHEN ZWEI DATEIEN.
        //
        // installer\TanssTagesabschluss.iss traegt unter AppMutex denselben Namen. Haelt ihn
        // niemand, bemerkt der Assistent die laufende Anwendung NICHT - und scheitert dann
        // beim Ersetzen von Dateien, die noch offen sind. Der Fehler faellt erst beim
        // Aktualisieren auf einem Kundenrechner auf, also spaetestmoeglich.
        //
        // Wer den Namen hier aendert, sieht diesen Test scheitern und weiss, wo er ihn
        // mitzuaendern hat. Der Wert steht deshalb ausgeschrieben da und nicht als Verweis.
        Assert.Equal(@"Local\TanssTagesabschluss.SingleInstance", SingleInstance.MutexName);
    }

    [Fact]
    public void Der_Mutex_ist_sitzungslokal_und_nicht_global()
    {
        // Auf einem Terminalserver arbeiten mehrere Techniker in getrennten Sitzungen, jeder
        // mit eigenem Profil, eigenem Token und eigenen Zeiten. Ein globaler Mutex liesse
        // dort nur einen von ihnen das Werkzeug benutzen.
        Assert.StartsWith(@"Local\", SingleInstance.MutexName, StringComparison.Ordinal);
    }
}
