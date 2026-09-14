using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.Api.Repository;

/// <summary>
/// Urlaub, Krankheit und sonstige Abwesenheiten.
/// </summary>
/// <remarks>
/// <para><b>Warum diese Quelle zusätzlich zur Zeiterfassung gebraucht wird.</b> Ein genehmigter
/// Urlaub steht hier ab dem Tag der Genehmigung. Als Zeitstempel steht er erst da, wenn der Tag
/// vorüber ist. Für die abendliche Erinnerung und für einen Blick auf morgen ist der Antrag
/// deshalb die einzige Quelle, die schon etwas weiß.</para>
/// <para><b>TANSS filtert hier nach Jahr und Monat, nicht nach Zeitraum.</b> Ein Zeitraum über
/// eine Monatsgrenze verlangt daher mehrere Aufrufe — siehe <see cref="ListRangeAsync"/>.</para>
/// </remarks>
public sealed class AbsenceRepository : IAbsenceRepository
{
    private readonly ITanssClient _client;

    /// <summary>Baut das Repository.</summary>
    /// <param name="client">Der HTTP-Zugang.</param>
    public AbsenceRepository(ITanssClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AbsenceRequest>> ListAsync(int employeeId, int year, int month,
                                                               CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(employeeId);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);

        AbsenceQuery query = new()
        {
            Year = year,
            Month = month,
            EmployeeIds = [employeeId],
        };

        // Gelesen wird ueber AbsenceList und nicht unmittelbar als Liste: Die Beschreibung zu
        // 10.10.0 sagt "array", die Instanz antwortet mit einem Objekt, in dem die Antraege
        // unter "vacationRequests" stehen. Nachgemessen am 14.09.2026 -- siehe
        // AbsenceListConverter, der beide Formen nimmt.
        AbsenceList? list = await _client
            .PutAsync<AbsenceList>(TanssRoutes.VacationRequestList, query, ct: ct)
            .ConfigureAwait(false);

        return list?.Requests ?? [];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>Fragt jeden berührten Monat einzeln und legt die Ergebnisse zusammen. Ein Antrag,
    /// der über den Monatswechsel läuft, kommt dabei in <b>beiden</b> Monaten zurück; die
    /// Kennung hält ihn auseinander.</para>
    /// <para><b>Ein Fehlschlag eines einzelnen Monats kostet den ganzen Aufruf.</b> Das ist
    /// Absicht: Ein stillschweigend übersprungener Monat hiesse, dass ein Urlaub nicht gefunden
    /// wird — und dann meldet das Werkzeug eine Lücke an einem Tag, an dem der Techniker am
    /// Strand lag. Eine unvollständige Antwort ist hier schlechter als gar keine.</para>
    /// </remarks>
    public async Task<IReadOnlyList<AbsenceRequest>> ListRangeAsync(
        int employeeId, DateOnly from, DateOnly until, CancellationToken ct = default)
    {
        if (until < from)
        {
            throw new ArgumentOutOfRangeException(
                nameof(until), "Der letzte Tag liegt vor dem ersten.");
        }

        Dictionary<int, AbsenceRequest> byId = [];

        DateOnly cursor = new(from.Year, from.Month, 1);
        DateOnly last = new(until.Year, until.Month, 1);

        while (cursor <= last)
        {
            foreach (AbsenceRequest request in
                     await ListAsync(employeeId, cursor.Year, cursor.Month, ct).ConfigureAwait(false))
            {
                byId[request.Id] = request;
            }

            cursor = cursor.AddMonths(1);
        }

        return [.. byId.Values.OrderBy(request => request.StartDate)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlanningAdditionalType>> ListAdditionalTypesAsync(
        CancellationToken ct = default)
    {
        try
        {
            List<PlanningAdditionalType>? types = await _client
                .GetAsync<List<PlanningAdditionalType>>(TanssRoutes.PlanningAdditionalTypes, ct: ct)
                .ConfigureAwait(false);

            return types ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException)
        {
            // Ohne diese Liste gilt eine Abwesenheit der Art ABSENCE als nicht berechnet und
            // erklaert damit die Luecke. Das ist die vorsichtigere Richtung: Sie erzeugt keine
            // falsche Mahnung. Umgekehrt waere es schlimmer - Home-Office ist berechnete Zeit.
            return [];
        }
    }
}
