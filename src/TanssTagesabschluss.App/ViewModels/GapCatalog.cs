using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.App.ViewModels;

/// <summary>
/// Woher eine Lückenzeile ihre Auswahllisten bekommt: Firmen, Tickets, Geräte.
/// </summary>
/// <remarks>
/// <para><b>Ein Typ statt vier weiterer Parameter am Erbauer von <see cref="GapRow"/>.</b> Alle
/// vier sind Funktionen mit ähnlicher Signatur; hintereinander in einer Parameterliste werden
/// sie irgendwann vertauscht — und vertauscht heisst hier, dass die Geräteliste eines fremden
/// Kunden erscheint.</para>
///
/// <para><b>Warum die Lückenzeile nicht einfach die Lager selbst bekommt.</b> Sie hängt dann
/// an <c>ITicketRepository</c>, <c>ICompanyRepository</c> und <c>IDeviceRepository</c> — drei
/// Schnittstellen der Netzschicht in einem Ansichtsmodell, das eine Zeile einer Liste
/// darstellt. Hier steht statt dessen genau das, was die Zeile braucht: vier Fragen, die der
/// Tagesansicht gestellt werden, samt Zwischenspeicher und Fehlerbehandlung, die dort schon
/// stehen.</para>
/// </remarks>
public sealed class GapCatalog
{
    /// <summary>Baut den Katalog.</summary>
    /// <param name="ownTickets">
    /// Die offenen Tickets des Technikers — die Vorbelegung, solange keine Firma gewählt ist.
    /// </param>
    /// <param name="searchCompanies">Die Firmensuche.</param>
    /// <param name="ticketsOfCompany">Die offenen Tickets einer Firma.</param>
    /// <param name="devicesOfCompany">Die Geräte einer Firma.</param>
    public GapCatalog(
        IReadOnlyList<TicketRow> ownTickets,
        Func<string, CancellationToken, Task<CompanySearchResult>> searchCompanies,
        Func<int, CancellationToken, Task<CompanyTickets>> ticketsOfCompany,
        Func<int, CancellationToken, Task<IReadOnlyList<DeviceRow>>> devicesOfCompany)
    {
        ArgumentNullException.ThrowIfNull(ownTickets);
        ArgumentNullException.ThrowIfNull(searchCompanies);
        ArgumentNullException.ThrowIfNull(ticketsOfCompany);
        ArgumentNullException.ThrowIfNull(devicesOfCompany);

        OwnTickets = ownTickets;
        SearchCompanies = searchCompanies;
        TicketsOfCompany = ticketsOfCompany;
        DevicesOfCompany = devicesOfCompany;
    }

    /// <summary>
    /// Die offenen Tickets des Technikers.
    /// </summary>
    /// <remarks>
    /// Der Normalfall: Der Techniker trägt seine eigene vergessene Zeit nach, und das Ticket
    /// dazu gehört ihm. Erst wer für einen Kollegen einspringt oder ohne Ticket auf eine Firma
    /// bucht, braucht die Suche.
    /// </remarks>
    public IReadOnlyList<TicketRow> OwnTickets { get; }

    /// <summary>Sucht Firmen zu einem Begriff.</summary>
    public Func<string, CancellationToken, Task<CompanySearchResult>> SearchCompanies { get; }

    /// <summary>Holt die offenen Tickets einer Firma.</summary>
    public Func<int, CancellationToken, Task<CompanyTickets>> TicketsOfCompany { get; }

    /// <summary>Holt die Geräte einer Firma.</summary>
    public Func<int, CancellationToken, Task<IReadOnlyList<DeviceRow>>> DevicesOfCompany { get; }
}
