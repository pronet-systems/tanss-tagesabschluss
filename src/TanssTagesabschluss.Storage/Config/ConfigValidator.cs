using System.Globalization;
using TanssTagesabschluss.Workday.Holidays;

namespace TanssTagesabschluss.Storage.Config;

/// <summary>
/// Die Regeln, die eine Konfiguration erfüllen muss — und die Begründung zu jeder.
/// </summary>
/// <remarks>
/// <para><b>Geprüft wird beim Laden und vor jedem Schreiben.</b> Eine Einstellung, die erst im
/// Betrieb auffällt, fällt am Freitagnachmittag auf.</para>
/// <para><b>Es werden alle Verstöße gesammelt und dann gemeinsam gemeldet.</b> Wer eine Datei
/// von Hand pflegt, soll nicht fünfmal starten müssen, um fünf Fehler zu finden.</para>
/// <para><b>Jede Meldung sagt drei Dinge:</b> was ist, warum das ein Problem ist und was zu tun
/// ist. Ein „ungültiger Wert“ allein hilft niemandem.</para>
/// </remarks>
public static class ConfigValidator
{
    /// <summary>Prüft die Konfiguration und wirft, wenn etwas nicht stimmt.</summary>
    /// <param name="config">Die zu prüfende Konfiguration.</param>
    /// <param name="origin">Woher sie stammt — steht in der Meldung.</param>
    /// <exception cref="ConfigValidationException">Mindestens eine Regel ist verletzt.</exception>
    public static void Validate(AppConfig config, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        List<string> problems = [];

        ValidateTanss(config.Tanss, problems);
        ValidateCompany(config.Company, problems);
        ValidateGaps(config.Gaps, problems);
        ValidateReminder(config.Reminder, problems);
        ValidateProxy(config.Proxy, problems);
        ValidateLogging(config.Logging, problems);

        if (problems.Count == 0)
        {
            return;
        }

        string where = string.IsNullOrWhiteSpace(origin) ? "Die Konfiguration" : $"\"{origin}\"";
        string headline = string.Create(CultureInfo.CurrentCulture,
            $"{where} ist nicht verwendbar — {problems.Count} Beanstandung(en):");

        throw new ConfigValidationException(
            headline + Environment.NewLine
            + string.Join(Environment.NewLine, problems.Select(problem => "• " + problem)),
            problems);
    }

    private static void ValidateTanss(TanssSection tanss, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(tanss.BaseUrl))
        {
            problems.Add("tanss.base_url fehlt. Ohne Basisadresse gibt es keine Instanz, mit der "
                         + "gesprochen werden könnte. Sie lautet üblicherweise "
                         + "https://tanss.beispiel.de/backend.");
        }
        else if (!Uri.TryCreate(tanss.BaseUrl, UriKind.Absolute, out Uri? uri)
                 || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add($"tanss.base_url (\"{tanss.BaseUrl}\") ist keine gültige Adresse. Sie "
                         + "muss mit http:// oder https:// beginnen.");
        }
        else if (!tanss.BaseUrl.TrimEnd('/').EndsWith("/backend", StringComparison.OrdinalIgnoreCase))
        {
            // Keine Formalie: Zeigt die Adresse auf die Weboberflaeche, antwortet die
            // PHP-Oberflaeche auf JEDE Anfrage mit HTTP 400 - auch ohne Token. Der Fehler
            // saehe dann wie ein kaputtes Werkzeug aus und nicht wie eine falsche Adresse.
            problems.Add($"tanss.base_url (\"{tanss.BaseUrl}\") endet nicht auf /backend. Zeigt "
                         + "die Adresse auf die TANSS-Weboberfläche statt auf die "
                         + "Schnittstelle, antwortet diese auf jede Anfrage mit HTTP 400, und "
                         + "der Fehler sieht wie ein Programmfehler aus. Bitte /backend anhängen.");
        }

        if (tanss.EmployeeId <= 0)
        {
            problems.Add("tanss.employee_id fehlt oder ist nicht positiv. An dieser Kennung "
                         + "hängt alles: Sie entscheidet, wessen Arbeitszeiten gelesen und "
                         + "wessen Lücken gezeigt werden.");
        }

        if (tanss.TimeoutSeconds is < 5 or > 300)
        {
            problems.Add(
                string.Create(CultureInfo.CurrentCulture,
                    $"tanss.timeout_seconds ({tanss.TimeoutSeconds}) liegt ausserhalb von 5 bis 300.")
                + " Unter fünf Sekunden bricht die Abfrage eines ganzen Monats regelmässig ab, "
                + "über fünf Minuten wirkt die Oberfläche eingefroren.");
        }

        if (tanss.RotateBeforeDays is < 1 or > 364)
        {
            problems.Add(
                string.Create(CultureInfo.CurrentCulture,
                    $"tanss.rotate_before_days ({tanss.RotateBeforeDays}) liegt ausserhalb von 1 bis 364.")
                + " Ein Token läuft ein Jahr; erneuert werden muss es davor.");
        }

        if (!tanss.VerifyTls)
        {
            // Kein Fehler, aber es gehoert ausgesprochen: Ohne Zertifikatspruefung ist die
            // Verbindung gegen einen Angreifer in der Mitte wertlos.
            problems.Add("tanss.verify_tls ist abgeschaltet. Damit ist die Verbindung gegen "
                         + "einen Angreifer in der Mitte wertlos — vertretbar allein für eine "
                         + "Instanz im eigenen Netz mit selbstsigniertem Zertifikat, und auch "
                         + "dort nur, bis das Zertifikat im Rechnerspeicher liegt. Ist das "
                         + "gewollt, gehört das Zertifikat aufgenommen und dieser Schalter "
                         + "wieder an.");
        }
    }

    private static void ValidateCompany(CompanySection company, List<string> problems)
    {
        if (company.FederalState is { } code && FederalState.ByCode(code) is null)
        {
            string known = string.Join(", ", FederalState.All.Select(state => state.Code));
            problems.Add($"company.federal_state (\"{code}\") ist kein bekanntes Bundesland. "
                         + $"Zulässig sind: {known}.");
        }

        if (company.PostalCode is { Length: > 0 } postal
            && (postal.Length != 5 || !postal.All(char.IsAsciiDigit)))
        {
            problems.Add($"company.postal_code (\"{postal}\") ist keine deutsche Postleitzahl. "
                         + "Sie hat fünf Ziffern. Aus ihr wird das Bundesland bestimmt und "
                         + "daraus der Feiertagskalender.");
        }

        if (company.OwnCompanyId < 0)
        {
            problems.Add("company.own_company_id ist negativ. 0 bedeutet „noch nicht "
                         + "ermittelt“; jede echte Firmenkennung ist positiv.");
        }
    }

    private static void ValidateGaps(GapSection gaps, List<string> problems)
    {
        if (gaps.MinimumMinutes is < 1 or > 240)
        {
            problems.Add(
                string.Create(CultureInfo.CurrentCulture,
                    $"gaps.minimum_minutes ({gaps.MinimumMinutes}) liegt ausserhalb von 1 bis 240.")
                + " Unter einer Minute stünde jede Rundungsdifferenz in der Liste; über vier "
                + "Stunden bliebe von der Prüfung nichts übrig.");
        }

        // Die Obergrenze lag einmal bei 90 Tagen, mit der Begruendung, an aelteren Tagen
        // liesse sich ohnehin nichts mehr nachtragen. Das stimmt so nicht: Eine vergessene
        // Leistung faellt oft erst bei der Quartalsabrechnung auf, und dann will jemand ein
        // ganzes Quartal oder ein Jahr durchsehen. Ein Jahr ist die neue Grenze - darueber
        // hinaus geht es nicht mehr um Nachtragen, sondern um Statistik.
        foreach (string day in gaps.WorkDays)
        {
            if (!Enum.TryParse(day, ignoreCase: true, out DayOfWeek _))
            {
                problems.Add(
                    $"gaps.work_days enthält „{day}“ — das ist kein Wochentag. Erlaubt sind "
                    + "MONDAY, TUESDAY, WEDNESDAY, THURSDAY, FRIDAY, SATURDAY und SUNDAY. "
                    + "Ein Tippfehler hiesse hier, dass an diesem Tag stillschweigend nicht "
                    + "gearbeitet wird und keine Lücke gemeldet wird.");
            }
        }

        // Leer heisst "nicht angegeben" und ist der Regelfall -- nur ein gesetzter Wert muss
        // eine Uhrzeit sein.
        if (!string.IsNullOrWhiteSpace(gaps.WorkBegin))
        {
            CheckTime(gaps.WorkBegin, "gaps.work_begin", problems);
        }

        if (!string.IsNullOrWhiteSpace(gaps.WorkEnd))
        {
            CheckTime(gaps.WorkEnd, "gaps.work_end", problems);
        }

        // Beides oder nichts: Ein Beginn ohne Ende ergaebe ein offenes Zeitfenster bis
        // Mitternacht, ein Ende ohne Beginn eines ab Mitternacht.
        if (string.IsNullOrWhiteSpace(gaps.WorkBegin) != string.IsNullOrWhiteSpace(gaps.WorkEnd))
        {
            problems.Add("gaps.work_begin und gaps.work_end gehören zusammen — es ist nur eines "
                + "von beiden gesetzt. Ein halber Arbeitsrahmen ergäbe ein offenes Zeitfenster "
                + "bis Mitternacht.");
        }

        if (TimeOnly.TryParse(gaps.WorkBegin, CultureInfo.InvariantCulture, out TimeOnly begin)
            && TimeOnly.TryParse(gaps.WorkEnd, CultureInfo.InvariantCulture, out TimeOnly end)
            && end <= begin)
        {
            problems.Add($"gaps.work_end ({gaps.WorkEnd}) liegt nicht nach gaps.work_begin "
                + $"({gaps.WorkBegin}). Nachtschichten über Mitternacht kennt dieses Werkzeug "
                + "nicht.");
        }

        if (gaps.HistoryDays is < 1 or > 365)
        {
            problems.Add(
                string.Create(CultureInfo.CurrentCulture,
                    $"gaps.history_days ({gaps.HistoryDays}) liegt ausserhalb von 1 bis 365.")
                + " Ein langer Rückblick lädt bei jedem Öffnen entsprechend viele Tage aus "
                + "TANSS; die Übersicht braucht dann spürbar länger.");
        }
    }

    private static void ValidateReminder(ReminderSection reminder, List<string> problems)
    {
        CheckTime(reminder.MorningTime, "reminder.morning_time", problems);
        CheckTime(reminder.EveningTime, "reminder.evening_time", problems);

        if (TryParse(reminder.MorningTime, out TimeOnly morning)
            && TryParse(reminder.EveningTime, out TimeOnly evening)
            && morning >= evening)
        {
            // Keine Formalie: Die beiden Zeitpunkte beantworten verschiedene Fragen - morgens
            // der Vortag, abends der laufende Tag. In der falschen Reihenfolge fragt die
            // "morgendliche" Pruefung nach einem Tag, der noch gar nicht vorbei ist.
            problems.Add($"reminder.morning_time ({reminder.MorningTime}) liegt nicht vor "
                         + $"reminder.evening_time ({reminder.EveningTime}). Die morgendliche "
                         + "Prüfung sieht den Vortag durch, die abendliche Erinnerung den "
                         + "laufenden Tag — in dieser Reihenfolge ergeben sie einen Sinn.");
        }
    }

    private static void ValidateProxy(ProxySection proxy, List<string> problems)
    {
        if (!proxy.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(proxy.Address))
        {
            problems.Add("proxy.enabled ist an, aber proxy.address ist leer.");
        }

        if (proxy.Port is < 1 or > 65535)
        {
            problems.Add(string.Create(CultureInfo.CurrentCulture,
                $"proxy.port ({proxy.Port}) liegt ausserhalb von 1 bis 65535."));
        }
    }

    private static void ValidateLogging(LoggingSection logging, List<string> problems)
    {
        string[] levels = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

        if (!levels.Contains(logging.Level, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"logging.level (\"{logging.Level}\") ist unbekannt. Zulässig sind: "
                         + string.Join(", ", levels) + ".");
        }
    }

    private static void CheckTime(string value, string field, List<string> problems)
    {
        if (!TryParse(value, out _))
        {
            problems.Add($"{field} (\"{value}\") ist keine Uhrzeit. Erwartet wird HH:mm, "
                         + "etwa 16:45.");
        }
    }

    /// <summary>Liest eine Uhrzeit im Format <c>HH:mm</c>.</summary>
    /// <remarks>
    /// Ausdrücklich invariant und mit festem Muster: Eine Konfigurationsdatei darf nicht anders
    /// gelesen werden, je nachdem, welche Ländereinstellung der Rechner hat.
    /// </remarks>
    /// <param name="value">Der Text.</param>
    /// <param name="time">Die gelesene Uhrzeit.</param>
    /// <returns><c>true</c>, wenn der Text eine Uhrzeit ist.</returns>
    public static bool TryParse(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH\\:mm", CultureInfo.InvariantCulture,
                               DateTimeStyles.None, out time);
}
