using System;
using System.Globalization;

public static class DateTimeUtils
{
    public static string? ToIsoDate(string? input, int year)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var txt = input.Trim()
                       .Replace('/', '.')
                       .Replace('-', '.');

        if (!txt.EndsWith(".")) txt += ".";

        var formats = new[] { "d.M.", "dd.MM." };

        if (DateTime.TryParseExact(txt, formats, CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var dm))
        {
            var dt = new DateTime(year, dm.Month, dm.Day);
            return dt.ToString("yyyy-MM-dd");
        }

        if (DateTime.TryParse(txt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2))
        {
            return d2.ToString("yyyy-MM-dd");
        }

        return null;
    }

    public static string? ToIsoTime(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var txt = input.Trim()
                       .Replace('.', ':')
                       .Replace('-', ':')
                       .Replace('h', ':')
                       .Replace('H', ':');

        if (TimeSpan.TryParse(txt, out var t))
            return new DateTime(1, 1, 1, t.Hours, t.Minutes, 0).ToString("HH:mm");

        if (TimeSpan.TryParseExact(txt, "hh\\:mm", CultureInfo.InvariantCulture, out var t12))
            return new DateTime(1, 1, 1, t12.Hours, t12.Minutes, 0).ToString("HH:mm");

        if (TimeSpan.TryParseExact(txt, "HH\\:mm", CultureInfo.InvariantCulture, out var t24))
            return new DateTime(1, 1, 1, t24.Hours, t24.Minutes, 0).ToString("HH:mm");

        return null;
    }

    public static string PrettyDate(string? isoDate)
    {
        if (string.IsNullOrWhiteSpace(isoDate)) return "";
        if (DateTime.TryParse(isoDate, out var d))
            return d.ToString("dd.MM.");
        return isoDate;
    }
}
