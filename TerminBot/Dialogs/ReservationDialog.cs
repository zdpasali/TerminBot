using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.EntityFrameworkCore;
using TerminBot.Data;
using TerminBot.Localization;
using TerminBot.Models;
using TerminBot.State;
using static DateTimeUtils;

namespace TerminBot.Dialogs
{
    public class ReservationDialog : ComponentDialog
    {
        private readonly AppDbContext _db;
        private const int MAX_PER_SLOT = 2; //po terminu

        public ReservationDialog(AppDbContext db) : base(nameof(ReservationDialog))
        {
            _db = db;

            var steps = new WaterfallStep[]
            {
                AskForServiceTypeStepAsync,
                AskForDayStepAsync,
                AskForTimeStepAsync,
                AskForNameStepAsync,
                AskForContactStepAsync,
                ConfirmSummaryStepAsync,
                SaveReservationStepAsync
            };

            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), steps));

            AddDialog(new TextPrompt("ServiceTypePrompt"));
            AddDialog(new TextPrompt("DatePrompt", DateValidationAsync));
            AddDialog(new TextPrompt("TimePrompt", TimeValidationAsync));
            AddDialog(new TextPrompt("NamePrompt"));
            AddDialog(new TextPrompt("ContactPrompt", ContactValidationAsync));
            AddDialog(new TextPrompt("ConfirmPrompt", ConfirmValidatorAsync));

            InitialDialogId = nameof(WaterfallDialog);
        }

        // helperi
        private async Task<string> GetLang(ITurnContext ctx, CancellationToken ct)
        {
            var userState = ctx.TurnState.Get<UserState>();
            if (userState != null)
            {
                var accessor = userState.CreateProperty<LanguageState>("LanguageState");
                var st = await accessor.GetAsync(ctx, () => new LanguageState(), ct);
                return string.IsNullOrWhiteSpace(st.Lang) ? "hr" : st.Lang;
            }
            return "hr";
        }

        private static bool IsYes(string s)
        {
            var t = (s ?? "").Trim().ToLowerInvariant();
            return t is "da" or "yes" or "y" or "ok" or "potvrdi" or "confirm";
        }
        private static bool IsNo(string s)
        {
            var t = (s ?? "").Trim().ToLowerInvariant();
            return t is "ne" or "no" or "n" or "odustani" or "cancel";
        }

        private async Task<DialogTurnResult> FailAsync(WaterfallStepContext stepContext, Exception ex, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync($"[ERR] {ex.GetType().Name}: {ex.Message}", cancellationToken: cancellationToken);
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> AskForServiceTypeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            try
            {
                var reservation = stepContext.Values.ContainsKey("reservation")
                    ? (ReservationState)stepContext.Values["reservation"]
                    : new ReservationState();

                if (stepContext.Options is ReservationState seeded1)
                {
                    if (!string.IsNullOrWhiteSpace(seeded1.ServiceType)) reservation.ServiceType = seeded1.ServiceType;
                    if (!string.IsNullOrWhiteSpace(seeded1.Day)) reservation.Day = seeded1.Day;
                    if (!string.IsNullOrWhiteSpace(seeded1.Time)) reservation.Time = seeded1.Time;
                    if (!string.IsNullOrWhiteSpace(seeded1.Name)) reservation.Name = seeded1.Name;
                    if (!string.IsNullOrWhiteSpace(seeded1.Contact)) reservation.Contact = seeded1.Contact;
                }

                var tsSeed = stepContext.Context.TurnState.Get<ReservationState>();
                if (tsSeed != null)
                {
                    if (string.IsNullOrWhiteSpace(reservation.ServiceType) && !string.IsNullOrWhiteSpace(tsSeed.ServiceType)) reservation.ServiceType = tsSeed.ServiceType;
                    if (string.IsNullOrWhiteSpace(reservation.Day) && !string.IsNullOrWhiteSpace(tsSeed.Day)) reservation.Day = tsSeed.Day;
                    if (string.IsNullOrWhiteSpace(reservation.Time) && !string.IsNullOrWhiteSpace(tsSeed.Time)) reservation.Time = tsSeed.Time;
                    if (string.IsNullOrWhiteSpace(reservation.Name) && !string.IsNullOrWhiteSpace(tsSeed.Name)) reservation.Name = tsSeed.Name;
                    if (string.IsNullOrWhiteSpace(reservation.Contact) && !string.IsNullOrWhiteSpace(tsSeed.Contact)) reservation.Contact = tsSeed.Contact;
                }

                stepContext.Values["reservation"] = reservation;

                var lang = await GetLang(stepContext.Context, cancellationToken);

                if (!string.IsNullOrWhiteSpace(reservation.ServiceType))
                {
                    var key = BusinessRules.NormalizeService(reservation.ServiceType);
                    if (key != null)
                    {
                        reservation.ServiceType = key;
                        return await stepContext.NextAsync(null, cancellationToken);
                    }
                    reservation.ServiceType = null;
                }

                var list = string.Join(", ", BusinessRules.AllServices(lang));
                var prompt = lang == "en"
                    ? $"What type of issue do you have? ({list})"
                    : $"Koji tip problema imate? ({list})";

                return await stepContext.PromptAsync(
                    "ServiceTypePrompt",
                    new PromptOptions { Prompt = MessageFactory.Text(prompt) },
                    cancellationToken);
            }
            catch (Exception ex) { return await FailAsync(stepContext, ex, cancellationToken); }
        }

        private async Task<DialogTurnResult> AskForDayStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            try
            {
                var reservation = (ReservationState)stepContext.Values["reservation"];

                if (string.IsNullOrWhiteSpace(reservation.ServiceType) && stepContext.Result is string s)
                {
                    var key = BusinessRules.NormalizeService(s);
                    if (key == null)
                    {
                        var lang = await GetLang(stepContext.Context, cancellationToken);
                        var list = string.Join(", ", BusinessRules.AllServices(lang));
                        await stepContext.Context.SendActivityAsync(
                            lang == "en"
                                ? $"I didn't recognize the service. Please choose service from down below"
                                : $"Nisam prepoznao uslugu. Odaberi jednu od dolje ponudenih",
                            cancellationToken: cancellationToken);

                        return await stepContext.ReplaceDialogAsync(this.Id, reservation, cancellationToken);
                    }
                    reservation.ServiceType = key;
                }

                if (!string.IsNullOrWhiteSpace(reservation.Day))
                    return await stepContext.NextAsync(null, cancellationToken);

                var lang2 = await GetLang(stepContext.Context, cancellationToken);
                return await stepContext.PromptAsync(
                    "DatePrompt",
                    new PromptOptions
                    {
                        Prompt = MessageFactory.Text(I18n.T(lang2, "Prompt_Date")),
                        RetryPrompt = MessageFactory.Text(I18n.T(lang2, "Retry_Date"))
                    },
                    cancellationToken);
            }
            catch (Exception ex) { return await FailAsync(stepContext, ex, cancellationToken); }
        }

        private async Task<DialogTurnResult> AskForTimeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            try
            {
                var reservation = (ReservationState)stepContext.Values["reservation"];
                if (string.IsNullOrWhiteSpace(reservation.Day) && stepContext.Result is string d) reservation.Day = d;

                if (!string.IsNullOrWhiteSpace(reservation.Time))
                    return await stepContext.NextAsync(null, cancellationToken);

                var lang = await GetLang(stepContext.Context, cancellationToken);
                return await stepContext.PromptAsync(
                    "TimePrompt",
                    new PromptOptions
                    {
                        Prompt = MessageFactory.Text(I18n.T(lang, "Prompt_Time")),
                        RetryPrompt = MessageFactory.Text(I18n.T(lang, "Retry_Time"))
                    },
                    cancellationToken);
            }
            catch (Exception ex) { return await FailAsync(stepContext, ex, cancellationToken); }
        }

        private async Task<DialogTurnResult> AskForNameStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            try
            {
                var reservation = (ReservationState)stepContext.Values["reservation"];
                if (string.IsNullOrWhiteSpace(reservation.Time) && stepContext.Result is string t) reservation.Time = t;

                if (!string.IsNullOrWhiteSpace(reservation.Name))
                    return await stepContext.NextAsync(null, cancellationToken);

                var lang = await GetLang(stepContext.Context, cancellationToken);
                return await stepContext.PromptAsync(
                    "NamePrompt",
                    new PromptOptions
                    {
                        Prompt = MessageFactory.Text(I18n.T(lang, "Prompt_Name"))
                    },
                    cancellationToken);
            }
            catch (Exception ex) { return await FailAsync(stepContext, ex, cancellationToken); }
        }

        private async Task<DialogTurnResult> AskForContactStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            try
            {
                var reservation = (ReservationState)stepContext.Values["reservation"];
                if (string.IsNullOrWhiteSpace(reservation.Name) && stepContext.Result is string n) reservation.Name = n;

                if (!string.IsNullOrWhiteSpace(reservation.Contact))
                    return await stepContext.NextAsync(null, cancellationToken);

                var lang = await GetLang(stepContext.Context, cancellationToken);
                return await stepContext.PromptAsync(
                    "ContactPrompt",
                    new PromptOptions { Prompt = MessageFactory.Text(I18n.T(lang, "Prompt_Contact")) },
                    cancellationToken);
            }
            catch (Exception ex) { return await FailAsync(stepContext, ex, cancellationToken); }
        }

        private async Task<DialogTurnResult> ConfirmSummaryStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            try
            {
                var reservation = (ReservationState)stepContext.Values["reservation"];

                if (string.IsNullOrWhiteSpace(reservation.Contact) && stepContext.Result is string c)
                    reservation.Contact = c;

                var lang = await GetLang(stepContext.Context, cancellationToken);

                // u iso
                var year = DateTime.Now.Year;
                var dayIso = ToIsoDate(reservation.Day, year);
                var timeIso = ToIsoTime(reservation.Time);

                if (dayIso is null)
                {
                    await stepContext.Context.SendActivityAsync(
                        lang == "en" ? "I couldn't understand the date. Try e.g. 17.09."
                                     : "Nisam razumio datum. Molim unesite npr. 17.09.",
                        cancellationToken: cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }
                if (timeIso is null)
                {
                    await stepContext.Context.SendActivityAsync(
                        lang == "en" ? "I couldn't understand the time. Use HH:mm (e.g., 14:30)."
                                     : "Nisam razumio vrijeme. Unesite HH:mm (npr. 14:30).",
                        cancellationToken: cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }

                // poslovna pravila
                if (DateTime.TryParse(dayIso, out var fullDate))
                {
                    if (!BusinessRules.IsBusinessDay(fullDate))
                    {
                        await stepContext.Context.SendActivityAsync(
                            lang == "en" ? "Bookings are not available on Sundays or public holidays. Pick another day."
                                         : "Termin nije moguć nedjeljom ili na praznik. Odaberite drugi dan.",
                            cancellationToken: cancellationToken);
                        return await stepContext.EndDialogAsync(null, cancellationToken);
                    }
                }
                if (TimeSpan.TryParse(timeIso, out var tt) && !BusinessRules.IsWithinBusinessHours(new TimeSpan(tt.Hours, tt.Minutes, 0)))
                {
                    await stepContext.Context.SendActivityAsync(
                        lang == "en" ? $"Working hours are from {BusinessRules.Start:hh\\:mm} to {BusinessRules.End:hh\\:mm}."
                                     : $"Radno vrijeme je od {BusinessRules.Start:hh\\:mm} do {BusinessRules.End:hh\\:mm}.",
                        cancellationToken: cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }

                var userId = stepContext.Context.Activity.From.Id;
                var name = reservation.Name?.Trim();

                // normalizacija usluge
                var serviceKey = BusinessRules.NormalizeService(reservation.ServiceType);
                if (serviceKey == null)
                {
                    var list = string.Join(", ", BusinessRules.AllServices(lang));
                    await stepContext.Context.SendActivityAsync(
                        lang == "en" ? $"Unknown service. Valid options: {list}"
                                     : $"Nepoznata usluga. Dopuštene opcije: {list}",
                        cancellationToken: cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }

                // jedinstven termin
                var clash = await _db.Appointments
                    .AnyAsync(a => a.UserId == userId && a.DayIso == dayIso && a.TimeIso == timeIso, cancellationToken);
                if (clash)
                {
                    await stepContext.Context.SendActivityAsync(
                        lang == "en" ? $"That slot ({PrettyDate(dayIso)} at {timeIso}) is already taken for your account."
                                     : $"Taj termin ({PrettyDate(dayIso)} u {timeIso}) je već zauzet za vaš račun.",
                        cancellationToken: cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }

                // globalni kapacitet
                var taken = await _db.Appointments.CountAsync(a => a.DayIso == dayIso && a.TimeIso == timeIso, cancellationToken);
                if (taken >= MAX_PER_SLOT)
                {
                    await stepContext.Context.SendActivityAsync(
                        lang == "en" ? $"Slot {PrettyDate(dayIso)} at {timeIso} is full (max {MAX_PER_SLOT}). Choose another time."
                                     : $"Nažalost, slot {PrettyDate(dayIso)} u {timeIso} je popunjen (max {MAX_PER_SLOT}). Odaberite drugi termin.",
                        cancellationToken: cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }

                // preklapanje po trajanju usluge
                var start = DateTime.Parse($"{dayIso} {timeIso}:00");
                var durationMin = BusinessRules.GetDefaultDurationMinutes(serviceKey);
                var end = start.AddMinutes(durationMin);

                var sameDay = await _db.Appointments
                    .AsNoTracking()
                    .Where(a => a.DayIso == dayIso)
                    .ToListAsync(cancellationToken);

                bool overlaps = sameDay.Any(a =>
                {
                    if (string.IsNullOrWhiteSpace(a.TimeIso)) return false;
                    var otherStart = DateTime.Parse($"{a.DayIso} {a.TimeIso}:00");
                    var otherDur = BusinessRules.GetDefaultDurationMinutes(a.ServiceType);
                    var otherEnd = otherStart.AddMinutes(otherDur);
                    return start < otherEnd && end > otherStart;
                });

                if (overlaps)
                {
                    var disp = BusinessRules.LocalizeService(serviceKey, lang);
                    await stepContext.Context.SendActivityAsync(
                        lang == "en"
                            ? $"The time {PrettyDate(dayIso)} {timeIso} ({durationMin} min, {disp}) overlaps with an existing booking."
                            : $"Termin {PrettyDate(dayIso)} {timeIso} ({durationMin} min, {disp}) se preklapa s postojećim.",
                        cancellationToken: cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }

                reservation.Day = dayIso;            
                reservation.Time = timeIso;
                reservation.ServiceType = serviceKey;

                var serviceDisplay = BusinessRules.LocalizeService(serviceKey, lang);
                var summary = lang == "en"
                    ? $"Please confirm your booking:\n" +
                      $"• Date: {PrettyDate(dayIso)}\n" +
                      $"• Time: {timeIso}\n" +
                      $"• Name: {name}\n" +
                      $"• Contact: {reservation.Contact}\n" +
                      $"• Service: {serviceDisplay}\n\n" +
                      $"Reply **yes** to confirm or **no** to cancel."
                    : $"Molim potvrdi rezervaciju:\n" +
                      $"• Datum: {PrettyDate(dayIso)}\n" +
                      $"• Vrijeme: {timeIso}\n" +
                      $"• Ime: {name}\n" +
                      $"• Kontakt: {reservation.Contact}\n" +
                      $"• Usluga: {serviceDisplay}\n\n" +
                      $"Odgovori **da** za potvrdu ili **ne** za odustajanje.";

                return await stepContext.PromptAsync(
                    "ConfirmPrompt",
                    new PromptOptions { Prompt = MessageFactory.Text(summary) },
                    cancellationToken);
            }
            catch (Exception ex) { return await FailAsync(stepContext, ex, cancellationToken); }
        }

        private async Task<DialogTurnResult> SaveReservationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            try
            {
                var reservation = (ReservationState)stepContext.Values["reservation"];
                var lang = await GetLang(stepContext.Context, cancellationToken);

                var reply = (stepContext.Result as string) ?? "";
                if (IsNo(reply))
                {
                    await stepContext.Context.SendActivityAsync(
                        lang == "en" ? "Cancelled." : "Odustao/la si.",
                        cancellationToken: cancellationToken);
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }
                if (!IsYes(reply))
                {
                    await stepContext.Context.SendActivityAsync(
                        lang == "en" ? "Please reply **yes** to confirm or **no** to cancel."
                                     : "Molim odgovori **da** za potvrdu ili **ne** za odustajanje.",
                        cancellationToken: cancellationToken);
                    return await stepContext.ReplaceDialogAsync(this.Id, reservation, cancellationToken);
                }

                // za da
                var code = BookingCodeUtil.NewCode();
                for (int i = 0; i < 3; i++)
                {
                    var exists = await _db.Appointments.AnyAsync(a => a.BookingCode == code, cancellationToken);
                    if (!exists) break;
                    code = BookingCodeUtil.NewCode();
                }

                var userId = stepContext.Context.Activity.From.Id;
                var appointment = new Appointment
                {
                    UserName = reservation.Name?.Trim(),
                    ServiceType = reservation.ServiceType,
                    UserId = userId,
                    DayIso = reservation.Day,
                    TimeIso = reservation.Time,
                    Contact = reservation.Contact?.Trim(),
                    BookingCode = code
                };

                _db.Appointments.Add(appointment);
                await _db.SaveChangesAsync(cancellationToken);

                var serviceDisplay = string.IsNullOrWhiteSpace(appointment.ServiceType)
                    ? "-"
                    : BusinessRules.LocalizeService(appointment.ServiceType, lang);

                // poruka s kodom
                await stepContext.Context.SendActivityAsync(
                    $"{appointment.BookingCode}\n" +
                    (lang == "en"
                        ? "is your reservation code, you can use this code later to change or cancel your booking."
                        : "je vaš kod rezervacije, možete ga iskoristiti kasnije za promjenu ili otkazivanje."),
                    cancellationToken: cancellationToken);

                // glavna potvrda
                var baseText = I18n.F(lang, "Confirm",
                                      PrettyDate(appointment.DayIso),
                                      appointment.TimeIso,
                                      appointment.UserName,
                                      serviceDisplay);

                if (!string.IsNullOrWhiteSpace(appointment.Contact))
                {
                    baseText += I18n.F(lang, "Confirm_ContactLine", appointment.Contact);
                }

                await stepContext.Context.SendActivityAsync(baseText, cancellationToken: cancellationToken);
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }
            catch (Exception ex) { return await FailAsync(stepContext, ex, cancellationToken); }
        }

        // validatori
        private async Task<bool> DateValidationAsync(PromptValidatorContext<string> ctx, CancellationToken cancellationToken)
        {
            try
            {
                var lang = await GetLang(ctx.Context, cancellationToken);

                var raw = ctx.Recognized.Value ?? "";
                var input = raw.Trim().Replace('/', '.').Replace('-', '.');
                if (!input.EndsWith(".")) input += ".";

                if (DateTime.TryParseExact(
                        input,
                        new[] { "d.M.", "dd.MM." },
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var d))
                {
                    var normalized = d.ToString("dd.MM.");
                    ctx.Recognized.Value = normalized;

                    var iso = ToIsoDate(normalized, DateTime.Now.Year);
                    if (iso != null && DateTime.TryParse(iso, out var full))
                    {
                        if (!BusinessRules.IsBusinessDay(full))
                        {
                            await ctx.Context.SendActivityAsync(
                                lang == "en"
                                    ? $"⛔ Bookings are not available on {normalized} (Sunday/holiday). Choose another day."
                                    : $"⛔ Termin nije moguć za {normalized}. Neradimo nedjeljom ili je državni praznik. Odaberite drugi dan.",
                                cancellationToken: cancellationToken);
                            return false;
                        }
                    }

                    return true;
                }

                await ctx.Context.SendActivityAsync(
                    lang == "en"
                        ? "⛔ Invalid date. Please enter **dd.MM.** (e.g., 17.09.)."
                        : "⛔ Datum nije prepoznat. Unesite **dd.MM.** (npr. 17.09.).",
                    cancellationToken: cancellationToken);
                return false;
            }
            catch
            {
                await ctx.Context.SendActivityAsync(
                    "⛔ " + (await GetLang(ctx.Context, cancellationToken) == "en"
                        ? "There was an error while parsing the date. Try again as **dd.MM.** (e.g., 17.09.)."
                        : "Došlo je do greške pri čitanju datuma. Pokušajte ponovno u obliku **dd.MM.** (npr. 17.09.)."),
                    cancellationToken: cancellationToken);
                return false;
            }
        }

        private async Task<bool> TimeValidationAsync(PromptValidatorContext<string> ctx, CancellationToken cancellationToken)
        {
            try
            {
                var lang = await GetLang(ctx.Context, cancellationToken);

                var raw = ctx.Recognized.Value ?? "";
                var input = raw.Trim()
                               .Replace('.', ':')
                               .Replace('-', ':')
                               .Replace('h', ':')
                               .Replace('H', ':');

                if (TimeSpan.TryParse(input, out var t))
                {
                    var hhmm = new DateTime(1, 1, 1, t.Hours, t.Minutes, 0).ToString("HH\\:mm");
                    ctx.Recognized.Value = hhmm;

                    if (!BusinessRules.IsWithinBusinessHours(new TimeSpan(t.Hours, t.Minutes, 0)))
                    {
                        await ctx.Context.SendActivityAsync(
                            lang == "en"
                                ? $"⛔ Time {hhmm} is outside working hours. We work **{BusinessRules.Start:hh\\:mm}–{BusinessRules.End:hh\\:mm}**."
                                : $"⛔ Vrijeme {hhmm} je izvan radnog vremena. Radimo **od {BusinessRules.Start:hh\\:mm} do {BusinessRules.End:hh\\:mm}**.",
                            cancellationToken: cancellationToken);
                        return false;
                    }

                    return true;
                }

                await ctx.Context.SendActivityAsync(
                    lang == "en"
                        ? "⛔ Invalid time. Please enter **HH:mm** (e.g., 14:30)."
                        : "⛔ Vrijeme nije prepoznato. Unesite **HH:mm** (npr. 14:30).",
                    cancellationToken: cancellationToken);
                return false;
            }
            catch
            {
                await ctx.Context.SendActivityAsync(
                    "⛔ " + (await GetLang(ctx.Context, cancellationToken) == "en"
                        ? "There was an error while parsing the time. Enter **HH:mm** (e.g., 09:05)."
                        : "Došlo je do greške pri čitanju vremena. Unesite **HH:mm** (npr. 09:05)."),
                    cancellationToken: cancellationToken);
                return false;
            }
        }

        private async Task<bool> ContactValidationAsync(PromptValidatorContext<string> ctx, CancellationToken cancellationToken)
        {
            var lang = await GetLang(ctx.Context, cancellationToken);
            var raw = (ctx.Recognized.Value ?? "").Trim();

            bool IsEmail(string s)
            {
                try { var addr = new System.Net.Mail.MailAddress(s); return addr.Address == s; }
                catch { return false; }
            }

            bool IsPhone(string s)
            {
                var digits = new string(s.Where(char.IsDigit).ToArray());
                return digits.Length >= 9 && digits.Length <= 12;
            }

            if (IsEmail(raw) || IsPhone(raw))
            {
                ctx.Recognized.Value = raw;
                return true;
            }

            await ctx.Context.SendActivityAsync(
                lang == "en"
                    ? "⛔ Please enter a valid **phone** (9–12 digits, you can include + and spaces) or a valid **e-mail**."
                    : "⛔ Unesite ispravan **telefon** (9–12 znamenki, dopušteni + i razmaci) ili ispravan **e-mail**.",
                cancellationToken: cancellationToken);
            return false;
        }

        private Task<bool> ConfirmValidatorAsync(PromptValidatorContext<string> ctx, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }
}
