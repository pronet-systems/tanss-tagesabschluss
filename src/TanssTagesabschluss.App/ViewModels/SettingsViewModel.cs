using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Auth;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Diagnostics;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.Api.Model;
using TanssTagesabschluss.App.Runtime;
using TanssTagesabschluss.Storage;
using TanssTagesabschluss.Storage.Config;
using TanssTagesabschluss.Storage.Secrets;
using TanssTagesabschluss.Workday.Holidays;

namespace TanssTagesabschluss.App.ViewModels;

/// <summary>
/// Einrichtung und Einstellungen — Verbindung, eigene Firma, Bundesland, Erinnerungen.
/// </summary>
/// <remarks>
/// <para><b>Eine Seite und kein Assistent.</b> Die Einrichtung besteht aus vier Angaben, von
/// denen zwei sich selbst ermitteln. Ein Assistent über fünf Schritte wäre länger als die
/// Sache — und wer später eine einzelne Angabe ändern will, müsste ihn erneut durchlaufen.</para>
///
/// <para><b>Die Anmeldung ist einmalig.</b> Aus Benutzername und Kennwort entsteht ein
/// kurzlebiger Sitzungsschlüssel, aus dem sich das Werkzeug ein langlaufendes Arbeitstoken
/// prägt. Kennwort und Sitzungsschlüssel werden <b>nicht</b> gespeichert; das Token liegt
/// DPAPI-verschlüsselt im lokalen Profil.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IRuntimeContext _context;
    private readonly IConfigStore _store;
    private readonly Func<bool> _reload;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private int _employeeId;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string? _status;

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

    private int _ownCompanyId;

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

    /// <summary>Die sechzehn Bundesländer für die Auswahl.</summary>
    public static IReadOnlyList<FederalState> FederalStates => FederalState.All;

    /// <summary>Die Verstöße der letzten Speicherung; leer, wenn alles in Ordnung war.</summary>
    public ObservableCollection<string> Problems { get; } = [];

    /// <summary>Gibt es Verstöße zu zeigen?</summary>
    public bool HasProblems => Problems.Count > 0;

    /// <summary>Wo die Konfigurationsdatei liegt.</summary>
    public string ConfigPath => _store.Path;

    /// <summary>Was über das Arbeitstoken bekannt ist.</summary>
    public string TokenText
    {
        get
        {
            if (_context.Composition is not { } composition)
            {
                return "Kein Token — die Anmeldung steht noch aus.";
            }

            try
            {
                double days = TanssAuth.DaysRemaining(composition.Tokens.Read(),
                                                      _context.Clock.GetUtcNow());

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
            Status = "Adresse und Anmeldename werden gebraucht.";
            return;
        }

        IsBusy = true;
        Status = "Meldet an …";

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
                BaseUrl = BaseUrl.Trim(),
                EmployeeId = login.EmployeeId,
                VerifyTls = VerifyTls,
            };

            using TanssClient session = new(options, new PlainTokenStore(login.ApiKey));
            string minted = await TanssAuth
                .MintAsync(session, info: "TANSS Tagesabschluss")
                .ConfigureAwait(true);

            DpapiTokenStore.Default().Write(minted);

            Status = string.Create(CultureInfo.CurrentCulture,
                $"Angemeldet als Mitarbeiter {login.EmployeeId}. Arbeitstoken geprägt und hinterlegt.");

            OnPropertyChanged(nameof(TokenText));
        }
        catch (TanssException ex)
        {
            Status = "Die Anmeldung ist fehlgeschlagen: " + Redaction.Scrub(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Ermittelt die eigene Firma und daraus das Bundesland.
    /// </summary>
    /// <remarks>
    /// Zwei Schritte, die beide fehlschlagen dürfen: Erst fragt das Werkzeug TANSS nach der
    /// eigenen Firma, dann die Postleitzahlenauskunft nach dem Bundesland. Was nicht klappt,
    /// steht als Satz im Status — und lässt sich von Hand setzen.
    /// </remarks>
    /// <returns>Der abgeschlossene Vorgang.</returns>
    [RelayCommand]
    public async Task DetectCompanyAsync()
    {
        if (_context.Composition is not { } composition)
        {
            Status = "Dafür muss die Verbindung stehen — bitte zuerst anmelden und speichern.";
            return;
        }

        IsBusy = true;
        Status = "Sucht die eigene Firma …";

        try
        {
            OwnCompanyResult own = await composition.Companies.FindOwnAsync().ConfigureAwait(true);

            Status = own.Message;

            if (own.Company is not { } company)
            {
                return;
            }

            _ownCompanyId = company.Id;
            OwnCompanyName = company.Name;
            PostalCode = company.PostCode;

            if (!own.HasAddress)
            {
                return;
            }

            if (FederalStateIsManual)
            {
                Status = own.Message + " Das Bundesland bleibt, wie es von Hand gesetzt wurde.";
                return;
            }

            FederalStateLookup lookup = await composition.FederalStates
                .ResolveAsync(company.PostCode!).ConfigureAwait(true);

            Status = own.Message + " " + lookup.Message;

            if (lookup.IsResolved)
            {
                FederalState = lookup.State;
            }
        }
        catch (TanssException ex)
        {
            Status = "Die Suche ist fehlgeschlagen: " + Redaction.Scrub(ex.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(TokenText));
        }
    }

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
        return true;
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
    }

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
