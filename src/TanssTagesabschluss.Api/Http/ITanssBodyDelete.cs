namespace TanssTagesabschluss.Api.Http;

/// <summary>
/// Löschen mit Rumpf.
/// </summary>
/// <remarks>
/// <para>Mehrere TANSS-Routen nehmen ihre Kennungen im <b>Rumpf</b> eines DELETE entgegen,
/// nicht im Pfad und nicht in der Abfragezeichenkette — beschrieben ist das für
/// <c>DELETE /api/v1/timestamps/dayClosing</c>, das ein Feld von
/// <c>{employeeId, date}</c>-Paaren erwartet. Das ist ungewöhnlich genug, dass der allgemeine
/// Vertrag <c>ITanssClient.DeleteAsync</c> es nicht vorsieht — und er soll dafür auch nicht
/// aufgebohrt werden, weil das jede andere Umsetzung zu einem Feld zwingen würde, das nur
/// wenige Routen brauchen.</para>
/// <para>Deshalb diese Zusatzschnittstelle: <see cref="TanssClient"/> erfüllt sie, und ein
/// Aufrufer, der sie vorfindet, benutzt den genauen Weg.</para>
/// <para><b>Dieses Werkzeug benutzt den Weg derzeit nicht.</b> Er gehört zum Vertrag des
/// Zugangs, weil er gemessen ist und die Umsetzung ihn ohnehin trägt; die eine Route, die ihn
/// bräuchte — das Zurücknehmen eines Tagesabschlusses —, schreibt dieses Werkzeug bewusst
/// nicht. Siehe <c>TanssRoutes.DayClosing</c>.</para>
/// </remarks>
public interface ITanssBodyDelete
{
    /// <summary>Sendet ein DELETE mit JSON-Rumpf.</summary>
    Task DeleteWithBodyAsync(string path, object? body,
                             IDictionary<string, string?>? query = null,
                             CancellationToken ct = default);
}
