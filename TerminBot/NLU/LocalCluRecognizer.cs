using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TerminBot.NLU
{
    public class LocalCluRecognizer : IIntentRecognizer
    {
        private static readonly string ServiceWordEN =
            @"(?:it|plumbing|electrical(?:\s+device)?|air\s*cond(?:ition(?:ing)?)?|ac|locksmith|carpenter|mechanic|drywall|lighting|appliance|device)";

        private static readonly string ServiceWordHR =
            @"(?:it|vodoinstalacija|elektri\w+|klima|bravar|stolar|automehaničar|automehanicar|gips|knauf|rasvjeta|svjetla|kućanski|kucanski)";

        public Task<IntentResult> RecognizeAsync(string text, string lang, CancellationToken ct = default)
        {
            var t = (text ?? "").Trim();
            var lower = t.ToLowerInvariant();


            if (Regex.IsMatch(lower, @"\b(rezerviraj|rezervacija|book|booking|schedule)\b"))
                return Task.FromResult(BuildResult(Intent.BookAppointment, t, lower, lang));

            if (Regex.IsMatch(lower, @"^(prikaži\s+rezervacije|show\s+reservations)\b"))
            {
                if (Regex.IsMatch(lower, @"(za|for)\s+\d{1,2}([./-])\d{1,2}\.?") || HasRelativeDate(lower, lang))
                    return Task.FromResult(BuildResult(Intent.ShowByDate, t, lower, lang));
                return Task.FromResult(new IntentResult { Intent = Intent.ShowAll, Score = 0.8 });
            }

            if (Regex.IsMatch(lower, @"^(otkaži|otkaz|cancel)\b"))
                return Task.FromResult(BuildResult(Intent.Cancel, t, lower, lang));

            if (Regex.IsMatch(lower, @"^(promijeni|promjena|change)\b"))
                return Task.FromResult(BuildResult(Intent.Change, t, lower, lang));

            if (Regex.IsMatch(lower, @"\b(provjeri\s+termin|check\s+slot|is\s+\d{1,2}[/.-]\d{1,2}\s+at)\b"))
                return Task.FromResult(BuildResult(Intent.CheckSlot, t, lower, lang));



            return Task.FromResult(new IntentResult { Intent = Intent.None, Score = 0.0 });
        }

        // parsiranje
        private static IntentResult BuildResult(Intent intent, string original, string lower, string lang)
        {
            var contact = ExtractContact(original);

            var svc = ExtractService(lower);
            var textNoService = svc == null ? original : RemoveServicePhrase(original, lang);

            var date = ExtractDateText(original, lower, lang);
            var time = ExtractTimeText(original);

            var textNoSvcNoContact = contact == null ? textNoService : textNoService.Replace(contact, "", StringComparison.OrdinalIgnoreCase);
            var name = ExtractName(textNoSvcNoContact, lang);

            var ents = new Entities
            {
                Service = svc,
                Date = date,
                Time = time,
                Name = name,
                Contact = contact
            };

            return new IntentResult
            {
                Intent = intent,
                Entities = ents,
                Score = 0.85
            };
        }

        private static string? ExtractService(string lower)
        {
            return BusinessRules.NormalizeService(lower);
        }

        private static string RemoveServicePhrase(string text, string lang)
        {
            var rx = lang == "en"
                ? new Regex($@"\b(for)\s+{ServiceWordEN}\b", RegexOptions.IgnoreCase)
                : new Regex($@"\b(za)\s+{ServiceWordHR}\b", RegexOptions.IgnoreCase);

            return rx.Replace(text, "");
        }

        private static bool HasRelativeDate(string lower, string lang)
        {
            if (lang == "hr")
                return Regex.IsMatch(lower, @"\b(danas|sutra|preksutra)\b");
            return Regex.IsMatch(lower, @"\b(today|tomorrow|day after tomorrow)\b");
        }

        private static string? ExtractDateText(string original, string lower, string lang)
        {
            // A) HR d.M. / dd.MM. / dd-MM / dd/MM
            var m = Regex.Match(lower, @"\b(?<d>\d{1,2})[.\-\/](?<m>\d{1,2})\.?");
            if (m.Success)
            {
                var d = int.Parse(m.Groups["d"].Value);
                var mm = int.Parse(m.Groups["m"].Value);
                if (d >= 1 && d <= 31 && mm >= 1 && mm <= 12)
                    return $"{d:00}.{mm:00}.";
            }

            // B) EN-US MM/DD u dd.MM.
            var us = Regex.Match(lower, @"\b(?<mm>\d{1,2})\/(?<dd>\d{1,2})\b");
            if (us.Success && lang == "en")
            {
                var mm = int.Parse(us.Groups["mm"].Value);
                var dd = int.Parse(us.Groups["dd"].Value);
                if (dd >= 1 && dd <= 31 && mm >= 1 && mm <= 12)
                    return $"{dd:00}.{mm:00}.";
            }

            var today = DateTime.Today;
            if (lang == "hr")
            {
                if (lower.Contains("danas")) return today.ToString("dd.MM.");
                if (lower.Contains("sutra")) return today.AddDays(1).ToString("dd.MM.");
                if (lower.Contains("preksutra")) return today.AddDays(2).ToString("dd.MM.");
            }
            else
            {
                if (lower.Contains("today")) return today.ToString("dd.MM.");
                if (lower.Contains("tomorrow")) return today.AddDays(1).ToString("dd.MM.");
                if (lower.Contains("day after tomorrow")) return today.AddDays(2).ToString("dd.MM.");
            }

            return null;
        }

        private static string? ExtractTimeText(string original)
        {
            var m1 = Regex.Match(original, @"\b(?<h>[01]?\d|2[0-3]):(?<m>\d{1,2})\b");
            if (m1.Success)
            {
                int h = int.Parse(m1.Groups["h"].Value);
                int mi = int.Parse(m1.Groups["m"].Value);
                if (h >= 0 && h <= 23 && mi >= 0 && mi <= 59)
                    return $"{h:00}:{mi:00}";
            }


            var m2 = Regex.Match(original, @"(?<!\d\.)\b(?<h>[01]?\d|2[0-3])\.(?<m>\d{1,2})\b(?!\.)");
            if (m2.Success)
            {
                int h = int.Parse(m2.Groups["h"].Value);
                int mi = int.Parse(m2.Groups["m"].Value);
                if (h >= 0 && h <= 23 && mi >= 0 && mi <= 59)
                    return $"{h:00}:{mi:00}";
            }

            var m3 = Regex.Match(original, @"\b(?<h>[01]?\d|2[0-3])h(?<m>\d{1,2})\b", RegexOptions.IgnoreCase);
            if (m3.Success)
            {
                int h = int.Parse(m3.Groups["h"].Value);
                int mi = int.Parse(m3.Groups["m"].Value);
                if (h >= 0 && h <= 23 && mi >= 0 && mi <= 59)
                    return $"{h:00}:{mi:00}";
            }
            var m3b = Regex.Match(original, @"\b(?<h>[01]?\d|2[0-3])h\b", RegexOptions.IgnoreCase);
            if (m3b.Success)
            {
                int h = int.Parse(m3b.Groups["h"].Value);
                if (h >= 0 && h <= 23) return $"{h:00}:00";
            }

            var m4 = Regex.Match(original, @"\b(?:at|u)\s*(?<h>[01]?\d|2[0-3])\b", RegexOptions.IgnoreCase);
            if (m4.Success)
            {
                int h = int.Parse(m4.Groups["h"].Value);
                if (h >= 0 && h <= 23) return $"{h:00}:00";
            }

            return null;
        }


        private static string? ExtractName(string text, string lang)
        {
            var s = Regex.Replace(text, @"\s+", " ").Trim();

            if (lang == "hr")
            {
                var rx = new Regex(
                    $@"\b(?:na\s+ime|na|za)\s+(?<name>[A-Za-zÀ-ž' -]{{2,}}?)(?=\s+(?:za\s+{ServiceWordHR})\b|,|$)",
                    RegexOptions.IgnoreCase);
                var m = rx.Match(s);
                if (m.Success)
                    return CleanName(m.Groups["name"].Value);
            }
            else
            {
                var rx = new Regex(
                    $@"\b(?:for|on)\s+(?<name>[A-Za-zÀ-ž' -]{{2,}}?)(?=\s+(?:for\s+{ServiceWordEN})\b|,|$)",
                    RegexOptions.IgnoreCase);
                var m = rx.Match(s);
                if (m.Success)
                    return CleanName(m.Groups["name"].Value);
            }

            return null;
        }

        private static string CleanName(string raw)
        {
            var x = Regex.Replace(raw, @"\s*(for|za)\s+$", "", RegexOptions.IgnoreCase).Trim().Trim(',', '.', ';');

            var parts = x.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Select(w => char.ToUpper(w[0]) + (w.Length > 1 ? w.Substring(1).ToLower() : ""));
            return string.Join(" ", parts);
        }

        private static bool LooksLikeEmail(string s)
        {
            try { var m = new System.Net.Mail.MailAddress(s); return m.Address == s; }
            catch { return false; }
        }

        private static bool LooksLikePhone(string s)
        {
            var digits = new string(s.Where(char.IsDigit).ToArray());
            return digits.Length >= 9 && digits.Length <= 12;
        }

        private static string? ExtractContact(string text)
        {
            var email = Regex.Match(text, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
            if (email.Success) return email.Value.Trim();


            var phoneMatches = Regex.Matches(text, @"\+?[\d\-\s()]{9,20}");
            for (int i = phoneMatches.Count - 1; i >= 0; i--)
            {
                var candidate = phoneMatches[i].Value.Trim();

                if (Regex.IsMatch(candidate, @"\d{1,2}:\d{2}")) continue;

                var digits = new string(candidate.Where(char.IsDigit).ToArray());
                if (digits.Length >= 9 && digits.Length <= 12)
                    return candidate;
            }

            return null;
        }

    }
}
