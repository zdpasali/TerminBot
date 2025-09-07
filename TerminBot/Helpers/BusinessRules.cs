using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

public static class BusinessRules
{
    // radno vrijeme / praznici
    public static readonly TimeSpan Start = new TimeSpan(8, 0, 0);
    public static readonly TimeSpan End = new TimeSpan(18, 0, 0);

    private static readonly HashSet<string> Holidays = new()
    {
        "01-01","05-01","06-22","06-25","08-05","08-15","11-01","12-25","12-26",
    };

    public static bool IsBusinessDay(DateTime date) =>
        date.DayOfWeek != DayOfWeek.Sunday && !Holidays.Contains(date.ToString("MM-dd", CultureInfo.InvariantCulture));

    public static bool IsWithinBusinessHours(TimeSpan t) => t >= Start && t < End;

    // katalog
    public static class ServiceKeys
    {
        public const string IT = "it";
        public const string Vodo = "vodo";
        public const string Elektro = "elektro";
        public const string Klima = "klima";
        public const string Bravar = "bravar";
        public const string Stolar = "stolar";
        public const string Automehanicar = "automehanicar";
        public const string Gips = "gips";
        public const string Rasvjeta = "rasvjeta";
        public const string Kucanski = "kucanski";
    }

    public record ServiceInfo(string Key, int DefaultMinutes, string DisplayHr, string DisplayEn);

    public static readonly IReadOnlyDictionary<string, ServiceInfo> Services =
        new Dictionary<string, ServiceInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [ServiceKeys.IT] = new(ServiceKeys.IT, 45, "IT", "IT"),
            [ServiceKeys.Vodo] = new(ServiceKeys.Vodo, 60, "Vodoinstalacija", "Plumbing"),
            [ServiceKeys.Elektro] = new(ServiceKeys.Elektro, 60, "Električni uređaj", "Electrical device"),
            [ServiceKeys.Klima] = new(ServiceKeys.Klima, 90, "Klima", "Air conditioning"),
            [ServiceKeys.Bravar] = new(ServiceKeys.Bravar, 45, "Bravar", "Locksmith"),
            [ServiceKeys.Stolar] = new(ServiceKeys.Stolar, 90, "Stolar", "Carpenter"),
            [ServiceKeys.Automehanicar] = new(ServiceKeys.Automehanicar, 60, "Automehaničar", "Auto mechanic"),
            [ServiceKeys.Gips] = new(ServiceKeys.Gips, 90, "Gips/Knauf", "Drywall/Knauf"),
            [ServiceKeys.Rasvjeta] = new(ServiceKeys.Rasvjeta, 45, "Rasvjeta", "Lighting"),
            [ServiceKeys.Kucanski] = new(ServiceKeys.Kucanski, 60, "Kućanski aparat", "Home appliance"),
        };


    private static readonly (string pattern, string key)[] _serviceSynonyms = new[]
    {
    // IT
    (@"\b(it|računalo|kompjuter|komp|laptop|pc|računar|computer|tech\s*support|softver|software|program|internet|network|wifi|lan|server)\b", ServiceKeys.IT),

    // Vodoinstalacija
    (@"\b(vodoinstalacija|vodoinstalater|plumbing|pipe|curenje|odvod|odštopavanje|odstopavanje|slavina|pipe\s*leak|toilet|wc|faucet|tap|sink|odvodnja)\b", ServiceKeys.Vodo),

    // Elektro
    (@"\b(elektri\w+|struja|aparat|uređaj|uredaj|electrical|device|circuit|strujni|struja\s*kvar|spoj|utičnica|uticnica|prekidač|prekidac|wire|cable|lampica|elektronika)\b", ServiceKeys.Elektro),

    // Klima
    (@"\b(klima(\s*uređaj|\s*uredaj)?|air\s*cond|aircon|a/c|ac|klimatizacija|ventilacija|hladnjak\s*zrak|air\s*cooler)\b", ServiceKeys.Klima),

    // Bravar
    (@"\b(bravar|locksmith|lock|klju\w+|brava|bravarski|ključ|key|door\s*lock|lock\s*repair|safe|lockout)\b", ServiceKeys.Bravar),

    // Stolar
    (@"\b(stolar|carpenter|woodwork|ormar|kuhinja|drvo|drven\w+|namještaj|namjestaj|wood|furniture|table|chair|wardrobe|kitchen)\b", ServiceKeys.Stolar),

    // Automehaničar
    (@"\b(automehaničar|automehanicar|mechanic|auto\s*mech|car\s*repair|servis\s*auta|automobil|vozilo|engine|motor|brake|kočnica|kocnica|ulje|oil\s*change)\b", ServiceKeys.Automehanicar),

    // Gips
    (@"\b(gips|knauf|drywall|rigips|pregradni\s*zid|zid\s*ploča|plasterboard|wallboard|gypsum|ceiling|strop|plafon)\b", ServiceKeys.Gips),

    // Rasvjeta
    (@"\b(rasvjeta|svjetl\w+|lighting|lamp|luster|žarulja|zarulja|lightbulb|light\s*fixture|fluorescent|neon|light\s*switch)\b", ServiceKeys.Rasvjeta),

    // Kućanski
    (@"\b(kućanski|kucanski|appliance|washer|dryer|fridge|dishwasher|microwave|stove|pećnica|pecnica|perilica|hladnjak|zamrzivač|zamrzivac|aparat\s*za\s*kavu|coffee\s*machine)\b", ServiceKeys.Kucanski),

};


    // normalizacija teksta u kljuc
    public static string? NormalizeService(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim().ToLowerInvariant();

        foreach (var (pattern, key) in _serviceSynonyms)
            if (Regex.IsMatch(s, pattern, RegexOptions.IgnoreCase))
                return key;

        return Services.ContainsKey(s) ? s : null;
    }

    // prikazni naziv
    public static string LocalizeService(string? key, string lang)
    {
        if (string.IsNullOrWhiteSpace(key)) return "-";
        if (!Services.TryGetValue(key, out var info)) return key;
        return lang == "en" ? info.DisplayEn : info.DisplayHr;
    }

    // lista svih servisa
    public static string[] AllServices(string lang)
    {
        return Services.Values
            .Select(v => lang == "en" ? v.DisplayEn : v.DisplayHr)
            .ToArray();
    }

    // default trajanje servisa
    public static int GetDefaultDurationMinutes(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 60;
        return Services.TryGetValue(key, out var info) ? info.DefaultMinutes : 60;
    }
}
