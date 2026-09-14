using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.App.Ai;
using TanssTagesabschluss.App.Services;
using TanssTagesabschluss.Workday.Model;

namespace TanssTagesabschluss.App.ViewModels;

/// <summary>Ein Ticket in der Auswahl beim Nachtragen.</summary>
/// <remarks>
/// Die Nummer steht mit im Anzeigetext. Zwei Tickets desselben Kunden heißen erfahrungsgemäß
/// ähnlich, und auf das falsche zu buchen fällt niemandem auf.
/// </remarks>
public sealed class TicketRow
{
    /// <summary>Baut die Zeile aus einem Ticket.</summary>
    /// <param name="ticket">Das Ticket.</param>
    public TicketRow(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        Id = ticket.Id;
        Title = string.IsNullOrWhiteSpace(ticket.Title) ? "(ohne Titel)" : ticket.Title;
        CompanyId = ticket.CompanyId;
    }

    /// <summary>Die Ticketnummer.</summary>
    public int Id { get; }

    /// <summary>Der Titel.</summary>
    public string Title { get; }

    /// <summary>Die Firma des Tickets.</summary>
    public int CompanyId { get; }

    /// <summary>Nummer und Titel in einer Zeile.</summary>
    /// <returns>Etwa <c>#4711 — Drucker streikt</c>.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.CurrentCulture, $"#{Id} — {Title}");
}

/// <summary>Ein Gerät in der Auswahl beim Nachtragen.</summary>
/// <remarks>
/// Die Geräteart steht mit im Anzeigetext: Ein Kunde hat leicht einen PC und einen Drucker mit
/// ähnlichem Namen, und beide tragen die Kennung, unter der ihre Art sie führt.
/// </remarks>
public sealed class DeviceRow
{
    /// <summary>Baut die Zeile aus einem Gerät.</summary>
    /// <param name="device">Das Gerät.</param>
    public DeviceRow(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        LinkTypeId = device.LinkTypeId;
        LinkId = device.Id;
        Label = device.ToString();
    }

    /// <summary>Der leere Eintrag „ohne Gerät“ — der Regelfall.</summary>
    /// <remarks>
    /// <b>Er steht ganz oben und ist die Vorbelegung.</b> Ein Gerät ist eine Verfeinerung; wer
    /// keines angibt, soll nichts wegklicken müssen. Und ein versehentlich stehengebliebenes
    /// Gerät aus der vorigen Zeile wäre schlimmer als gar keines.
    /// </remarks>
    public static DeviceRow None { get; } = new();

    private DeviceRow()
    {
        LinkTypeId = 0;
        LinkId = 0;
        Label = "— ohne Gerät —";
    }

    /// <summary>Der Zuordnungstyp; <c>0</c> für „ohne Gerät“.</summary>
    public int LinkTypeId { get; }

    /// <summary>Die Kennung des Geräts; <c>0</c> für „ohne Gerät“.</summary>
    public int LinkId { get; }

    /// <summary>Der Anzeigetext.</summary>
    public string Label { get; }

    /// <summary>Der Anzeigetext.</summary>
    /// <returns>Art und Name.</returns>
    public override string ToString() => Label;
}

/// <summary>Eine bereits erfasste Leistung in der Tagesliste.</summary>
public sealed class SupportRow
{
    /// <summary>Baut die Zeile.</summary>
    /// <param name="entry">Die Leistung.</param>
    public SupportRow(SupportEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Time = entry.Begin is { } begin && entry.End is { } end
            ? string.Create(CultureInfo.CurrentCulture, $"{begin.ToLocalTime():HH:mm}–{end.ToLocalTime():HH:mm}")
            : "—";

        Duration = string.Create(CultureInfo.CurrentCulture, $"{entry.Duration} min");
        Text = string.IsNullOrWhiteSpace(entry.Text) ? "(ohne Text)" : entry.Text.Trim();
        Ticket = entry.TicketId > 0
            ? string.Create(CultureInfo.CurrentCulture, $"#{entry.TicketId}")
            : "kein Ticket";
    }

    /// <summary>Von wann bis wann.</summary>
    public string Time { get; }

    /// <summary>Wie lange.</summary>
    public string Duration { get; }

    /// <summary>Was getan wurde.</summary>
    public string Text { get; }

    /// <summary>Das Ticket.</summary>
    public string Ticket { get; }
}

/// <summary>
/// Eine Lücke — mit dem Textfeld und der Schaltfläche, die sie schliesst.
/// </summary>
/// <remarks>
/// <para><b>Veränderlich, weil der Techniker hineinschreibt.</b> Alles andere in diesem Werkzeug
/// ist ein Datensatz; diese Zeile ist der eine Ort, an dem etwas entsteht.</para>
/// <para><b>Nach dem Buchen bleibt die Zeile stehen und wird stillgelegt</b>, statt zu
/// verschwinden. Wer gerade fünf Lücken abarbeitet, verliert sonst die Übersicht, welche schon
/// erledigt ist — und die Bestätigung, dass es geklappt hat, wäre mit der Zeile verschwunden.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class GapRow : ObservableObject
{
    private readonly Gap _gap;
    private readonly Func<GapRow, CancellationToken, Task<BookingResult>> _book;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private TicketRow? _selectedTicket;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private bool _isInternal;

    [ObservableProperty]
    private DeviceRow? _selectedDevice = DeviceRow.None;

    private readonly Func<int, CancellationToken, Task<IReadOnlyList<DeviceRow>>> _devices;
    private readonly Func<string, AiTask, CancellationToken, Task<string>>? _revise;

    /// <summary>Baut die Zeile.</summary>
    /// <param name="gap">Die Lücke.</param>
    /// <param name="tickets">Die Tickets zur Auswahl.</param>
    /// <param name="book">Was beim Buchen geschieht.</param>
    /// <param name="devices">Woher die Geräte einer Firma kommen.</param>
    /// <param name="revise">
    /// Die Sprachmodell-Unterstützung, oder <see langword="null"/>, wenn sie nicht zur Verfügung
    /// steht. <see cref="CanUseAi"/> blendet die Schaltflächen dann aus, statt sie ins Leere
    /// laufen zu lassen.
    /// </param>
    public GapRow(Gap gap, IReadOnlyList<TicketRow> tickets,
                  Func<GapRow, CancellationToken, Task<BookingResult>> book,
                  Func<int, CancellationToken, Task<IReadOnlyList<DeviceRow>>> devices,
                  Func<string, AiTask, CancellationToken, Task<string>>? revise = null)
    {
        ArgumentNullException.ThrowIfNull(gap);
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(devices);

        _gap = gap;
        _book = book;
        _devices = devices;
        _revise = revise;
        Tickets = tickets;

        Devices.Add(DeviceRow.None);

        // Der Vorschlag kommt aus den Nachbarinnen und nur bei Einigkeit - siehe
        // Gap.SuggestedTicketId. Kein Vorschlag ist besser als ein geratener.
        SelectedTicket = gap.SuggestedTicketId > 0
            ? tickets.FirstOrDefault(ticket => ticket.Id == gap.SuggestedTicketId)
            : null;
    }

    /// <summary>
    /// Die Geräte der Firma des gewählten Tickets.
    /// </summary>
    /// <remarks>
    /// <b>Erst gefüllt, wenn ein Ticket gewählt ist</b> — vorher ist die Firma unbekannt, und
    /// ohne Firma gibt es keine sinnvolle Geräteliste. Geladen wird je Zeile auf Abruf und
    /// nicht für alle Lücken im Voraus: Die meisten Lücken bekommen gar kein Gerät.
    /// </remarks>
    public ObservableCollection<DeviceRow> Devices { get; } = [];

    /// <summary>Die Lücke selbst.</summary>
    public Gap Gap => _gap;

    /// <summary>Die Tickets zur Auswahl.</summary>
    public IReadOnlyList<TicketRow> Tickets { get; }

    /// <summary>Von wann bis wann.</summary>
    public string Time => _gap.Segment.ToString();

    /// <summary>Wie lange, in Minuten.</summary>
    public string Duration =>
        string.Create(CultureInfo.CurrentCulture, $"{_gap.Duration.TotalMinutes:0} min");

    /// <summary>
    /// Was links und rechts der Lücke steht — der Anhaltspunkt, worum es ging.
    /// </summary>
    /// <remarks>
    /// <b>Das ist der eigentliche Nutzen der Liste.</b> „09:15–10:00“ allein sagt niemandem, was
    /// er da getan hat; „davor: Drucker eingerichtet“ bringt die Erinnerung zurück.
    /// </remarks>
    public string Context
    {
        get
        {
            List<string> parts = [];

            if (_gap.Before?.Text is { Length: > 0 } before)
            {
                parts.Add("davor: " + Shorten(before));
            }

            if (_gap.After?.Text is { Length: > 0 } after)
            {
                parts.Add("danach: " + Shorten(after));
            }

            return parts.Count == 0
                ? "keine Leistung davor oder danach"
                : string.Join("   ·   ", parts);
        }
    }

    /// <summary>Lässt sich diese Zeile noch buchen?</summary>
    public bool CanBook => !IsBusy && !IsDone;

    /// <summary>Steht die Sprachmodell-Unterstützung zur Verfügung?</summary>
    /// <remarks>
    /// Steht sie nicht, werden die beiden Schaltflächen ausgeblendet. Eine Schaltfläche, die
    /// bei jedem Druck dasselbe „ist nicht eingerichtet“ sagt, ist schlechter als keine.
    /// </remarks>
    public bool CanUseAi => _revise is not null;

    /// <summary>Lässt sich der Text gerade überarbeiten?</summary>
    public bool CanRevise => CanUseAi && !IsBusy && !IsDone && !string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// Korrigiert Rechtschreibung, Grammatik und Zeichensetzung — sonst nichts.
    /// </summary>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    private Task ProofreadAsync() => ReviseAsync(AiTask.Proofread);

    /// <summary>
    /// Formuliert die Stichworte zu Sätzen aus.
    /// </summary>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    private Task ImproveAsync() => ReviseAsync(AiTask.Improve);

    /// <summary>
    /// Lässt den Text überarbeiten.
    /// </summary>
    /// <remarks>
    /// <para><b>Der bisherige Text bleibt erhalten, bis der neue da ist.</b> Schlägt der Aufruf
    /// fehl, steht im Feld weiterhin das, was der Techniker getippt hat — alles andere wäre ein
    /// Verlust an genau der Stelle, an der etwas entsteht.</para>
    /// <para><b>Und was vorher dastand, lässt sich zurückholen.</b> Ein Modell, das einen
    /// brauchbaren Stichwortzettel in etwas Unbrauchbares verwandelt, darf die Arbeit nicht
    /// kosten — siehe <see cref="UndoRevisionCommand"/>.</para>
    /// </remarks>
    private async Task ReviseAsync(AiTask task)
    {
        if (_revise is null || !CanRevise)
        {
            return;
        }

        IsBusy = true;
        Status = null;

        try
        {
            string revised = await _revise(Text, task, CancellationToken.None).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(revised))
            {
                _beforeRevision = Text;
                Text = revised.Trim();
                Status = task == AiTask.Proofread
                    ? "Text korrigiert. „Zurücknehmen“ stellt die vorherige Fassung wieder her."
                    : "Text ausformuliert. „Zurücknehmen“ stellt die vorherige Fassung wieder her.";
            }
        }
        catch (OperationCanceledException)
        {
            // Der Tag wurde weitergeblaettert.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // BREIT GEFANGEN, und mit Grund: Dahinter stehen zwei fremde Anbieter und zwei
            // fremde Bibliotheken, die alles Moegliche werfen - HttpRequestException,
            // JsonException, die eigenen Fehlertypen des Herstellers. Sie einzeln aufzuzaehlen
            // hiesse, beim naechsten Bibliotheksstand eine zu vergessen, und dann stuerzt das
            // Fenster ab, weil eine Rechtschreibpruefung nicht geklappt hat.
            // Der bisherige Text steht noch da - siehe Kommentar oben.
            Status = "Die Überarbeitung ist fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Stellt den Text vor der letzten Überarbeitung wieder her.</summary>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    private void UndoRevision()
    {
        if (_beforeRevision is not { } previous)
        {
            return;
        }

        Text = previous;
        _beforeRevision = null;
        Status = "Vorherige Fassung wiederhergestellt.";
    }

    /// <summary>Gibt es eine Fassung, die sich zurückholen lässt?</summary>
    public bool CanUndoRevision => _beforeRevision is not null;

    private string? _beforeRevision;

    /// <summary>Trägt die Leistung nach.</summary>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    private async Task BookAsync()
    {
        if (!CanBook)
        {
            return;
        }

        IsBusy = true;
        Status = null;

        try
        {
            BookingResult result = await _book(this, CancellationToken.None).ConfigureAwait(true);

            Status = result.Message;

            // Stillgelegt wird auch bei "steht schon da": In beiden Faellen ist die Luecke
            // geschlossen, und ein zweiter Versuch waere bestenfalls sinnlos.
            IsDone = result.Outcome is BookingOutcome.Created or BookingOutcome.AlreadyExists;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedTicketChanged(TicketRow? value)
    {
        // Die Geraete gehoeren zur Firma des Tickets. Wechselt das Ticket, ist die bisherige
        // Auswahl womoeglich ein Geraet eines anderen Kunden - und das waere die falsche
        // Zuordnung an der unauffaelligsten Stelle.
        SelectedDevice = DeviceRow.None;
        Devices.Clear();
        Devices.Add(DeviceRow.None);

        if (value is { CompanyId: > 0 } ticket)
        {
            _ = LoadDevicesAsync(ticket.CompanyId);
        }
    }

    /// <summary>
    /// Holt die Geräte einer Firma nach.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht und meldet auch nichts.</b> Die Gerätezuordnung ist eine Verfeinerung;
    /// bleibt die Liste leer, wird ohne Gerät gebucht. Eine Fehlermeldung an dieser Stelle
    /// stünde über einer Lücke, die sich sehr wohl schliessen lässt.
    /// </remarks>
    private async Task LoadDevicesAsync(int companyId)
    {
        try
        {
            IReadOnlyList<DeviceRow> found =
                await _devices(companyId, CancellationToken.None).ConfigureAwait(true);

            foreach (DeviceRow device in found)
            {
                Devices.Add(device);
            }
        }
        catch (OperationCanceledException)
        {
            // Der Tag wurde weitergeblaettert; diese Zeile gibt es nicht mehr.
        }
    }

    partial void OnIsBusyChanged(bool value) => RaiseCanExecute();

    partial void OnIsDoneChanged(bool value) => RaiseCanExecute();

    partial void OnTextChanged(string value) => RaiseCanExecute();

    private void RaiseCanExecute()
    {
        OnPropertyChanged(nameof(CanBook));
        OnPropertyChanged(nameof(CanRevise));
        OnPropertyChanged(nameof(CanUndoRevision));

        BookCommand.NotifyCanExecuteChanged();
        ProofreadCommand.NotifyCanExecuteChanged();
        ImproveCommand.NotifyCanExecuteChanged();
        UndoRevisionCommand.NotifyCanExecuteChanged();
    }

    private static string Shorten(string text)
    {
        string flat = string.Join(' ', text.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return flat.Length <= 70 ? flat : flat[..69] + "…";
    }
}

/// <summary>Ein Tag in der Mehrtagesübersicht.</summary>
public sealed class DayRow
{
    /// <summary>Baut die Zeile.</summary>
    /// <param name="analysis">Das Ergebnis des Tages.</param>
    public DayRow(DayAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        Analysis = analysis;
        Date = analysis.Date;
        DateText = string.Create(CultureInfo.CurrentCulture, $"{analysis.Date:ddd, dd.MM.}");

        (Verdict, IsOpen) = Describe(analysis);

        GapText = analysis.Gaps.Count == 0
            ? "—"
            : string.Create(CultureInfo.CurrentCulture,
                $"{analysis.Gaps.Count} · {analysis.GapTotal.TotalHours:0.0} h");

        AttendanceText = analysis.AttendanceTotal == TimeSpan.Zero
            ? "—"
            : string.Create(CultureInfo.CurrentCulture, $"{analysis.AttendanceTotal.TotalHours:0.0} h");

        CoveragePercent = Math.Round(analysis.Coverage * 100);
    }

    /// <summary>Das vollständige Ergebnis.</summary>
    public DayAnalysis Analysis { get; }

    /// <summary>Der Tag.</summary>
    public DateOnly Date { get; }

    /// <summary>Der Tag als Text.</summary>
    public string DateText { get; }

    /// <summary>Das Urteil in einem Wort.</summary>
    public string Verdict { get; }

    /// <summary>Ist an diesem Tag etwas zu tun?</summary>
    public bool IsOpen { get; }

    /// <summary>Die Lücken in Zahlen.</summary>
    public string GapText { get; }

    /// <summary>Die anwesende Zeit.</summary>
    public string AttendanceText { get; }

    /// <summary>Wie viel Prozent gedeckt sind.</summary>
    public double CoveragePercent { get; }

    /// <summary>
    /// Das Urteil als Text — und ob es etwas zu tun gibt.
    /// </summary>
    /// <remarks>
    /// Der Name des Feiertags beziehungsweise der Abwesenheit steht mit drin. „Frei“ allein
    /// liesse den Leser rätseln, warum ausgerechnet dieser Mittwoch frei war.
    /// </remarks>
    private static (string Verdict, bool IsOpen) Describe(DayAnalysis analysis) =>
        analysis.Verdict switch
        {
            DayVerdict.HasGaps => ("offen", true),
            DayVerdict.NoTimeRecorded => ("keine Zeit erfasst", true),
            DayVerdict.Complete => ("vollständig", false),
            DayVerdict.Ongoing => ("läuft noch", false),
            DayVerdict.DayOff => (analysis.HolidayName ?? analysis.AbsenceLabel ?? "frei", false),
            _ => ("—", false),
        };
}
