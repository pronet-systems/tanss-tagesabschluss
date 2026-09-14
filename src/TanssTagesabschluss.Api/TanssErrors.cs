namespace TanssTagesabschluss.Api;

/// <summary>Basis aller Fehler, die aus dem Umgang mit TANSS stammen.</summary>
public class TanssException : Exception
{
    /// <summary>Der HTTP-Status, sofern es einen gab.</summary>
    public int? Status { get; }

    /// <summary>Der Wert aus <c>error.text</c>, etwa <c>CAN_ONLY_EDIT_OWN_TIMESTAMPS</c>.</summary>
    public string? Detail { get; }

    /// <summary>Baut den Fehler mit allem, was über ihn bekannt ist.</summary>
    /// <param name="message">Was ist, warum, und was zu tun ist — in dieser Reihenfolge.</param>
    /// <param name="status">Der HTTP-Status, sofern einer vorliegt.</param>
    /// <param name="detail">Der Kennwert aus <c>error.text</c> oder <c>error.type</c>.</param>
    /// <param name="inner">Die zugrunde liegende Ausnahme.</param>
    public TanssException(string message, int? status = null, string? detail = null,
                          Exception? inner = null)
        : base(message, inner)
    {
        Status = status;
        Detail = detail;
    }
}

/// <summary>TANSS war nicht erreichbar, oder die Verbindung brach ab.</summary>
public sealed class TanssUnreachableException : TanssException
{
    /// <summary>Baut den Fehler.</summary>
    /// <param name="message">Die Meldung.</param>
    /// <param name="inner">Die zugrunde liegende Ausnahme.</param>
    public TanssUnreachableException(string message, Exception? inner = null)
        : base(message, inner: inner) { }
}

/// <summary>
/// Zugriff verweigert (401/403). Die Nachricht nennt die üblichen Ursachen, weil
/// eine nackte 403 den Aufrufer sonst ratlos lässt.
/// </summary>
public sealed class TanssAuthException : TanssException
{
    /// <summary>Baut den Fehler.</summary>
    /// <param name="message">Die Meldung.</param>
    /// <param name="status">Der HTTP-Status.</param>
    /// <param name="detail">Der Kennwert aus der Antwort.</param>
    public TanssAuthException(string message, int? status = null, string? detail = null)
        : base(message, status, detail) { }
}

/// <summary>Das angefragte Objekt gibt es nicht.</summary>
public sealed class TanssNotFoundException : TanssException
{
    /// <summary>Baut den Fehler.</summary>
    /// <param name="message">Die Meldung.</param>
    /// <param name="status">Der HTTP-Status.</param>
    /// <param name="detail">Der Kennwert aus der Antwort.</param>
    public TanssNotFoundException(string message, int? status = null, string? detail = null)
        : base(message, status, detail) { }
}

/// <summary>
/// Die Einstellungen taugen nicht — zuallererst die Basisadresse.
/// </summary>
/// <remarks>
/// Getrennt von <see cref="TanssUnreachableException"/>, weil der Aufrufer anders reagieren
/// muss: hier hilft kein zweiter Versuch und kein Warten, sondern nur eine berichtigte
/// Einstellung. Entsprechend gilt dieser Fehler nie als vorübergehend.
/// </remarks>
public sealed class TanssConfigurationException : TanssException
{
    /// <summary>Baut den Fehler.</summary>
    /// <param name="message">Die Meldung.</param>
    /// <param name="inner">Die zugrunde liegende Ausnahme.</param>
    public TanssConfigurationException(string message, Exception? inner = null)
        : base(message, inner: inner) { }
}

/// <summary>
/// Ein Modul ist auf der Instanz nicht lizenziert.
/// </summary>
/// <remarks>
/// Für dieses Werkzeug ist das der wichtigste einzelne Fehlschlag überhaupt: Ohne das Modul
/// <b>Zeiterfassung</b> gibt es keine Zeitstempel, ohne Zeitstempel keine Arbeitszeit — und
/// ohne Arbeitszeit lässt sich eine Lücke nicht von einem Feierabend unterscheiden. Das
/// Werkzeug sagt das ausdrücklich, statt einen leeren Tag anzuzeigen.
/// </remarks>
public sealed class TanssModuleNotLicensedException : TanssException
{
    /// <summary>Baut den Fehler.</summary>
    /// <param name="message">Die Meldung.</param>
    /// <param name="status">Der HTTP-Status.</param>
    /// <param name="detail">Der Kennwert aus der Antwort.</param>
    public TanssModuleNotLicensedException(string message, int? status = null, string? detail = null)
        : base(message, status, detail) { }
}

/// <summary>
/// Dem angemeldeten Mitarbeiter fehlt das Recht, die Zeiten eines anderen zu sehen.
/// </summary>
/// <remarks>
/// <para>Eine eigene Art, weil die Handlungsanweisung eine andere ist als bei einer
/// gewöhnlichen 403: Hier ist weder das Token falsch noch die Adresse, sondern es fehlt in
/// TANSS ein Recht (476 „Auswertung von Zeiten“ und seine Abkömmlinge). Das vergibt ein
/// TANSS-Administrator, kein Neustart.</para>
/// <para>Für die eigenen Zeiten tritt dieser Fehler nicht auf — deshalb fragt dieses Werkzeug
/// im Regelfall ausschliesslich nach dem angemeldeten Mitarbeiter.</para>
/// </remarks>
public sealed class TanssTimeAccessException : TanssException
{
    /// <summary>Baut den Fehler.</summary>
    /// <param name="message">Die Meldung.</param>
    /// <param name="status">Der HTTP-Status.</param>
    /// <param name="detail">Der Kennwert aus der Antwort.</param>
    public TanssTimeAccessException(string message, int? status = null, string? detail = null)
        : base(message, status, detail) { }
}
