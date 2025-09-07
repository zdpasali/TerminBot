using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using System.Collections.Generic;

namespace TerminBot.Bots
{
    public class EchoBot : ActivityHandler
    {
        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var userMessage = turnContext.Activity.Text?.ToLower();

            if (userMessage != null && userMessage.Contains("rezerviraj"))
            {
                await turnContext.SendActivityAsync("Za koji dan želite rezervaciju?", cancellationToken: cancellationToken);
            }
            else
            {
                await turnContext.SendActivityAsync($"Bot kaže: {turnContext.Activity.Text}", cancellationToken: cancellationToken);
            }
        }


        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text("Pozdrav! Ja sam TerminBot. Pitaj me nešto..."), cancellationToken);
                }
            }
        }
    }
}
