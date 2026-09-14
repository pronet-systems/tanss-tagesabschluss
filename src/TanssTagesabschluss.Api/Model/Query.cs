using System.Text.Json.Serialization;

namespace TanssTagesabschluss.Api.Model;

/// <summary>
/// Ein Zeitraum, wie TANSS ihn in Filterrümpfen erwartet: <c>from</c> und <c>to</c> in
/// Unix-<b>Sekunden</b>.
/// </summary>
/// <remarks>
/// <b>Das Feld heißt <c>to</c> und nicht <c>till</c>.</b> Die Zeitstempel-Routen nehmen ihre
/// Grenzen als Abfrageparameter <c>from</c> und <c>till</c> entgegen, die Leistungsliste
/// dagegen als Rumpfobjekt mit <c>from</c> und <c>to</c>. Wer die beiden verwechselt, bekommt
/// einen unbegrenzten Zeitraum zurück — nicht einen Fehler.
/// </remarks>
public sealed record Timeframe
{
    /// <summary>Beginn in Unix-Sekunden.</summary>
    [JsonPropertyName("from")] public long From { get; init; }

    /// <summary>Ende in Unix-Sekunden.</summary>
    [JsonPropertyName("to")] public long To { get; init; }

    /// <summary>Baut den Zeitraum aus zwei Zeitpunkten.</summary>
    /// <param name="from">Beginn.</param>
    /// <param name="to">Ende.</param>
    /// <returns>Der Zeitraum.</returns>
    public static Timeframe Between(DateTimeOffset from, DateTimeOffset to) => new()
    {
        From = TanssTime.ToUnixSeconds(from),
        To = TanssTime.ToUnixSeconds(to),
    };

    /// <summary>
    /// Der volle Kalendertag in <b>Ortszeit</b>, von 0:00:00 bis 23:59:59.
    /// </summary>
    /// <remarks>
    /// <para><b>Ortszeit und nicht UTC.</b> Ein Arbeitstag ist ein Begriff der Ortszeit; in UTC
    /// gerechnet begänne der deutsche Sommertag um 22 Uhr des Vortags, und die erste Leistung
    /// des Morgens fiele aus dem Zeitraum.</para>
    /// <para>Das Ende ist die letzte Sekunde des Tages und nicht Mitternacht des Folgetags —
    /// sonst erschiene eine Leistung, die um Punkt 0:00 des nächsten Tages beginnt, an
    /// beiden Tagen.</para>
    /// </remarks>
    /// <param name="day">Der Kalendertag.</param>
    /// <returns>Der Zeitraum des ganzen Tages.</returns>
    public static Timeframe Day(DateOnly day)
    {
        DateTimeOffset start = new(day.ToDateTime(TimeOnly.MinValue),
                                   TimeZoneInfo.Local.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue)));
        DateTimeOffset end = start.AddDays(1).AddSeconds(-1);
        return Between(start, end);
    }
}

/// <summary>
/// Der Filterrumpf von <c>PUT /api/v1/supports/list</c>.
/// </summary>
/// <remarks>
/// <para>Die Route kennt über dreissig Filter; gesetzt werden muss nur, was gebraucht wird.
/// Dieses Werkzeug fragt nach Zeitraum und Mitarbeiter — mehr würde die Antwort verengen und
/// damit womöglich gerade die Leistung verbergen, die eine Lücke füllt.</para>
/// <para><b>Ausdrücklich kein Filter auf <c>planningTypes</c>.</b> Es ist verlockend, gleich
/// nur <c>SUPPORT</c> anzufragen — dann kämen aber die Urlaube und Krankheiten nicht mit, die
/// einen leeren Tag erklären. Gefiltert wird nach dem Lesen, und zwar sichtbar in
/// <see cref="SupportEntry.IsSupport"/>.</para>
/// </remarks>
public sealed record SupportListQuery
{
    /// <summary>Der Zeitraum.</summary>
    [JsonPropertyName("timeframe")] public required Timeframe Timeframe { get; init; }

    /// <summary>Die Mitarbeiter, deren Leistungen gesucht werden.</summary>
    [JsonPropertyName("employees")] public IReadOnlyList<int> Employees { get; init; } = [];
}

/// <summary>
/// Eine Pausenregel der Instanz: ab wie viel Arbeit wie viel Pause zu nehmen ist.
/// </summary>
/// <remarks>
/// <b>Der Tippfehler <c>fromMinues</c> steht so in der Beschreibung</b> zu 10.10.0 — und damit
/// vermutlich auch auf der Leitung. Gelesen werden deshalb beide Schreibweisen; welche ankommt,
/// ist ungemessen.
/// </remarks>
public sealed record PauseConfig
{
    /// <summary>Die Kennung.</summary>
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>Ab wie vielen Minuten Arbeit diese Regel greift — TANSS schreibt <c>fromMinues</c>.</summary>
    [JsonPropertyName("fromMinues")] public int FromMinutesTypo { get; init; }

    /// <summary>Dieselbe Angabe in richtiger Schreibweise, falls die Instanz sie so schreibt.</summary>
    [JsonPropertyName("fromMinutes")] public int FromMinutesCorrect { get; init; }

    /// <summary>Die Mindestpause in Minuten.</summary>
    [JsonPropertyName("minimumPause")] public int MinimumPause { get; init; }

    /// <summary>Die Schwelle, gleich welcher Schreibweise sie entstammt.</summary>
    /// <remarks>
    /// Der grössere der beiden Werte wäre falsch — es ist immer nur einer gesetzt, der andere
    /// bleibt 0. Genommen wird deshalb der erste, der nicht 0 ist.
    /// </remarks>
    [JsonIgnore]
    public int FromMinutes => FromMinutesTypo != 0 ? FromMinutesTypo : FromMinutesCorrect;
}
