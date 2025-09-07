using Microsoft.Bot.Builder.Dialogs;
using System.Threading;
using System.Threading.Tasks;

namespace TerminBot.Dialogs
{
    public class MainDialog : ComponentDialog
    {
        public MainDialog(ReservationDialog reservationDialog)
            : base(nameof(MainDialog))
        {
            AddDialog(reservationDialog);
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
            {
                StartReservationStepAsync
            }));

            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> StartReservationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.BeginDialogAsync(nameof(ReservationDialog), null, cancellationToken);
        }
    }
}
