using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Diagnostics;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.App.Ai;
using TanssTagesabschluss.App.Services;
using TanssTagesabschluss.Workday.Model;

namespace TanssTagesabschluss.App.ViewModels;

/// <summary>Eine Firma in der Auswahl beim Nachtragen.</summary>
/// <remarks>
/// <para><b>Der Name allein genügt nicht.</b> Nachgemessen gegen eine Instanz stand derselbe
/// Firmenname fünfmal mit verschiedenen Kennungen in der Trefferliste. Deshalb steht neben dem
/// Namen, was die Dubletten auseinanderhält: Kundennummer, Postleitzahl, Ort.</para>
/// <para><b>Gesperrte Firmen bleiben sichtbar, aber unwählbar.</b> Sie wegzulassen hiesse, dass
/// jemand sucht und nicht findet, ohne zu erfahren, warum.</para>
/// </remarks>
public sealed class CompanyRow
{
    /// <summary>Baut die Zeile aus einer Firma.</summary>
    /// <param name="company">Die Firma.</param>
    public CompanyRow(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        Id = company.Id;
        Name = string.IsNullOrWhiteSpace(company.Name) ? "(ohne Namen)" : company.Name;
        Distinguisher = company.Distinguisher;
        Selectable = company.Selectable;
        CustomerNumber = company.DisplayId;
        Place = string.Join(" ",
            new[] { company.PostCode, company.City }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
        Role = RoleOf(company.CentralType);
    }

    /// <summary>Die Kundennummer, wie sie in TANSS steht — <b>nicht</b> die interne Kennung.</summary>
    /// <remarks>
    /// Sie ist es, die auf Rechnungen und in Gesprächen genannt wird. Die interne
    /// <see cref="Id"/> kennt ausserhalb der Schnittstelle niemand.
    /// </remarks>
    public string? CustomerNumber { get; }

    /// <summary>
    /// <c>Zentrale</c>, <c>Filiale</c> oder leer.
    /// </summary>
    /// <remarks>
    /// <b>Der Unterschied entscheidet, wem die Leistung berechnet wird.</b> Gemessen am
    /// 14.09.2026 tragen von 540 Firmen 9 den Wert <c>CENTRAL</c> und 15 den Wert
    /// <c>BRANCH</c>; bei den übrigen steht <c>NONE</c>, und dort ist die Angabe schlicht keine
    /// Auskunft — deshalb bleibt sie dann leer, statt „keine“ zu behaupten.
    /// </remarks>
    public string? Role { get; }

    /// <summary>Postleitzahl und Ort, soweit vorhanden.</summary>
    public string Place { get; }

    private static string? RoleOf(string? centralType) => centralType switch
    {
        "CENTRAL" => "Zentrale",
        "BRANCH" => "Filiale",
        _ => null,
    };

    /// <summary>Die <c>companyId</c>.</summary>
    public int Id { get; }

    /// <summary>Der Firmenname.</summary>
    public string Name { get; }

    /// <summary>Kundennummer, Postleitzahl, Ort — was Dubletten auseinanderhält.</summary>
    public string Distinguisher { get; }

    /// <summary>Darf auf diese Firma gebucht werden?</summary>
    public bool Selectable { get; }

    /// <summary>
    /// Die Zeile, wie sie in der Auswahl steht.
    /// </summary>
    /// <remarks>
    /// <b>Der Name allein genügt nicht.</b> Gemessen stand derselbe Firmenname mehrfach mit
    /// verschiedenen Kennungen in der Trefferliste — und zwei davon waren Zentrale und Filiale
    /// desselben Hauses. Deshalb stehen Kundennummer und Rolle daneben: Sie sind es, die den
    /// Unterschied ausmachen, und sie sind es auch, die auf der Rechnung landen.
    /// </remarks>
    /// <returns>Etwa <c>Musterfirma GmbH · 10235 · Zentrale · 59757 Arnsberg</c>.</returns>
    public override string ToString()
    {
        List<string> parts = [Name];

        if (!string.IsNullOrWhiteSpace(CustomerNumber))
        {
            parts.Add(CustomerNumber);
        }

        if (!string.IsNullOrWhiteSpace(Role))
        {
            parts.Add(Role);
        }

        if (!string.IsNullOrWhiteSpace(Place))
        {
            parts.Add(Place);
        }

        return string.Join(" · ", parts);
    }
}

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
public sealed partial class GapRow : ObservableObject, IDisposable
{
    private readonly Gap _gap;
    private readonly Func<GapRow, CancellationToken, Task<BookingResult>> _book;
    private readonly GapCatalog _catalog;
    private readonly Func<string, AiTask, CancellationToken, Task<string>>? _revise;

    /// <summary>Für welche Firma die Geräteliste zuletzt geholt wurde.</summary>
    /// <remarks>
    /// Ohne diese Zahl liefe bei jedem Ticketwechsel innerhalb derselben Firma ein weiterer
    /// Aufruf — und die Liste flackerte, während die Auswahl daneben zurückgesetzt würde.
    /// </remarks>
    private int _devicesFor;

    private string? _beforeRevision;

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

    /// <summary>Der Suchbegriff für die Firma.</summary>
    [ObservableProperty]
    private string _companyQuery = string.Empty;

    [ObservableProperty]
    private CompanyRow? _selectedCompany;

    /// <summary>Was die Firmensuche zuletzt gesagt hat.</summary>
    [ObservableProperty]
    private string? _companyStatus;

    [ObservableProperty]
    private bool _isSearching;

    /// <summary>Baut die Zeile.</summary>
    /// <param name="gap">Die Lücke.</param>
    /// <param name="catalog">Woher Firmen, Tickets und Geräte kommen.</param>
    /// <param name="book">Was beim Buchen geschieht.</param>
    /// <param name="revise">
    /// Die Sprachmodell-Unterstützung, oder <see langword="null"/>, wenn sie nicht zur Verfügung
    /// steht. <see cref="CanUseAi"/> blendet die Schaltflächen dann aus, statt sie ins Leere
    /// laufen zu lassen.
    /// </param>
    public GapRow(Gap gap, GapCatalog catalog,
                  Func<GapRow, CancellationToken, Task<BookingResult>> book,
                  Func<string, AiTask, CancellationToken, Task<string>>? revise = null)
    {
        ArgumentNullException.ThrowIfNull(gap);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(book);

        _gap = gap;
        _book = book;
        _catalog = catalog;
        _revise = revise;

        foreach (TicketRow ticket in catalog.OwnTickets)
        {
            Tickets.Add(ticket);
        }

        Devices.Add(DeviceRow.None);

        // Der Vorschlag kommt aus den Nachbarinnen und nur bei Einigkeit - siehe
        // Gap.SuggestedTicketId. Kein Vorschlag ist besser als ein geratener.
        SelectedTicket = gap.SuggestedTicketId > 0
            ? Tickets.FirstOrDefault(ticket => ticket.Id == gap.SuggestedTicketId)
            : null;
    }

    /// <summary>
    /// Die Firmen, die die letzte Suche ergeben hat.
    /// </summary>
    /// <remarks>
    /// Leer, solange niemand gesucht hat. Eine Liste aller Firmen des Hauses vorzuladen wäre
    /// nutzlos — bei vierstelligen Kundenzahlen sucht man, statt zu blättern.
    /// </remarks>
    public ObservableCollection<CompanyRow> Companies { get; } = [];

    /// <summary>
    /// Die Tickets zur Auswahl.
    /// </summary>
    /// <remarks>
    /// <b>Veränderlich, weil die Liste vom gewählten Kunden abhängt.</b> Ohne Firma stehen hier
    /// die offenen Tickets des Technikers; ist eine Firma gewählt, stehen hier deren offene
    /// Tickets — und zwar nur deren. Wer für einen Kollegen einspringt, findet das Ticket sonst
    /// nicht, weil es ihm nicht gehört.
    /// </remarks>
    public ObservableCollection<TicketRow> Tickets { get; } = [];

    /// <summary>
    /// Die Geräte der gewählten Firma.
    /// </summary>
    /// <remarks>
    /// <b>Erst gefüllt, wenn eine Firma feststeht</b> — über die Auswahl oder über das gewählte
    /// Ticket. Vorher gibt es keine sinnvolle Geräteliste. Geladen wird je Zeile auf Abruf und
    /// nicht für alle Lücken im Voraus: Die meisten Lücken bekommen gar kein Gerät.
    /// </remarks>
    public ObservableCollection<DeviceRow> Devices { get; } = [];

    /// <summary>Die Lücke selbst.</summary>
    public Gap Gap => _gap;

    /// <summary>
    /// Die Firma, auf die gebucht wird — <c>0</c>, wenn keine feststeht.
    /// </summary>
    /// <remarks>
    /// <b>Das Ticket hat Vorrang vor der Auswahl.</b> Es ist das Genauere: Über das Ticket
    /// findet TANSS Vertrag, Stundensatz und Abrechnungsart. Die Firma allein führt zwar auch
    /// zu einem Stundensatz, aber nicht zum Vertrag — und der entscheidet über den Preis.
    /// </remarks>
    public int EffectiveCompanyId =>
        SelectedTicket is { CompanyId: > 0 } ticket ? ticket.CompanyId : SelectedCompany?.Id ?? 0;

    /// <summary>Ist eine Firma gewählt, deren Auswahl sich zurücknehmen lässt?</summary>
    public bool HasCompany => SelectedCompany is not null;

    /// <summary>Lässt sich gerade nach einer Firma suchen?</summary>
    public bool CanSearchCompanies =>
        !IsSearching && !IsBusy && !IsDone && CompanyQuery.Trim().Length >= MinimumQuery;

    /// <summary>
    /// Sucht Firmen zum eingegebenen Begriff.
    /// </summary>
    /// <remarks>
    /// <b>Auf Knopfdruck und nicht bei jedem Tastendruck.</b> Die Suche geht über
    /// <c>PUT /api/v1/search</c> an die Produktivinstanz des Hauses; sie bei jedem Buchstaben
    /// loszuschicken hiesse, für ein Wort ein Dutzend Abfragen zu erzeugen — und die Antwort
    /// auf „Mus“ ist ohnehin nicht die, die jemand sucht.
    /// </remarks>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    private async Task SearchCompaniesAsync()
    {
        if (!CanSearchCompanies)
        {
            return;
        }

        IsSearching = true;
        CompanyStatus = "Sucht …";

        try
        {
            CompanySearchResult found =
                await _catalog.SearchCompanies(CompanyQuery.Trim(), CancellationToken.None)
                    .ConfigureAwait(true);

            Companies.Clear();
            foreach (Company company in found.Companies)
            {
                Companies.Add(new CompanyRow(company));
            }

            // Der Satz kommt aus dem Lager und nicht von hier: Dort wird unterschieden, ob es
            // keine Firma gibt oder ob es zu viele waren - und nur dort ist diese
            // Unterscheidung hergeleitet statt geraten.
            CompanyStatus = found.Explanation;

            // Genau ein Treffer braucht keine Auswahl. Bei mehreren waere das Vorbelegen ein
            // Raten, und geraten wird beim Kunden nicht.
            if (Companies.Count == 1 && Companies[0].Selectable)
            {
                SelectedCompany = Companies[0];
            }
        }
        catch (OperationCanceledException)
        {
            // Der Tag wurde weitergeblaettert; diese Zeile gibt es nicht mehr.
        }
        catch (TanssException ex)
        {
            // Die Suche ist eine Bequemlichkeit, die Buchung ist Arbeitszeit. Ein Aussetzer der
            // Leitung darf die Zeile nicht stilllegen - gebucht werden kann weiter ueber das
            // eigene Ticket.
            CompanyStatus = "Die Firmensuche ist fehlgeschlagen: " + Redaction.Scrub(ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>
    /// Nimmt die Firmenauswahl zurück und stellt die eigenen Tickets wieder her.
    /// </summary>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    private void ClearCompany()
    {
        SelectedCompany = null;
        CompanyQuery = string.Empty;
        Companies.Clear();
        CompanyStatus = null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Die Zeile lebt so lange wie der angezeigte Tag. Ohne dieses Aufräumen bliebe je Lücke
    /// eine Abbruchmarke liegen, und beim Durchblättern einer Woche summiert sich das.
    /// </remarks>
    public void Dispose()
    {
        _typing?.Cancel();
        _typing?.Dispose();
        _typing = null;
    }

    /// <summary>Die kürzeste Eingabe, mit der überhaupt gesucht wird.</summary>
    /// <remarks>
    /// Unter drei Zeichen trifft eine Teilzeichenkettensuche fast jede Firma des Hauses — und
    /// läuft damit ohnehin in die Ergebnisgrenze, hinter der TANSS eine <b>leere</b> Liste
    /// liefert statt einer gekürzten.
    /// </remarks>
    private const int MinimumQuery = 3;

    /// <summary>Wie lange nach dem letzten Tastendruck gewartet wird.</summary>
    /// <remarks>
    /// <b>Ohne diese Pause erzeugt ein getipptes Wort ein Dutzend Abfragen an die
    /// Produktivinstanz</b> — und die Antwort auf „Mus“ ist ohnehin nicht die gesuchte. Eine
    /// dreifünftel Sekunde ist lang genug, dass Tippen nicht sucht, und kurz genug, dass
    /// niemand auf das Ergebnis wartet.
    /// </remarks>
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(600);

    private CancellationTokenSource? _typing;

    partial void OnCompanyQueryChanged(string value)
    {
        RaiseCanExecute();
        _ = SearchAfterTypingAsync(value);
    }

    /// <summary>
    /// Sucht selbsttätig, sobald das Tippen zur Ruhe kommt.
    /// </summary>
    /// <remarks>
    /// <para><b>Jeder neue Tastendruck bricht den vorigen Lauf ab.</b> Sonst stünde am Ende das
    /// Ergebnis zu „Mus“ über dem zu „Musterfirma“ — die kürzere Anfrage ist schneller fertig
    /// und käme zuletzt an.</para>
    /// <para><b>Die Auswahl selbst löst keine Suche aus.</b> Wer einen Eintrag wählt, schreibt
    /// dessen Text ins Feld zurück; ohne diese Prüfung suchte das Werkzeug daraufhin nach
    /// genau der Zeile, die schon gewählt ist.</para>
    /// </remarks>
    private async Task SearchAfterTypingAsync(string query)
    {
        _typing?.Cancel();
        _typing?.Dispose();

        if (SelectedCompany is { } chosen
            && string.Equals(query, chosen.ToString(), StringComparison.Ordinal))
        {
            _typing = null;
            return;
        }

        if (query.Trim().Length < MinimumQuery)
        {
            _typing = null;
            Companies.Clear();
            CompanyStatus = null;
            return;
        }

        CancellationTokenSource cts = new();
        _typing = cts;

        try
        {
            await Task.Delay(SearchDelay, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await SearchCompaniesAsync().ConfigureAwait(true);
    }

    partial void OnIsSearchingChanged(bool value) => RaiseCanExecute();

    /// <summary>
    /// Eine gewählte Firma legt fest, welche Tickets und Geräte überhaupt zur Wahl stehen.
    /// </summary>
    /// <remarks>
    /// <b>Die bisherige Ticketauswahl fällt dabei weg</b>, und zwar ausdrücklich: Ein
    /// stehengebliebenes Ticket des vorigen Kunden wäre die falsche Zuordnung an der
    /// unauffälligsten Stelle — die Leistung landete auf dessen Rechnung.
    /// </remarks>
    partial void OnSelectedCompanyChanged(CompanyRow? value)
    {
        SelectedTicket = null;
        Tickets.Clear();

        OnPropertyChanged(nameof(HasCompany));
        OnPropertyChanged(nameof(EffectiveCompanyId));

        if (value is null)
        {
            foreach (TicketRow ticket in _catalog.OwnTickets)
            {
                Tickets.Add(ticket);
            }

            SyncDevices();
            return;
        }

        _ = LoadCompanyTicketsAsync(value.Id);
        SyncDevices();
    }

    /// <summary>
    /// Holt die offenen Tickets der gewählten Firma.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Das Lager gibt bei einem Fehlschlag „nicht ermittelt“ samt fertigem
    /// Satz zurück; gebucht werden kann dann ohne Ticket auf die Firma.
    /// </remarks>
    private async Task LoadCompanyTicketsAsync(int companyId)
    {
        try
        {
            CompanyTickets found =
                await _catalog.TicketsOfCompany(companyId, CancellationToken.None)
                    .ConfigureAwait(true);

            // Die Firma koennte inzwischen gewechselt haben - dann gehoeren diese Tickets nicht
            // mehr hierher.
            if (SelectedCompany?.Id != companyId)
            {
                return;
            }

            Tickets.Clear();
            foreach (Ticket ticket in found.Tickets)
            {
                Tickets.Add(new TicketRow(ticket));
            }

            CompanyStatus = found.CompanyName is { Length: > 0 } name
                ? name + ": " + found.Explanation
                : found.Explanation;
        }
        catch (OperationCanceledException)
        {
            // Der Tag wurde weitergeblaettert.
        }
    }

    partial void OnSelectedTicketChanged(TicketRow? value)
    {
        OnPropertyChanged(nameof(EffectiveCompanyId));
        SyncDevices();
    }

    /// <summary>
    /// Sorgt dafür, dass die Geräteliste zur Firma passt, auf die gerade gebucht würde.
    /// </summary>
    /// <remarks>
    /// <b>Nur beim Wechsel der Firma wird nachgeladen.</b> Zwei Tickets desselben Kunden führen
    /// zur selben Geräteliste; sie dazwischen zu leeren und neu zu holen kostete einen Aufruf
    /// und liesse die Auswahl des Technikers grundlos verschwinden.
    /// </remarks>
    private void SyncDevices()
    {
        int companyId = EffectiveCompanyId;

        if (companyId == _devicesFor)
        {
            return;
        }

        _devicesFor = companyId;

        SelectedDevice = DeviceRow.None;
        Devices.Clear();
        Devices.Add(DeviceRow.None);

        if (companyId > 0)
        {
            _ = LoadDevicesAsync(companyId);
        }
    }

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
                await _catalog.DevicesOfCompany(companyId, CancellationToken.None)
                    .ConfigureAwait(true);

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
        OnPropertyChanged(nameof(CanSearchCompanies));

        SearchCompaniesCommand.NotifyCanExecuteChanged();
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

/// <summary>Wie eine Prüfung ausgegangen ist.</summary>
/// <remarks>
/// <see cref="Unknown"/> ist eine eigene Stufe und ausdrücklich nicht dasselbe wie
/// <see cref="Ok"/>. Wer beides gleich anzeigt, behauptet Befunde, die niemand erhoben hat —
/// und beendet damit die Fehlersuche, bevor sie anfängt.
/// </remarks>
public enum CheckLevel
{
    /// <summary>Nicht geprüft. Keine Aussage.</summary>
    Unknown,

    /// <summary>Geprüft und in Ordnung.</summary>
    Ok,

    /// <summary>Läuft, verlangt aber Aufmerksamkeit.</summary>
    Warn,

    /// <summary>Geprüft und nicht in Ordnung.</summary>
    Fail,
}

/// <summary>Ein einzelner Befund der Verbindungsprüfung.</summary>
/// <param name="Name">Was geprüft wurde.</param>
/// <param name="Level">Wie es ausging.</param>
/// <param name="Detail">Woraus sich das ergibt — im Klartext und mit der Route als Quelle.</param>
public sealed record CheckRow(string Name, CheckLevel Level, string Detail)
{
    /// <summary>Ist der Befund in Ordnung?</summary>
    public bool IsOk => Level == CheckLevel.Ok;

    /// <summary>Verlangt der Befund Aufmerksamkeit?</summary>
    public bool IsWarn => Level == CheckLevel.Warn;

    /// <summary>Ist der Befund nicht in Ordnung?</summary>
    public bool IsFail => Level == CheckLevel.Fail;

    /// <summary>Wurde gar nicht geprüft?</summary>
    public bool IsUnknown => Level == CheckLevel.Unknown;
}
