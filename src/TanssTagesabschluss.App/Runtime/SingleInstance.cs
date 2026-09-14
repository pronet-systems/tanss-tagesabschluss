using System.Runtime.Versioning;

namespace TanssTagesabschluss.App.Runtime;

/// <summary>
/// Sorgt dafür, dass das Werkzeug genau einmal läuft — und holt die laufende Instanz nach vorn,
/// wenn jemand es ein zweites Mal startet.
/// </summary>
/// <remarks>
/// <para><b>Der Mutex ist mehr als eine Bequemlichkeit: Das Setup hängt daran.</b>
/// <c>installer\TanssTagesabschluss.iss</c> trägt unter <c>AppMutex</c> genau diesen Namen. Hält
/// ihn niemand, bemerkt der Assistent die laufende Anwendung nicht — und scheitert dann beim
/// Ersetzen von Dateien, die noch offen sind. <b>Ändert sich der Name hier, muss er dort
/// mitwandern.</b></para>
///
/// <para><b>Und ohne ihn liefe die Erinnerung doppelt.</b> Wer die Verknüpfung zweimal
/// anklickt, hätte zwei Symbole im Infobereich, zwei Tokendienste und zweimal dieselbe
/// Meldung — bei einem Werkzeug, dessen ganzer Zweck eine Meldung am Tag ist, wäre das der
/// schnellste Weg, es abzuschalten.</para>
///
/// <para><b><c>Local\</c> und nicht <c>Global\</c>.</b> Auf einem Terminalserver arbeiten
/// mehrere Techniker in getrennten Sitzungen, jeder mit eigenem Profil, eigenem Token und
/// eigenen Zeiten. Ein globaler Mutex liesse dort nur einen von ihnen das Werkzeug benutzen.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// Der Name des Mutex. <b>Steht wortgleich in <c>installer\TanssTagesabschluss.iss</c>.</b>
    /// </summary>
    public const string MutexName = @"Local\TanssTagesabschluss.SingleInstance";

    /// <summary>Das Signal, mit dem eine zweite Instanz die erste nach vorn bittet.</summary>
    private const string ActivateName = @"Local\TanssTagesabschluss.SingleInstance.Activate";

    private readonly Mutex _mutex;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    private SingleInstance(Mutex mutex, bool isFirst)
    {
        _mutex = mutex;
        IsFirst = isFirst;
    }

    /// <summary>Ist dies die erste laufende Instanz?</summary>
    public bool IsFirst { get; }

    /// <summary>
    /// Belegt den Platz — oder stellt fest, dass er schon belegt ist.
    /// </summary>
    /// <returns>Der Wächter. Der Aufrufer gibt ihn frei.</returns>
    public static SingleInstance Acquire()
    {
        // initiallyOwned: false. Mit true besaesse der erzeugende Strang den Mutex, und ein
        // Freigeben von einem anderen Strang aus waere eine ApplicationException - genau das
        // droht beim Beenden, wo der Aufraeumpfad nicht zwingend derselbe Strang ist. Fuer die
        // Frage "laeuft schon eines?" genuegt das Ergebnis von createdNew.
        Mutex mutex = new(initiallyOwned: false, MutexName, out bool createdNew);

        return new SingleInstance(mutex, createdNew);
    }

    /// <summary>
    /// Hört auf zweite Startversuche und meldet sie.
    /// </summary>
    /// <remarks>
    /// Nur von der <b>ersten</b> Instanz aufzurufen. Der Rückruf läuft auf einem Strang des
    /// Vorrats — wer ein Fenster anfasst, muss selbst auf den Strang der Oberfläche wechseln.
    /// </remarks>
    /// <param name="onSecondStart">Was geschieht, wenn jemand ein zweites Mal startet.</param>
    public void ListenForSecondStart(Action onSecondStart)
    {
        ArgumentNullException.ThrowIfNull(onSecondStart);

        if (!IsFirst)
        {
            return;
        }

        _activate = new EventWaitHandle(initialState: false, EventResetMode.AutoReset,
                                        ActivateName);

        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activate,
            (_, _) => onSecondStart(),
            state: null,
            // Timeout.InfiniteTimeSpan und nicht Timeout.Infinite: Der Uebersetzer
            // waehlt hier die Ueberladung mit TimeSpan, und die nimmt kein int.
            timeout: Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
    }

    /// <summary>
    /// Bittet die bereits laufende Instanz, sich zu zeigen.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Gibt es das Signal nicht — etwa weil die andere Instanz gerade
    /// beendet wird —, ist das kein Grund, den zweiten Start mit einer Fehlermeldung zu
    /// quittieren. Er endet dann einfach still, und das ist auch richtig so.
    /// </remarks>
    /// <returns><c>true</c>, wenn das Signal abgesetzt wurde.</returns>
    public static bool AskRunningInstanceToShow()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ActivateName, out EventWaitHandle? handle))
            {
                using (handle)
                {
                    return handle.Set();
                }
            }
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // Es gibt kein Signal. Siehe Kommentar.
        }
        catch (UnauthorizedAccessException)
        {
            // Die andere Instanz laeuft unter einem anderen Benutzer. Dann ist sie ohnehin
            // nicht "unsere" - und ihr Fenster zu zeigen waere falsch.
        }

        return false;
    }

    /// <summary>Gibt Mutex und Signal frei.</summary>
    /// <remarks>
    /// <para><b>Auf das Abmelden wird gewartet.</b> <c>Unregister(null)</c> kehrt sofort
    /// zurück, während ein Rückruf noch laufen kann — und der griffe dann auf ein Objekt zu,
    /// das gerade freigegeben wird. Mit Wartemarke ist die Abmeldung durch, bevor das Signal
    /// fällt.</para>
    /// <para><b>Auf den Start hat das keinen Einfluss</b>, und das ist wichtig zu wissen: Ob
    /// eine Instanz läuft, entscheidet der <b>Mutex</b> und nicht dieses Ereignis (siehe
    /// <c>App.OnStartup</c>). Ein Ereignis, das einen Moment nachhallt, kostet höchstens ein
    /// Signal ins Leere.</para>
    /// <para><b>Mit Frist.</b> Hängt ein Rückruf, ist das Beenden wichtiger als das saubere
    /// Abmelden — zwei Sekunden sind mehr, als ein Rückruf hier je braucht.</para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_registration is { } registration)
        {
            using ManualResetEvent unregistered = new(initialState: false);

            if (registration.Unregister(unregistered))
            {
                unregistered.WaitOne(TimeSpan.FromSeconds(2));
            }
        }

        _activate?.Dispose();
        _mutex.Dispose();
    }
}
