using TanssTagesabschluss.App.ViewModels;
using Xunit;

namespace TanssTagesabschluss.App.Tests;

/// <summary>
/// Welcher Leistungstext das Zusammenfügen zweier Zeitfenster überlebt.
/// </summary>
/// <remarks>
/// <b>Hier geht Arbeit verloren oder nicht.</b> Einen der beiden Texte stillschweigend zu
/// verwerfen hiesse, Geschriebenes wegzuwerfen; sie stillschweigend aneinanderzuhängen hiesse,
/// dem Kunden zwei Beschreibungen derselben Stunde auf die Rechnung zu setzen. Was hier
/// geprüft wird, ist die Form der Zusammenführung — dass gefragt wird, entscheidet die
/// Tagesansicht.
/// </remarks>
public sealed class MergeTextTests
{
    [Fact]
    public void Beide_Texte_stehen_in_der_Reihenfolge_des_Tages()
    {
        // Chronologisch und nicht alphabetisch: Der Text beschreibt einen Ablauf, und ein
        // Ablauf liest sich in der Reihenfolge, in der er geschehen ist.
        MergeTextRequest question = new("Drucker getauscht", "Anruf mit dem Lieferanten");

        string[] zeilen = question.Both.Split(Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Drucker getauscht", zeilen[0]);
        Assert.Equal("Anruf mit dem Lieferanten", zeilen[1]);
    }

    [Fact]
    public void Zwischen_den_Texten_steht_eine_Leerzeile()
    {
        // Ohne sie klebten zwei Saetze aneinander, und auf der Rechnung des Kunden staende ein
        // Absatz, den niemand so geschrieben hat.
        MergeTextRequest question = new("Erstes", "Zweites");

        Assert.Equal("Erstes" + Environment.NewLine + Environment.NewLine + "Zweites",
                     question.Both);
    }

    [Fact]
    public void Ein_leerer_Text_erzeugt_keine_leere_Zeile()
    {
        Assert.Equal("Allein", new MergeTextRequest("Allein", "   ").Both);
        Assert.Equal("Allein", new MergeTextRequest(string.Empty, "Allein").Both);
    }

    [Fact]
    public void Aussenliegende_Leerzeichen_fallen_weg()
    {
        // Sonst zieht das Textfeld beim naechsten Bearbeiten einen Absatz mit, den niemand
        // sieht und den der Kunde doch bekommt.
        Assert.Equal("Erstes" + Environment.NewLine + Environment.NewLine + "Zweites",
                     new MergeTextRequest("  Erstes \n", "\n Zweites  ").Both);
    }

    [Fact]
    public void Ohne_Antwort_gilt_es_als_abgebrochen()
    {
        // Die Vorgabe ist NICHT "dann eben beide": Wer die Frage wegklickt, hat sich gegen den
        // Eingriff entschieden. Die Tagesansicht laesst die Zeitfenster dann, wie sie waren.
        Assert.Null(new MergeTextRequest("Erstes", "Zweites").Chosen);
    }
}
