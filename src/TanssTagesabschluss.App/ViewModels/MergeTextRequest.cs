namespace TanssTagesabschluss.App.ViewModels;

/// <summary>
/// Die Frage, welcher Text beim Zusammenfügen zweier Zeitfenster bleiben soll.
/// </summary>
/// <remarks>
/// <para><b>Gefragt wird nur, wenn wirklich etwas auf dem Spiel steht</b> — also wenn beide
/// Zeilen einen Text tragen und die beiden sich unterscheiden. Trägt nur eine einen, wird er
/// übernommen; sind sie gleich, bleibt einer übrig. Eine Rückfrage, deren Antwort feststeht,
/// ist keine Rückfrage, sondern eine Hürde.</para>
///
/// <para><b>Warum überhaupt gefragt wird.</b> Stillschweigend einen der beiden Texte zu
/// verwerfen hiesse, geschriebene Arbeit wegzuwerfen; sie stillschweigend aneinanderzuhängen
/// hiesse, dem Kunden zwei Beschreibungen derselben Stunde auf die Rechnung zu setzen. Beides
/// kann richtig sein — entscheiden kann es nur der, der die Texte geschrieben hat.</para>
///
/// <para><b>Diese Klasse trägt die Frage, nicht die Antwortmaschine.</b> Das Ansichtsmodell
/// meldet sie; wer ein Fenster öffnen kann, beantwortet sie und trägt das Ergebnis ein. So
/// bleibt die Tagesansicht frei von Fenstern, und sie bleibt prüfbar.</para>
/// </remarks>
/// <param name="first">Der Text des ersten, früheren Zeitfensters.</param>
/// <param name="second">Der Text des zweiten, späteren Zeitfensters.</param>
public sealed class MergeTextRequest(string first, string second)
{
    /// <summary>Der Text des früheren Zeitfensters.</summary>
    public string First { get; } = first;

    /// <summary>Der Text des späteren Zeitfensters.</summary>
    public string Second { get; } = second;

    /// <summary>
    /// Der gewählte Text; <see langword="null"/> bedeutet <b>abgebrochen</b>.
    /// </summary>
    /// <remarks>
    /// <b>Abgebrochen heisst: gar nicht zusammenfügen.</b> Wer die Frage wegklickt, hat sich
    /// gegen den Eingriff entschieden und nicht für einen der beiden Texte. Die beiden
    /// Zeitfenster bleiben dann, wie sie waren.
    /// </remarks>
    public string? Chosen { get; set; }

    /// <summary>
    /// Beide Texte in der Reihenfolge des Tages, durch eine Leerzeile getrennt.
    /// </summary>
    /// <remarks>
    /// Chronologisch und nicht alphabetisch oder nach Länge: Der Text beschreibt einen Ablauf,
    /// und ein Ablauf liest sich in der Reihenfolge, in der er geschehen ist.
    /// </remarks>
    public string Both =>
        string.Join(Environment.NewLine + Environment.NewLine,
            new[] { First, Second }.Where(text => !string.IsNullOrWhiteSpace(text))
                                   .Select(text => text.Trim()));
}
