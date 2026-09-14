using System.Text.Json.Serialization;

namespace TanssTagesabschluss.Api.Model;

/// <summary>
/// Was TANSS unter <c>GET /api/v1/employees/ownState</c> über den Träger des Tokens sagt.
/// </summary>
/// <remarks>
/// <para>Die Antwort trägt weit mehr als das hier Abgebildete — Rechteliste, Modulliste,
/// ungelesene Ereignisse, Firmenzugriffe. Abgebildet ist nur, was dieses Werkzeug auswertet:
/// die eigene Firma und der angemeldete Benutzer. Was nicht gelesen wird, steht auch nicht
/// hier; ein Feld, das niemand benutzt, ist ein Feld, das niemand pflegt.</para>
/// <para><b>Diese Klasse beantwortet zwei Fragen, an denen das Werkzeug sonst hängt.</b>
/// Erstens die eigene Firma und damit über deren Postleitzahl das Bundesland und die
/// Feiertage. Zweitens die eigene Mitarbeiterkennung: Sie steckt im Token, und aus
/// <see cref="LoggedInUser"/> lässt sie sich ablesen, statt sie jemanden auswählen zu
/// lassen — eine Auswahl, bei der jede andere Zahl als die eigene fremde Zeiten anzeigt.</para>
/// </remarks>
public sealed class OwnState
{
    /// <summary>Der Mitarbeiter, dem das Token gehört.</summary>
    [JsonPropertyName("loggedInUser")] public OwnStateUser? LoggedInUser { get; init; }

    /// <summary>
    /// Die eigene Firma als Kennung — in TANSS der Konfigurationswert
    /// <c>system.eigeneFirma.ID</c>.
    /// </summary>
    /// <remarks>
    /// <c>0</c> heißt „nicht gesetzt“. Keine andere beschriebene Route gibt diesen Wert heraus.
    /// </remarks>
    [JsonPropertyName("ownCompanyId")] public int OwnCompanyId { get; init; }

    /// <summary>
    /// Die eigene Firma als ganzer Satz Stammdaten, Anschrift eingeschlossen.
    /// </summary>
    /// <remarks>
    /// <b>Gemessen deckungsgleich mit <see cref="OwnCompanyId"/></b> (beide 100000 auf der
    /// Instanz vom 14.09.2026). Deckungsgleich heißt nicht garantiert: Wo beide etwas sagen und
    /// sich widersprechen, gilt <see cref="OwnCompanyId"/>, denn das ist der
    /// Konfigurationswert selbst — siehe <c>CompanyRepository.FindOwnAsync</c>.
    /// </remarks>
    [JsonPropertyName("defaultCompany")] public Company? DefaultCompany { get; init; }

    /// <summary>Der Stand der Schnittstelle, etwa <c>10.10.0</c>.</summary>
    [JsonPropertyName("apiVersion")] public string? ApiVersion { get; init; }

    /// <summary>Die Art des Benutzers, etwa <c>TECHNICIAN</c>.</summary>
    /// <remarks>
    /// Nur zur Anzeige in der Verbindungsprüfung. Ein <c>CUSTOMER</c>-Token sieht keine fremden
    /// Zeiten und fast keine Leistungen — dann steht das Werkzeug ohne erkennbaren Grund leer
    /// da, und dieser eine Wert erklärt es.
    /// </remarks>
    [JsonPropertyName("userType")] public string? UserType { get; init; }
}

/// <summary>Der angemeldete Mitarbeiter, so weit dieses Werkzeug ihn braucht.</summary>
public sealed class OwnStateUser
{
    /// <summary>Die Mitarbeiterkennung — der Wert für <c>loggedInUserId</c>.</summary>
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>Der Anzeigename, gemessen in der Form <c>Nachname, Vorname</c>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>Ist das Konto in Betrieb?</summary>
    [JsonPropertyName("active")] public bool Active { get; init; }
}

/// <summary>
/// Ein Mitarbeiter, wie <c>GET /api/v1/employees/{id}</c> ihn herausgibt.
/// </summary>
/// <remarks>
/// <para>Abgebildet ist nur, was dieses Werkzeug auswertet. Die Antwort trägt weit mehr —
/// Anschrift, Telefonnummern, Geburtstag, Firmenzuordnungen. Ein Feld, das niemand benutzt,
/// ist ein Feld, das niemand pflegt; und Kontaktdaten eines Menschen ohne Anlass mitzulesen
/// ist keine Kleinigkeit.</para>
/// <para><b>Der Grund, warum es diese Klasse gibt, ist ein einziges Feld:</b>
/// <see cref="WorkingHourModelId"/>. Das Arbeitszeitmodell hängt am Menschen und nicht am
/// Tag — siehe dort.</para>
/// </remarks>
public sealed class Employee
{
    /// <summary>Die Mitarbeiterkennung.</summary>
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>Der Anzeigename, gemessen in der Form <c>Nachname, Vorname</c>.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    /// <summary>
    /// Das Arbeitszeitmodell dieses Mitarbeiters. <c>0</c> heißt: keines zugeordnet.
    /// </summary>
    /// <remarks>
    /// <para><b>Eine Zahl je Mensch, nicht je Tag.</b> Der Tag der Zeitauswertung führt zwar
    /// ein eigenes <c>workingTimeModelId</c> mit, aber gemessen am 14.09.2026 steht dort auf
    /// jedem Tag <c>0</c>. Wer das Modell am Tag sucht, findet auf dieser Instanz keines — und
    /// hält dann jeden Samstag für einen Arbeitstag oder umgekehrt.</para>
    /// <para><b>Steht hier <c>0</c>, ist nichts kaputt.</b> Dann ist dem Mitarbeiter in TANSS
    /// kein Modell zugeordnet, und die Fachschicht behandelt Montag bis Freitag als
    /// Arbeitstage — sichtbar als Hinweis am jeweiligen Tag, statt stillschweigend.</para>
    /// </remarks>
    [JsonPropertyName("workingHourModelId")] public int WorkingHourModelId { get; init; }

    /// <summary>Ist das Konto in Betrieb?</summary>
    [JsonPropertyName("active")] public bool Active { get; init; }
}
