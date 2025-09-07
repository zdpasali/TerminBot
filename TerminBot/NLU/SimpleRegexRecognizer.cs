using System.Text.RegularExpressions;

namespace TerminBot.NLU
{
    public class SimpleRegexRecognizer : IIntentRecognizer
    {
        public Task<IntentResult> RecognizeAsync(string text, string lang, CancellationToken ct)
        {
            var t = (text ?? "").Trim();

            // BOOK
            if (Regex.IsMatch(t, @"\b(rezerviraj|rezervacija|book|booking)\b", RegexOptions.IgnoreCase))
                return Task.FromResult(new IntentResult { Intent = Intent.BookAppointment, Score = 0.8 });

            // SHOW ALL
            if (Regex.IsMatch(t, @"^(prikaži\s+rezervacije|show\s+reservations)\b", RegexOptions.IgnoreCase))
                return Task.FromResult(new IntentResult { Intent = Intent.ShowAll, Score = 0.7 });

            // SHOW BY DATE
            if (Regex.IsMatch(t, @"(prikaži\s+rezervacije\s+za|show\s+reservations\s+for)", RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(t, @"\b(?<date>\d{1,2}([./-])\d{1,2}\.?)");
                return Task.FromResult(new IntentResult
                {
                    Intent = Intent.ShowByDate,
                    Score = 0.7,
                    Entities = new Entities { Date = m.Success ? m.Groups["date"].Value : null }
                });
            }

            // CANCEL
            if (Regex.IsMatch(t, @"^(otkaži|otkaz|cancel)\b", RegexOptions.IgnoreCase))
                return Task.FromResult(new IntentResult { Intent = Intent.Cancel, Score = 0.6 });

            // CHANGE
            if (Regex.IsMatch(t, @"^(promijeni|promjena|change)\b", RegexOptions.IgnoreCase))
                return Task.FromResult(new IntentResult { Intent = Intent.Change, Score = 0.6 });

            return Task.FromResult(new IntentResult { Intent = Intent.None, Score = 0.0 });
        }
    }
}
