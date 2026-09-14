using System.Globalization;

namespace TanssTagesabschluss.Workday.Model;

/// <summary>
/// Ein Zeitabschnitt — <b>halboffen</b>: <see cref="Start"/> gehört dazu, <see cref="End"/> nicht.
/// </summary>
/// <remarks>
/// <para><b>Halboffen ist keine Geschmacksfrage, sondern die Voraussetzung dafür, dass die
/// Rechnung aufgeht.</b> Zwei lückenlos aneinandergrenzende Leistungen — die eine bis 12:00, die
/// nächste ab 12:00 — dürfen sich nicht überschneiden, und zwischen ihnen darf keine Lücke von
/// null Minuten entstehen. Mit geschlossenen Intervallen wäre beides falsch, und beides fiele
/// erst in der Anzeige auf: einmal als verweigerter Nachtrag, einmal als Liste voller
/// Nulllücken.</para>
/// <para><b>Alle Rechnungen dieses Werkzeugs laufen über diesen Typ.</b> Anwesenheit, erfasste
/// Leistungen, Abwesenheiten und Pausen sind dasselbe: Mengen von Abschnitten, die vereinigt und
/// voneinander abgezogen werden. Die Fachlogik besteht danach fast nur noch aus
/// <see cref="Subtract"/>.</para>
/// </remarks>
/// <param name="Start">Beginn, einschließlich.</param>
/// <param name="End">Ende, ausschließlich.</param>
public readonly record struct TimeSegment(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>Die Dauer. Niemals negativ — siehe <see cref="IsEmpty"/>.</summary>
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    /// <summary>Ist der Abschnitt leer oder verkehrt herum?</summary>
    public bool IsEmpty => End <= Start;

    /// <summary>Überschneidet sich dieser Abschnitt mit einem anderen?</summary>
    /// <remarks>Berührung zählt nicht: 9–12 und 12–15 überschneiden sich <b>nicht</b>.</remarks>
    /// <param name="other">Der andere Abschnitt.</param>
    /// <returns><c>true</c> bei echter Überschneidung.</returns>
    public bool Overlaps(TimeSegment other) => Start < other.End && other.Start < End;

    /// <summary>Liegt ein Zeitpunkt in diesem Abschnitt?</summary>
    /// <param name="moment">Der Zeitpunkt.</param>
    /// <returns><c>true</c>, wenn er dazugehört.</returns>
    public bool Contains(DateTimeOffset moment) => moment >= Start && moment < End;

    /// <summary>Enthält dieser Abschnitt den anderen vollständig?</summary>
    /// <param name="other">Der andere Abschnitt.</param>
    /// <returns><c>true</c>, wenn der andere ganz hier drin liegt.</returns>
    public bool Contains(TimeSegment other) => other.Start >= Start && other.End <= End;

    /// <summary>
    /// Beschneidet den Abschnitt auf ein Fenster; leer, wenn nichts übrig bleibt.
    /// </summary>
    /// <param name="window">Das Fenster.</param>
    /// <returns>Der beschnittene Abschnitt.</returns>
    public TimeSegment ClipTo(TimeSegment window)
    {
        DateTimeOffset start = Start > window.Start ? Start : window.Start;
        DateTimeOffset end = End < window.End ? End : window.End;
        return new TimeSegment(start, end < start ? start : end);
    }

    /// <summary>Der Abschnitt als Uhrzeitspanne, für Anzeige und Protokoll.</summary>
    /// <returns>Etwa <c>09:15–10:00</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.CurrentCulture,
        $"{Start.ToLocalTime():HH:mm}–{End.ToLocalTime():HH:mm}");

    /// <summary>
    /// Fasst überlappende und aneinandergrenzende Abschnitte zu möglichst wenigen zusammen.
    /// </summary>
    /// <remarks>
    /// <para><b>Aneinandergrenzende werden mitverschmolzen</b> (9–12 und 12–15 ergeben 9–15).
    /// Andernfalls entstünde zwischen ihnen eine Lücke der Dauer null, die sich durch die ganze
    /// Rechnung zöge und am Ende als Eintrag in der Liste stünde.</para>
    /// <para>Leere Abschnitte fallen weg. Das Ergebnis ist nach Beginn geordnet und
    /// überschneidungsfrei — worauf sich <see cref="Subtract"/> verlässt.</para>
    /// </remarks>
    /// <param name="segments">Die Abschnitte, in beliebiger Reihenfolge.</param>
    /// <returns>Die zusammengefassten Abschnitte, nach Beginn geordnet.</returns>
    public static IReadOnlyList<TimeSegment> Merge(IEnumerable<TimeSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        List<TimeSegment> ordered = [.. segments.Where(segment => !segment.IsEmpty)
                                                .OrderBy(segment => segment.Start)];

        if (ordered.Count == 0)
        {
            return [];
        }

        List<TimeSegment> merged = [];
        TimeSegment current = ordered[0];

        for (int i = 1; i < ordered.Count; i++)
        {
            TimeSegment next = ordered[i];

            // "<=" und nicht "<": Beruehrung verschmilzt mit, sonst bliebe eine Nulllueke.
            if (next.Start <= current.End)
            {
                current = current with { End = next.End > current.End ? next.End : current.End };
                continue;
            }

            merged.Add(current);
            current = next;
        }

        merged.Add(current);
        return merged;
    }

    /// <summary>
    /// Zieht eine Menge von Abschnitten von einer anderen ab — die eine Rechnung, um die es geht.
    /// </summary>
    /// <remarks>
    /// <para>Aus „Anwesenheit minus erfasste Leistungen“ werden hier die Lücken. Beide Seiten
    /// werden zuerst über <see cref="Merge"/> geordnet und entflochten; ohne das wären
    /// überlappende Leistungen doppelt abgezogen und rissen Löcher, die es nicht gibt.</para>
    /// <para>Das Ergebnis ist wieder geordnet und überschneidungsfrei, sodass sich mehrere
    /// Abzüge hintereinanderschalten lassen: erst die Leistungen, dann die Abwesenheiten,
    /// dann die Pausen.</para>
    /// </remarks>
    /// <param name="from">Wovon abgezogen wird.</param>
    /// <param name="subtract">Was abgezogen wird.</param>
    /// <returns>Was übrig bleibt.</returns>
    public static IReadOnlyList<TimeSegment> Subtract(IEnumerable<TimeSegment> from,
                                                      IEnumerable<TimeSegment> subtract)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(subtract);

        IReadOnlyList<TimeSegment> minuend = Merge(from);
        IReadOnlyList<TimeSegment> subtrahend = Merge(subtract);

        if (minuend.Count == 0 || subtrahend.Count == 0)
        {
            return minuend;
        }

        List<TimeSegment> remainder = [];

        foreach (TimeSegment segment in minuend)
        {
            DateTimeOffset cursor = segment.Start;

            foreach (TimeSegment cut in subtrahend)
            {
                if (cut.End <= cursor)
                {
                    // Liegt noch ganz links vom Rest - uebergehen.
                    continue;
                }

                if (cut.Start >= segment.End)
                {
                    // Ab hier liegt alles rechts vom Abschnitt; die Liste ist geordnet,
                    // also ist auch alles Folgende rechts davon.
                    break;
                }

                if (cut.Start > cursor)
                {
                    remainder.Add(new TimeSegment(cursor, cut.Start));
                }

                if (cut.End > cursor)
                {
                    cursor = cut.End;
                }
            }

            if (cursor < segment.End)
            {
                remainder.Add(new TimeSegment(cursor, segment.End));
            }
        }

        return remainder;
    }

    /// <summary>Die Gesamtdauer einer Menge von Abschnitten, ohne Doppelzählung.</summary>
    /// <remarks>
    /// Zählt über <see cref="Merge"/>: Zwei Leistungen, die sich überschneiden, ergeben nicht
    /// mehr belegte Zeit als der Zeitraum, den sie gemeinsam abdecken.
    /// </remarks>
    /// <param name="segments">Die Abschnitte.</param>
    /// <returns>Die Summe der Dauern nach dem Zusammenfassen.</returns>
    public static TimeSpan TotalOf(IEnumerable<TimeSegment> segments)
    {
        TimeSpan total = TimeSpan.Zero;

        foreach (TimeSegment segment in Merge(segments))
        {
            total += segment.Duration;
        }

        return total;
    }
}
