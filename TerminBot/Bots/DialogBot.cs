using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TerminBot.Data;
using TerminBot.Dialogs;
using TerminBot.Localization;
using TerminBot.Models;
using TerminBot.NLU;
using TerminBot.State;
using static DateTimeUtils;

namespace TerminBot.Bots
{
    public class DialogBot<T> : ActivityHandler where T : Dialog
    {
        private readonly T _dialog;
        private readonly ConversationState _conversationState;
        private readonly UserState _userState;
        private readonly ILogger<DialogBot<T>> _logger;
        private readonly AppDbContext _db;
        private readonly IConfiguration _config;

        private readonly SimpleRegexRecognizer _regex = new SimpleRegexRecognizer();
        private readonly CluRecognizer _clu;
        private readonly LocalCluRecognizer _local = new LocalCluRecognizer();

        private readonly string _adminPassword;


        //pending potvrde
        private static readonly ConcurrentDictionary<string, (string Kind, string Code, string? DayIso, string? TimeIso)> _pending
            = new();

        public DialogBot(
            ConversationState conversationState,
            UserState userState,
            T dialog,
            ILogger<DialogBot<T>> logger,
            AppDbContext db,
            IConfiguration config)
        {
            _conversationState = conversationState;
            _userState = userState;
            _dialog = dialog;
            _logger = logger;
            _db = db;
            _config = config;
            _clu = new CluRecognizer(config);

            _adminPassword = _config["Admin:Password"] ?? "";
        }


        //helperi
        //admin helper (za stanja)
        private async Task<bool> IsAdminAsync(ITurnContext turnContext, CancellationToken ct)
        {
            //n
            var sessionAcc = _userState.CreateProperty<AdminSessionState>("AdminSession");
            var session = await sessionAcc.GetAsync(turnContext, () => new AdminSessionState(), ct);

            //s
            var acc = _userState.CreateProperty<AdminState>("AdminState");
            var st = await acc.GetAsync(turnContext, () => new AdminState(), ct);

            return (session?.IsAuthenticated ?? false) || (st?.IsAdmin ?? false);
        }

        private async Task SetAdminAsync(ITurnContext turnContext, bool isAdmin, CancellationToken ct)
        {
            //sync admina st
            var acc = _userState.CreateProperty<AdminState>("AdminState");
            var st = await acc.GetAsync(turnContext, () => new AdminState(), ct);
            st.IsAdmin = isAdmin;
            await acc.SetAsync(turnContext, st, ct);

            var sessionAcc = _userState.CreateProperty<AdminSessionState>("AdminSession");
            var session = await sessionAcc.GetAsync(turnContext, () => new AdminSessionState(), ct);
            session.IsAuthenticated = isAdmin;
            if (!isAdmin) session.Username = null;
            await sessionAcc.SetAsync(turnContext, session, ct);
        }


        private static bool TryParseDayIsoLoose(string input, out string? dayIso)
        {
            dayIso = ToIsoDate(input?.Trim(), DateTime.Now.Year);
            return !string.IsNullOrWhiteSpace(dayIso);
        }

        private static (string? fromIso, string? toIso) ParseRange(string input)
        {
            var parts = input.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2) return (null, null);
            var ok1 = TryParseDayIsoLoose(parts[0], out var fromIso);
            var ok2 = TryParseDayIsoLoose(parts[1], out var toIso);
            if (!ok1 || !ok2) return (null, null);
            return (fromIso, toIso);
        }

        //A–Z 0–9
        private static string NormalizeCodeLettersDigits(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var ch in s)
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
            return sb.ToString();
        }

        //yes no potrvde
        private static bool IsYes(string s) => s is "da" or "yes" or "y" or "ok" or "potvrdi" or "confirm";
        private static bool IsNo(string s) => s is "ne" or "no" or "n" or "odustani" or "cancel";



        //UX helperi
        private static string IntroText(string lang) =>
            lang == "en"
            ? "👋 Hi! I can book, list, change or cancel your appointments.\n\n" +
              "Try for example:\n" +
              "• **book 09/17 at 2pm for John, IT, 091234567**\n" +
              "• **show reservations**\n" +
              "• **cancel reservation AB12-CD34**\n" +
              "• **services** (show available service types)"
            : "👋 Bok! Mogu ti rezervirati, prikazati, promijeniti ili otkazati termin.\n\n" +
              "Pokušaj, npr.:\n" +
              "• **rezerviraj 17.09. u 14:00 na Ivan, IT, 091234567**\n" +
              "• **prikaži rezervacije**\n" +
              "• **otkaži rezervaciju AB12-CD34**\n" +
              "• **usluge** (popis dostupnih usluga)";

            private static IMessageActivity BuildSuggestedActions(ITurnContext turnContext, string lang)
            {
                var reply = MessageFactory.Text(
                    lang == "en"
                        ? "Not sure what you meant — pick one:"
                        : "Nisam siguran što želite — odaberite:");

                reply.SuggestedActions = new SuggestedActions
                {
                    Actions = new List<CardAction>
                {
                    new CardAction(ActionTypes.ImBack,
                        title: lang=="en" ? "Book" : "Rezerviraj",
                        value: lang=="en" ? "book" : "rezerviraj"),

                    new CardAction(ActionTypes.ImBack,
                        title: lang=="en" ? "Show reservations" : "Prikaži rezervacije",
                        value: lang=="en" ? "show reservations" : "prikaži rezervacije"),

                    new CardAction(ActionTypes.ImBack,
                        title: lang=="en" ? "Services" : "Usluge",
                        value: lang=="en" ? "services" : "usluge"),

                    new CardAction(ActionTypes.ImBack,
                       title: lang=="en" ? "Help" : "Pomoć",
                       value: lang=="en" ? "help" : "pomoć")
                }
                };

                return reply;
            }



    public override async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default)
        {
            await base.OnTurnAsync(turnContext, cancellationToken);
            await _conversationState.SaveChangesAsync(turnContext, false, cancellationToken);
            await _userState.SaveChangesAsync(turnContext, false, cancellationToken);
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Pokreće se dijalog...");
            var message = turnContext.Activity.Text?.Trim() ?? string.Empty;
            var msgLower = message.ToLowerInvariant();

            // 1) jezik korisnika
            var langAccessor = _userState.CreateProperty<LanguageState>("LanguageState");
            var langState = await langAccessor.GetAsync(turnContext, () => new LanguageState(), cancellationToken);
            var langCode = langState.Lang ?? "hr";

            // ako se ceka potvrda za akciju
            var userKey = turnContext.Activity.From.Id;
            if (_pending.TryGetValue(userKey, out var pending))
            {
                if (IsYes(msgLower))
                {
                    if (pending.Kind == "cancel")
                    {
                        var ids = await _db.Appointments
                            .AsNoTracking()
                            .Select(a => new { a.Id, a.BookingCode, a.DayIso, a.TimeIso })
                            .ToListAsync(cancellationToken);

                        var hit = ids.FirstOrDefault(x =>
                            NormalizeCodeLettersDigits(x.BookingCode) == NormalizeCodeLettersDigits(pending.Code));

                        if (hit == null)
                        {
                            await turnContext.SendActivityAsync(
                                langCode == "en" ? "I couldn't find a reservation for that code."
                                                 : "Nisam pronašao rezervaciju za taj kod.",
                                cancellationToken: cancellationToken);
                        }
                        else
                        {
                            var appt = await _db.Appointments.FindAsync(new object?[] { hit.Id }, cancellationToken);
                            if (appt != null)
                            {
                                _db.Appointments.Remove(appt);
                                await _db.SaveChangesAsync(cancellationToken);
                                await turnContext.SendActivityAsync(
                                    I18n.F(langCode, "Cancelled", PrettyDate(hit.DayIso), hit.TimeIso),
                                    cancellationToken: cancellationToken);
                            }
                        }
                    }
                    else if (pending.Kind == "change")
                    {
                        var ids = await _db.Appointments
                            .AsNoTracking()
                            .Select(a => new { a.Id, a.BookingCode })
                            .ToListAsync(cancellationToken);

                        var hitId = ids.FirstOrDefault(x =>
                            NormalizeCodeLettersDigits(x.BookingCode) == NormalizeCodeLettersDigits(pending.Code))?.Id;

                        if (hitId == null)
                        {
                            await turnContext.SendActivityAsync(
                                langCode == "en" ? "I couldn't find a reservation for that code."
                                                 : "Nisam pronašao rezervaciju za taj kod.",
                                cancellationToken: cancellationToken);
                        }
                        else
                        {
                            var appt = await _db.Appointments.FindAsync(new object?[] { hitId }, cancellationToken);
                            if (appt != null)
                            {
                                appt.DayIso = pending.DayIso!;
                                appt.TimeIso = pending.TimeIso!;
                                appt.Day = PrettyDate(appt.DayIso);
                                appt.Time = appt.TimeIso;
                                await _db.SaveChangesAsync(cancellationToken);

                                await turnContext.SendActivityAsync(
                                    I18n.F(langCode, "ChangedTo", PrettyDate(appt.DayIso), appt.TimeIso),
                                    cancellationToken: cancellationToken);
                            }
                        }
                    }

                    _pending.TryRemove(userKey, out _);
                    return;
                }
                if (IsNo(msgLower))
                {
                    _pending.TryRemove(userKey, out _);
                    await turnContext.SendActivityAsync(
                        langCode == "en" ? "Cancelled." : "Odustao/la si.",
                        cancellationToken: cancellationToken);
                    return;
                }

                await turnContext.SendActivityAsync(
                    langCode == "en"
                        ? "Please confirm: **yes** to proceed or **no** to abort."
                        : "Molim potvrdi: **da** za nastavak ili **ne** za odustajanje.",
                    cancellationToken: cancellationToken);
                return;
            }


            //odustajanje od dijaloga
            if (message == "cancel" || message == "prekini")
            {
                var dialogStateAccessor = _conversationState.CreateProperty<DialogState>("DialogState");
                var dialogState = await dialogStateAccessor.GetAsync(turnContext, () => new DialogState(), cancellationToken);

                dialogState.DialogStack.Clear();
                await turnContext.SendActivityAsync(
                    langCode == "en" ? "Conversation reset." : "Razgovor je prekinut.",
                    cancellationToken: cancellationToken
                );
                return;
            }


            if (msgLower.StartsWith("admin login"))
            {
                var parts = (turnContext.Activity.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    var username = parts[2];
                    var password = string.Join(' ', parts.Skip(3));

                    var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
                    if (user != null && TerminBot.Security.PasswordHasher.Verify(password, user.PasswordHash))
                    {
                        var adminAcc = _userState.CreateProperty<AdminSessionState>("AdminSession");
                        var sess = await adminAcc.GetAsync(turnContext, () => new AdminSessionState(), cancellationToken);
                        sess.IsAuthenticated = true;
                        sess.Username = username;
                        await adminAcc.SetAsync(turnContext, sess, cancellationToken);


                        await SetAdminAsync(turnContext, true, cancellationToken);

                        await turnContext.SendActivityAsync("✅ Admin login successful.", cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await turnContext.SendActivityAsync("⛔ Invalid admin credentials.", cancellationToken: cancellationToken);
                    }
                    return;
                }
                await turnContext.SendActivityAsync("Use: admin login <username> <password>", cancellationToken: cancellationToken);
                return;
            }


            if (msgLower.Equals("admin logout"))
            {
                await SetAdminAsync(turnContext, false, cancellationToken);
                await turnContext.SendActivityAsync("🔒 Admin pristup isključen.", cancellationToken: cancellationToken);
                return;
            }


            // DialogContext
            var dlgState = _conversationState.CreateProperty<DialogState>("DialogState");
            var dialogSet = new DialogSet(dlgState);
            dialogSet.Add(_dialog);
            var dc = await dialogSet.CreateContextAsync(turnContext, cancellationToken);

            if (!dc.Context.TurnState.ContainsKey(typeof(UserState).FullName))
                dc.Context.TurnState.Add(_userState);

            if (dc.ActiveDialog != null)
            {
                await dc.ContinueDialogAsync(cancellationToken);
                return;
            }

            // 2) NLU MODE komanda
            if (msgLower.StartsWith("nlu:"))
            {
                var nluAcc = _userState.CreateProperty<NluModeState>("NluModeState");
                var st = await nluAcc.GetAsync(turnContext, () => new NluModeState(), cancellationToken);

                if (msgLower.Contains("clu")) st.Mode = "clu";
                else if (msgLower.Contains("local")) st.Mode = "local";
                else st.Mode = "regex";

                await nluAcc.SetAsync(turnContext, st, cancellationToken);
                var key = st.Mode == "clu" ? "NluSetClu" : st.Mode == "local" ? "NluSetLocal" : "NluSetRegex";
                await turnContext.SendActivityAsync(I18n.T(langCode, key), cancellationToken: cancellationToken);
                return;
            }

            // 3) LANGUAGE komanda
            if (msgLower.StartsWith("language:"))
            {
                langState.Lang = msgLower.Contains("en") ? "en" : "hr";
                await langAccessor.SetAsync(turnContext, langState, cancellationToken);
                await turnContext.SendActivityAsync(I18n.T(langState.Lang, "LangSet"), cancellationToken: cancellationToken);
                return;
            }

            // 4) izbor recognizera prema modu
            var nluModeAcc = _userState.CreateProperty<NluModeState>("NluModeState");
            var modeState = await nluModeAcc.GetAsync(turnContext, () => new NluModeState { Mode = "local" }, cancellationToken);
            var mode = modeState.Mode;

            if (msgLower == "nlu?" || msgLower == "nlu status")
            {
                var txt = mode switch
                {
                    "clu" => I18n.T(langCode, "NluIsClu"),
                    "local" => I18n.T(langCode, "NluIsLocal"),
                    _ => I18n.T(langCode, "NluIsRegex"),
                };
                await turnContext.SendActivityAsync(txt, cancellationToken: cancellationToken);
                return;
            }

            IIntentRecognizer recognizer = _regex;
            if (mode == "local") recognizer = _local;
            if (mode == "clu" && _clu.IsConfigured) recognizer = _clu;

            // 5) prepoznaj namjeru
            var nlu = await recognizer.RecognizeAsync(message, langCode, cancellationToken);


            // nd
            if (nlu.Intent == Intent.BookAppointment)
            {
                bool missing = string.IsNullOrWhiteSpace(nlu.Entities?.Service)
                            || string.IsNullOrWhiteSpace(nlu.Entities?.Date)
                            || string.IsNullOrWhiteSpace(nlu.Entities?.Time)
                            || string.IsNullOrWhiteSpace(nlu.Entities?.Name)
                            || string.IsNullOrWhiteSpace(nlu.Entities?.Contact);

                if (missing && recognizer != _local)
                {
                    var extra = await _local.RecognizeAsync(message, langCode, cancellationToken);
                    nlu.Entities ??= new Entities();
                    nlu.Entities.Service ??= extra.Entities?.Service;
                    nlu.Entities.Date ??= extra.Entities?.Date;
                    nlu.Entities.Time ??= extra.Entities?.Time;
                    nlu.Entities.Name ??= extra.Entities?.Name;
                    nlu.Entities.Contact ??= extra.Entities?.Contact;
                }
            }
            if (nlu.Intent == Intent.ShowByDate && string.IsNullOrWhiteSpace(nlu.Entities?.Date) && recognizer != _local)
            {
                var extra = await _local.RecognizeAsync(message, langCode, cancellationToken);
                nlu.Entities ??= new Entities();
                nlu.Entities.Date ??= extra.Entities?.Date;
            }

            // 5a) BOOK
            if (nlu.Intent == Intent.BookAppointment)
            {
                var seed = new ReservationState
                {
                    ServiceType = nlu.Entities.Service,
                    Day = nlu.Entities.Date,
                    Time = nlu.Entities.Time,
                    Name = nlu.Entities.Name,
                    Contact = nlu.Entities.Contact
                };

                if (!dc.Context.TurnState.ContainsKey(typeof(ReservationState).FullName))
                    dc.Context.TurnState.Add(seed);

                await dc.BeginDialogAsync(_dialog.Id, seed, cancellationToken);
                return;
            }

            // 5b) SHOW BY DATE
            if (nlu.Intent == Intent.ShowByDate && !string.IsNullOrWhiteSpace(nlu.Entities.Date))
            {
                var iso = ToIsoDate(nlu.Entities.Date, DateTime.Now.Year);
                if (string.IsNullOrEmpty(iso))
                {
                    await turnContext.SendActivityAsync(I18n.T(langCode, "UnknownDate"), cancellationToken: cancellationToken);
                    return;
                }

                var userId = turnContext.Activity.From.Id;
                var list = await _db.Appointments
                    .AsNoTracking()
                    .Where(a => a.UserId == userId && a.DayIso == iso)
                    .OrderBy(a => a.TimeIso)
                    .ToListAsync(cancellationToken);

                if (!list.Any())
                {
                    await turnContext.SendActivityAsync(I18n.F(langCode, "NoReservationsForDay", PrettyDate(iso)), cancellationToken: cancellationToken);
                }
                else
                {
                    foreach (var r in list)
                    {
                        var svcDisp = string.IsNullOrWhiteSpace(r.ServiceType) ? "" : $" ({BusinessRules.LocalizeService(r.ServiceType, langCode)})";
                        await turnContext.SendActivityAsync(
                            $"{PrettyDate(r.DayIso)} {r.TimeIso} – {r.UserName}{svcDisp} | code: {r.BookingCode}",
                            cancellationToken: cancellationToken);
                    }

                }
                return;
            }

            // 5c) SHOW ALL
            if (nlu.Intent == Intent.ShowAll)
            {
                var userId = turnContext.Activity.From.Id;

                var reservations = await _db.Appointments
                    .AsNoTracking()
                    .Where(r => r.UserId == userId)
                    .OrderBy(r => r.DayIso).ThenBy(r => r.TimeIso)
                    .ToListAsync(cancellationToken);

                if (!reservations.Any())
                {
                    await turnContext.SendActivityAsync(I18n.T(langCode, "NoReservations"), cancellationToken: cancellationToken);
                }
                else
                {
                    foreach (var r in reservations)
                    {
                        var svcDisp = string.IsNullOrWhiteSpace(r.ServiceType) ? "" : $" ({BusinessRules.LocalizeService(r.ServiceType, langCode)})";
                        await turnContext.SendActivityAsync(
                            $"{PrettyDate(r.DayIso)} {r.TimeIso} – {r.UserName}{svcDisp} | code: {r.BookingCode}",
                            cancellationToken: cancellationToken);
                    }

                }
                return;
            }

            // 6) HELP
            if (msgLower is "help" or "pomoć" or "menu" or "izbornik")
            {
                await turnContext.SendActivityAsync(I18n.T(langCode, "Help"), cancellationToken: cancellationToken);
                return;
            }

            if (msgLower == "/help")
            {
                await turnContext.SendActivityAsync(MessageFactory.Text(IntroText(langCode)), cancellationToken);
                var actions = BuildSuggestedActions(turnContext, langCode);
                await turnContext.SendActivityAsync(actions, cancellationToken);
                return;
            }


            // LISTA USLUGA
            if (msgLower is "usluge" or "services" or "service list" or "lista usluga")
            {
                var list = string.Join(", ", BusinessRules.AllServices(langCode));
                await turnContext.SendActivityAsync(
                    langCode == "en" ? $"Available services: {list}" : $"Dostupne usluge: {list}",
                    cancellationToken: cancellationToken);
                return;
            }

            // ADMIN HELP
            if (msgLower == "admin help" && await IsAdminAsync(turnContext, cancellationToken))
            {
                await turnContext.SendActivityAsync(
                    "Dostupne admin komande:\n" +
                    "• admin list all\n" +
                    "• admin list day 22.09.\n" +
                    "• admin list range 17.09.-20.09.\n" +
                    "• admin list service vodoinstalacija\n" +
                    "• admin list user <UserId ili ime>\n" +
                    "• admin cancel 22.09. 10:00 <UserId>\n" +
                    "• admin logout",
                    cancellationToken: cancellationToken);
                return;
            }

            // 7) ZADNJA REZERVACIJA
            if (msgLower is "moje zadnje" or "zadnja rezervacija")
            {
                var userId = turnContext.Activity.From.Id;
                var last = await _db.Appointments
                    .AsNoTracking()
                    .Where(a => a.UserId == userId)
                    .OrderByDescending(a => a.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (last is null)
                {
                    await turnContext.SendActivityAsync(I18n.T(langCode, "NoReservations"), cancellationToken: cancellationToken);
                }
                else
                {
                    var svcDisp = string.IsNullOrWhiteSpace(last.ServiceType) ? "" : $" ({BusinessRules.LocalizeService(last.ServiceType, langCode)})";
                    var dateText = !string.IsNullOrEmpty(last.DayIso) ? PrettyDate(last.DayIso) : last.Day;
                    var timeText = !string.IsNullOrEmpty(last.TimeIso) ? last.TimeIso : last.Time;

                    await turnContext.SendActivityAsync(
                        $"{dateText} {timeText} – {last.UserName}{svcDisp} | code: {last.BookingCode}",
                        cancellationToken: cancellationToken);

                }
                return;
            }

            // 8) ADMIN – list all
            if (msgLower == "admin list all" && await IsAdminAsync(turnContext, cancellationToken))
            {
                var adminLang = (await _userState.CreateProperty<LanguageState>("LanguageState")
                    .GetAsync(turnContext, () => new LanguageState(), cancellationToken)).Lang ?? "hr";

                var all = await _db.Appointments
                    .AsNoTracking()
                    .OrderBy(a => a.DayIso).ThenBy(a => a.TimeIso)
                    .ToListAsync(cancellationToken);

                if (!all.Any())
                {
                    await turnContext.SendActivityAsync("Nema rezervacija.", cancellationToken: cancellationToken);
                }
                else
                {
                    foreach (var r in all)
                    {
                        var svcDisp = string.IsNullOrWhiteSpace(r.ServiceType) ? "" : $" ({BusinessRules.LocalizeService(r.ServiceType, adminLang)})";
                        var dateText = !string.IsNullOrEmpty(r.DayIso) ? PrettyDate(r.DayIso) : r.Day;
                        var timeText = !string.IsNullOrEmpty(r.TimeIso) ? r.TimeIso : r.Time;
                        await turnContext.SendActivityAsync(
                            $"{dateText} {timeText} – {r.UserName} [{r.UserId}]{svcDisp} | code: {r.BookingCode}" +
                            (string.IsNullOrWhiteSpace(r.Contact) ? "" : $" | contact: {r.Contact}"),
                            cancellationToken: cancellationToken);
                    }
                }
                return;
            }

            // 8... ADMIN - list day
            if (msgLower.StartsWith("admin list day") && await IsAdminAsync(turnContext, cancellationToken))
            {
                var tail = message.Substring(message.IndexOf("day", StringComparison.InvariantCultureIgnoreCase) + 3).Trim();
                if (!TryParseDayIsoLoose(tail, out var dayIso) || dayIso is null)
                {
                    await turnContext.SendActivityAsync("❌ Neispravan datum. Primjer: admin list day 22.09.", cancellationToken: cancellationToken);
                    return;
                }

                var adminLang = (await _userState.CreateProperty<LanguageState>("LanguageState")
                    .GetAsync(turnContext, () => new LanguageState(), cancellationToken)).Lang ?? "hr";

                var list = await _db.Appointments.AsNoTracking()
                    .Where(a => a.DayIso == dayIso)
                    .OrderBy(a => a.TimeIso)
                    .ToListAsync(cancellationToken);

                if (!list.Any())
                {
                    await turnContext.SendActivityAsync($"Nema rezervacija za {PrettyDate(dayIso)}.", cancellationToken: cancellationToken);
                    return;
                }

                foreach (var r in list)
                {
                    var svcDisp = string.IsNullOrWhiteSpace(r.ServiceType) ? "" : $" ({BusinessRules.LocalizeService(r.ServiceType, adminLang)})";
                    await turnContext.SendActivityAsync(
                        $"{PrettyDate(r.DayIso)} {r.TimeIso} – {r.UserName} [{r.UserId}]{svcDisp} | code: {r.BookingCode}" +
                        (string.IsNullOrWhiteSpace(r.Contact) ? "" : $" | contact: {r.Contact}"),
                        cancellationToken: cancellationToken);
                }
                return;
            }

            // 8.. ADMIN - list range
            if (msgLower.StartsWith("admin list range") && await IsAdminAsync(turnContext, cancellationToken))
            {
                try
                {
                    var startIdx = message.IndexOf("range", StringComparison.InvariantCultureIgnoreCase);
                    var tail = startIdx >= 0 ? message.Substring(startIdx + 5).Trim() : "";

                    var ms = Regex.Matches(tail, @"\b(\d{1,2})[.\-\/](\d{1,2})\.?\b", RegexOptions.CultureInvariant);
                    if (ms.Count < 2)
                    {
                        await turnContext.SendActivityAsync("❌ Neispravan raspon. Primjer: admin list range 17.09.-20.09.", cancellationToken: cancellationToken);
                        return;
                    }

                    var fromText = $"{ms[0].Groups[1].Value}.{ms[0].Groups[2].Value}.";
                    var toText = $"{ms[1].Groups[1].Value}.{ms[1].Groups[2].Value}.";
                    var fromIso = ToIsoDate(fromText, DateTime.Now.Year);
                    var toIso = ToIsoDate(toText, DateTime.Now.Year);

                    if (string.IsNullOrEmpty(fromIso) || string.IsNullOrEmpty(toIso))
                    {
                        await turnContext.SendActivityAsync("❌ Neispravan raspon. Primjer: admin list range 17.09.-20.09.", cancellationToken: cancellationToken);
                        return;
                    }

                    if (string.CompareOrdinal(fromIso, toIso) > 0) (fromIso, toIso) = (toIso, fromIso);

                    var adminLang = (await _userState.CreateProperty<LanguageState>("LanguageState")
                        .GetAsync(turnContext, () => new LanguageState(), cancellationToken)).Lang ?? "hr";

                    var all = await _db.Appointments.AsNoTracking()
                        .Where(a => a.DayIso != null)
                        .ToListAsync(cancellationToken);

                    var list = all
                        .Where(a => string.CompareOrdinal(a.DayIso, fromIso) >= 0
                                 && string.CompareOrdinal(a.DayIso, toIso) <= 0)
                        .OrderBy(a => a.DayIso).ThenBy(a => a.TimeIso)
                        .ToList();

                    if (!list.Any())
                    {
                        await turnContext.SendActivityAsync($"Nema rezervacija u rasponu {PrettyDate(fromIso)}–{PrettyDate(toIso)}.", cancellationToken: cancellationToken);
                        return;
                    }

                    foreach (var r in list)
                    {
                        var svcDisp = string.IsNullOrWhiteSpace(r.ServiceType) ? "" : $" ({BusinessRules.LocalizeService(r.ServiceType, adminLang)})";
                        await turnContext.SendActivityAsync(
                            $"{PrettyDate(r.DayIso)} {r.TimeIso} – {r.UserName} [{r.UserId}]{svcDisp} | code: {r.BookingCode}" +
                            (string.IsNullOrWhiteSpace(r.Contact) ? "" : $" | contact: {r.Contact}"),
                            cancellationToken: cancellationToken);
                    }
                    return;
                }
                catch
                {
                    await turnContext.SendActivityAsync("❌ Nešto je pošlo po zlu pri čitanju raspona. Pokušaj: admin list range 17.09.-20.09.", cancellationToken: cancellationToken);
                    return;
                }
            }

            // 8... ADMIN - list service
            if (msgLower.StartsWith("admin list service") && await IsAdminAsync(turnContext, cancellationToken))
            {
                var startIdx = message.IndexOf("service", StringComparison.InvariantCultureIgnoreCase);
                var tail = startIdx >= 0 ? message.Substring(startIdx + 7).Trim() : "";
                var key = BusinessRules.NormalizeService(tail);
                if (key is null)
                {
                    await turnContext.SendActivityAsync("❌ Nepoznata usluga. Primjer: admin list service vodoinstalacija", cancellationToken: cancellationToken);
                    return;
                }

                var adminLang = (await _userState.CreateProperty<LanguageState>("LanguageState")
                    .GetAsync(turnContext, () => new LanguageState(), cancellationToken)).Lang ?? "hr";

                var all = await _db.Appointments.AsNoTracking()
                    .Where(a => a.ServiceType != null)
                    .ToListAsync(cancellationToken);

                var list = all
                    .Where(a => BusinessRules.NormalizeService(a.ServiceType) == key)
                    .OrderBy(a => a.DayIso).ThenBy(a => a.TimeIso)
                    .ToList();

                if (!list.Any())
                {
                    await turnContext.SendActivityAsync("Nema rezervacija za tu uslugu.", cancellationToken: cancellationToken);
                    return;
                }

                foreach (var r in list)
                {
                    var svcDisp = BusinessRules.LocalizeService(r.ServiceType, adminLang);
                    await turnContext.SendActivityAsync(
                        $"{PrettyDate(r.DayIso)} {r.TimeIso} – {r.UserName} [{r.UserId}] ({svcDisp}) | code: {r.BookingCode}" +
                        (string.IsNullOrWhiteSpace(r.Contact) ? "" : $" | contact: {r.Contact}"),
                        cancellationToken: cancellationToken);
                }
                return;
            }

            // 8... ADMIN - list user
            if (msgLower.StartsWith("admin list user") && await IsAdminAsync(turnContext, cancellationToken))
            {
                var query = message.Substring(message.IndexOf("user", StringComparison.InvariantCultureIgnoreCase) + 4).Trim();
                if (string.IsNullOrWhiteSpace(query))
                {
                    await turnContext.SendActivityAsync("Primjer: admin list user 12345 ili admin list user Ivan", cancellationToken: cancellationToken);
                    return;
                }

                var adminLang = (await _userState.CreateProperty<LanguageState>("LanguageState")
                    .GetAsync(turnContext, () => new LanguageState(), cancellationToken)).Lang ?? "hr";

                var qLower = query.ToLowerInvariant();

                var list = await _db.Appointments.AsNoTracking()
                    .Where(a => (!string.IsNullOrEmpty(a.UserId) && a.UserId.ToLower().Contains(qLower))
                             || (!string.IsNullOrEmpty(a.UserName) && a.UserName.ToLower().Contains(qLower)))
                    .OrderBy(a => a.DayIso).ThenBy(a => a.TimeIso)
                    .ToListAsync(cancellationToken);

                if (!list.Any())
                {
                    await turnContext.SendActivityAsync("Nema rezultata.", cancellationToken: cancellationToken);
                    return;
                }

                foreach (var r in list)
                {
                    var svcDisp = string.IsNullOrWhiteSpace(r.ServiceType) ? "" : $" ({BusinessRules.LocalizeService(r.ServiceType, adminLang)})";
                    await turnContext.SendActivityAsync(
                        $"{PrettyDate(r.DayIso)} {r.TimeIso} – {r.UserName} [{r.UserId}]{svcDisp} | code: {r.BookingCode}" +
                        (string.IsNullOrWhiteSpace(r.Contact) ? "" : $" | contact: {r.Contact}"),
                        cancellationToken: cancellationToken);
                }
                return;
            }

            // 8... ADMIN - cancel (po datumu/vremenu i useru)
            if (msgLower.StartsWith("admin cancel") && await IsAdminAsync(turnContext, cancellationToken))
            {
                var startIdx = message.IndexOf("cancel", StringComparison.InvariantCultureIgnoreCase);
                var tail = startIdx >= 0 ? message.Substring(startIdx + 6).Trim() : "";
                var parts = tail.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                {
                    await turnContext.SendActivityAsync("Primjer: admin cancel 22.09. 10:00 USER_ID ili admin cancel 22.09. 10:00 Ime", cancellationToken: cancellationToken);
                    return;
                }

                var dayIso = ToIsoDate(parts[0], DateTime.Now.Year);
                var timeIso = ToIsoTime(parts[1]);
                var idOrName = parts[2].Trim();

                if (string.IsNullOrEmpty(dayIso))
                {
                    await turnContext.SendActivityAsync("❌ Neispravan datum.", cancellationToken: cancellationToken);
                    return;
                }
                if (string.IsNullOrEmpty(timeIso))
                {
                    await turnContext.SendActivityAsync("❌ Neispravno vrijeme.", cancellationToken: cancellationToken);
                    return;
                }

                var appt = await _db.Appointments.FirstOrDefaultAsync(a =>
                    a.UserId == idOrName && a.DayIso == dayIso && a.TimeIso == timeIso, cancellationToken);

                if (appt == null)
                {
                    var candidates = await _db.Appointments
                        .Where(a => a.DayIso == dayIso && a.TimeIso == timeIso && a.UserName != null)
                        .ToListAsync(cancellationToken);

                    var matches = candidates
                        .Where(a => string.Equals(a.UserName?.Trim(), idOrName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (matches.Count == 1) appt = matches[0];
                    else if (matches.Count > 1)
                    {
                        await turnContext.SendActivityAsync("⚠️ Više rezervacija na to ime za isti termin. Koristi UserId.", cancellationToken: cancellationToken);
                        return;
                    }
                }

                if (appt == null)
                {
                    await turnContext.SendActivityAsync("Nisam pronašao rezervaciju za taj termin i korisnika.", cancellationToken: cancellationToken);
                    return;
                }

                _db.Appointments.Remove(appt);
                await _db.SaveChangesAsync(cancellationToken);
                await turnContext.SendActivityAsync($"Otkazano: {PrettyDate(dayIso)} {timeIso} – {appt.UserName} [{appt.UserId}].", cancellationToken: cancellationToken);
                return;
            }

            //  USER - cancel by CODE
            if (Regex.IsMatch(msgLower,
                @"^(otkaži\s+(rezervaciju|booking)|cancel\s+(reservation|booking))\s+([a-z0-9\-–—\u2011]{6,20})$",
                RegexOptions.IgnoreCase))
            {
                var parts = (turnContext.Activity.Text ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var codeRaw = parts.Length > 0 ? parts[^1] : null;
                var codeKey = NormalizeCodeLettersDigits(codeRaw);

                if (string.IsNullOrEmpty(codeKey))
                {
                    await turnContext.SendActivityAsync(
                        langCode == "en" ? "Please provide a booking code, e.g. **cancel reservation ABCD-1234**."
                                         : "Molim upišite kod rezervacije, npr. **otkaži rezervaciju ABCD-1234**.",
                        cancellationToken: cancellationToken);
                    return;
                }

                var all = await _db.Appointments
                    .AsNoTracking()
                    .Select(a => new { a.BookingCode, a.DayIso, a.TimeIso })
                    .ToListAsync(cancellationToken);
                var hit = all.FirstOrDefault(x => NormalizeCodeLettersDigits(x.BookingCode) == codeKey);
                if (hit == null)
                {
                    await turnContext.SendActivityAsync(
                        langCode == "en" ? "I couldn't find a reservation for that code."
                                         : "Nisam pronašao rezervaciju za taj kod.",
                        cancellationToken: cancellationToken);
                    return;
                }

                _pending[userKey] = ("cancel", codeRaw!, null, null);
                await turnContext.SendActivityAsync(
                    langCode == "en"
                        ? $"Are you sure you want to cancel **{PrettyDate(hit.DayIso)} at {hit.TimeIso}**? Reply **yes** to confirm or **no** to abort."
                        : $"Jesi li siguran/na da želiš otkazati **{PrettyDate(hit.DayIso)} u {hit.TimeIso}**? Odgovori **da** za potvrdu ili **ne** za odustajanje.",
                    cancellationToken: cancellationToken);
                return;
            }

            // USER - change by CODE
            if (Regex.IsMatch(msgLower,
                @"^(promijeni\s+(rezervaciju|booking)|change\s+(reservation|booking))\s+[a-z0-9\-–—\u2011]{6,20}\s+",
                RegexOptions.IgnoreCase))
            {
                var m = Regex.Match(
                    message,
                    @"(?:promijeni\s+(?:rezervaciju|booking)|change\s+(?:reservation|booking))\s+([A-Za-z0-9\-–—\u2011]{6,20})\s+(?:u|to)\s+(.+)$",
                    RegexOptions.IgnoreCase);

                if (!m.Success)
                {
                    await turnContext.SendActivityAsync(
                        langCode == "en"
                            ? "Try: **change reservation ABCD-1234 to 17.09. at 15:00**"
                            : "Pokušaj: **promijeni rezervaciju ABCD-1234 u 17.09. u 15:00**",
                        cancellationToken: cancellationToken);
                    return;
                }

                var codeRaw = m.Groups[1].Value.Trim();
                var tail = m.Groups[2].Value.Trim();

                // datum
                string? dayText = null;
                var dateMatch = Regex.Match(tail, @"\b(\d{1,2}[./-]\d{1,2}\.?)\b");
                if (dateMatch.Success) dayText = dateMatch.Groups[1].Value;
                var newDayIso = ToIsoDate(dayText, DateTime.Now.Year);

                // vrijeme
                string? timeText = null;
                var normTail = tail.Replace('-', ':').Replace('h', ':').Replace('H', ':');
                var timeMatches = Regex.Matches(normTail, @"\b(\d{1,2}:\d{1,2})\b");
                if (timeMatches.Count > 0) timeText = timeMatches[timeMatches.Count - 1].Groups[1].Value;
                var newTimeIso = ToIsoTime(timeText);

                if (string.IsNullOrEmpty(newDayIso) || string.IsNullOrEmpty(newTimeIso))
                {
                    await turnContext.SendActivityAsync(
                        langCode == "en"
                            ? "Couldn't parse the new date/time. Example: **change reservation ABCD-1234 to 17.09. at 15:00**"
                            : "Ne mogu pročitati novi datum/vrijeme. Primjer: **promijeni rezervaciju ABCD-1234 u 17.09. u 15:00**",
                        cancellationToken: cancellationToken);
                    return;
                }

                _pending[userKey] = ("change", codeRaw, newDayIso, newTimeIso);
                await turnContext.SendActivityAsync(
                    langCode == "en"
                        ? $"Change **{codeRaw}** to **{PrettyDate(newDayIso)} at {newTimeIso}**? Reply **yes** to confirm or **no** to abort."
                        : $"Promijeniti **{codeRaw}** na **{PrettyDate(newDayIso)} u {newTimeIso}**? Odgovori **da** za potvrdu ili **ne** za odustajanje.",
                    cancellationToken: cancellationToken);
                return;
            }

            // 9) PREGLED SVIH
            if (msgLower.Contains("prikaži rezervacije"))
            {
                var userId = turnContext.Activity.From.Id;
                var reservations = await _db.Appointments
                    .AsNoTracking()
                    .Where(r => r.UserId == userId)
                    .OrderBy(r => r.DayIso).ThenBy(r => r.TimeIso)
                    .ToListAsync(cancellationToken);

                if (!reservations.Any())
                {
                    await turnContext.SendActivityAsync(I18n.T(langCode, "NoReservations"), cancellationToken: cancellationToken);
                }
                else
                {
                    foreach (var r in reservations)
                    {
                        var svcDisp = string.IsNullOrWhiteSpace(r.ServiceType) ? "" : $" ({BusinessRules.LocalizeService(r.ServiceType, langCode)})";
                        await turnContext.SendActivityAsync(
                            $"🗓 {PrettyDate(r.DayIso)} {r.TimeIso} – {r.UserName}{svcDisp} | code: {r.BookingCode}",
                            cancellationToken: cancellationToken);

                    }
                }
                return;
            }

            // CHECK SLOT
            if (nlu.Intent == Intent.CheckSlot &&
                !string.IsNullOrWhiteSpace(nlu.Entities?.Date) &&
                !string.IsNullOrWhiteSpace(nlu.Entities?.Time))
            {
                await HandleCheckSlotAsync(turnContext, nlu.Entities.Date, nlu.Entities.Time, langCode, cancellationToken);
                return;
            }


            // 11) PROMJENA TERMINA (po datumu i vremenu)
            if (msgLower.StartsWith("promijeni rezervaciju za"))
            {
                var parts = msgLower.Split("za", StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && parts[1].Contains("u"))
                {
                    var split = parts[1].Split("u", StringSplitOptions.RemoveEmptyEntries);
                    if (split.Length >= 3)
                    {
                        var oldDayRaw = split[0].Trim();
                        var oldTimeRaw = split[1].Trim();
                        var newDayRaw = split[2].Trim();
                        string newTimeRaw = split.Length >= 4 ? split[3].Trim() : null;

                        var oldDayIso = ToIsoDate(oldDayRaw, DateTime.Now.Year);
                        var oldTimeIso = ToIsoTime(oldTimeRaw);
                        var newDayIso = ToIsoDate(newDayRaw, DateTime.Now.Year);
                        var newTimeIso = newTimeRaw != null ? ToIsoTime(newTimeRaw) : null;

                        var userId = turnContext.Activity.From.Id;

                        var appointment = await _db.Appointments
                            .FirstOrDefaultAsync(a => a.UserId == userId && a.DayIso == oldDayIso && a.TimeIso == oldTimeIso, cancellationToken);

                        if (appointment != null)
                        {
                            appointment.DayIso = newDayIso;
                            if (newTimeIso != null) appointment.TimeIso = newTimeIso;
                            appointment.Day = PrettyDate(appointment.DayIso);
                            appointment.Time = appointment.TimeIso;

                            await _db.SaveChangesAsync(cancellationToken);

                            await turnContext.SendActivityAsync(
                                I18n.F(langCode, "ChangedTo", PrettyDate(appointment.DayIso), appointment.TimeIso),
                                cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await turnContext.SendActivityAsync(I18n.T(langCode, "NoReservations"), cancellationToken: cancellationToken);
                        }
                    }
                }
                return;
            }

            // 12) OTKAZIVANJE (po datumu i vremenu)
            if (msgLower.StartsWith("otkaži rezervaciju za"))
            {
                var parts = message.Split("za", StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1)
                {
                    var details = parts[1].Trim();
                    var segments = details.Split("u", StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length == 2)
                    {
                        var dayIso = ToIsoDate(segments[0].Trim(), DateTime.Now.Year);
                        var timeIso = ToIsoTime(segments[1].Trim());
                        var userId = turnContext.Activity.From.Id;

                        var appointment = await _db.Appointments.FirstOrDefaultAsync(a =>
                            a.UserId == userId && a.DayIso == dayIso && a.TimeIso == timeIso, cancellationToken);

                        if (appointment != null)
                        {
                            _db.Appointments.Remove(appointment);
                            await _db.SaveChangesAsync(cancellationToken);
                            await turnContext.SendActivityAsync(I18n.F(langCode, "Cancelled", PrettyDate(dayIso), timeIso), cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await turnContext.SendActivityAsync(I18n.T(langCode, "NoReservations"), cancellationToken: cancellationToken);
                        }
                    }
                }
                return;
            }

            // 13) pokretanje rezervacije
            if (msgLower.StartsWith("rezerviraj") || msgLower.StartsWith("book"))
            {
                await dc.BeginDialogAsync(_dialog.Id, null, cancellationToken);
                return;
            }

            // 14) default poruka
            var reply = BuildSuggestedActions(turnContext, langCode);
            await turnContext.SendActivityAsync(reply, cancellationToken);

        }

        private async Task HandleCheckSlotAsync(ITurnContext<IMessageActivity> turnContext, string dateText, string timeText, string langCode, CancellationToken cancellationToken)
        {
            var isoDate = DateTimeUtils.ToIsoDate(dateText, DateTime.Now.Year);
            var isoTime = DateTimeUtils.ToIsoTime(timeText);

            if (isoDate == null || isoTime == null)
            {
                await turnContext.SendActivityAsync(
                    langCode == "en"
                        ? "I couldn't understand the date or time."
                        : "Nisam prepoznao datum ili vrijeme.",
                    cancellationToken: cancellationToken);
                return;
            }

            // broj rezervacija u slotu
            const int MAX_PER_SLOT = 2;
            var taken = await _db.Appointments
                .CountAsync(a => a.DayIso == isoDate && a.TimeIso == isoTime, cancellationToken);

            if (taken >= MAX_PER_SLOT)
            {
                await turnContext.SendActivityAsync(
                    langCode == "en"
                        ? $"Slot {DateTimeUtils.PrettyDate(isoDate)} at {isoTime} is FULL ({taken}/{MAX_PER_SLOT})."
                        : $"Termin {DateTimeUtils.PrettyDate(isoDate)} u {isoTime} je POPUNJEN ({taken}/{MAX_PER_SLOT}).",
                    cancellationToken: cancellationToken);
            }
            else
            {
                await turnContext.SendActivityAsync(
                    langCode == "en"
                        ? $"Slot {DateTimeUtils.PrettyDate(isoDate)} at {isoTime} is FREE ({taken}/{MAX_PER_SLOT})."
                        : $"Termin {DateTimeUtils.PrettyDate(isoDate)} u {isoTime} je SLOBODAN ({taken}/{MAX_PER_SLOT}).",
                    cancellationToken: cancellationToken);
            }
        }


        protected override async Task OnMembersAddedAsync(
            System.Collections.Generic.IList<ChannelAccount> membersAdded,
            ITurnContext<IConversationUpdateActivity> turnContext,
            CancellationToken cancellationToken)
        {
            var langAccessor = _userState.CreateProperty<LanguageState>("LanguageState");
            var langState = await langAccessor.GetAsync(turnContext, () => new LanguageState(), cancellationToken);
            var langCode = langState.Lang ?? "hr";

            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(IntroText(langCode)), cancellationToken);
                }
            }
        }
    }
}
