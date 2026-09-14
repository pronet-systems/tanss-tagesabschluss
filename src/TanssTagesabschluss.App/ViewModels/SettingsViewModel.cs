using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Auth;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Diagnostics;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.Api.Repository;
using TanssTagesabschluss.App.Runtime;
using TanssTagesabschluss.App.Services;
using TanssTagesabschluss.Storage;
using TanssTagesabschluss.Storage.Config;
using TanssTagesabschluss.Storage.Secrets;
using TanssTagesabschluss.Workday.Holidays;

namespace TanssTagesabschluss.App.ViewModels;

/// <summary>
/// Einrichtung und Einstellungen — Verbindung, eigene Firma, Bundesland, Erinnerungen.
/// </summary>
/// <remarks>
/// <para><b>Eine Seite und kein Assistent.</b> Die Einrichtung besteht aus drei Angaben, von
/// denen zwei sich selbst ermitteln. Ein Assistent über fünf Schritte wäre länger als die
/// Sache — und wer später eine einzelne Angabe ändern will, müsste ihn erneut durchlaufen.</para>
///
/// <para><b>Die Anmeldung ist einmalig.</b> Aus Benutzername und Kennwort entsteht ein
/// kurzlebiger Sitzungsschlüssel, aus dem sich das Werkzeug ein langlaufendes Arbeitstoken
/// prägt. Kennwort und Sitzungsschlüssel werden <b>nicht</b> gespeichert; das Token liegt
/// DPAPI-verschlüsselt im lokalen Profil.</para>
///
/// <para><b>Jeder Vorgang meldet in seine eigene Karte.</b> Anmelden, Ermitteln und Speichern
/// sind drei Dinge an drei Stellen der Seite. Eine gemeinsame Statuszeile am Seitenende
/// beantwortet eine Frage dort, wo sie niemand gestellt hat — wer oben auf „Ermitteln“ drückt
/// und die Antwort unten bekommt, sieht gar keine.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IRuntimeContext _context;
    private readonly IConfigStore _store;
    private readonly Func<bool> _reload;
    private HttpClient? _publicHttp;
    private int _ownCompanyId;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _userName = string.Empty;

    /// <summary>
    /// Die Mitarbeiterkennung. <b>Wird gesetzt, nicht eingegeben.</b>
    /// </summary>
    /// <remarks>
    /// <para><b>Warum sie kein Eingabefeld ist.</b> Sie kommt aus der Anmeldung: TANSS
    /// antwortet auf <c>POST /api/v1/login</c> mit der <c>employeeId</c> des angemeldeten
    /// Benutzers. Sie tippen zu lassen hiesse, um eine Angabe zu bitten, die bereits
    /// zweifelsfrei feststeht — und dabei die einzige Gelegenheit zu schaffen, sie falsch zu
    /// setzen.</para>
    /// <para><b>Und falsch wäre sie teuer.</b> An dieser Kennung hängt, <i>wessen</i>
    /// Arbeitszeiten gelesen und wessen Lücken gezeigt werden. Eine vertippte Ziffer lässt das
    /// Werkzeug klaglos den Tag eines Kollegen prüfen — nichts wirkt kaputt, es stimmt nur
    /// alles nicht.</para>
    /// </remarks>
    [ObservableProperty]
    private int _employeeId;

    /// <summary>Der Name zur Kennung, damit sie sich prüfen lässt, ohne sie zu kennen.</summary>
    [ObservableProperty]
    private string? _employeeName;

    /// <summary>Das Ergebnis des Speicherns — steht neben der Schaltfläche „Speichern“.</summary>
    [ObservableProperty]
    private string? _status;

    /// <summary>Das Ergebnis der Anmeldung — steht in der Karte „Verbindung“.</summary>
    [ObservableProperty]
    private string? _connectionStatus;

    /// <summary>Das Ergebnis der Firmenermittlung — steht in der Karte „Eigene Firma“.</summary>
    [ObservableProperty]
    private string? _companyStatus;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _ownCompanyName;

    [ObservableProperty]
    private string? _postalCode;

    [ObservableProperty]
    private FederalState? _federalState;

    [ObservableProperty]
    private bool _federalStateIsManual;

    [ObservableProperty]
    private bool _morningEnabled = true;

    [ObservableProperty]
    private string _morningTime = "08:30";

    [ObservableProperty]
    private bool _eveningEnabled = true;

    [ObservableProperty]
    private string _eveningTime = "16:45";

    [ObservableProperty]
    private bool _onlyWhenSomethingIsOpen = true;

    [ObservableProperty]
    private int _minimumMinutes = 15;

    [ObservableProperty]
    private int _historyDays = 14;

    [ObservableProperty]
    private bool _verifyTls = true;

    /// <summary>Baut das Ansichtsmodell.</summary>
    /// <param name="context">Der Zugang zu Zustand und Zusammenbau.</param>
    /// <param name="store">Die Ablage der Konfiguration.</param>
    /// <param name="reload">Was nach dem Speichern geschieht — üblicherweise <c>AppHost.Reload</c>.</param>
    public SettingsViewModel(IRuntimeContext context, IConfigStore store, Func<bool> reload)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(reload);

        _context = context;
        _store = store;
        _reload = reload;

        LoadFromConfig();
    }

    /// <summary>Meldet, dass die Einstellungen zur Sprachmodell-Unterstützung gewünscht sind.</summary>
    public event EventHandler? AiSettingsRequested;

    /// <summary>Die sechzehn Bundesländer für die Auswahl.</summary>
    public static IReadOnlyList<FederalState> FederalStates => FederalState.All;

    /// <summary>Die Verstöße der letzten Speicherung; leer, wenn alles in Ordnung war.</summary>
    public ObservableCollection<string> Problems { get; } = [];

    /// <summary>
    /// Die Befunde der letzten Verbindungsprüfung.
    /// </summary>
    /// <remarks>
    /// Leer, solange nichts geprüft wurde — und dann steht das auch dort. Fest eingebaute
    /// grüne Haken zeigten im Schwesterprojekt einmal „Token trägt“, während dasselbe Token auf
    /// jede Anfrage eine 403 erzeugte.
    /// </remarks>
    public ObservableCollection<CheckRow> Checks { get; } = [];

    /// <summary>Gibt es Verstöße zu zeigen?</summary>
    public bool HasProblems => Problems.Count > 0;

    /// <summary>Wurde schon einmal geprüft?</summary>
    public bool HasChecks => Checks.Count > 0;

    /// <summary>Wo die Konfigurationsdatei liegt.</summary>
    public string ConfigPath => _store.Path;

    /// <summary>
    /// Die Mitarbeiterkennung, wie sie angezeigt wird.
    /// </summary>
    /// <remarks>
    /// „7 — Sebastian Michel“ beantwortet mit einem Blick, ob die richtige Kennung dasteht.
    /// „Mitarbeiter 7“ allein tut das nicht.
    /// </remarks>
    public string EmployeeText => EmployeeId <= 0
        ? "wird bei der Anmeldung gesetzt"
        : EmployeeName is { Length: > 0 } name
            ? string.Create(CultureInfo.CurrentCulture, $"{EmployeeId} — {name}")
            : string.Create(CultureInfo.CurrentCulture, $"{EmployeeId}");

    /// <summary>Was über das Arbeitstoken bekannt ist.</summary>
    public string TokenText
    {
        get
        {
            string token = DpapiTokenStore.Default().Read();

            if (string.IsNullOrWhiteSpace(token))
            {
                return "Kein Token — die Anmeldung steht noch aus.";
            }

            try
            {
                double days = TanssAuth.DaysRemaining(token, _context.Clock.GetUtcNow());

                return days < 0
                    ? "Das Token ist abgelaufen. Eine neue Anmeldung ist nötig."
                    : string.Create(CultureInfo.CurrentCulture, $"Token gültig, noch {days:0} Tage.");
            }
            catch (TanssAuthException ex)
            {
                return "Das hinterlegte Token ist nicht lesbar: " + Redaction.Scrub(ex.Message);
            }
        }
    }

    /// <summary>Der Zustand der Sprachmodell-Unterstützung, in einem Satz.</summary>
    public string AiText
    {
        get
        {
            if (_context.Composition?.Config.Ai is not { } ai || !ai.Enabled)
            {
                return "Abgeschaltet. Es wird nichts übermittelt.";
            }

            if (!ai.HasConsent)
            {
                return "Eingeschaltet, aber ohne Einwilligung — es wird nichts übermittelt.";
            }

            return string.IsNullOrWhiteSpace(ai.Model)
                ? $"Eingeschaltet ({ai.Provider}), aber kein Modell gewählt."
                : $"Eingeschaltet: {ai.Provider}, Modell {ai.Model}.";
        }
    }

    /// <summary>
    /// Meldet sich an und prägt ein Arbeitstoken.
    /// </summary>
    /// <remarks>
    /// <para><b>Das Kennwort kommt als Parameter und liegt in keiner Eigenschaft.</b> Ein
    /// gebundenes Kennwortfeld hielte es im Speicher des Ansichtsmodells, so lange die Seite
    /// offen ist — und in einem Speicherabbild stünde es dann im Klartext. Der Weg über einen
    /// Parameter hält es auf dem Stapel dieses einen Aufrufs.</para>
    /// <para>Gespeichert wird nur das geprägte Token. Kennwort und Sitzungsschlüssel werden
    /// nicht abgelegt.</para>
    /// </remarks>
    /// <param name="password">Das Kennwort.</param>
    /// <param name="twoFactor">Der zweite Faktor, falls die Instanz einen verlangt.</param>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    public async Task SignInAsync(string password, string? twoFactor = null)
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(UserName))
        {
            ConnectionStatus = "Adresse und Anmeldename werden gebraucht.";
            return;
        }

        IsBusy = true;
        ConnectionStatus = "Meldet an …";

        try
        {
            LoginResult login = await TanssAuth
                .LoginAsync(BaseUrl.Trim(), UserName.Trim(), password, twoFactor)
                .ConfigureAwait(true);

            EmployeeId = login.EmployeeId;

            // Das Sitzungstoken aus /login gilt auf /api/tanss.x/v1 NICHT. Gepraegt wird
            // deshalb sofort ein Arbeitstoken - genau der Fehler, der das Schwesterprojekt
            // wochenlang gekostet hat.
            TanssOptions options = new()
            {
                BaseUrl = BaseUrl.Trim().TrimEnd('/'),
                EmployeeId = login.EmployeeId,
                VerifyTls = VerifyTls,
            };

            using (TanssClient session = new(options, new PlainTokenStore(login.ApiKey)))
            {
                string minted = await TanssAuth
                    .MintAsync(session, info: "TANSS Tagesabschluss")
                    .ConfigureAwait(true);

                DpapiTokenStore.Default().Write(minted);
            }

            ConnectionStatus = string.Create(CultureInfo.CurrentCulture,
                $"Angemeldet als Mitarbeiter {login.EmployeeId}. Arbeitstoken geprägt und hinterlegt.")
                + " Die eigene Firma lässt sich jetzt ermitteln.";

            await ResolveEmployeeNameAsync().ConfigureAwait(true);
        }
        catch (TanssException ex)
        {
            ConnectionStatus = "Die Anmeldung ist fehlgeschlagen: " + Redaction.Scrub(ex.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(TokenText));
        }
    }

    /// <summary>
    /// Fragt TANSS nacheinander alles ab, was dieses Werkzeug im Betrieb braucht.
    /// </summary>
    /// <remarks>
    /// <b>Das ist mehr als eine Verbindungsanzeige.</b> Keine der benutzten Routen ist bisher
    /// gegen eine Instanz gemessen; diese Prüfung misst sie — dort, wo der Techniker sowieso
    /// steht. Jede Zeile nennt ihre Route. Es wird ausschliesslich gelesen.
    /// </remarks>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    public async Task CheckAsync()
    {
        IsBusy = true;
        Checks.Clear();
        OnPropertyChanged(nameof(HasChecks));
        ConnectionStatus = "Prüft …";

        TanssClient? client = null;

        try
        {
            if (!Connect(out client, out string problem))
            {
                ConnectionStatus = problem;
                return;
            }

            ConnectionCheck check = new(client!, EmployeeId, VerifyTls, Resolver(), FederalState);

            foreach (CheckRow row in await check.RunAsync().ConfigureAwait(true))
            {
                Checks.Add(row);
            }

            int failed = Checks.Count(row => row.IsFail);
            int warned = Checks.Count(row => row.IsWarn);

            ConnectionStatus = failed > 0
                ? string.Create(CultureInfo.CurrentCulture,
                    $"{failed} Prüfung(en) nicht in Ordnung — die Einzelheiten stehen unten.")
                : warned > 0
                    ? string.Create(CultureInfo.CurrentCulture,
                        $"Verbindung steht. {warned} Punkt(e) verlangen Aufmerksamkeit.")
                    : "Verbindung steht. Alle Prüfungen in Ordnung.";
        }
        catch (TanssException ex)
        {
            ConnectionStatus = "Die Prüfung ist fehlgeschlagen: " + Redaction.Scrub(ex.Message);
        }
        finally
        {
            Release(client);
            IsBusy = false;
            OnPropertyChanged(nameof(HasChecks));
            OnPropertyChanged(nameof(TokenText));
        }
    }

    /// <summary>
    /// Ermittelt die eigene Firma und daraus das Bundesland.
    /// </summary>
    /// <remarks>
    /// <para><b>Funktioniert unmittelbar nach der Anmeldung — ohne vorheriges Speichern.</b>
    /// Das war einmal anders und war falsch: Wer sich angemeldet hatte und auf „Ermitteln“
    /// drückte, bekam die Auskunft „bitte zuerst speichern“ — und zwar am unteren Seitenende,
    /// weit weg vom Knopf. Von seinem Platz aus passierte gar nichts. Gebraucht werden hier
    /// Basisadresse, Mitarbeiterkennung und Token, und alle drei liegen nach der Anmeldung
    /// vor; der Umweg über die gespeicherte Konfiguration war eine selbstgebaute Hürde.</para>
    /// <para>Zwei Schritte, die beide fehlschlagen dürfen: Erst fragt das Werkzeug TANSS nach
    /// der eigenen Firma, dann die Postleitzahlenauskunft nach dem Bundesland. Was nicht
    /// klappt, steht als Satz <b>in dieser Karte</b> — und lässt sich von Hand setzen.</para>
    /// </remarks>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    public async Task DetectCompanyAsync()
    {
        IsBusy = true;
        CompanyStatus = "Sucht die eigene Firma …";

        TanssClient? client = null;

        try
        {
            if (!Connect(out client, out string problem))
            {
                CompanyStatus = problem;
                return;
            }

            OwnCompanyResult found = await new CompanyRepository(client!)
                .FindOwnAsync().ConfigureAwait(true);

            CompanyStatus = found.Message;

            if (found.Company is not { } company)
            {
                return;
            }

            _ownCompanyId = company.Id;
            OwnCompanyName = company.Name;
            PostalCode = company.PostCode;

            if (!found.HasAddress)
            {
                return;
            }

            if (FederalStateIsManual)
            {
                CompanyStatus = found.Message
                    + " Das Bundesland bleibt, wie es von Hand gesetzt wurde.";
                return;
            }

            FederalStateLookup lookup = await Resolver()
                .ResolveAsync(company.PostCode!).ConfigureAwait(true);

            CompanyStatus = found.Message + " " + lookup.Message;

            if (lookup.IsResolved)
            {
                FederalState = lookup.State;
            }
        }
        catch (TanssException ex)
        {
            CompanyStatus = "Die Suche ist fehlgeschlagen: " + Redaction.Scrub(ex.Message);
        }
        finally
        {
            Release(client);
            IsBusy = false;
            OnPropertyChanged(nameof(TokenText));
        }
    }

    /// <summary>
    /// Erneuert das Arbeitstoken von Hand.
    /// </summary>
    /// <remarks>
    /// <b>Das neue Token wird gegengeprüft, bevor es das bisherige ablöst.</b> Misslingt die
    /// Probe, bleibt alles, wie es war — ein halb gewechseltes Token wäre schlimmer als ein
    /// altes. Und weil TANSS 10.10.0 ein ausgestelltes Token nicht widerrufen kann, prägt
    /// dieser Weg nur dann, wenn die Restlaufzeit unter die eingestellte Schwelle gefallen ist.
    /// </remarks>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    public async Task RotateTokenAsync()
    {
        if (_context.Composition is not { } composition)
        {
            ConnectionStatus = "Dafür muss die Konfiguration gespeichert sein.";
            return;
        }

        IsBusy = true;
        ConnectionStatus = "Erneuert das Token …";

        try
        {
            TokenRotationResult result = await TanssAuth.RotateIfNeededAsync(
                composition.Client,
                composition.Tokens,
                (minted, token) => VerifyAsync(composition, minted, token),
                composition.Config.Tanss.RotateBeforeDays,
                info: "TANSS Tagesabschluss",
                now: _context.Clock.GetUtcNow()).ConfigureAwait(true);

            ConnectionStatus = result.Error is null
                ? result.Reason
                : result.Reason + " " + result.Error;
        }
        catch (TanssException ex)
        {
            ConnectionStatus = "Die Erneuerung ist fehlgeschlagen: " + Redaction.Scrub(ex.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(TokenText));
        }
    }

    /// <summary>Öffnet die Einstellungen zur Sprachmodell-Unterstützung.</summary>
    [RelayCommand]
    private void OpenAiSettings() => AiSettingsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Der Befehl hinter der Schaltfläche „Speichern“.
    /// </summary>
    /// <remarks>
    /// Ein eigener Einstieg, weil <see cref="Save"/> einen Rückgabewert hat und der
    /// Befehlsgenerator damit nicht umgehen kann. Den Rückgabewert loszuwerden wäre der
    /// falsche Weg herum: Er sagt dem Aufrufer, ob er weitermachen darf.
    /// </remarks>
    [RelayCommand]
    private void SaveSettings() => Save();

    /// <summary>Prüft und speichert die Einstellungen.</summary>
    /// <returns><c>true</c>, wenn gespeichert wurde.</returns>
    public bool Save()
    {
        Problems.Clear();

        AppConfig config = new()
        {
            // Der Sprachmodell-Abschnitt wird UNVERAENDERT durchgereicht: Er wird im eigenen
            // Fenster gepflegt, und ein Speichern dieser Seite darf die dort erteilte
            // Einwilligung nicht stillschweigend zuruecksetzen.
            Ai = _context.Composition?.Config.Ai ?? new AiSection(),
            Tanss = new TanssSection
            {
                BaseUrl = BaseUrl.Trim().TrimEnd('/'),
                EmployeeId = EmployeeId,
                VerifyTls = VerifyTls,
            },
            Company = new CompanySection
            {
                OwnCompanyId = _ownCompanyId,
                OwnCompanyName = OwnCompanyName,
                PostalCode = PostalCode,
                FederalState = FederalState?.Code,
                FederalStateIsManual = FederalStateIsManual,
            },
            Gaps = new GapSection
            {
                MinimumMinutes = MinimumMinutes,
                HistoryDays = HistoryDays,
            },
            Reminder = new ReminderSection
            {
                MorningEnabled = MorningEnabled,
                MorningTime = MorningTime,
                EveningEnabled = EveningEnabled,
                EveningTime = EveningTime,
                OnlyWhenSomethingIsOpen = OnlyWhenSomethingIsOpen,
            },
        };

        try
        {
            _store.Save(config);
        }
        catch (ConfigValidationException ex)
        {
            foreach (string problem in ex.Problems)
            {
                Problems.Add(problem);
            }

            Status = "Nicht gespeichert — bitte die Beanstandungen berichtigen.";
            OnPropertyChanged(nameof(HasProblems));
            return false;
        }
        catch (ConfigException ex)
        {
            Problems.Add(Redaction.Scrub(ex.Message));
            Status = "Nicht gespeichert.";
            OnPropertyChanged(nameof(HasProblems));
            return false;
        }

        OnPropertyChanged(nameof(HasProblems));

        Status = _reload()
            ? "Gespeichert. Die Verbindung steht."
            : "Gespeichert. Die Verbindung steht noch nicht — der Betriebszustand nennt, was fehlt.";

        OnPropertyChanged(nameof(TokenText));
        OnPropertyChanged(nameof(AiText));
        return true;
    }

    /// <summary>Liest die Sprachmodell-Angaben neu — nach dem Schliessen des eigenen Fensters.</summary>
    public void RefreshAi() => OnPropertyChanged(nameof(AiText));

    /// <summary>
    /// Baut einen Zugang aus dem, was gerade im Formular steht.
    /// </summary>
    /// <remarks>
    /// <para><b>Bevorzugt den bestehenden Zusammenbau</b>, wenn einer vorliegt: Der trägt
    /// Proxy-Einstellungen und Zeitgrenzen aus der geprüften Konfiguration. Erst wenn es
    /// keinen gibt — also vor dem ersten Speichern —, entsteht ein eigener aus den
    /// Formularwerten und dem hinterlegten Token.</para>
    /// <para>Das Token kommt aus dem DPAPI-Speicher und nicht aus dem Formular: Dort steht es
    /// nie. Fehlt es, ist die Anmeldung noch nicht gelaufen, und genau das sagt die Meldung.</para>
    /// </remarks>
    /// <param name="client">Der Zugang, wenn einer entstanden ist.</param>
    /// <param name="problem">Was fehlt, wenn keiner entsteht — fertig für die Anzeige.</param>
    /// <returns><c>true</c>, wenn ein Zugang vorliegt.</returns>
    private bool Connect(out TanssClient? client, out string problem)
    {
        if (_context.Composition is { } composition)
        {
            client = composition.Client;
            problem = string.Empty;
            return true;
        }

        client = null;

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            problem = "Es fehlt die Basisadresse.";
            return false;
        }

        if (EmployeeId <= 0)
        {
            problem = "Die Mitarbeiterkennung steht noch nicht fest. Sie wird bei der "
                + "Anmeldung gesetzt — bitte zuerst oben anmelden.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DpapiTokenStore.Default().Read()))
        {
            problem = "Es liegt kein Arbeitstoken vor. Bitte zuerst oben anmelden.";
            return false;
        }

        client = new TanssClient(
            new TanssOptions
            {
                BaseUrl = BaseUrl.Trim().TrimEnd('/'),
                EmployeeId = EmployeeId,
                VerifyTls = VerifyTls,
            },
            DpapiTokenStore.Default());

        problem = string.Empty;
        return true;
    }

    /// <summary>
    /// Gibt einen Zugang frei — aber nur den selbst gebauten.
    /// </summary>
    /// <remarks>
    /// Der aus dem Zusammenbau gehört diesem und nicht dieser Seite. Ihn hier zu schliessen
    /// legte die ganze Anwendung still: Die Hintergrunddienste sprechen über denselben.
    /// </remarks>
    private void Release(TanssClient? client)
    {
        if (client is not null && !ReferenceEquals(client, _context.Composition?.Client))
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// Die Postleitzahlenauskunft.
    /// </summary>
    /// <remarks>
    /// Aus dem Zusammenbau, wenn es einen gibt — der teilt sich eine Verbindungsschicht mit
    /// dem Feiertagsdienst. Sonst eine eigene: Sie spricht mit einem öffentlichen Verzeichnis
    /// und trägt kein Token.
    /// </remarks>
    private IFederalStateResolver Resolver() =>
        _context.Composition?.FederalStates
        ?? new OpenPlzStateResolver(_publicHttp ??= new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        });

    /// <summary>Die Probe auf ein frisch geprägtes Token.</summary>
    /// <remarks>
    /// Über den Zugang und nicht über ein Repository: Jenes fängt Fehler ab und gäbe eine
    /// leere Liste zurück — ein Token, auf das TANSS mit 403 antwortet, bestünde die Probe
    /// dann klaglos.
    /// </remarks>
    private static async Task<bool> VerifyAsync(RuntimeComposition composition, string minted,
                                                CancellationToken ct)
    {
        using TanssClient probe = composition.CreateClientWith(minted);

        try
        {
            _ = await probe.GetAsync<List<PauseConfig>>(TanssRoutes.PauseConfigs, ct: ct)
                .ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException)
        {
            return false;
        }
    }

    /// <summary>
    /// Löst die eigene Mitarbeiterkennung in einen Namen auf — und prüft sie gegen das Token.
    /// </summary>
    /// <remarks>
    /// <para><b>Damit sich die Kennung prüfen lässt, ohne sie auswendig zu kennen.</b>
    /// „Mitarbeiter 7“ sagt niemandem, ob das der richtige ist; „7 — Sebastian Michel“
    /// beantwortet die Frage mit einem Blick.</para>
    ///
    /// <para><b>Gefragt wird zuerst <c>ownState</c>, und das aus einem Grund, der über die
    /// Anzeige hinausgeht.</b> Die Technikerliste sagt, wie Mitarbeiter 7 heißt; <c>ownState</c>
    /// sagt, wer das Token in der Hand hält. Nur die zweite Antwort kann eine falsche Kennung
    /// aufdecken — und eine falsche Kennung ist hier kein Schönheitsfehler: <c>loggedInUserId</c>
    /// steuert, <b>wessen</b> Zeiten und Leistungen TANSS herausgibt. Stünde dort eine fremde
    /// Zahl, zeigte das Werkzeug fremde Lücken an und mahnte zu Leistungen, die ein anderer
    /// erfasst. Läuft die Kennung auseinander, gewinnt deshalb das Token, und die Karte sagt
    /// es.</para>
    ///
    /// <para>Die Technikerliste bleibt als Rückfall: Auf einer Instanz ohne <c>ownState</c>
    /// bleibt sie der einzige Weg vom Zahlenwert zu einem Namen.</para>
    /// </remarks>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    private async Task ResolveEmployeeNameAsync()
    {
        if (EmployeeId <= 0)
        {
            return;
        }

        TanssClient? client = null;

        try
        {
            if (!Connect(out client, out _))
            {
                return;
            }

            try
            {
                OwnState? state = await client!.GetAsync<OwnState>(TanssRoutes.OwnState)
                    .ConfigureAwait(true);

                if (state?.LoggedInUser is { Id: > 0 } user)
                {
                    if (user.Id != EmployeeId)
                    {
                        int stale = EmployeeId;
                        EmployeeId = user.Id;
                        ConnectionStatus =
                            $"Die hinterlegte Mitarbeiterkennung war {stale.ToString(CultureInfo.CurrentCulture)}, "
                            + $"das Token gehört aber {user.Name ?? "einem anderen Mitarbeiter"} "
                            + $"(Kennung {user.Id.ToString(CultureInfo.CurrentCulture)}). Die "
                            + "Kennung des Tokens gilt — mit der alten hätte das Werkzeug fremde "
                            + "Zeiten gelesen. Bitte speichern.";
                    }

                    EmployeeName = user.Name;
                    return;
                }
            }
            catch (TanssException)
            {
                // Kennt die Instanz die Route nicht, traegt die Technikerliste weiter.
            }

            IReadOnlyList<Technician> technicians =
                await new TechnicianRepository(client!).ListAsync().ConfigureAwait(true);

            EmployeeName = NameOf(technicians.FirstOrDefault(t => t.Id == EmployeeId));
        }
        catch (TanssException)
        {
            // Beiwerk: Ohne Namen steht dort die Kennung allein. Das ist weniger hilfreich,
            // aber kein Grund, die Einrichtung anzuhalten.
        }
        finally
        {
            Release(client);
        }
    }

    /// <summary>Der Anzeigename eines Technikers; <see langword="null"/>, wenn keiner dasteht.</summary>
    private static string? NameOf(Technician? technician)
    {
        if (technician is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(technician.Name))
        {
            return technician.Name.Trim();
        }

        string[] parts = [technician.FirstName ?? string.Empty, technician.LastName ?? string.Empty];
        string joined = string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));

        return joined.Length == 0 ? null : joined;
    }

    /// <summary>Liest die Einstellungen aus der bestehenden Konfiguration.</summary>
    private void LoadFromConfig()
    {
        if (_context.Composition?.Config is not { } config)
        {
            return;
        }

        BaseUrl = config.Tanss.BaseUrl;
        EmployeeId = config.Tanss.EmployeeId;
        VerifyTls = config.Tanss.VerifyTls;

        _ownCompanyId = config.Company.OwnCompanyId;
        OwnCompanyName = config.Company.OwnCompanyName;
        PostalCode = config.Company.PostalCode;
        FederalState = FederalState.ByCode(config.Company.FederalState);
        FederalStateIsManual = config.Company.FederalStateIsManual;

        MinimumMinutes = config.Gaps.MinimumMinutes;
        HistoryDays = config.Gaps.HistoryDays;

        MorningEnabled = config.Reminder.MorningEnabled;
        MorningTime = config.Reminder.MorningTime;
        EveningEnabled = config.Reminder.EveningEnabled;
        EveningTime = config.Reminder.EveningTime;
        OnlyWhenSomethingIsOpen = config.Reminder.OnlyWhenSomethingIsOpen;

        // Der Name kostet einen Aufruf und ist Beiwerk - deshalb nebenher und ohne Warten.
        _ = ResolveEmployeeNameAsync();
    }

    partial void OnEmployeeIdChanged(int value) => OnPropertyChanged(nameof(EmployeeText));

    partial void OnEmployeeNameChanged(string? value) => OnPropertyChanged(nameof(EmployeeText));

    /// <summary>
    /// Ein Tokenspeicher für den Sitzungsschlüssel aus <c>/api/v1/login</c>.
    /// </summary>
    /// <remarks>
    /// <b>Er hängt „Bearer “ an, wenn es fehlt.</b> Der Sitzungsschlüssel kommt ohne dieses
    /// Präfix; ohne es überspringt TANSS die Prüfung und antwortet mit 403 — der Fehler, der im
    /// Schwesterprojekt Wochen gekostet hat, weil jede Attrappe ihn verbarg.
    /// </remarks>
    private sealed class PlainTokenStore : ITokenStore
    {
        private readonly string _token;

        public PlainTokenStore(string token) =>
            _token = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? token
                : "Bearer " + token;

        public string Read() => _token;

        public void Write(string token)
        {
            // Absichtlich leer: Dieser Speicher lebt fuer die Dauer eines einzigen Aufrufs.
        }
    }
}
