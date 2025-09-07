using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using TerminBot.Adapters;

namespace TerminBot.Controllers
{
    [ApiController]
    [Route("webchat")]
    public class WebChatController : ControllerBase
    {
        private readonly LocalAdapter _adapter;
        private readonly IBot _bot;

        public WebChatController(LocalAdapter adapter, IBot bot)
        {
            _adapter = adapter;
            _bot = bot;
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] ChatIn dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Text))
                return BadRequest(new { error = "text is required" });

            var conversationId = string.IsNullOrWhiteSpace(dto.ConversationId)
                ? Guid.NewGuid().ToString("N")
                : dto.ConversationId;

            var userId = string.IsNullOrWhiteSpace(dto.UserId)
                ? $"web-{conversationId[..8]}"
                : dto.UserId;

            var activity = new Activity
            {
                Type = ActivityTypes.Message,
                ChannelId = "web-embed",
                From = new ChannelAccount(userId, "web user"),
                Recipient = new ChannelAccount("bot", "bot"),
                Conversation = new ConversationAccount(id: conversationId),
                ServiceUrl = "http://localhost/",
                Text = dto.Text,
                Locale = dto.Locale ?? "hr"
            };

            var outbox = new List<Activity>();
            var turnContext = new TurnContext(_adapter, activity);

            turnContext.OnSendActivities(async (ctx, activities, next) =>
            {
                outbox.AddRange(activities);
                return await next();
            });

            await _bot.OnTurnAsync(turnContext, ct);



            var replies = new List<string>();
            var actions = new List<ActionItem>();

            foreach (var a in outbox)
            {
                if (a.Type != ActivityTypes.Message) continue;

                if (!string.IsNullOrWhiteSpace(a.Text))
                    replies.Add(a.Text);

                if (a.SuggestedActions?.Actions != null)
                {
                    foreach (var act in a.SuggestedActions.Actions)
                    {
                        actions.Add(new ActionItem
                        {
                            Title = act.Title,
                            Value = (string)(act.Value ?? act.Title)
                        });
                    }
                }

                if (a.Attachments != null)
                {
                    foreach (var att in a.Attachments)
                    {
                        if (att?.Content is HeroCard hc && !string.IsNullOrWhiteSpace(hc.Text))
                            replies.Add(hc.Text);
                    }
                }
            }

            return Ok(new ChatOut
            {
                ConversationId = conversationId,
                Replies = replies,
                Actions = actions
            });
            
        }


        private static List<string> Flatten(List<Activity> outgoing)
        {
            var list = new List<string>();
            foreach (var a in outgoing)
            {
                if (a.Type != ActivityTypes.Message) continue;

                if (!string.IsNullOrWhiteSpace(a.Text))
                    list.Add(a.Text);

                if (a.Attachments != null)
                {
                    foreach (var att in a.Attachments)
                    {
                        if (att?.Content is HeroCard hc && !string.IsNullOrWhiteSpace(hc.Text))
                            list.Add(hc.Text);
                    }
                }
            }
            return list;
        }

        public class ChatIn
        {
            public string? ConversationId { get; set; }
            public string? UserId { get; set; }
            public string? Locale { get; set; }
            public string Text { get; set; } = "";
        }

        public class ChatOut
        {
            public string ConversationId { get; set; } = "";
            public List<string> Replies { get; set; } = new();
            public List<ActionItem> Actions { get; set; } = new(); 
        }

        public class ActionItem
        {
            public string Title { get; set; } = "";
            public string Value { get; set; } = "";
        }

    }
}
