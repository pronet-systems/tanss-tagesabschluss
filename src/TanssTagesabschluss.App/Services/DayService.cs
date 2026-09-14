using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.Workday;
using TanssTagesabschluss.Workday.Holidays;
using TanssTagesabschluss.Workday.Model;

namespace TanssTagesabschluss.App.Services;

/// <summary>
/// Holt alles, was zu einem Zeitraum gehört, und lässt den <see cref="GapFinder"/> rechnen.
/// </summary>
/// <remarks>
/// <para><b>Vier Quellen, ein Ergebnis.</b> Zeitstempel, Leistungen und Abwesenheiten kommen aus
/// TANSS, die Feiertage aus einem fremden Dienst. Sie hier zusammenzuführen und nicht in der
/// Oberfläche hat einen handfesten Grund: Jede der vier kann für sich fehlschlagen, und was ein
/// Fehlschlag jeweils bedeutet, ist eine fachliche Frage und keine der Anzeige.</para>
///
/// <para><b>Für einen ganzen Zeitraum wird einmal gefragt, nicht je Tag.</b> Vierzehn Tage
/// einzeln abzufragen wären rund sechzig Aufrufe an TANSS; so sind es vier. Das ist nicht nur
/// schneller — es ist der Unterschied zwischen einer Übersicht, die beim Öffnen dasteht, und
/// einer, bei der man zusieht, wie sie sich füllt.</para>
///
/// <para><b>Was hier fehlschlägt und was nicht.</b> Ohne Zeitstempel gibt es keine Anwesenheit
/// und damit keine Aussage — das wirft. Ohne Leistungen wäre jeder Tag eine einzige Lücke — das
/// wirft ebenfalls. Ohne Abwesenheiten und ohne Feiertage lässt sich dagegen weiterrechnen: Das
/// Ergebnis ist dann vorsichtiger als nötig und sagt das in seinen Hinweisen.</para>
/// </remarks>
public sealed class DayService
{
    private readonly ITimestampRepository _timestamps;
    private readonly ISupportRepository _supports;
    private readonly IAbsenceRepository _absences;
    private readonly IHolidayProvider _holidays;
    private readonly TimeProvider _clock;

    /// <summary>Baut den Dienst.</summary>
    /// <param name="timestamps">Die Zeiterfassung.</param>
    /// <param name="supports">Die Leistungen.</param>
    /// <param name="absences">Die Abwesenheiten.</param>
    /// <param name="holidays">Die Feiertage.</param>
    /// <param name="clock">Die Uhr; für Tests einsetzbar.</param>
    public DayService(ITimestampRepository timestamps, ISupportRepository supports,
                      IAbsenceRepository absences, IHolidayProvider holidays,
                      TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentNullException.ThrowIfNull(supports);
        ArgumentNullException.ThrowIfNull(absences);
        ArgumentNullException.ThrowIfNull(holidays);

        _timestamps = timestamps;
        _supports = supports;
        _absences = absences;
        _holidays = holidays;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Beurteilt jeden Tag eines Zeitraums.
    /// </summary>
    /// <param name="employeeId">Der Mitarbeiter.</param>
    /// <param name="from">Erster Tag, einschließlich.</param>
    /// <param name="until">Letzter Tag, einschließlich.</param>
    /// <param name="state">
    /// Das Bundesland für die Feiertage; <see langword="null"/>, solange keines feststeht. Dann
    /// wird ohne Feiertage gerechnet, und jeder betroffene Tag sagt das.
    /// </param>
    /// <param name="options">Was als Lücke gilt.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Ein Ergebnis je Tag, aufsteigend nach Datum.</returns>
    public async Task<IReadOnlyList<DayAnalysis>> AnalyseAsync(
        int employeeId, DateOnly from, DateOnly until, FederalState? state,
        GapOptions? options = null, CancellationToken ct = default)
    {
        if (until < from)
        {
            throw new ArgumentOutOfRangeException(
                nameof(until), "Der letzte Tag liegt vor dem ersten.");
        }

        // Die beiden tragenden Abfragen. Sie laufen nebeneinander, weil keine von der anderen
        // abhaengt - und weil die Zeitauswertung ueber zwei Wochen spuerbar Zeit kostet.
        Task<TimeRecording> recordingTask = _timestamps.ReadAsync(employeeId, from, until, ct);
        Task<IReadOnlyList<SupportEntry>> supportTask = _supports.ListAsync(
            employeeId, Api.Repository.TimestampRepository.Span(from, until), ct);

        TimeRecording recording = await recordingTask.ConfigureAwait(false);
        IReadOnlyList<SupportEntry> supports = await supportTask.ConfigureAwait(false);

        // Die beiden nachgiebigen. Ein Fehlschlag kostet hier Genauigkeit, nicht das Ergebnis.
        IReadOnlyList<AbsenceRequest> absences = await SafeAbsencesAsync(employeeId, from, until, ct)
            .ConfigureAwait(false);
        IReadOnlyList<PlanningAdditionalType> additionalTypes =
            await _absences.ListAdditionalTypesAsync(ct).ConfigureAwait(false);

        IReadOnlyDictionary<int, HolidaySet> holidays =
            await HolidaysForAsync(from, until, state, ct).ConfigureAwait(false);

        GapFinder finder = new(options);
        DateTimeOffset now = _clock.GetLocalNow();

        List<DayAnalysis> result = [];

        for (DateOnly day = from; day <= until; day = day.AddDays(1))
        {
            TimestampDay? timestamps = recording.DayOn(day);

            result.Add(finder.Analyse(new DayInput
            {
                Date = day,
                Timestamps = timestamps,
                Supports = [.. supports.Where(entry => FallsOn(entry, day))],
                Absences = [.. absences.Where(request => request.DayOn(day) is not null)],
                AdditionalTypes = additionalTypes,
                Holidays = holidays.TryGetValue(day.Year, out HolidaySet? set) ? set : HolidaySet.Empty,
                Now = now,
            }));
        }

        return result;
    }

    /// <summary>Beurteilt einen einzelnen Tag.</summary>
    /// <param name="employeeId">Der Mitarbeiter.</param>
    /// <param name="day">Der Tag.</param>
    /// <param name="state">Das Bundesland; <see langword="null"/>, wenn keines feststeht.</param>
    /// <param name="options">Was als Lücke gilt.</param>
    /// <param name="ct">Abbruchmarke.</param>
    /// <returns>Das Ergebnis.</returns>
    public async Task<DayAnalysis> AnalyseDayAsync(int employeeId, DateOnly day,
                                                   FederalState? state, GapOptions? options = null,
                                                   CancellationToken ct = default) =>
        (await AnalyseAsync(employeeId, day, day, state, options, ct).ConfigureAwait(false))[0];

    /// <summary>
    /// Gehört diese Leistung zu diesem Kalendertag?
    /// </summary>
    /// <remarks>
    /// Entschieden am <b>Beginn</b> in Ortszeit. Eine Leistung, die um 23:30 anfängt und bis
    /// 00:30 läuft, gehört zum Vortag — dort hat sie der Techniker erbracht, und dort sucht er
    /// sie. Der <see cref="GapFinder"/> beschneidet sie anschliessend ohnehin auf den Tag.
    /// </remarks>
    private static bool FallsOn(SupportEntry entry, DateOnly day) =>
        entry.Begin is { } begin && DateOnly.FromDateTime(begin.ToLocalTime().DateTime) == day;

    /// <summary>
    /// Die Abwesenheiten — und was es bedeutet, wenn sie fehlen.
    /// </summary>
    /// <remarks>
    /// <b>Ein Fehlschlag wird geschluckt, und das ist die unangenehmere von zwei Richtungen.</b>
    /// Ohne die Anträge fehlt die Erklärung für einen Urlaubstag, an dem nichts gestempelt ist —
    /// dann steht dort „keine Zeit erfasst“, wo in Wahrheit Urlaub war. Die Alternative wäre,
    /// die ganze Übersicht scheitern zu lassen, und das hilft niemandem: Die Lücken der übrigen
    /// Tage stimmen ja. Ein Tag mit Anwesenheit ist davon ohnehin nicht betroffen.
    /// </remarks>
    private async Task<IReadOnlyList<AbsenceRequest>> SafeAbsencesAsync(
        int employeeId, DateOnly from, DateOnly until, CancellationToken ct)
    {
        try
        {
            return await _absences.ListRangeAsync(employeeId, from, until, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Api.TanssException)
        {
            return [];
        }
    }

    /// <summary>Die Feiertage aller Jahre, die der Zeitraum berührt.</summary>
    /// <remarks>
    /// Üblicherweise ein Jahr, um den Jahreswechsel zwei. Ohne Bundesland kommt für jedes Jahr
    /// ein ausdrücklich als unbekannt gekennzeichneter Satz — die Rechnung läuft dann ohne
    /// Feiertage weiter und sagt es.
    /// </remarks>
    private async Task<IReadOnlyDictionary<int, HolidaySet>> HolidaysForAsync(
        DateOnly from, DateOnly until, FederalState? state, CancellationToken ct)
    {
        Dictionary<int, HolidaySet> byYear = [];

        for (int year = from.Year; year <= until.Year; year++)
        {
            byYear[year] = state is null
                ? HolidaySet.Unknown(
                    "Das Bundesland steht nicht fest, deshalb sind die Feiertage unbekannt. "
                    + "Es lässt sich in den Einstellungen setzen — ohne das erscheint an einem "
                    + "Feiertag ohne Zeiterfassung ein Hinweis, der keiner sein müsste.")
                : await _holidays.GetAsync(year, state, ct).ConfigureAwait(false);
        }

        return byYear;
    }
}
