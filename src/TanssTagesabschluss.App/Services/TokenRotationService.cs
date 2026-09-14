using System.Globalization;
using System.Runtime.Versioning;
using TanssTagesabschluss.Api;
using TanssTagesabschluss.Api.Auth;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.App.Runtime;

namespace TanssTagesabschluss.App.Services;

/// <summary>
/// Erneuert das Arbeitstoken, bevor es abläuft.
/// </summary>
/// <remarks>
/// <para><b>Ein TANSS-Token läuft ein Jahr und lässt sich in 10.10.0 nicht widerrufen.</b> Aus
/// beidem folgt dasselbe: Es wird so selten wie möglich geprägt. Der Takt ist deshalb ein Tag
/// und nicht eine Stunde, und geprägt wird erst, wenn die Restlaufzeit unter die eingestellte
/// Schwelle fällt.</para>
///
/// <para><b>Das neue Token wird gegengeprüft, bevor es das alte ersetzt.</b> Solange das alte
/// gültig ist, ist der schlechtestmögliche Ausgang einer misslungenen Erneuerung, dass alles
/// bleibt, wie es war. Würde erst geschrieben und dann geprüft, stünde das Werkzeug bei einem
/// fehlerhaften neuen Token ohne jeden Zugang da.</para>
///
/// <para><b>Die Probe ist ein echter, aber harmloser Aufruf.</b> Gewählt ist die Liste der
/// Pausenregeln: Sie ist klein, sie liest, und sie verlangt dieselben Rechte wie alles andere,
/// was dieses Werkzeug tut.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class TokenRotationService : PeriodicService
{
    /// <summary>Baut den Dienst.</summary>
    /// <param name="context">Der Zugang zu Zustand und Zusammenbau.</param>
    public TokenRotationService(IRuntimeContext context) : base(context)
    {
    }

    /// <inheritdoc />
    public override string Name => "Token";

    /// <inheritdoc />
    public override string Description =>
        "Erneuert das Arbeitstoken, bevor es abläuft — und übernimmt es erst nach bestandener Probe.";

    /// <inheritdoc />
    protected override TimeSpan Interval => TimeSpan.FromHours(24);

    /// <inheritdoc />
    protected override string NotConfiguredMessage =>
        "Ruht: Ohne Konfiguration ist keine Instanz bekannt, bei der ein Token zu prägen wäre.";

    /// <inheritdoc />
    protected override string FaultMessage =>
        "Die Tokenprüfung ist fehlgeschlagen. Das bisherige Token bleibt in Kraft; der nächste "
        + "Takt versucht es erneut.";

    /// <inheritdoc />
    protected override async Task<string> RunCycleAsync(RuntimeComposition composition,
                                                        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(composition);

        TokenRotationResult result = await TanssAuth.RotateIfNeededAsync(
            composition.Client,
            composition.Tokens,
            (minted, token) => VerifyAsync(composition, minted, token),
            composition.Config.Tanss.RotateBeforeDays,
            info: "TANSS Tagesabschluss",
            now: Context.Clock.GetUtcNow(),
            ct: ct).ConfigureAwait(false);

        if (result.Rotated)
        {
            return result.NewExpiry is { } expiry
                ? string.Create(CultureInfo.CurrentCulture,
                    $"Token erneuert; es läuft bis {expiry.ToLocalTime():d}.")
                : "Token erneuert.";
        }

        return result.Error is null
            ? result.Reason
            : result.Reason + " " + result.Error;
    }

    /// <summary>
    /// Die Probe auf ein frisch geprägtes Token.
    /// </summary>
    /// <remarks>
    /// <b>Wirft nicht.</b> Ein Fehlschlag ist die Antwort „nicht bestanden“, und die führt
    /// dazu, dass das bisherige Token in Kraft bleibt — genau so, wie es sein soll.
    /// </remarks>
    private static async Task<bool> VerifyAsync(RuntimeComposition composition, string minted,
                                                CancellationToken ct)
    {
        using TanssClient probe = composition.CreateClientWith(minted);

        try
        {
            // UEBER DEN CLIENT UND NICHT UEBER DAS REPOSITORY. TimestampRepository
            // .ListPauseConfigsAsync faengt jede TanssException ab und gibt eine leere Liste
            // zurueck - mit Absicht, denn die Pausenregeln sind dort nur eine Verfeinerung.
            // Als Probe waere genau das verhaengnisvoll: Ein Token, auf das TANSS mit 403
            // antwortet, bestuende sie klaglos, wuerde uebernommen und legte das Werkzeug still.
            // Die Probe muss den Fehlschlag SEHEN.
            _ = await probe.GetAsync<List<Api.Model.PauseConfig>>(TanssRoutes.PauseConfigs, ct: ct)
                .ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TanssException)
        {
            return false;
        }
    }
}
