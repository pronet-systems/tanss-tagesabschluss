using System.Diagnostics;
using System.Runtime.Versioning;
using TanssTagesabschluss.Api.Diagnostics;
using TanssTagesabschluss.App.Runtime;

namespace TanssTagesabschluss.App.Services;

/// <summary>Was ein Hintergrunddienst gerade tut.</summary>
public enum ServiceState
{
    /// <summary>Noch nicht gestartet oder bereits beendet.</summary>
    Stopped,

    /// <summary>Läuft und wartet auf den nächsten Takt.</summary>
    Idle,

    /// <summary>Arbeitet gerade.</summary>
    Working,

    /// <summary>
    /// Läuft, tut aber nichts — abgeschaltet, oder es fehlt die Voraussetzung.
    /// </summary>
    /// <remarks>
    /// Getrennt von <see cref="Stopped"/>, weil der Unterschied für den Techniker zählt: Ein
    /// angehaltener Dienst nimmt seine Arbeit von selbst wieder auf, sobald die Voraussetzung
    /// da ist. Ein beendeter nicht.
    /// </remarks>
    Paused,

    /// <summary>
    /// Der letzte Takt ist fehlgeschlagen. Der Dienst läuft weiter.
    /// </summary>
    /// <remarks>
    /// Der Fehler eines einzelnen Vorgangs bricht niemals einen ganzen Dienst ab. Ein gestörter
    /// Takt ist ein Befund und kein Ende — der nächste kann gelingen.
    /// </remarks>
    Faulted,
}

/// <summary>Der Stand eines Hintergrunddienstes, wie ihn die Oberfläche anzeigt.</summary>
/// <remarks>
/// Unveränderlich und als Ganzes ausgetauscht, damit eine Ansicht nie einen halb
/// fortgeschriebenen Stand liest — „arbeitet“ neben der Fehlermeldung des vorletzten Takts.
/// </remarks>
public sealed record ServiceActivity
{
    /// <summary>Der Zustand.</summary>
    public required ServiceState State { get; init; }

    /// <summary>Was gerade geschieht oder zuletzt geschah — in einem Satz, deutsch.</summary>
    public required string Message { get; init; }

    /// <summary>Wann dieser Stand entstand.</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;

    /// <summary>Wann der nächste Takt frühestens läuft; <see langword="null"/>, wenn keiner ansteht.</summary>
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>Wie viele Takte dieser Dienst seit dem Start gelaufen ist.</summary>
    public long Cycles { get; init; }

    /// <summary>Die letzte Fehlermeldung, bereits geschwärzt; sonst <see langword="null"/>.</summary>
    public string? LastError { get; init; }

    /// <summary>Wie lange der letzte Takt gebraucht hat.</summary>
    public TimeSpan? LastCycle { get; init; }
}

/// <summary>Ein Dienst, der im Hintergrund im Takt arbeitet.</summary>
/// <remarks>
/// <b>Jeder einzeln abschaltbar und mit eigenem Fehlerriegel.</b> Wer den Verdacht hat, dass
/// die Erinnerung stört, soll sie anhalten können, ohne die Tokenerneuerung zu verlieren — und
/// ein Fehlschlag der einen darf die andere nicht mitreissen.
/// </remarks>
public interface IBackgroundService
{
    /// <summary>Der Anzeigename, deutsch.</summary>
    string Name { get; }

    /// <summary>Wozu dieser Dienst da ist — ein Satz für die Anzeige.</summary>
    string Description { get; }

    /// <summary>Arbeitet dieser Dienst? Auf <c>false</c> gesetzt hält er an, ohne sich zu beenden.</summary>
    bool IsEnabled { get; set; }

    /// <summary>Der aktuelle Stand.</summary>
    ServiceActivity Activity { get; }

    /// <summary>Meldet jeden neuen Stand; wird auf dem Strang der Oberfläche ausgelöst.</summary>
    event EventHandler<ServiceActivity>? ActivityChanged;

    /// <summary>Startet den Takt. Blockiert nicht.</summary>
    /// <param name="ct">Abbruchmarke für den Start selbst.</param>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Hält den Takt an.</summary>
    /// <param name="ct">Abbruchmarke; sie begrenzt das Warten.</param>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    Task StopAsync(CancellationToken ct = default);
}

/// <summary>
/// Das gemeinsame Gerüst der Hintergrunddienste: Takt, Abschaltbarkeit, Fehlerriegel.
/// </summary>
/// <remarks>
/// <para><b>Der Fehlerriegel steht hier und nicht in den Diensten.</b> Er darf nicht davon
/// abhängen, dass jede Umsetzung ihn gleich gut schreibt: Ein Takt, der wirft, wird gemeldet,
/// und der Dienst läuft weiter.</para>
///
/// <para><b>Der Takt läuft zuerst und wartet danach.</b> Umgekehrt geschähe nach dem Start eine
/// Minute lang nichts — beim Tokendienst wäre das ein ganzer Tag, und ein abgelaufenes Token
/// bliebe abgelaufen.</para>
///
/// <para><b>Der Zusammenbau wird bei jedem Takt neu erfragt.</b> Er ist <see langword="null"/>,
/// solange nichts eingerichtet ist, und nach einem erneuten Laden ein anderer. Ein Dienst, der
/// ihn sich merkte, arbeitete nach dem Berichtigen der Konfiguration weiter gegen die alte
/// Instanz.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public abstract class PeriodicService : IBackgroundService, IDisposable
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _shutdown;
    private Task? _loop;
    private long _cycles;
    private bool _enabled = true;
    private bool _disposed;
    private TimeSpan? _lastCycle;

    /// <summary>Nimmt die gemeinsamen Bausteine auf.</summary>
    /// <param name="context">Der Zugang zu Zustand und Zusammenbau.</param>
    protected PeriodicService(IRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
        Activity = new ServiceActivity
        {
            State = ServiceState.Stopped,
            Message = "Noch nicht gestartet.",
        };
    }

    /// <inheritdoc />
    public event EventHandler<ServiceActivity>? ActivityChanged;

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public ServiceActivity Activity { get; private set; }

    /// <inheritdoc />
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;

            // Sofort melden und nicht erst beim naechsten Takt: Ein Schalter, der bis zu einer
            // Minute lang nichts sichtbar bewirkt, wird ein zweites Mal betaetigt.
            Report(new ServiceActivity
            {
                State = value ? ServiceState.Idle : ServiceState.Paused,
                Message = value
                    ? "Eingeschaltet; arbeitet ab dem nächsten Takt."
                    : "Von Hand abgeschaltet. Ein laufender Takt wird noch zu Ende geführt.",
                Cycles = Interlocked.Read(ref _cycles),
            });
        }
    }

    /// <summary>Der Zugang zu Zustand und Zusammenbau.</summary>
    protected IRuntimeContext Context { get; }

    /// <summary>Der Abstand zwischen zwei Takten.</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>Was gemeldet wird, wenn nichts eingerichtet ist.</summary>
    protected virtual string NotConfiguredMessage =>
        "Ruht: Es liegt keine gültige Konfiguration vor.";

    /// <summary>Was gemeldet wird, wenn ein Takt mit einer Ausnahme abbricht.</summary>
    protected virtual string FaultMessage =>
        "Der letzte Takt ist mit einem unerwarteten Fehler abgebrochen. Der Dienst läuft weiter; "
        + "der nächste Takt kann gelingen.";

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_loop is not null)
            {
                return Task.CompletedTask;
            }

            _shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
            CancellationToken token = _shutdown.Token;

            // Task.Run und nicht einfach aufrufen: Der erste Takt liefe sonst auf dem Strang
            // der Oberflaeche bis zum ersten echten await.
            _loop = Task.Run(() => LoopAsync(token), CancellationToken.None);
        }

        Report(new ServiceActivity { State = ServiceState.Idle, Message = "Gestartet." });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        Task? loop;
        CancellationTokenSource? shutdown;

        lock (_gate)
        {
            loop = _loop;
            shutdown = _shutdown;
            _loop = null;
            _shutdown = null;
        }

        if (shutdown is not null)
        {
            await shutdown.CancelAsync().ConfigureAwait(false);
        }

        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Der Aufrufer wartet nicht laenger. Der Takt laeuft von selbst aus.
            }
        }

        shutdown?.Dispose();

        Report(new ServiceActivity
        {
            State = ServiceState.Stopped,
            Message = "Beendet.",
            Cycles = Interlocked.Read(ref _cycles),
        });
    }

    /// <summary>Gibt die Abbruchmarke frei.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Ein einzelner Takt.
    /// </summary>
    /// <remarks>
    /// Gibt den Satz zurück, der danach als Stand angezeigt wird. Ausdrücklich ein Rückgabewert
    /// und kein Seiteneffekt: So kann kein Dienst vergessen, sich zu melden, und keiner bleibt
    /// nach getaner Arbeit auf „Arbeitet“ stehen.
    /// </remarks>
    /// <param name="composition">Die Bausteine; niemals <see langword="null"/>, wenn dies läuft.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Der Satz für die Anzeige.</returns>
    protected abstract Task<string> RunCycleAsync(RuntimeComposition composition,
                                                  CancellationToken ct);

    /// <summary>Gibt die Abbruchmarke frei.</summary>
    /// <param name="disposing">Wird von <see cref="Dispose()"/> aufgerufen?</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (disposing)
        {
            _shutdown?.Dispose();
            _shutdown = null;
        }
    }

    /// <summary>Setzt den Stand und meldet ihn auf dem Strang der Oberfläche.</summary>
    /// <param name="activity">Der neue Stand.</param>
    protected void Report(ServiceActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        Activity = activity;
        Context.Notifier.Raise(ActivityChanged, this, activity);
    }

    /// <summary>Der Takt selbst: arbeiten, warten, wiederholen — und niemals daran sterben.</summary>
    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await StepAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Der letzte Riegel. Was hier ankommt, ist ein Programmfehler - er kostet
                // diesen einen Takt und nicht den Dienst.
                Report(new ServiceActivity
                {
                    State = ServiceState.Faulted,
                    Message = FaultMessage,
                    LastError = Redaction.Scrub(ex.Message),
                    Cycles = Interlocked.Read(ref _cycles),
                    LastCycle = _lastCycle,
                    NextRunAt = Context.Clock.GetLocalNow() + Interval,
                });
            }

            try
            {
                await Task.Delay(Interval, Context.Clock, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Ein Takt samt Vorbedingungen.</summary>
    private async Task StepAsync(CancellationToken ct)
    {
        long cycles = Interlocked.Read(ref _cycles);

        if (!_enabled)
        {
            return;
        }

        if (Context.Composition is not { } composition)
        {
            // Kein Fehler, sondern der Normalfall auf einem frisch aufgesetzten Rechner.
            Report(new ServiceActivity
            {
                State = ServiceState.Paused,
                Message = NotConfiguredMessage,
                Cycles = cycles,
                NextRunAt = Context.Clock.GetLocalNow() + Interval,
            });

            return;
        }

        Report(new ServiceActivity
        {
            State = ServiceState.Working,
            Message = "Arbeitet.",
            Cycles = cycles,
        });

        long started = Stopwatch.GetTimestamp();
        string summary = await RunCycleAsync(composition, ct).ConfigureAwait(false);
        _lastCycle = Stopwatch.GetElapsedTime(started);

        Report(new ServiceActivity
        {
            State = ServiceState.Idle,
            Message = summary,
            Cycles = Interlocked.Increment(ref _cycles),
            LastCycle = _lastCycle,
            NextRunAt = Context.Clock.GetLocalNow() + Interval,
        });
    }
}
