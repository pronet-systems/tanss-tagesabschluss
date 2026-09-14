using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using TanssTagesabschluss.Workday.Holidays;

namespace TanssTagesabschluss.Storage.Cache;

/// <summary>
/// Legt die Feiertage eines Jahres auf der Platte ab und holt sie nur einmal.
/// </summary>
/// <remarks>
/// <para><b>Warum überhaupt zwischengespeichert wird.</b> Die Übersicht rechnet beim Blättern
/// durch den Kalender jeden Tag neu. Ohne Zwischenspeicher ginge dabei bei jedem Tastendruck
/// eine Anfrage an einen fremden Dienst — das ist langsam, es belastet einen Dienst, der uns
/// nichts berechnet, und es macht das Werkzeug ohne Netz unbrauchbar.</para>
///
/// <para><b>Feiertage eines vergangenen Jahres ändern sich nicht.</b> Die eines künftigen
/// theoretisch schon — ein Land kann einen Feiertag einführen. Deshalb verfällt der Eintrag
/// nach <see cref="MaxAge"/>; ein abgelaufener wird erneuert, ist der Dienst aber nicht
/// erreichbar, bleibt der alte in Gebrauch. Ein veralteter Kalender ist allemal besser als
/// keiner.</para>
///
/// <para><b>Der Zwischenspeicher ist wegwerfbar.</b> Er enthält kein Geheimnis und keinen
/// Bestand; wer den Ordner löscht, verliert nichts, was sich nicht in einer Sekunde wieder
/// holen liesse.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class HolidayCache : IHolidayProvider
{
    /// <summary>Nach dieser Zeit wird ein Eintrag erneuert.</summary>
    /// <remarks>
    /// Dreissig Tage: lang genug, dass im Alltag nie gefragt wird, kurz genug, dass ein neu
    /// eingeführter Feiertag innerhalb eines Monats ankommt.
    /// </remarks>
    public static TimeSpan MaxAge { get; } = TimeSpan.FromDays(30);

    private readonly IHolidayProvider _source;
    private readonly string _directory;
    private readonly TimeProvider _clock;

    /// <summary>Baut den Zwischenspeicher.</summary>
    /// <param name="source">Woher die Feiertage kommen, wenn sie hier fehlen.</param>
    /// <param name="directory">Der Ordner; ohne Angabe der vorgesehene im lokalen Profil.</param>
    /// <param name="clock">Die Uhr; für Tests einsetzbar.</param>
    public HolidayCache(IHolidayProvider source, string? directory = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _directory = directory ?? StoragePaths.HolidayCacheDirectory;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<HolidaySet> GetAsync(int year, FederalState state,
                                           CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        string path = PathFor(year, state);
        CachedYear? cached = Read(path);

        if (cached is not null && _clock.GetUtcNow() - cached.FetchedAt < MaxAge)
        {
            return ToSet(cached);
        }

        HolidaySet fresh = await _source.GetAsync(year, state, ct).ConfigureAwait(false);

        if (fresh.IsKnown)
        {
            Write(path, fresh);
            return fresh;
        }

        // Der Dienst war nicht erreichbar. Ein veralteter Kalender ist besser als keiner -
        // und die Feiertage eines bereits begonnenen Jahres aendern sich ohnehin nicht mehr.
        if (cached is not null)
        {
            return ToSet(cached);
        }

        return fresh;
    }

    /// <summary>Verwirft den gesamten Zwischenspeicher.</summary>
    /// <remarks>
    /// Für den Fall, dass das Bundesland gewechselt hat oder jemand dem Kalender nicht traut.
    /// Wirft nicht: Ein Zwischenspeicher, der sich nicht löschen lässt, ist ein Ärgernis und
    /// kein Grund, das Werkzeug anzuhalten.
    /// </remarks>
    /// <returns><c>true</c>, wenn danach nichts mehr dasteht.</returns>
    public bool Clear()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string PathFor(int year, FederalState state) => Path.Combine(
        _directory,
        string.Create(CultureInfo.InvariantCulture, $"{year}-{state.Code}.json"));

    private static HolidaySet ToSet(CachedYear cached) =>
        HolidaySet.Known(cached.Holidays.Select(entry =>
            new Holiday(entry.Date, entry.Name, entry.IsConditional, entry.Note)));

    private static CachedYear? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CachedYear>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Ein unlesbarer Zwischenspeicher ist kein Fehler, sondern ein fehlender
            // Zwischenspeicher. Er wird beim naechsten Erfolg ueberschrieben.
            return null;
        }
    }

    private void Write(string path, HolidaySet set)
    {
        try
        {
            Directory.CreateDirectory(_directory);

            CachedYear payload = new()
            {
                FetchedAt = _clock.GetUtcNow(),
                Holidays = [.. set.ByDate.Values.Select(holiday => new CachedHoliday
                {
                    Date = holiday.Date,
                    Name = holiday.Name,
                    IsConditional = holiday.IsConditional,
                    Note = holiday.Note,
                })],
            };

            AtomicFile.WriteText(path, JsonSerializer.Serialize(payload, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Der Zwischenspeicher ist Beschleunigung, kein Bestand. Laesst er sich nicht
            // schreiben, wird eben jedes Mal gefragt - das kostet Zeit, nicht Richtigkeit.
        }
    }

    private static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>Ein abgelegtes Jahr.</summary>
    private sealed record CachedYear
    {
        /// <summary>Wann es geholt wurde. Entscheidet über den Verfall.</summary>
        public DateTimeOffset FetchedAt { get; init; }

        /// <summary>Die Feiertage.</summary>
        public IReadOnlyList<CachedHoliday> Holidays { get; init; } = [];
    }

    /// <summary>Ein abgelegter Feiertag.</summary>
    private sealed record CachedHoliday
    {
        /// <summary>Der Tag.</summary>
        public DateOnly Date { get; init; }

        /// <summary>Der Name.</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Gilt er nur in einem Teil des Landes?</summary>
        public bool IsConditional { get; init; }

        /// <summary>Der Wortlaut der Einschränkung.</summary>
        public string? Note { get; init; }
    }
}
