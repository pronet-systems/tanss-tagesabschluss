using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Diagnostics;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.App.Ai;
using TanssTagesabschluss.App.Runtime;
using TanssTagesabschluss.App.Services;
using TanssTagesabschluss.Workday.Model;

namespace TanssTagesabschluss.App.ViewModels;

/// <summary>
/// Die Tagesansicht: ein Tag, seine Lücken, und der Weg, sie zu schliessen.
/// </summary>
/// <remarks>
/// <para><b>Ein Tag auf einmal, und zwischen den Tagen wird geblättert.</b> Eine Ansicht, die
/// zwei Wochen gleichzeitig zeigt, zeigt von jedem Tag zu wenig, um etwas nachzutragen — und
/// nachtragen ist der Zweck. Die Mehrtagesübersicht steht auf einer eigenen Seite und führt
/// hierher.</para>
///
/// <para><b>Geladen wird nach jedem Wechsel des Tages neu.</b> Ein Zwischenspeicher über die
/// Tage wäre verlockend, führte aber dazu, dass eine soeben in TANSS erfasste Leistung nicht
/// auftaucht — und der Techniker sie ein zweites Mal erfasst.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class DayViewModel : ObservableObject
{
    private readonly IRuntimeContext _context;

    /// <summary>
    /// Die Geräte je Firma, einmal geholt.
    /// </summary>
    /// <remarks>
    /// An einem Tag stehen die Lücken oft beim selben Kunden. Ohne diesen Zwischenspeicher
    /// ginge je Lücke und Ticketwechsel eine eigene Abfrage hinaus — bei fünf Lücken derselben
    /// Firma fünfmal dieselbe.
    /// </remarks>
    private readonly Dictionary<int, IReadOnlyList<DeviceRow>> _devicesByCompany = [];

    private CancellationTokenSource? _running;

    /// <summary>
    /// Der Zugang zum Sprachmodell, oder <see langword="null"/>, wenn keiner zusteht.
    /// </summary>
    /// <remarks>
    /// <b>Einmal je Ladevorgang geholt und nicht je Zeile.</b> Der Zugang trägt den Schlüssel;
    /// ihn zwanzigmal zu bauen hiesse, ihn zwanzigmal aus dem Schlüsselspeicher zu entsiegeln.
    /// Er wird beim nächsten Laden verworfen und neu geholt — so wirkt eine geänderte
    /// Einstellung ohne Neustart.
    /// </remarks>
    private IAiAssistant? _ai;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private DayAnalysis? _analysis;

    /// <summary>Baut das Ansichtsmodell.</summary>
    /// <param name="context">Der Zugang zu Zustand und Zusammenbau.</param>
    public DayViewModel(IRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _selectedDate = context.Clock.GetLocalNow().LocalDateTime.Date;
    }

    /// <summary>Die Lücken des Tages, in der Reihenfolge des Tages.</summary>
    public ObservableCollection<GapRow> Gaps { get; } = [];

    /// <summary>Die bereits erfassten Leistungen des Tages.</summary>
    public ObservableCollection<SupportRow> Supports { get; } = [];

    /// <summary>Die Hinweise zur Rechnung — was nicht ermittelt werden konnte.</summary>
    public ObservableCollection<string> Notes { get; } = [];

    /// <summary>Ist etwas eingerichtet?</summary>
    public bool IsConfigured => _context.Composition is not null;

    /// <summary>Der Tag als Überschrift.</summary>
    public string DateHeadline =>
        string.Create(CultureInfo.CurrentCulture, $"{SelectedDate:dddd, d. MMMM yyyy}");

    /// <summary>Das Urteil über den Tag, in einem Satz.</summary>
    public string VerdictText => Analysis is not { } day
        ? "Noch nichts geladen."
        : day.Verdict switch
        {
            DayVerdict.HasGaps => string.Create(CultureInfo.CurrentCulture,
                $"{day.Gaps.Count} Lücke(n) ohne erfasste Leistung — zusammen {day.GapTotal.TotalHours:0.0} Stunden."),
            DayVerdict.Complete => string.Create(CultureInfo.CurrentCulture,
                $"Vollständig erfasst: {day.AttendanceTotal.TotalHours:0.0} Stunden Anwesenheit, keine Lücke."),
            DayVerdict.NoTimeRecorded =>
                "Für diesen Tag ist keine Zeit erfasst — und weder ein Feiertag noch eine "
                + "Abwesenheit erklärt das.",
            DayVerdict.Ongoing => string.Create(CultureInfo.CurrentCulture,
                $"Der Tag läuft noch. Bis jetzt: {day.AttendanceTotal.TotalHours:0.0} Stunden, keine Lücke."),
            DayVerdict.DayOff => DayOffText(day),
            _ => "—",
        };

    /// <summary>Wie viel der Anwesenheit belegt ist, in Prozent.</summary>
    public double CoveragePercent => Analysis is { } day ? Math.Round(day.Coverage * 100) : 0;

    /// <summary>
    /// Der gedeckte Anteil des Balkens, als Sternmaß.
    /// </summary>
    /// <remarks>
    /// <b>Zwei Sternspalten statt einer Breite in Punkten.</b> Eine feste Breite müsste die
    /// Breite des Fensters kennen und bei jeder Grössenänderung nachgerechnet werden. Ein
    /// Sternverhältnis überlässt das dem Layout — und es stimmt auch dann noch, wenn jemand
    /// das Fenster zieht.
    /// </remarks>
    public GridLength CoverageStar => new(Math.Max(0.0001, Coverage), GridUnitType.Star);

    /// <summary>Der offene Anteil des Balkens, als Sternmaß.</summary>
    public GridLength RemainderStar => new(Math.Max(0.0001, 1 - Coverage), GridUnitType.Star);

    /// <summary>Der gedeckte Anteil, von 0 bis 1.</summary>
    private double Coverage => Analysis is { } day ? day.Coverage : 0d;

    /// <summary>Die Deckung als Text.</summary>
    public string CoverageText => Analysis is not { } day || day.AttendanceTotal == TimeSpan.Zero
        ? "keine Anwesenheit"
        : string.Create(CultureInfo.CurrentCulture,
            $"{day.RecordedTotal.TotalHours:0.0} von {day.AttendanceTotal.TotalHours:0.0} Stunden erfasst ({CoveragePercent:0} %)");

    /// <summary>Die gestempelten Pausen, als Text.</summary>
    public string PauseText => Analysis is not { Pauses.Count: > 0 } day
        ? "keine gestempelte Pause"
        : "Pause: " + string.Join(", ", day.Pauses.Select(pause => pause.ToString()));

    /// <summary>Gibt es an diesem Tag etwas zu tun?</summary>
    public bool NeedsAttention => Analysis?.NeedsAttention ?? false;

    /// <summary>Gibt es Lücken zum Nachtragen?</summary>
    public bool HasGaps => Gaps.Count > 0;

    /// <summary>Gibt es Hinweise?</summary>
    public bool HasNotes => Notes.Count > 0;

    /// <summary>Gibt es bereits erfasste Leistungen?</summary>
    public bool HasSupports => Supports.Count > 0;

    /// <summary>Ist der gezeigte Tag heute?</summary>
    public bool IsToday => SelectedDate.Date == _context.Clock.GetLocalNow().LocalDateTime.Date;

    /// <summary>Blättert einen Tag zurück.</summary>
    [RelayCommand]
    private void PreviousDay() => SelectedDate = SelectedDate.AddDays(-1);

    /// <summary>Blättert einen Tag vor.</summary>
    [RelayCommand]
    private void NextDay() => SelectedDate = SelectedDate.AddDays(1);

    /// <summary>Springt auf heute.</summary>
    [RelayCommand]
    private void Today() => SelectedDate = _context.Clock.GetLocalNow().LocalDateTime.Date;

    /// <summary>Lädt den gezeigten Tag neu.</summary>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    public async Task ReloadAsync()
    {
        // Ein noch laufender Ladevorgang wird abgebrochen: Wer schnell durch die Tage
        // blaettert, soll am Ende den zuletzt gewaehlten Tag sehen und nicht den, dessen
        // Abfrage zufaellig als letzte zurueckkam.
        CancellationTokenSource source = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _running, source);
        if (previous is not null)
        {
            await previous.CancelAsync().ConfigureAwait(true);
            previous.Dispose();
        }

        if (_context.Composition is not { } composition)
        {
            Error = "Es ist noch nichts eingerichtet. Unter „Einstellungen“ lässt sich die "
                    + "Verbindung zu TANSS herstellen.";
            Clear();
            return;
        }

        IsLoading = true;
        Error = null;

        try
        {
            DateOnly day = DateOnly.FromDateTime(SelectedDate.Date);

            DayAnalysis analysis = await composition.Days
                .AnalyseDayAsync(composition.Config.Tanss.EmployeeId, day, composition.State,
                                 composition.GapOptions, source.Token)
                .ConfigureAwait(true);

            IReadOnlyList<TicketRow> tickets = await LoadTicketsAsync(composition, source.Token)
                .ConfigureAwait(true);

            if (source.Token.IsCancellationRequested)
            {
                return;
            }

            // Der Zugang wird je Ladevorgang erneuert: So wirkt eine geaenderte Einstellung,
            // ohne dass jemand das Fenster schliessen muss.
            _ai?.Dispose();
            _ai = AiGateway.TryCreate(composition.Config, out _);

            Apply(analysis, tickets, composition);
        }
        catch (OperationCanceledException)
        {
            // Ein abgebrochener Ladevorgang ist kein Fehler, sondern ein weitergeblaetterter Tag.
        }
        catch (TanssException ex)
        {
            Error = Redaction.Scrub(ex.Message);
            Clear();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Übernimmt ein Ergebnis in die Anzeige.</summary>
    private void Apply(DayAnalysis analysis, IReadOnlyList<TicketRow> tickets,
                       RuntimeComposition composition)
    {
        Analysis = analysis;

        Gaps.Clear();
        foreach (Gap gap in analysis.Gaps)
        {
            Gaps.Add(new GapRow(
                gap,
                tickets,
                (row, ct) => BookAsync(composition, row, ct),
                (companyId, ct) => DevicesAsync(composition, companyId, ct),
                _ai is null ? null : (text, task, ct) => ReviseAsync(text, task, ct)));
        }

        Supports.Clear();
        foreach (SupportEntry entry in analysis.Supports)
        {
            Supports.Add(new SupportRow(entry));
        }

        Notes.Clear();
        foreach (string note in analysis.Notes)
        {
            Notes.Add(note);
        }

        RaiseDerived();
    }

    /// <summary>Trägt eine Lücke nach.</summary>
    /// <remarks>
    /// <b>Die Firma kommt aus dem gewählten Ticket und nicht aus dem Vorschlag der Lücke.</b>
    /// Das Ticket ist das, was der Techniker gerade bestätigt hat; der Vorschlag war eine
    /// Vermutung aus den Nachbarinnen.
    /// </remarks>
    private static async Task<BookingResult> BookAsync(RuntimeComposition composition, GapRow row,
                                                       CancellationToken ct)
    {
        int ticketId = row.SelectedTicket?.Id ?? 0;
        int companyId = row.SelectedTicket?.CompanyId ?? row.Gap.SuggestedCompanyId;
        DeviceRow device = row.SelectedDevice ?? DeviceRow.None;

        BookingTarget target = new(ticketId, companyId, device.LinkTypeId, device.LinkId,
                                   row.IsInternal);

        return await composition.Booking
            .BookAsync(row.Gap.Start, row.Gap.Duration, row.Text, target, ct)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Die Geräte einer Firma — einmal geholt, dann aus dem Zwischenspeicher.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Ohne Geräteliste wird ohne Gerät gebucht; das ist eine Verfeinerung
    /// weniger und keine verlorene Leistung. Der Fehlschlag wird auch nicht zwischengespeichert
    /// — beim nächsten Ticketwechsel darf es wieder gelingen.
    /// </remarks>
    private async Task<IReadOnlyList<DeviceRow>> DevicesAsync(RuntimeComposition composition,
                                                              int companyId, CancellationToken ct)
    {
        if (_devicesByCompany.TryGetValue(companyId, out IReadOnlyList<DeviceRow>? cached))
        {
            return cached;
        }

        try
        {
            IReadOnlyList<Device> devices = await composition.Devices
                .ListAsync(companyId, ct).ConfigureAwait(true);

            List<DeviceRow> rows = [.. devices.Select(device => new DeviceRow(device))];
            _devicesByCompany[companyId] = rows;
            return rows;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException)
        {
            return [];
        }
    }

    /// <summary>
    /// Die offenen Tickets des Technikers.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Die Ticketauswahl ist eine Bequemlichkeit; ohne sie bleibt die Liste
    /// leer, und die Lücken sind trotzdem zu sehen. Ein Aussetzer der Leitung darf nicht die
    /// ganze Tagesansicht kosten.
    /// </remarks>
    private static async Task<IReadOnlyList<TicketRow>> LoadTicketsAsync(
        RuntimeComposition composition, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<Ticket> tickets = await composition.Tickets
                .SearchOpenAsync(composition.Config.Tanss.EmployeeId, ct).ConfigureAwait(true);

            return [.. tickets.Select(ticket => new TicketRow(ticket))];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException)
        {
            return [];
        }
    }

    /// <summary>
    /// Lässt einen Text vom Sprachmodell überarbeiten.
    /// </summary>
    /// <remarks>
    /// Wirft weiter, wenn es fehlschlägt: Die Zeile selbst entscheidet, was das bedeutet — sie
    /// behält dann ihren bisherigen Text und zeigt den Grund an.
    /// </remarks>
    private async Task<string> ReviseAsync(string text, AiTask task, CancellationToken ct)
    {
        if (_ai is null || _context.Composition?.Config is not { } config)
        {
            return text;
        }

        return await _ai.ReviseAsync(text, task, config.Ai.Model, ct).ConfigureAwait(true);
    }

    private void Clear()
    {
        Analysis = null;
        Gaps.Clear();
        Supports.Clear();
        Notes.Clear();
        RaiseDerived();
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(DateHeadline));
        OnPropertyChanged(nameof(IsToday));
        _ = ReloadAsync();
    }

    partial void OnAnalysisChanged(DayAnalysis? value) => RaiseDerived();

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(VerdictText));
        OnPropertyChanged(nameof(CoveragePercent));
        OnPropertyChanged(nameof(CoverageText));
        OnPropertyChanged(nameof(CoverageStar));
        OnPropertyChanged(nameof(RemainderStar));
        OnPropertyChanged(nameof(PauseText));
        OnPropertyChanged(nameof(NeedsAttention));
        OnPropertyChanged(nameof(HasGaps));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasSupports));
        OnPropertyChanged(nameof(IsConfigured));
    }

    /// <summary>Warum dieser Tag frei war — mit Namen, nicht nur als „frei“.</summary>
    private static string DayOffText(DayAnalysis day) => day.Kind switch
    {
        DayKind.Holiday => $"Feiertag: {day.HolidayName}. Es war nichts zu erfassen.",
        DayKind.Absence => $"{day.AbsenceLabel ?? "Abwesend"} — ganztägig. Es war nichts zu erfassen.",
        DayKind.Weekend => "Kein Arbeitstag nach dem Arbeitszeitmodell.",
        _ => "Freier Tag.",
    };
}
