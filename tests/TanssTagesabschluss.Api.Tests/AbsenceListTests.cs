using System.Text.Json;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.Api.Model;
using Xunit;

namespace TanssTagesabschluss.Api.Tests;

/// <summary>
/// Die Antwort von <c>PUT /api/v1/vacationRequests/list</c> in ihren beiden Formen.
/// </summary>
/// <remarks>
/// <para><b>Der Anlass ist gemessen.</b> <c>api-doc-10.10.0.yaml</c> beschreibt den Inhalt als
/// <c>array</c>; die Instanz vom 14.09.2026 antwortet mit einem Objekt, in dem die Anträge
/// unter <c>vacationRequests</c> stehen, daneben <c>employeeSummaries</c>. Gegen eine Liste
/// gelesen ergab das einen Lesefehler — und ein verschluckter Lesefehler hätte jeden
/// Urlaubstag zu einer gemeldeten Lücke gemacht.</para>
/// <para>Die Werte unten sind erfunden; die <b>Form</b> ist die gemessene.</para>
/// </remarks>
public sealed class AbsenceListTests
{
    /// <summary>Die gemessene Form: ein Objekt mit <c>vacationRequests</c>.</summary>
    private const string Measured = """
        {
          "vacationRequests": [
            {
              "id": 344,
              "planningType": "VACATION",
              "status": "APPROVED",
              "startDate": 1785708000,
              "endDate": 1786053600,
              "requesterId": 7,
              "requestReason": "",
              "supervisorId": 1459,
              "planningAdditionalId": 0,
              "days": [
                { "vacationRequestId": 344, "date": 1785708000, "forenoon": true,
                  "afternoon": true, "startHour": 0, "startMinute": 0, "endHour": 0,
                  "endMinute": 0, "pause": 0 }
              ]
            }
          ],
          "employeeSummaries": {
            "7": { "employeeId": 7, "byPlanningType": {},
                   "vacationDaysForYear": { "employeeId": 7, "year": 2026,
                                            "numberOfDays": 28.0, "transferred": 0.0 } }
          }
        }
        """;

    /// <summary>Die beschriebene Form: eine blanke Liste.</summary>
    private const string Documented = """
        [
          { "id": 344, "planningType": "VACATION", "status": "APPROVED",
            "startDate": 1785708000, "endDate": 1786053600, "days": [] }
        ]
        """;

    [Fact]
    public void Die_gemessene_Form_wird_gelesen()
    {
        AbsenceList list = Parse(Measured);

        AbsenceRequest request = Assert.Single(list.Requests);
        Assert.Equal(344, request.Id);
        Assert.True(request.IsApproved);
        Assert.Equal("VACATION", request.PlanningType);

        AbsenceDay day = Assert.Single(request.Days);
        Assert.Equal(AbsenceCoverage.FullDay, day.Coverage);
    }

    [Fact]
    public void Die_beschriebene_Form_wird_auch_gelesen()
    {
        // Auf welcher Version die Instanz des Kunden steht, entscheidet nicht dieses Werkzeug.
        AbsenceList list = Parse(Documented);

        Assert.Equal(344, Assert.Single(list.Requests).Id);
    }

    [Fact]
    public void Ein_leerer_Monat_ist_kein_Fehler()
    {
        // Der Regelfall: niemand hatte Urlaub. Gemessen kommt dann ein leeres Feld -- und
        // "employeeSummaries" steht trotzdem da.
        AbsenceList list = Parse("""
            {
              "vacationRequests": [],
              "employeeSummaries": { "7": { "employeeId": 7, "byPlanningType": {} } }
            }
            """);

        Assert.Empty(list.Requests);
    }

    [Fact]
    public void Ein_Objekt_ohne_das_erwartete_Feld_wird_gemeldet_und_nicht_verschwiegen()
    {
        // Der Fall, der still am teuersten waere: Ein leeres Ergebnis hiesse "kein Urlaub",
        // und dann meldet das Werkzeug eine Luecke an einem Tag am Strand. Lieber laut.
        JsonException error = Assert.Throws<JsonException>(
            () => Parse("""{ "irgendwas": [] }"""));

        Assert.Contains("vacationRequests", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Zahl_statt_einer_Liste_wird_gemeldet()
    {
        Assert.Throws<JsonException>(() => Parse("42"));
    }

    [Fact]
    public void Abwesenheiten_werden_nur_gelesen_niemals_geschrieben()
    {
        // Der Konverter kennt keinen Schreibweg. Das ist kein Versehen: Dieses Werkzeug traegt
        // Leistungen nach und ruehrt Urlaubsantraege nicht an.
        Assert.Throws<NotSupportedException>(
            () => JsonSerializer.Serialize(new AbsenceList(), TanssJson.Options));
    }

    private static AbsenceList Parse(string json) =>
        JsonSerializer.Deserialize<AbsenceList>(json, TanssJson.Options)
        ?? throw new InvalidOperationException("Die Vorlage liess sich nicht lesen.");
}
