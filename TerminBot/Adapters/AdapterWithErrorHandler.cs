using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Bot.Builder;

namespace TerminBot.Adapters
{
    public class AdapterWithErrorHandler : BotFrameworkHttpAdapter
    {
        public AdapterWithErrorHandler(ILogger<BotFrameworkHttpAdapter> logger)
            : base()
        {
            OnTurnError = async (turnContext, exception) =>
            {
                logger.LogError($"[OnTurnError] unhandled error : {exception.Message}");

                await turnContext.SendActivityAsync("Došlo je do pogreške u komunikaciji s botom.");
                await turnContext.TraceActivityAsync("OnTurnError Trace", exception.Message, "https://www.botframework.com/schemas/error", "Error");
            };
        }
    }
}
