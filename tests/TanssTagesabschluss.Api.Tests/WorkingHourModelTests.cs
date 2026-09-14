using System.Text.Json;
using TanssTagesabschluss.Api.Contract;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.Api.Model;
using Xunit;

namespace TanssTagesabschluss.Api.Tests;

/// <summary>
/// Das Arbeitszeitmodell hängt am <b>Mitarbeiter</b>, nicht am Tag.
/// </summary>
/// <remarks>
/// <para><b>Nachgemessen am 14.09.2026</b> gegen 10.10.0: <c>GET /api/v1/employees/1</c> trägt
/// <c>workingHourModelId</c>; die Zeitauswertung trägt je Tag ein <c>workingTimeModelId</c>,
/// das dort auf jedem gelesenen Tag <c>0</c> ist.</para>
/// <para><b>Die Route <c>/api/v1/workingHours/client</c> ist es nicht.</b> Sie antwortet mit
/// demselben Token 403 — genau wie ein frei erfundener Pfad unter <c>/api/v1/</c> —, und im
/// ausgelieferten Archiv bildet kein Controller sie ab; dort liegen nur die Datenklassen ohne
/// <c>@RequestMapping</c>.</para>
/// </remarks>
public sealed class WorkingHourModelTests
{
    [Fact]
    public void Die_Mitarbeiterroute_steht_wortgetreu()
    {
        Assert.Equal("/api/v1/employees/7", TanssRoutes.EmployeeById(7));
        Assert.True(TanssRoutes.NeedsLoggedInUserId(TanssRoutes.EmployeeById(7)));
    }

    [Fact]
    public void Der_Mitarbeiter_traegt_seine_Modellkennung()
    {
        // Form gemessen, Werte erfunden. Gelesen wird nur, was ausgewertet wird -- die Antwort
        // traegt daneben Anschrift, Telefonnummern und Geburtstag.
        Employee employee = JsonSerializer.Deserialize<Employee>("""
            {
              "id": 7, "name": "Muster, Erika", "initials": "em",
              "workingHourModelId": 3, "accountingTypeId": 1, "active": true,
              "birthday": "1983-04-07", "emailAddress": "erika@beispiel.de"
            }
            """, TanssJson.Options)!;

        Assert.Equal(7, employee.Id);
        Assert.Equal(3, employee.WorkingHourModelId);
        Assert.True(employee.Active);
    }

    [Fact]
    public void Ohne_zugeordnetes_Modell_steht_dort_null()
    {
        // Der gemessene Fall auf dieser Instanz. Er ist kein Fehler: Dann gilt Montag bis
        // Freitag, und der Tag sagt das auch.
        Employee employee = JsonSerializer.Deserialize<Employee>(
            """{ "id": 1, "name": "Michel", "workingHourModelId": 0 }""", TanssJson.Options)!;

        Assert.Equal(0, employee.WorkingHourModelId);
    }

    [Fact]
    public void Das_Modell_des_Mitarbeiters_gilt_fuer_alle_seine_Tage()
    {
        WorkingTimeModel model = new() { Id = 3, Name = "Vollzeit" };
        TimeRecording recording = new(
            [Day(new DateOnly(2026, 9, 14), 0), Day(new DateOnly(2026, 9, 15), 0)],
            new Dictionary<int, WorkingTimeModel> { [3] = model },
            EmployeeModelId: 3);

        Assert.Same(model, recording.ModelFor(recording.Days[0]));
        Assert.Same(model, recording.ModelFor(recording.Days[1]));
    }

    [Fact]
    public void Traegt_ein_Tag_doch_eine_eigene_Kennung_hat_sie_Vorrang()
    {
        // Auf der gemessenen Instanz greift dieser Zweig nie -- dort steht ueberall 0. Er
        // steht trotzdem da: Traefe eine Instanz die Unterscheidung wirklich je Tag, waere die
        // Zahl am Tag die genauere, und sie zu uebergehen hiesse, an einem Schichtwechsel die
        // falsche Sollzeit anzunehmen.
        WorkingTimeModel schicht = new() { Id = 9, Name = "Schicht" };
        WorkingTimeModel vollzeit = new() { Id = 3, Name = "Vollzeit" };

        TimeRecording recording = new(
            [Day(new DateOnly(2026, 9, 14), 9)],
            new Dictionary<int, WorkingTimeModel> { [3] = vollzeit, [9] = schicht },
            EmployeeModelId: 3);

        Assert.Same(schicht, recording.ModelFor(recording.Days[0]));
    }

    [Fact]
    public void Ohne_Modellkennung_gibt_es_kein_Modell_und_keine_Ausnahme()
    {
        TimeRecording recording = new(
            [Day(new DateOnly(2026, 9, 14), 0)],
            new Dictionary<int, WorkingTimeModel>(),
            EmployeeModelId: 0);

        Assert.Null(recording.ModelFor(recording.Days[0]));
    }

    private static TimestampDay Day(DateOnly date, int modelId) =>
        new() { EmployeeId = 7, Date = date, WorkingTimeModelId = modelId };
}
