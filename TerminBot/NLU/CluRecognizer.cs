using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace TerminBot.NLU
{
    public class CluRecognizer : IIntentRecognizer
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _http = new HttpClient();

        public bool IsConfigured { get; }

        public CluRecognizer(IConfiguration config)
        {
            _config = config;
            var ep = _config["CLU:endpoint"];
            var key = _config["CLU:key"];
            IsConfigured = !string.IsNullOrWhiteSpace(ep) && !string.IsNullOrWhiteSpace(key);
        }

        public async Task<IntentResult> RecognizeAsync(string text, string lang, CancellationToken ct)
        {
            if (!IsConfigured || string.IsNullOrWhiteSpace(text))
                return new IntentResult { Intent = Intent.None, Score = 0.0 };

            var endpoint = _config["CLU:endpoint"]?.TrimEnd('/');
            var key = _config["CLU:key"];

            var project = _config[$"CLU:{lang}:project"] ?? _config["CLU:project"];
            var deployment = _config[$"CLU:{lang}:deployment"] ?? _config["CLU:deployment"];
            var language = _config[$"CLU:{lang}:language"] ?? lang;

            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(deployment))
                return new IntentResult { Intent = Intent.None, Score = 0.0 };

            var url = $"{endpoint}/language/:analyze-conversations?api-version=2023-04-01";

            var payload = new
            {
                kind = "Conversation",
                analysisInput = new
                {
                    conversationItem = new
                    {
                        text = text,
                        id = "1",
                        modality = "text",
                        language = language,
                        participantId = "user"
                    }
                },
                parameters = new
                {
                    projectName = project,
                    deploymentName = deployment,
                    stringIndexType = "TextElement_V8"
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("Ocp-Apim-Subscription-Key", key);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                using var res = await _http.SendAsync(req, ct);
                var json = await res.Content.ReadAsStringAsync(ct);
                if (!res.IsSuccessStatusCode) return new IntentResult { Intent = Intent.None, Score = 0.0 };

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;


                var topIntent = root.GetProperty("result").GetProperty("prediction").GetProperty("topIntent").GetString() ?? "";


                Intent mapped = topIntent.ToLowerInvariant() switch
                {
                    "book" or "bookappointment" or "rezerviraj" => Intent.BookAppointment,
                    "showall" or "showreservations" => Intent.ShowAll,
                    "showbydate" => Intent.ShowByDate,
                    "cancel" or "cancelreservation" => Intent.Cancel,
                    "change" or "changereservation" => Intent.Change,
                    _ => Intent.None
                };


                var ents = new Entities();
                if (root.GetProperty("result").GetProperty("prediction").TryGetProperty("entities", out var entities))
                {
                    foreach (var e in entities.EnumerateArray())
                    {
                        var category = e.GetProperty("category").GetString()?.ToLowerInvariant();
                        var textVal = e.GetProperty("text").GetString();

                        switch (category)
                        {
                            case "date": ents.Date = textVal; break;
                            case "time": ents.Time = textVal; break;
                            case "service": ents.Service = textVal; break;
                            case "name": ents.Name = textVal; break;
                        }
                    }
                }

                double score = 0.0;
                if (root.GetProperty("result").GetProperty("prediction").TryGetProperty("intents", out var intentsArr))
                {
                    foreach (var i in intentsArr.EnumerateArray())
                    {
                        var name = i.GetProperty("category").GetString();
                        if (string.Equals(name, topIntent, StringComparison.OrdinalIgnoreCase))
                        {
                            score = i.GetProperty("confidenceScore").GetDouble();
                            break;
                        }
                    }
                }

                return new IntentResult { Intent = mapped, Entities = ents, Score = score };
            }
            catch
            {
                return new IntentResult { Intent = Intent.None, Score = 0.0 };
            }
        }
    }
}
