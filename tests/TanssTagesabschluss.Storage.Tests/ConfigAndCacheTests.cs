using TanssTagesabschluss.Storage.Cache;
using TanssTagesabschluss.Storage.Config;
using TanssTagesabschluss.Workday.Holidays;
using Xunit;

namespace TanssTagesabschluss.Storage.Tests;

/// <summary>
/// Die Prüfung der Konfiguration — jede Regel steht für einen Fehler, der sonst erst im
/// Betrieb auffällt.
/// </summary>
public sealed class ConfigValidatorTests
{
    private static AppConfig Valid => new()
    {
        Tanss = new TanssSection
        {
            BaseUrl = "https://tanss.beispiel.de/backend",
            EmployeeId = 7,
        },
    };

    [Fact]
    public void Eine_vollstaendige_Konfiguration_geht_durch() => Valid.Validate();

    /// <summary>
    /// Der Rückblick darf bis zu einem Jahr reichen.
    /// </summary>
    /// <remarks>
    /// Die Grenze lag einmal bei 90 Tagen, mit der Begründung, an älteren Tagen liesse sich
    /// ohnehin nichts mehr nachtragen. Das stimmt so nicht: Eine vergessene Leistung fällt oft
    /// erst bei der Quartalsabrechnung auf, und dann will jemand ein ganzes Quartal oder ein
    /// Jahr durchsehen.
    /// </remarks>
    /// <param name="days">Der zu prüfende Rückblick.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(365)]
    public void Ein_Rueckblick_bis_zu_einem_Jahr_geht_durch(int days) =>
        (Valid with { Gaps = Valid.Gaps with { HistoryDays = days } }).Validate();

    /// <param name="days">Der zu prüfende Rückblick.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void Ein_Rueckblick_ausserhalb_von_1_bis_365_wird_beanstandet(int days)
    {
        AppConfig config = Valid with { Gaps = Valid.Gaps with { HistoryDays = days } };

        ConfigValidationException error =
            Assert.Throws<ConfigValidationException>(() => config.Validate());

        Assert.Contains(error.Problems,
            problem => problem.Contains("gaps.history_days", StringComparison.Ordinal));
    }

    [Fact]
    public void Eine_Adresse_ohne_backend_wird_beanstandet()
    {
        // Keine Formalie: Zeigt die Adresse auf die Weboberflaeche, antwortet die
        // PHP-Oberflaeche auf JEDE Anfrage mit HTTP 400 - auch ohne Token. Der Fehler saehe
        // dann wie ein kaputtes Werkzeug aus und nicht wie eine falsche Adresse.
        AppConfig config = Valid with
        {
            Tanss = Valid.Tanss with { BaseUrl = "https://tanss.beispiel.de" },
        };

        ConfigValidationException error =
            Assert.Throws<ConfigValidationException>(() => config.Validate());

        Assert.Contains(error.Problems, p => p.Contains("/backend", StringComparison.Ordinal));
    }

    [Fact]
    public void Ohne_Mitarbeiterkennung_geht_nichts()
    {
        AppConfig config = Valid with { Tanss = Valid.Tanss with { EmployeeId = 0 } };

        ConfigValidationException error =
            Assert.Throws<ConfigValidationException>(() => config.Validate());

        Assert.Contains(error.Problems, p => p.Contains("employee_id", StringComparison.Ordinal));
    }

    [Fact]
    public void Alle_Beanstandungen_kommen_auf_einmal()
    {
        // Wer eine Datei von Hand pflegt, soll nicht fuenfmal starten muessen, um fuenf
        // Fehler zu finden.
        AppConfig config = Valid with
        {
            Tanss = new TanssSection { BaseUrl = "kaputt", EmployeeId = 0, TimeoutSeconds = 1 },
        };

        ConfigValidationException error =
            Assert.Throws<ConfigValidationException>(() => config.Validate());

        Assert.True(error.Problems.Count >= 3);
    }

    [Fact]
    public void Ein_unbekanntes_Bundesland_wird_beanstandet()
    {
        AppConfig config = Valid with
        {
            Company = new CompanySection { FederalState = "XX" },
        };

        ConfigValidationException error =
            Assert.Throws<ConfigValidationException>(() => config.Validate());

        Assert.Contains(error.Problems, p => p.Contains("federal_state", StringComparison.Ordinal));
    }

    [Fact]
    public void Eine_Erinnerung_am_Abend_vor_der_am_Morgen_wird_beanstandet()
    {
        // Die beiden Zeitpunkte beantworten verschiedene Fragen - morgens der Vortag,
        // abends der laufende Tag. In der falschen Reihenfolge fragt die "morgendliche"
        // Pruefung nach einem Tag, der noch gar nicht vorbei ist.
        AppConfig config = Valid with
        {
            Reminder = new ReminderSection { MorningTime = "18:00", EveningTime = "08:00" },
        };

        ConfigValidationException error =
            Assert.Throws<ConfigValidationException>(() => config.Validate());

        Assert.Contains(error.Problems, p => p.Contains("morning_time", StringComparison.Ordinal));
    }

    [Fact]
    public void Eine_vertippte_Uhrzeit_wird_beanstandet()
    {
        AppConfig config = Valid with
        {
            Reminder = new ReminderSection { EveningTime = "halb fünf" },
        };

        Assert.Throws<ConfigValidationException>(() => config.Validate());
    }

    [Fact]
    public void Eine_abgeschaltete_Zertifikatspruefung_wird_ausgesprochen()
    {
        // Kein Fehler, aber es gehoert gesagt: Ohne Pruefung ist die Verbindung gegen einen
        // Angreifer in der Mitte wertlos.
        AppConfig config = Valid with { Tanss = Valid.Tanss with { VerifyTls = false } };

        ConfigValidationException error =
            Assert.Throws<ConfigValidationException>(() => config.Validate());

        Assert.Contains(error.Problems, p => p.Contains("verify_tls", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("08:30", true)]
    [InlineData("16:45", true)]
    [InlineData("8:30", false)]
    [InlineData("24:00", false)]
    [InlineData("", false)]
    public void Uhrzeiten_werden_unabhaengig_von_der_Laendereinstellung_gelesen(
        string value, bool expected) =>
        Assert.Equal(expected, ConfigValidator.TryParse(value, out _));
}

/// <summary>
/// Das Lesen und Schreiben der Konfigurationsdatei.
/// </summary>
public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "tagesabschluss-test-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_folder, "config.json");

    public ConfigStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Ein liegengebliebener Testordner im Temp-Verzeichnis ist kein Fehlschlag.
        }
    }

    [Fact]
    public void Geschrieben_und_wieder_gelesen_ergibt_dasselbe()
    {
        ConfigStore store = new(File);
        AppConfig config = new()
        {
            Tanss = new TanssSection
            {
                BaseUrl = "https://tanss.beispiel.de/backend",
                EmployeeId = 42,
            },
            Company = new CompanySection { FederalState = "HE", PostalCode = "64380" },
        };

        store.Save(config);
        AppConfig read = store.Load();

        Assert.Equal(42, read.Tanss.EmployeeId);
        Assert.Equal("HE", read.Company.FederalState);
        Assert.Equal("64380", read.Company.PostalCode);
    }

    [Fact]
    public void Eine_fehlerhafte_Konfiguration_wird_gar_nicht_erst_geschrieben()
    {
        // ERST pruefen, DANN schreiben. Umgekehrt laege eine Datei auf der Platte, die das
        // Werkzeug selbst geschrieben hat und beim naechsten Start nicht laden kann.
        ConfigStore store = new(File);
        AppConfig broken = new()
        {
            Tanss = new TanssSection { BaseUrl = "kaputt", EmployeeId = 0 },
        };

        Assert.Throws<ConfigValidationException>(() => store.Save(broken));
        Assert.False(store.Exists());
    }

    [Fact]
    public void Eine_fehlende_Datei_sagt_was_zu_tun_ist()
    {
        ConfigStore store = new(File);

        ConfigException error = Assert.Throws<ConfigException>(store.Load);

        Assert.Contains("eingerichtet", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ein_unbekanntes_Feld_wird_abgelehnt()
    {
        // Eine vertippte Einstellung, die stillschweigend ignoriert wird, ist schlimmer als
        // ein Fehler: Wer sie schreibt, haelt sie fuer wirksam.
        System.IO.File.WriteAllText(File, """
        { "tanss": { "base_url": "https://x.de/backend", "employee_id": 1 },
          "erinnerung": { "morning_time": "08:30" } }
        """);

        Assert.Throws<ConfigException>(new ConfigStore(File).Load);
    }

    [Fact]
    public void Fehlende_Abschnitte_bekommen_die_Vorgaben()
    {
        // Disallow verbietet UNBEKANNTE Felder, nicht ausgelassene.
        System.IO.File.WriteAllText(File,
            """{ "tanss": { "base_url": "https://x.de/backend", "employee_id": 1 } }""");

        AppConfig config = new ConfigStore(File).Load();

        Assert.Equal(15, config.Gaps.MinimumMinutes);
        Assert.Equal("16:45", config.Reminder.EveningTime);
    }
}

/// <summary>Der Feiertagszwischenspeicher.</summary>
public sealed class HolidayCacheTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "tagesabschluss-feiertage-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Siehe oben.
        }
    }

    [Fact]
    public async Task Der_Dienst_wird_nur_einmal_gefragt()
    {
        CountingProvider source = new(HolidaySet.Known(
            [new Holiday(new DateOnly(2026, 12, 25), "1. Weihnachtstag", false, null)]));

        HolidayCache cache = new(source, _folder);

        _ = await cache.GetAsync(2026, FederalState.Hessen);
        _ = await cache.GetAsync(2026, FederalState.Hessen);

        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task Ein_veralteter_Kalender_ist_besser_als_keiner()
    {
        // Der Dienst war beim zweiten Mal nicht erreichbar. Die Feiertage eines bereits
        // begonnenen Jahres aendern sich ohnehin nicht mehr.
        CountingProvider source = new(HolidaySet.Known(
            [new Holiday(new DateOnly(2026, 12, 25), "1. Weihnachtstag", false, null)]));

        HolidayCache cache = new(source, _folder);
        _ = await cache.GetAsync(2026, FederalState.Hessen);

        source.Next = HolidaySet.Unknown("Dienst nicht erreichbar.");
        HolidaySet second = await new HolidayCache(source, _folder,
            new OldClock(TimeSpan.FromDays(90))).GetAsync(2026, FederalState.Hessen);

        Assert.True(second.IsKnown);
        Assert.NotNull(second.On(new DateOnly(2026, 12, 25)));
    }

    [Fact]
    public async Task Ohne_Zwischenspeicher_kommt_der_Fehlschlag_durch()
    {
        // Eine leere Abbildung ohne dieses Kennzeichen bedeutete zweierlei: "dieses Jahr
        // hat keine Feiertage" und "wir konnten nicht nachsehen".
        CountingProvider source = new(HolidaySet.Unknown("Dienst nicht erreichbar."));

        HolidaySet result = await new HolidayCache(source, _folder)
            .GetAsync(2026, FederalState.Hessen);

        Assert.False(result.IsKnown);
        Assert.NotNull(result.Note);
    }

    private sealed class CountingProvider : IHolidayProvider
    {
        public CountingProvider(HolidaySet next) => Next = next;

        public HolidaySet Next { get; set; }

        public int Calls { get; private set; }

        public Task<HolidaySet> GetAsync(int year, FederalState state, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Next);
        }
    }

    /// <summary>Eine Uhr, die weit in der Zukunft steht — damit der Eintrag verfallen ist.</summary>
    private sealed class OldClock : TimeProvider
    {
        private readonly TimeSpan _ahead;

        public OldClock(TimeSpan ahead) => _ahead = ahead;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _ahead;
    }
}
