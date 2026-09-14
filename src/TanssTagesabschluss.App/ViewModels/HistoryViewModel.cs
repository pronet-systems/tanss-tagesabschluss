using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Diagnostics;
using TanssTagesabschluss.App.Runtime;
using TanssTagesabschluss.Workday.Model;

namespace TanssTagesabschluss.App.ViewModels;

/// <summary>
/// Die Mehrtagesübersicht: wo in den letzten Wochen noch etwas offen ist.
/// </summary>
/// <remarks>
/// <para><b>Die Frage dieser Seite ist eine andere als die der Tagesansicht.</b> Dort geht es
/// um „was fehlt an diesem Tag“, hier um „welcher Tag verdient überhaupt einen Blick“. Deshalb
/// steht hier keine Zeile je Lücke, sondern eine je Tag — und ein Klick führt hinüber.</para>
/// <para><b>Der Zeitraum wird in <b>einem</b> Zug geladen.</b> Vierzehn Tage einzeln wären rund
/// sechzig Aufrufe an TANSS; siehe <c>DayService</c>.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IRuntimeContext _context;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private DayRow? _selected;

    [ObservableProperty]
    private bool _onlyOpen = true;

    /// <summary>Baut das Ansichtsmodell.</summary>
    /// <param name="context">Der Zugang zu Zustand und Zusammenbau.</param>
    public HistoryViewModel(IRuntimeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Meldet, dass ein Tag zur Bearbeitung gewählt wurde.</summary>
    public event EventHandler<DateOnly>? DayChosen;

    /// <summary>Die Tage, der jüngste zuerst.</summary>
    public ObservableCollection<DayRow> Days { get; } = [];

    /// <summary>Ist etwas eingerichtet?</summary>
    public bool IsConfigured => _context.Composition is not null;

    /// <summary>Die Zusammenfassung über den ganzen Zeitraum.</summary>
    public string Summary
    {
        get
        {
            if (_all.Count == 0)
            {
                return "Noch nichts geladen.";
            }

            int open = _all.Count(row => row.IsOpen);
            double hours = _all.Sum(row => row.Analysis.GapTotal.TotalHours);

            return open == 0
                ? string.Create(CultureInfo.CurrentCulture,
                    $"{_all.Count} Tage geprüft — nichts offen.")
                : string.Create(CultureInfo.CurrentCulture,
                    $"{open} von {_all.Count} Tagen offen, zusammen {hours:0.0} Stunden ohne erfasste Leistung.");
        }
    }

    /// <summary>Gibt es überhaupt Tage?</summary>
    public bool HasDays => Days.Count > 0;

    private readonly List<DayRow> _all = [];

    /// <summary>Lädt den Zeitraum neu.</summary>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    public async Task ReloadAsync()
    {
        if (_context.Composition is not { } composition)
        {
            Error = "Es ist noch nichts eingerichtet.";
            _all.Clear();
            Days.Clear();
            RaiseDerived();
            return;
        }

        IsLoading = true;
        Error = null;

        try
        {
            DateOnly today = DateOnly.FromDateTime(_context.Clock.GetLocalNow().LocalDateTime.Date);
            DateOnly from = today.AddDays(-(composition.Config.Gaps.HistoryDays - 1));

            IReadOnlyList<DayAnalysis> days = await composition.Days
                .AnalyseAsync(composition.Config.Tanss.EmployeeId, from, today, composition.State,
                              composition.GapOptions)
                .ConfigureAwait(true);

            _all.Clear();
            // Der juengste zuerst: Was gestern fehlt, erinnert man noch; was vor zwei Wochen
            // fehlt, kaum. Die Reihenfolge folgt der Wahrscheinlichkeit, etwas zu retten.
            _all.AddRange(days.OrderByDescending(day => day.Date).Select(day => new DayRow(day)));

            ApplyFilter();
        }
        catch (TanssException ex)
        {
            Error = Redaction.Scrub(ex.Message);
            _all.Clear();
            Days.Clear();
            RaiseDerived();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Öffnet den gewählten Tag in der Tagesansicht.</summary>
    /// <param name="row">Der Tag.</param>
    [RelayCommand]
    private void Open(DayRow? row)
    {
        if (row is not null)
        {
            DayChosen?.Invoke(this, row.Date);
        }
    }

    partial void OnOnlyOpenChanged(bool value) => ApplyFilter();

    /// <summary>
    /// Wendet den Filter an.
    /// </summary>
    /// <remarks>
    /// <b>Vorgabe ist „nur offene“.</b> Eine Liste, in der vierzehn Zeilen stehen und zwei
    /// davon etwas bedeuten, wird überflogen; eine mit zwei Zeilen wird gelesen. Die übrigen
    /// sind mit einem Schalter wieder da.
    /// </remarks>
    private void ApplyFilter()
    {
        Days.Clear();

        foreach (DayRow row in _all.Where(row => !OnlyOpen || row.IsOpen))
        {
            Days.Add(row);
        }

        RaiseDerived();
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasDays));
        OnPropertyChanged(nameof(IsConfigured));
    }
}
