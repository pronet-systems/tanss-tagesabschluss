using TanssTagesabschluss.Storage.Config;
using Xunit;

namespace TanssTagesabschluss.Storage.Tests;

/// <summary>
/// Die Beispielkonfiguration muss sich lesen lassen.
/// </summary>
/// <remarks>
/// <para><b>Warum das ein Test ist und keine Selbstverständlichkeit.</b> Die Datei erklärt jede
/// Einstellung und wird deshalb beim Ergänzen einer neuen zuerst angefasst — und beim Umbenennen
/// einer alten zuletzt. Da unbekannte Felder <b>abgelehnt</b> werden, macht genau dieser
/// Zeitversatz aus einer veralteten Erklärung eine Datei, die nicht mehr lädt.</para>
/// <para>Wer diesen Test scheitern sieht, hat entweder ein Feld umbenannt und die Erklärung
/// vergessen — oder umgekehrt.</para>
/// </remarks>
public sealed class ConfigExampleTests
{
    /// <summary>
    /// Der Pfad zur Beispieldatei, vom Testverzeichnis aus.
    /// </summary>
    /// <remarks>
    /// Gesucht wird aufwärts statt über einen festen Sprung nach oben: Wie tief unter
    /// <c>bin\</c> der Testläufer die Baugruppe ablegt, hängt am Zielframework und an der
    /// Konfiguration — ein fest gezählter Weg ist an dem Tag falsch, an dem sich eines von
    /// beiden ändert.
    /// </remarks>
    private static string Find()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);

        while (folder is not null)
        {
            string candidate = Path.Combine(folder.FullName, "config.example.json");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            folder = folder.Parent;
        }

        throw new FileNotFoundException(
            "config.example.json wurde von " + AppContext.BaseDirectory
            + " aufwärts nicht gefunden. Sie gehört in die Wurzel der Projektmappe.");
    }

    [Fact]
    public void Die_Beispielkonfiguration_laedt_und_besteht_die_Pruefung()
    {
        // Sie wird ueber dieselbe Ablage gelesen wie die echte Datei - einschliesslich der
        // Ablehnung unbekannter Felder und der vollstaendigen Pruefung.
        AppConfig config = new ConfigStore(Find()).Load();

        Assert.EndsWith("/backend", config.Tanss.BaseUrl, StringComparison.Ordinal);
        Assert.True(config.Tanss.EmployeeId > 0);
    }

    [Fact]
    public void Die_Beispielkonfiguration_nennt_die_Vorgabewerte()
    {
        // Wenn hier etwas auseinanderlaeuft, erklaert die Datei etwas anderes, als das
        // Programm tut - und das ist schlimmer als eine fehlende Erklaerung.
        AppConfig config = new ConfigStore(Find()).Load();

        Assert.Equal(15, config.Gaps.MinimumMinutes);
        Assert.Equal(14, config.Gaps.HistoryDays);
        Assert.Equal("08:30", config.Reminder.MorningTime);
        Assert.Equal("16:45", config.Reminder.EveningTime);
        Assert.True(config.Reminder.OnlyWhenSomethingIsOpen);
    }

    [Fact]
    public void Die_Sprachmodell_Unterstuetzung_ist_im_Beispiel_abgeschaltet()
    {
        // Das ist die Zusage der README und keine Geschmacksfrage: Eine Beispieldatei, die
        // sie eingeschaltet zeigt, wird irgendwann eingeschaltet uebernommen.
        AppConfig config = new ConfigStore(Find()).Load();

        Assert.False(config.Ai.Enabled);
        Assert.Null(config.Ai.ConsentGivenAt);
    }

    [Fact]
    public void Die_Zertifikatspruefung_ist_im_Beispiel_eingeschaltet()
    {
        AppConfig config = new ConfigStore(Find()).Load();

        Assert.True(config.Tanss.VerifyTls);
    }
}
