namespace TanssTagesabschluss.App.Runtime;

/// <summary>Der Betriebszustand, wie ihn die Fußzeile zeigt.</summary>
public enum AppState
{
    /// <summary>Es liegt keine gültige Konfiguration vor.</summary>
    NotConfigured,

    /// <summary>Eingerichtet und zuletzt erfolgreich mit TANSS gesprochen.</summary>
    Connected,

    /// <summary>
    /// Eingerichtet, aber etwas stimmt nicht.
    /// </summary>
    /// <remarks>
    /// Ausdrücklich nicht dasselbe wie <see cref="NotConfigured"/>: Dort fehlt die Einrichtung,
    /// hier ist sie da und der Betrieb gestört. Die Handlungsanweisung ist eine andere.
    /// </remarks>
    Degraded,
}

/// <summary>
/// Ein Hinweis auf etwas, das der Techniker wissen sollte.
/// </summary>
/// <remarks>
/// <b>Drei Felder, und keines ist entbehrlich.</b> Ein Hinweis, der nur sagt, was ist, lässt
/// den Leser ratlos; einer, der nur sagt, was zu tun ist, wirkt willkürlich. Der Grund dazwischen
/// ist das, was aus einer Meldung eine Auskunft macht.
/// </remarks>
/// <param name="Title">Was ist — in wenigen Worten.</param>
/// <param name="Reason">Warum das so ist.</param>
/// <param name="Advice">Was zu tun ist.</param>
public sealed record AppWarning(string Title, string Reason, string Advice);

/// <summary>
/// Der Zustand der Anwendung als Ganzes.
/// </summary>
/// <remarks>
/// Unveränderlich und als Ganzes ausgetauscht, damit die Anzeige nie einen halb
/// fortgeschriebenen Stand liest — „verbunden“ neben der Fehlermeldung von vorhin.
/// </remarks>
public sealed record AppStatus
{
    /// <summary>Der Zustand.</summary>
    public required AppState State { get; init; }

    /// <summary>Was gerade gilt — in einem Satz, deutsch.</summary>
    public required string Message { get; init; }

    /// <summary>Die Hinweise; leer, wenn alles in Ordnung ist.</summary>
    public IReadOnlyList<AppWarning> Warnings { get; init; } = [];

    /// <summary>Wann dieser Stand entstand.</summary>
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;

    /// <summary>Der Zustand vor der ersten Prüfung.</summary>
    public static AppStatus Starting { get; } = new()
    {
        State = AppState.NotConfigured,
        Message = "Wird geladen …",
    };

    /// <summary>Gibt es etwas zu beachten?</summary>
    public bool HasWarnings => Warnings.Count > 0;
}
