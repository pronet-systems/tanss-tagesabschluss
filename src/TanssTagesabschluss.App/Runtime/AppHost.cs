using System.IO;
using System.Runtime.Versioning;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Diagnostics;
using TanssTagesabschluss.App.Services;
using TanssTagesabschluss.Storage;
using TanssTagesabschluss.Storage.Config;

namespace TanssTagesabschluss.App.Runtime;

/// <summary>
/// Was die Oberfläche über den laufenden Betrieb wissen darf.
/// </summary>
/// <remarks>
/// Eine Schnittstelle, damit Ansichtsmodelle sich gegen eine Attrappe prüfen lassen — ohne
/// Konfiguration, ohne Netz und ohne Fenster.
/// </remarks>
public interface IRuntimeContext
{
    /// <summary>Der Zusammenbau; <see langword="null"/>, solange nichts eingerichtet ist.</summary>
    RuntimeComposition? Composition { get; }

    /// <summary>Der aktuelle Betriebszustand.</summary>
    AppStatus Status { get; }

    /// <summary>Die Uhr.</summary>
    TimeProvider Clock { get; }

    /// <summary>Bringt Meldungen auf den Strang der Oberfläche.</summary>
    RuntimeNotifier Notifier { get; }

    /// <summary>Meldet jeden neuen Betriebszustand; wird auf dem Strang der Oberfläche ausgelöst.</summary>
    event EventHandler<AppStatus>? StatusChanged;
}

/// <summary>
/// Hält Konfiguration, Zusammenbau und Hintergrunddienste zusammen.
/// </summary>
/// <remarks>
/// <para><b>Die Anwendung startet auch ohne Konfiguration.</b> Ein frisch aufgesetzter Rechner
/// hat keine, und das ist kein Fehlerfall, sondern der erste Start: Die Oberfläche geht auf,
/// sagt, was fehlt, und bietet die Einrichtung an. Ein Programm, das dabei mit einer Ausnahme
/// abbricht, lässt sich nicht einrichten.</para>
///
/// <para><b>Neu laden heisst: alles neu bauen.</b> Nach einer geänderten Konfiguration ist die
/// Basisadresse womöglich eine andere. Ein Dienst, der sich seinen Zusammenbau gemerkt hätte,
/// arbeitete weiter gegen die alte Instanz.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class AppHost : IRuntimeContext, IDisposable
{
    private readonly IConfigStore _store;
    private readonly List<IBackgroundService> _services = [];
    private RuntimeComposition? _composition;
    private bool _disposed;

    /// <summary>Baut den Wirt.</summary>
    /// <param name="store">Woher die Konfiguration kommt.</param>
    /// <param name="clock">Die Uhr; für Tests einsetzbar.</param>
    /// <param name="notifier">
    /// Die Umlenkung auf den Strang der Oberfläche. Sie muss <b>auf diesem Strang</b> entstehen —
    /// siehe <see cref="RuntimeNotifier"/>.
    /// </param>
    public AppHost(IConfigStore store, TimeProvider? clock = null, RuntimeNotifier? notifier = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        Clock = clock ?? TimeProvider.System;
        Notifier = notifier ?? new RuntimeNotifier();
        Status = AppStatus.Starting;
    }

    /// <inheritdoc />
    public event EventHandler<AppStatus>? StatusChanged;

    /// <inheritdoc />
    public RuntimeComposition? Composition => _composition;

    /// <summary>
    /// Die geladene Konfiguration; <see langword="null"/>, solange nichts eingerichtet ist.
    /// </summary>
    /// <remarks>
    /// Eine Abkürzung auf <c>Composition?.Config</c> und kein zweiter Speicher: Wer die
    /// Einstellungen lesen will, soll nicht erst durch den Zusammenbau greifen müssen — und
    /// ein eigenes Feld liefe nach einem erneuten Laden auseinander.
    /// </remarks>
    public AppConfig? Config => _composition?.Config;

    /// <inheritdoc />
    public AppStatus Status { get; private set; }

    /// <inheritdoc />
    public TimeProvider Clock { get; }

    /// <inheritdoc />
    public RuntimeNotifier Notifier { get; }

    /// <summary>Die Hintergrunddienste — für die Diagnoseseite.</summary>
    public IReadOnlyList<IBackgroundService> Services => _services;

    /// <summary>Nimmt einen Hintergrunddienst auf. Vor <see cref="StartAsync"/> aufzurufen.</summary>
    /// <param name="service">Der Dienst.</param>
    public void Add(IBackgroundService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _services.Add(service);
    }

    /// <summary>
    /// Lädt die Konfiguration und baut alles neu.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Jeder Fehlschlag wird zu einem Betriebszustand mit Hinweisen — die
    /// Oberfläche soll aufgehen und sagen, was fehlt, statt gar nicht erst zu erscheinen.
    /// </remarks>
    /// <returns><c>true</c>, wenn danach ein Zusammenbau vorliegt.</returns>
    public bool Reload()
    {
        _composition?.Dispose();
        _composition = null;

        if (!_store.Exists())
        {
            Report(new AppStatus
            {
                State = AppState.NotConfigured,
                Message = "Noch nicht eingerichtet.",
                Warnings =
                [
                    new AppWarning(
                        "Keine Konfiguration",
                        $"Unter \"{_store.Path}\" liegt keine Datei. Ohne Basisadresse und "
                        + "Mitarbeiterkennung gibt es keine Instanz, deren Zeiten geprüft werden "
                        + "könnten.",
                        "Die Einrichtung führt in wenigen Schritten hindurch: Adresse der "
                        + "TANSS-Instanz, Anmeldung, danach prägt sich das Werkzeug selbst ein "
                        + "Arbeitstoken."),
                ],
            });

            return false;
        }

        AppConfig config;
        try
        {
            config = _store.Load();
        }
        catch (ConfigValidationException ex)
        {
            Report(new AppStatus
            {
                State = AppState.NotConfigured,
                Message = "Die Konfiguration ist nicht verwendbar.",
                Warnings = [.. ex.Problems.Select(problem => new AppWarning(
                    "Einstellung beanstandet", problem,
                    $"Die Datei liegt unter \"{_store.Path}\". Sie lässt sich dort berichtigen "
                    + "oder über die Einrichtung neu schreiben."))],
            });

            return false;
        }
        catch (ConfigException ex)
        {
            Report(new AppStatus
            {
                State = AppState.NotConfigured,
                Message = "Die Konfiguration liess sich nicht laden.",
                Warnings =
                [
                    new AppWarning("Konfiguration unlesbar", Redaction.Scrub(ex.Message),
                                   $"Die Datei liegt unter \"{_store.Path}\"."),
                ],
            });

            return false;
        }

        try
        {
            _composition = new RuntimeComposition(config, Clock);
        }
        catch (Exception ex) when (ex is TanssException or IOException or UnauthorizedAccessException)
        {
            Report(new AppStatus
            {
                State = AppState.Degraded,
                Message = "Der Zugang liess sich nicht aufbauen.",
                Warnings =
                [
                    new AppWarning("Zugang nicht aufgebaut", Redaction.Scrub(ex.Message),
                                   "Zu prüfen sind Basisadresse, Netzverbindung und Proxy."),
                ],
            });

            return false;
        }

        Report(BuildStatus(config));
        return true;
    }

    /// <summary>Startet alle Hintergrunddienste.</summary>
    /// <param name="ct">Abbruchmarke für den Start selbst.</param>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    public async Task StartAsync(CancellationToken ct = default)
    {
        foreach (IBackgroundService service in _services)
        {
            await service.StartAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Hält alle Hintergrunddienste an.</summary>
    /// <param name="ct">Abbruchmarke; sie begrenzt das Warten.</param>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    public async Task StopAsync(CancellationToken ct = default)
    {
        foreach (IBackgroundService service in _services)
        {
            await service.StopAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Setzt den Betriebszustand und meldet ihn auf dem Strang der Oberfläche.</summary>
    /// <param name="status">Der neue Zustand.</param>
    public void Report(AppStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        Status = status;
        Notifier.Raise(StatusChanged, this, status);
    }

    /// <summary>Gibt den Zusammenbau frei.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (IDisposable service in _services.OfType<IDisposable>())
        {
            service.Dispose();
        }

        _composition?.Dispose();
        _composition = null;
    }

    /// <summary>
    /// Der Zustand nach erfolgreichem Laden — samt der Hinweise, die kein Fehler sind.
    /// </summary>
    /// <remarks>
    /// <b>Das fehlende Bundesland ist der wichtigste davon.</b> Ohne es rechnet das Werkzeug
    /// ohne Feiertage weiter und meldet an Weihnachten einen fehlenden Arbeitstag. Das ist kein
    /// Fehler, der den Betrieb hindert — aber es gehört gesagt, und zwar bevor es auffällt.
    /// </remarks>
    private static AppStatus BuildStatus(AppConfig config)
    {
        List<AppWarning> warnings = [];

        if (string.IsNullOrWhiteSpace(config.Company.FederalState))
        {
            warnings.Add(new AppWarning(
                "Bundesland unbekannt",
                "Ohne Bundesland lassen sich die gesetzlichen Feiertage nicht bestimmen. An "
                + "einem Feiertag, an dem niemand gestempelt hat, erscheint dann der Hinweis "
                + "„keine Zeit erfasst“ — obwohl nichts zu erfassen war.",
                "Unter „Einstellungen“ die eigene Firma ermitteln lassen; aus deren "
                + "Postleitzahl folgt das Bundesland. Von Hand wählen geht ebenso."));
        }

        if (config.Company.OwnCompanyId <= 0)
        {
            warnings.Add(new AppWarning(
                "Eigene Firma nicht hinterlegt",
                "Sie wird gebraucht, um aus der Postleitzahl das Bundesland zu bestimmen.",
                "Unter „Einstellungen“ ermitteln lassen oder auswählen."));
        }

        if (!config.Reminder.MorningEnabled && !config.Reminder.EveningEnabled)
        {
            warnings.Add(new AppWarning(
                "Keine Erinnerung eingeschaltet",
                "Weder die morgendliche Prüfung noch die abendliche Erinnerung läuft. Das "
                + "Werkzeug meldet sich dann von sich aus nie — gefunden werden Lücken nur noch, "
                + "wenn jemand das Fenster öffnet.",
                "Unter „Einstellungen“ mindestens eine der beiden einschalten."));
        }

        // Die Hinweise oben sind Empfehlungen und keine Stoerung: Ohne Bundesland rechnet das
        // Werkzeug weiter, nur vorsichtiger. Ein "gestoert" an dieser Stelle machte die Plakette
        // zur Dauerwarnung - und eine Dauerwarnung liest nach drei Tagen niemand mehr.
        return new AppStatus
        {
            State = AppState.Connected,
            Message = config.Tanss.BaseUrl,
            Warnings = warnings,
        };
    }
}
