using System.Text.Json;
using TanssTagesabschluss.Api.Http;
using TanssTagesabschluss.Api.Model;
using Xunit;

namespace TanssTagesabschluss.Api.Tests;

/// <summary>
/// Die Regeln der Netzschicht, die sich gegen eine Attrappe <b>nicht</b> prüfen lassen — und
/// deshalb hier gegen die Regel selbst geprüft werden.
/// </summary>
public sealed class RouteTests
{
    [Theory]
    [InlineData("/api/v1/timestamps/statistics", true)]
    [InlineData("/api/v1/supports/list", true)]
    [InlineData("/api/v1/tickets", true)]
    [InlineData("/api/tanss.x/v1/technicians", false)]
    [InlineData("/api/erp/v1/companies/employees", false)]
    public void LoggedInUserId_nur_auf_dem_v1_Praefix(string path, bool expected) =>
        Assert.Equal(expected, TanssRoutes.NeedsLoggedInUserId(path));

    [Fact]
    public void Token_praegen_gilt_als_seiteneffektbehaftet()
    {
        // TANSS stellt bei JEDEM Aufruf ein neues JWT aus und kann es in 10.10.0 nicht
        // widerrufen. Eine Wiederholung nach einer Zeitueberschreitung praegte ein zweites,
        // von dem niemand mehr erfaehrt.
        Assert.True(TanssRoutes.HasSideEffectOnGet(TanssRoutes.MintToken));
    }

    [Fact]
    public void Der_Riegel_haengt_am_Praefix_und_nicht_an_der_einen_Route()
    {
        // Jede Tokenart, die unter /jwts spaeter dazukommt, ist damit vom ersten Aufruf an
        // geschuetzt - statt erst dann, wenn jemand den Riegel nachtraegt.
        Assert.True(TanssRoutes.HasSideEffectOnGet(TanssRoutes.JwtPrefix + "/irgendetwas"));
    }

    [Fact]
    public void Lesende_Routen_sind_nicht_seiteneffektbehaftet()
    {
        Assert.False(TanssRoutes.HasSideEffectOnGet(TanssRoutes.TimestampStatistics));
        Assert.False(TanssRoutes.HasSideEffectOnGet(TanssRoutes.SupportList));
    }
}

/// <summary>Die Einheitenfrage, an der still Daten verfälscht werden.</summary>
public sealed class TanssTimeTests
{
    [Fact]
    public void TANSS_rechnet_in_Sekunden_und_nicht_in_Millisekunden()
    {
        DateTimeOffset moment = new(2026, 9, 14, 10, 30, 0, TimeSpan.Zero);

        // 1_789_381_800 = 2026-09-14 10:30:00 UTC. Nachgerechnet und nicht geschaetzt:
        // Waere hier ein Millisekundenwert richtig, stuende dort eine Zahl mit drei
        // Nullen mehr - und genau diese Verwechslung ist der Grund fuer diesen Test.
        Assert.Equal(1_789_381_800, TanssTime.ToUnixSeconds(moment));
    }

    [Fact]
    public void Die_Null_heisst_nicht_gesetzt()
    {
        // Wuerde sie als Zeitpunkt gelesen, stuende ueberall der 1.1.1970 - und das sieht
        // nach einem Datum aus und nicht nach einer fehlenden Angabe.
        Assert.Null(TanssTime.FromUnixSeconds(0));
    }

    [Fact]
    public void Hin_und_zurueck_ergibt_denselben_Zeitpunkt()
    {
        DateTimeOffset moment = new(2026, 3, 29, 1, 30, 0, TimeSpan.Zero);

        Assert.Equal(moment, TanssTime.FromUnixSeconds(TanssTime.ToUnixSeconds(moment)));
    }
}

/// <summary>
/// Das Lesen der Zeitauswertung — vor allem die Stelle, an der TANSS seiner eigenen
/// Beschreibung widerspricht.
/// </summary>
public sealed class TimestampDayTests
{
    [Fact]
    public void Types_wird_als_Feld_gelesen()
    {
        // So zeigt es das Beispiel der Beschreibung, und so ist es der Normalfall: Wer
        // mittags Pause macht, hat zwei WORK-Abschnitte.
        const string Json = """
        {
          "employeeId": 1, "date": "2026-09-14", "weekDay": "MONDAY", "workingTimeModelId": 0,
          "types": { "WORK": [
            { "begin": 100, "end": 200, "seconds": 100, "state": "COMPLETE" },
            { "begin": 300, "end": 400, "seconds": 100, "state": "COMPLETE" } ] }
        }
        """;

        TimestampDay? day = JsonSerializer.Deserialize<TimestampDay>(Json, TanssJson.Options);

        Assert.NotNull(day);
        Assert.Equal(2, day.PeriodsOf(TimestampType.Work).Count);
    }

    [Fact]
    public void Types_wird_auch_als_einzelnes_Objekt_gelesen()
    {
        // So sagt es das SCHEMA derselben Datei. Welche der beiden Formen eine Instanz
        // schickt, ist ungemessen - gelesen werden deshalb beide.
        const string Json = """
        {
          "employeeId": 1, "date": "2026-09-14", "weekDay": "MONDAY", "workingTimeModelId": 0,
          "types": { "WORK": { "begin": 100, "end": 200, "seconds": 100, "state": "COMPLETE" } }
        }
        """;

        TimestampDay? day = JsonSerializer.Deserialize<TimestampDay>(Json, TanssJson.Options);

        Assert.NotNull(day);
        TimestampPeriod only = Assert.Single(day.PeriodsOf(TimestampType.Work));
        Assert.Equal(100, only.Begin);
    }

    [Fact]
    public void Eine_unbekannte_Art_kostet_die_Art_und_nicht_den_Tag()
    {
        const string Json = """
        {
          "employeeId": 1, "date": "2026-09-14", "weekDay": "MONDAY", "workingTimeModelId": 0,
          "types": { "WORK": [ { "begin": 100, "end": 200, "seconds": 100 } ],
                     "ETWAS_NEUES": "keine Abschnitte" }
        }
        """;

        TimestampDay? day = JsonSerializer.Deserialize<TimestampDay>(Json, TanssJson.Options);

        Assert.NotNull(day);
        Assert.Single(day.PeriodsOf(TimestampType.Work));
        Assert.Empty(day.PeriodsOf("ETWAS_NEUES"));
    }

    [Fact]
    public void DOCUMENTED_SUPPORT_zaehlt_nicht_als_Anwesenheit()
    {
        // Die Art beschreibt die Deckung und nicht die Zeit, in der jemand da war. Sie
        // mitzuzaehlen machte jede Luecke unsichtbar, die sie aufdecken soll.
        Assert.False(TimestampType.IsAttendance(TimestampType.DocumentedSupport));
        Assert.True(TimestampType.IsAttendance(TimestampType.Work));
        Assert.True(TimestampType.IsAttendance(TimestampType.Inhouse));
        Assert.True(TimestampType.IsAttendance(TimestampType.Errand));
    }

    [Fact]
    public void Urlaub_und_Krankheit_erklaeren_eine_fehlende_Leistung()
    {
        Assert.True(TimestampType.IsAbsence(TimestampType.Vacation));
        Assert.True(TimestampType.IsAbsence(TimestampType.Illness));
        Assert.False(TimestampType.IsAbsence(TimestampType.Work));
    }
}

/// <summary>Die Zuordnungstypen — die Zahlen stammen aus dem Server, nicht aus der Beschreibung.</summary>
public sealed class LinkTypeTests
{
    [Fact]
    public void PC_und_Server_tragen_dieselbe_Kennung()
    {
        // Kein Lesefehler, sondern die Bauform: Ein Server ist in TANSS ein PC mit
        // gesetztem Serverkennzeichen, beide haengen an derselben Tabelle.
        Assert.Equal(1, LinkType.Pc);
    }

    [Theory]
    [InlineData(LinkType.Pc, "PC / Server")]
    [InlineData(LinkType.Periphery, "Peripherie")]
    [InlineData(LinkType.Component, "Komponente")]
    [InlineData(LinkType.Ticket, "Ticket")]
    public void Jede_benutzte_Kennung_hat_einen_deutschen_Namen(int id, string expected) =>
        Assert.Equal(expected, LinkType.NameOf(id));
}

/// <summary>Die Deutung einer gelesenen Leistung.</summary>
public sealed class SupportEntryTests
{
    [Fact]
    public void Eine_Leistung_ohne_Planungsart_gilt_als_Leistung()
    {
        // Die aelteste und haeufigste Form traegt keine. Sie als Termin zu behandeln machte
        // jeden gewoehnlichen Tag zu einer einzigen Luecke.
        Assert.True(new SupportEntry { PlanningType = null }.IsSupport);
        Assert.True(new SupportEntry { PlanningType = "" }.IsSupport);
    }

    [Fact]
    public void Ein_fester_Termin_ist_keine_Leistung()
    {
        Assert.False(new SupportEntry { PlanningType = PlanningType.AppointmentFix }.IsSupport);
        Assert.False(new SupportEntry { PlanningType = PlanningType.AppointmentProposal }.IsSupport);
    }

    [Fact]
    public void Die_Dauer_kommt_in_Minuten_und_der_Beginn_in_Sekunden()
    {
        // Die beiden Einheiten stossen in diesem Werkzeug unmittelbar aufeinander.
        SupportEntry entry = new()
        {
            Date = TanssTime.ToUnixSeconds(new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero)),
            Duration = 90,
        };

        Assert.Equal(TimeSpan.FromMinutes(90), entry.Span);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 10, 30, 0, TimeSpan.Zero), entry.End);
    }
}

/// <summary>Der Entwurf, der an TANSS zurückgeht.</summary>
public sealed class SupportDraftTests
{
    private static SupportDraft Draft(string json) =>
        SupportDraft.From(System.Text.Json.Nodes.JsonNode.Parse(json)!);

    [Fact]
    public void Die_vorbelegten_Felder_bleiben_unangetastet()
    {
        // DER Grund fuer den JsonObject-Umweg: Von den ueber achtzig Feldern verstehen wir
        // eine Handvoll. Die uebrigen entscheiden ueber Stundensatz und Abrechnung.
        SupportDraft draft = Draft("""{"hourlyRate":95.5,"accountingTypeId":3,"text":""}""");

        draft.Text = "Neuer Text";

        Assert.Equal("95.5", draft.Raw("hourlyRate"));
        Assert.Equal("3", draft.Raw("accountingTypeId"));
    }

    [Fact]
    public void Die_Dauer_wird_abgerundet_und_nicht_gerundet()
    {
        // Aufrunden hiesse, dem Kunden eine Minute zu berechnen, die niemand gearbeitet hat.
        SupportDraft draft = Draft("{}");

        draft.Duration = TimeSpan.FromSeconds(29 * 60 + 36);

        Assert.Equal("29", draft.Raw("duration"));
    }

    [Fact]
    public void Der_Beginn_geht_als_Unix_Sekunden_hinaus()
    {
        SupportDraft draft = Draft("{}");
        DateTimeOffset begin = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

        draft.Begin = begin;

        Assert.Equal(TanssTime.ToUnixSeconds(begin).ToString(
            System.Globalization.CultureInfo.InvariantCulture), draft.Raw("date"));
    }

    [Fact]
    public void Eine_Zuordnung_wird_nur_vollstaendig_gesetzt()
    {
        // Ein Typ ohne Kennung zeigt auf nichts, eine Kennung ohne Typ ist mehrdeutig -
        // die 17 ist ein PC UND ein Drucker.
        SupportDraft draft = Draft("{}");

        draft.AssignTo(LinkType.Pc, 0);

        Assert.Equal(0, draft.LinkTypeId);
        Assert.Equal(0, draft.LinkId);
    }

    [Fact]
    public void Eine_vollstaendige_Zuordnung_geht_durch()
    {
        SupportDraft draft = Draft("{}");

        draft.AssignTo(LinkType.Periphery, 17);

        Assert.Equal(LinkType.Periphery, draft.LinkTypeId);
        Assert.Equal(17, draft.LinkId);
    }

    [Fact]
    public void Das_interne_Kennzeichen_laesst_sich_setzen()
    {
        SupportDraft draft = Draft("""{"internal":false}""");

        draft.Internal = true;

        Assert.True(draft.Internal);
        Assert.Equal("true", draft.Raw("internal"));
    }
}
