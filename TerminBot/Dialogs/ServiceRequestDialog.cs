using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder;
using System.Threading.Tasks;
using System.Threading;
using TerminBot.Models;
using TerminBot.Data;
using System;

namespace TerminBot.Dialogs
{
    public class ServiceRequestDialog : ComponentDialog
    {
        private readonly AppDbContext _db;

        public ServiceRequestDialog(AppDbContext db) : base(nameof(ServiceRequestDialog))
        {
            _db = db;

            var waterfallSteps = new WaterfallStep[]
            {
                AskProblemTypeStepAsync,
                AskAddressStepAsync,
                AskDateTimeStepAsync,
                AskContactStepAsync,
                SaveToDatabaseStepAsync
            };

            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), waterfallSteps));

            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> AskProblemTypeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(nameof(TextPrompt),
                new PromptOptions { Prompt = MessageFactory.Text("Koji problem imate? (npr. IT, električni uređaj, vodoinstalacija...)") },
                cancellationToken);
        }

        private async Task<DialogTurnResult> AskAddressStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            stepContext.Values["ProblemType"] = (string)stepContext.Result;

            return await stepContext.PromptAsync(nameof(TextPrompt),
                new PromptOptions { Prompt = MessageFactory.Text("Molimo unesite adresu gdje se nalazi uređaj.") },
                cancellationToken);
        }

        private async Task<DialogTurnResult> AskDateTimeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            stepContext.Values["Address"] = (string)stepContext.Result;

            return await stepContext.PromptAsync(nameof(TextPrompt),
                new PromptOptions { Prompt = MessageFactory.Text("Kada vam odgovara termin? (npr. 2025-08-10 14:30)") },
                cancellationToken);
        }

        private async Task<DialogTurnResult> AskContactStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            stepContext.Values["RequestedDateTime"] = DateTime.Parse((string)stepContext.Result);

            return await stepContext.PromptAsync(nameof(TextPrompt),
                new PromptOptions { Prompt = MessageFactory.Text("Molimo unesite svoj kontakt broj ili e-mail.") },
                cancellationToken);
        }

        private async Task<DialogTurnResult> SaveToDatabaseStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            stepContext.Values["Contact"] = (string)stepContext.Result;

            var serviceRequest = new ServiceRequest
            {
                ProblemType = (string)stepContext.Values["ProblemType"],
                Address = (string)stepContext.Values["Address"],
                RequestedDateTime = (DateTime)stepContext.Values["RequestedDateTime"],
                Contact = (string)stepContext.Values["Contact"]
            };

            _db.ServiceRequests.Add(serviceRequest);
            await _db.SaveChangesAsync();

            await stepContext.Context.SendActivityAsync("Vaš zahtjev za tehničkom podrškom je uspješno zaprimljen!", cancellationToken: cancellationToken);

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }
    }
}
