namespace TerminBot.NLU
{
    public enum Intent
    {
        None,
        BookAppointment,
        ShowAll,
        ShowByDate,
        Cancel,
        Change,
        CheckSlot
    }

    public class Entities
    {
        public string Service { get; set; }
        public string Date { get; set; }
        public string Time { get; set; }
        public string Name { get; set; }
        public string Contact { get; set; }
    }

    public class IntentResult
    {
        public Intent Intent { get; set; }
        public Entities Entities { get; set; } = new();
        public double Score { get; set; }
    }

    public interface IIntentRecognizer
    {
        Task<IntentResult> RecognizeAsync(string text, string lang, CancellationToken ct);
    }
}
