using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Model;

namespace TanssTagesabschluss.Api.Repository;

/// <summary>
/// Die Geräte einer Firma: PCs und Server, Peripherie, Komponenten.
/// </summary>
/// <remarks>
/// <para><b>Drei Routen, eine Liste.</b> TANSS führt die drei Gerätearten getrennt
/// (<c>/pcs</c>, <c>/peripheries</c>, <c>/components</c>), und alle drei nehmen denselben
/// Filterrumpf entgegen. Für die Auswahl beim Nachtragen ist die Trennung ohne Belang — der
/// Techniker sucht ein Gerät, nicht eine Geräteart. Getrennt bleibt allein der Zuordnungstyp,
/// und der wird hier gesetzt: Er kommt aus der Route und nicht aus der Antwort.</para>
///
/// <para><b>Eine Geräteart, die nicht antwortet, kostet nur sich selbst.</b> Peripherie und
/// Komponenten sind auf mancher Instanz gar nicht gepflegt, und ein 403 auf eine dieser Routen
/// darf nicht die PCs mitnehmen. Gesammelt wird, was kommt.</para>
/// </remarks>
public sealed class DeviceRepository : IDeviceRepository
{
    private readonly ITanssClient _client;

    /// <summary>Baut das Repository.</summary>
    /// <param name="client">Der HTTP-Zugang.</param>
    public DeviceRepository(ITanssClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Device>> ListAsync(int companyId, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(companyId);

        DeviceQuery query = new() { CompanyId = companyId };

        // Nacheinander und nicht nebeneinander: Drei gleichzeitige Anfragen an dieselbe
        // Instanz sparen hier kaum Zeit, machen aber aus einem Aussetzer drei.
        List<Device> devices = [];

        devices.AddRange(await FetchAsync(TanssRoutes.Pcs, LinkType.Pc, query, ct)
            .ConfigureAwait(false));
        devices.AddRange(await FetchAsync(TanssRoutes.Peripheries, LinkType.Periphery, query, ct)
            .ConfigureAwait(false));
        devices.AddRange(await FetchAsync(TanssRoutes.Components, LinkType.Component, query, ct)
            .ConfigureAwait(false));

        return [.. devices
            .OrderBy(device => device.LinkTypeId)
            .ThenBy(device => device.DisplayName, StringComparer.CurrentCulture)];
    }

    /// <summary>
    /// Holt eine Geräteart.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Siehe Klassenkommentar: Eine nicht gepflegte oder nicht freigegebene
    /// Geräteart ergibt eine leere Teilliste, nicht einen Fehlschlag der ganzen Auswahl. Die
    /// Gerätezuordnung ist ohnehin eine Verfeinerung — gebucht werden kann auch ohne sie.
    /// </remarks>
    private async Task<IReadOnlyList<Device>> FetchAsync(string route, int linkTypeId,
                                                         DeviceQuery query, CancellationToken ct)
    {
        try
        {
            List<Device>? devices = await _client
                .PutAsync<List<Device>>(route, query, ct: ct).ConfigureAwait(false);

            // Der Zuordnungstyp kommt aus der Route: TANSS sagt einem PC nicht, dass er einer ist.
            return devices is null
                ? []
                : [.. devices.Select(device => device with { LinkTypeId = linkTypeId })];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException)
        {
            return [];
        }
    }
}
