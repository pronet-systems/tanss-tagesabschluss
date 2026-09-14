using System.Globalization;
using System.Runtime.Versioning;
using TanssTagesabschluss.App.Runtime;
using TanssTagesabschluss.Storage.Config;
using TanssTagesabschluss.Workday.Model;

namespace TanssTagesabschluss.App.Services;

/// <summary>Welche der beiden Erinnerungen gemeint ist.</summary>
public enum ReminderKind
{
    /// <summary>Die morgendliche Prüfung des <b>Vortags</b>.</summary>
    Morning,

    /// <summary>Die abendliche Erinnerung für den <b>laufenden</b> Tag.</summary>
    Evening,
}

/// <summary>Was eine Erinnerung gefunden hat.</summary>
/// <param name="Kind">Welche der beiden.</param>
/// <param name="Day">Der geprüfte Tag.</param>
/// <param name="Analysis">Das Ergebnis.</param>
/// <param name="Headline">Die Überschrift der Meldung.</param>
/// <param name="Detail">Der Text darunter.</param>
public sealed record ReminderFinding(ReminderKind Kind, DateOnly Day, DayAnalysis Analysis,
                                     string Headline, string Detail);

/// <summary>
/// Die beiden Erinnerungen: morgens der Vortag, abends der laufende Tag.
/// </summary>
/// <remarks>
/// <para><b>Warum zwei Zeitpunkte mit verschiedenen Fragen.</b> Abends ist der Techniker noch
/// da und weiss noch, was er getan hat — das ist der wertvollere Zeitpunkt, weil sich eine Lücke
/// dann in einer Minute schliessen lässt. Morgens ist der Tag vorbei und die Erinnerung blasser,
/// aber dafür stehen die Zeiten endgültig fest; diese Prüfung fängt auf, was die abendliche
/// verpasst hat, weil jemand schon weg war.</para>
///
/// <para><b>Der Takt ist eine Minute, und der Zeitpunkt wird nicht „getroffen“, sondern
/// überschritten.</b> Ein Dienst, der auf die genaue Minute wartet, verpasst seinen Termin,
/// sobald der Rechner im Ruhezustand war — und Ruhezustand über Mittag ist der Normalfall. Hier
/// wird bei jedem Takt gefragt: Ist der Zeitpunkt heute schon vorbei, und habe ich heute schon
/// gemeldet? Damit kommt die Meldung auch dann, wenn der Rechner erst um 17:30 wieder aufwacht.</para>
///
/// <para><b>Gemeldet wird höchstens einmal je Tag und Art.</b> Alles andere wäre eine Meldung
/// alle sechzig Sekunden — und die erste, die weggeklickt wird, ist die letzte, die jemand liest.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ReminderService : PeriodicService
{
    private DateOnly _lastMorning = DateOnly.MinValue;
    private DateOnly _lastEvening = DateOnly.MinValue;

    /// <summary>Baut den Dienst.</summary>
    /// <param name="context">Der Zugang zu Zustand und Zusammenbau.</param>
    public ReminderService(IRuntimeContext context) : base(context)
    {
    }

    /// <summary>Meldet einen Befund; wird auf dem Strang der Oberfläche ausgelöst.</summary>
    public event EventHandler<ReminderFinding>? Found;

    /// <inheritdoc />
    public override string Name => "Erinnerung";

    /// <inheritdoc />
    public override string Description =>
        "Prüft morgens den Vortag und erinnert abends vor Feierabend an den laufenden Tag.";

    /// <inheritdoc />
    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override string NotConfiguredMessage =>
        "Ruht: Ohne Konfiguration gibt es keine Zeiten, an die erinnert werden könnte.";

    /// <inheritdoc />
    protected override string FaultMessage =>
        "Die letzte Prüfung ist fehlgeschlagen. Der Dienst läuft weiter und versucht es beim "
        + "nächsten Takt erneut; gemeldet wird eine Erinnerung höchstens einmal je Tag.";

    /// <summary>
    /// Prüft, ob eine der beiden Erinnerungen fällig ist, und führt sie gegebenenfalls aus.
    /// </summary>
    /// <inheritdoc />
    protected override async Task<string> RunCycleAsync(RuntimeComposition composition,
                                                        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(composition);

        ReminderSection settings = composition.Config.Reminder;
        DateTimeOffset now = Context.Clock.GetLocalNow();
        DateOnly today = DateOnly.FromDateTime(now.LocalDateTime.Date);

        if (settings.MorningEnabled
            && _lastMorning != today
            && IsDue(settings.MorningTime, now))
        {
            _lastMorning = today;
            return await CheckAsync(composition, ReminderKind.Morning, today.AddDays(-1), ct)
                .ConfigureAwait(false);
        }

        if (settings.EveningEnabled
            && _lastEvening != today
            && IsDue(settings.EveningTime, now))
        {
            _lastEvening = today;
            return await CheckAsync(composition, ReminderKind.Evening, today, ct)
                .ConfigureAwait(false);
        }

        return Waiting(settings, now, today);
    }

    /// <summary>Ist der eingestellte Zeitpunkt heute bereits überschritten?</summary>
    /// <remarks>
    /// Überschritten und nicht getroffen — siehe Klassenkommentar. Eine unlesbare Uhrzeit gilt
    /// als nie fällig: Die Prüfung beim Laden fängt das ab, und ein Dienst, der bei einer
    /// vertippten Einstellung jede Minute meldete, wäre schlimmer als einer, der schweigt.
    /// </remarks>
    private static bool IsDue(string configured, DateTimeOffset now) =>
        ConfigValidator.TryParse(configured, out TimeOnly time)
        && TimeOnly.FromDateTime(now.LocalDateTime) >= time;

    /// <summary>Prüft einen Tag und meldet, wenn etwas offen ist.</summary>
    private async Task<string> CheckAsync(RuntimeComposition composition, ReminderKind kind,
                                          DateOnly day, CancellationToken ct)
    {
        DayAnalysis analysis = await composition.Days
            .AnalyseDayAsync(composition.Config.Tanss.EmployeeId, day, composition.State,
                             composition.GapOptions, ct)
            .ConfigureAwait(false);

        bool quiet = composition.Config.Reminder.OnlyWhenSomethingIsOpen;

        if (quiet && !analysis.NeedsAttention)
        {
            return string.Create(CultureInfo.CurrentCulture,
                $"{Label(kind)} für {day:d}: nichts offen, keine Meldung.");
        }

        (string headline, string detail) = Describe(kind, day, analysis);
        Context.Notifier.Raise(Found, this, new ReminderFinding(kind, day, analysis, headline, detail));

        return string.Create(CultureInfo.CurrentCulture, $"{Label(kind)} für {day:d}: {headline}");
    }

    /// <summary>Die Meldung selbst — Überschrift und Text.</summary>
    /// <remarks>
    /// <b>Die Zahl steht in der Überschrift.</b> Eine Meldung, die im Infobereich aufblitzt,
    /// wird mit halbem Blick gelesen; „2 Lücken, 1:45 Stunden“ ist in diesem halben Blick
    /// erfassbar, „Es wurden Unstimmigkeiten festgestellt“ nicht.
    /// </remarks>
    private static (string Headline, string Detail) Describe(ReminderKind kind, DateOnly day,
                                                             DayAnalysis analysis)
    {
        string when = kind == ReminderKind.Morning
            ? string.Create(CultureInfo.CurrentCulture, $"Gestern ({day:ddd, d. MMMM})")
            : "Heute";

        if (analysis.Verdict == DayVerdict.NoTimeRecorded)
        {
            return ("Keine Zeit erfasst",
                    $"{when} ist kein einziger Zeitstempel vorhanden — und weder Feiertag noch "
                    + "Urlaub erklärt das. Entweder wurde das Ein- und Ausstempeln vergessen, "
                    + "oder es war ein freier Tag, der in TANSS nicht hinterlegt ist.");
        }

        if (analysis.Gaps.Count == 0)
        {
            return ("Alles erfasst",
                    $"{when} ist die Arbeitszeit vollständig durch Leistungen belegt.");
        }

        string count = analysis.Gaps.Count == 1 ? "1 Lücke" : string.Create(
            CultureInfo.CurrentCulture, $"{analysis.Gaps.Count} Lücken");

        string total = string.Create(CultureInfo.CurrentCulture,
            $"{analysis.GapTotal.TotalHours:0.0} Stunden");

        string first = string.Join(", ", analysis.Gaps.Take(3).Select(gap => gap.Segment.ToString()));
        string more = analysis.Gaps.Count > 3 ? " …" : string.Empty;

        return ($"{count}, {total}",
                $"{when} fehlen Leistungen für: {first}{more}. " + (kind == ReminderKind.Evening
                    ? "Jetzt nachzutragen ist leichter als morgen früh."
                    : "Die Zeiten des Vortags stehen fest; was jetzt fehlt, fehlt endgültig."));
    }

    /// <summary>Der Satz, der zwischen zwei Erinnerungen im Betriebszustand steht.</summary>
    private static string Waiting(ReminderSection settings, DateTimeOffset now, DateOnly today)
    {
        List<string> parts = [];

        if (settings.MorningEnabled)
        {
            parts.Add($"morgens {settings.MorningTime}");
        }

        if (settings.EveningEnabled)
        {
            parts.Add($"abends {settings.EveningTime}");
        }

        if (parts.Count == 0)
        {
            return "Beide Erinnerungen sind abgeschaltet; es wird nichts geprüft.";
        }

        return string.Create(CultureInfo.CurrentCulture,
            $"Wartet ({string.Join(", ", parts)}). Es ist {now:HH:mm} am {today:d}.");
    }

    private static string Label(ReminderKind kind) =>
        kind == ReminderKind.Morning ? "Morgendliche Prüfung" : "Abendliche Erinnerung";
}
