using System.Collections.Generic;

namespace TerminBot.Localization
{
    public static class I18n
    {
        private static readonly Dictionary<string, Dictionary<string, string>> dict = new()
        {
            ["hr"] = new()
            {
                ["Prompt_Service"] = "Koji tip problema imate? (IT / električni uređaj / vodoinstalacija)",
                ["Prompt_Date"] = "Za koji dan želite rezervaciju?",
                ["Retry_Date"] = "Unos nije ispravan. Molimo unesite datum u formatu dd.MM. (npr. 17.10.)",
                ["Prompt_Time"] = "U koliko sati želite rezervaciju?",
                ["Retry_Time"] = "Unos nije ispravan. Molimo unesite vrijeme u formatu HH:mm (npr. 14:30)",
                ["Prompt_Name"] = "Molim vas, unesite svoje ime.",
                ["Confirm"] = "Rezervacija potvrđena za {0} u {1} na ime {2}.\nVrsta usluge: {3}.",
                ["Help"] = @"Popis dostupnih naredbi:
rezerviraj ...
prikaži rezervacije
moje zadnje
otkaži rezervaciju [kod]
promijeni rezervaciju [kod] u [datum] u [vrijeme]
provjeri termin [datum] u [vrijeme]
usluge
language: hr
language: en
--- Admin naredbe ---
admin login [username] [password]
admin logout
admin list all
admin list day [dd.MM.]
admin list range [dd.MM.]-[dd.MM.]
admin list service [naziv_usluge]
admin list user [userId]
admin cancel [kod]
admin change [kod] [dd.MM.] u [HH:mm]",
                ["LangSet"] = "Jezik postavljen na hrvatski.",
                ["NoReservations"] = "Trenutno nema rezervacija.",
                ["NoReservationsForDay"] = "Nema rezervacija za {0}.",
                ["UnknownDate"] = "Datum nije prepoznat. Unesite npr. 17.09.",
                ["Fallback"] = "Nisam siguran što želite. Ako želite rezervirati termin, napišite 'rezerviraj'.",
                ["Welcome"] = "Pozdrav! Za rezervaciju termina napiši 'rezerviraj'.",
                ["ListLine"] = "{0} u {1} – {2}{3}",
                ["AdminListLine"] = "{0} {1} – {2} [{3}]{4}",
                ["Cancelled"] = "Rezervacija za {0} u {1} je otkazana.",
                ["ChangedTo"] = "Rezervacija je promijenjena na {0} u {1}.",
                ["NluSetClu"] = "NLU način rada: CLU (cloud).",
                ["NluSetRegex"] = "NLU način rada: Regex.",
                ["NluSetLocal"] = "NLU način rada: Lokalni CLU.",
                ["Prompt_Contact"] = "Unesite kontakt (telefon ili e-mail).",
                ["Confirm_ContactLine"] = "\nKontakt: {0}",
                ["NluIsLocal"] = "NLU je trenutačno: LOCAL.",
                ["NluIsClu"] = "NLU je trenutačno: CLU.",
                ["NluIsRegex"] = "NLU je trenutačno: REGEX.",


            },
            ["en"] = new()
            {
                ["Prompt_Service"] = "What kind of issue do you have? (IT / electrical device / plumbing)",
                ["Prompt_Date"] = "For which day would you like to book?",
                ["Retry_Date"] = "Invalid input. Please enter date as dd.MM. (e.g., 17.10.)",
                ["Prompt_Time"] = "At what time would you like to book?",
                ["Retry_Time"] = "Invalid input. Please enter time as HH:mm (e.g., 14:30)",
                ["Prompt_Name"] = "Please enter your name.",
                ["Confirm"] = "Booking confirmed for {0} at {1} under the name {2}.\nService type: {3}.",
                ["Help"] = @"List of available commands:
book ...
show reservations
my last
cancel reservation [code]
change reservation [code] to [date] at [time]
check slot [date] at [time]
services
language: hr
language: en
--- Admin commands ---
admin login [username] [password]
admin logout
admin list all
admin list day [dd.MM.]
admin list range [dd.MM.]-[dd.MM.]
admin list service [service_name]
admin list user [userId]
admin cancel [code]
admin change [code] [dd.MM.] at [HH:mm]",
                ["LangSet"] = "Language set to English.",
                ["NoReservations"] = "There are no reservations.",
                ["NoReservationsForDay"] = "No reservations for {0}.",
                ["UnknownDate"] = "I couldn't understand the date. Try e.g. 17.09.",
                ["Fallback"] = "I'm not sure what you need. Type 'book' to make a reservation.",
                ["Welcome"] = "Welcome! Type 'book' to make a reservation.",
                ["ListLine"] = "{0} at {1} – {2}{3}",
                ["AdminListLine"] = "{0} {1} – {2} [{3}]{4}",
                ["Cancelled"] = "Reservation for {0} at {1} has been cancelled.",
                ["ChangedTo"] = "Reservation changed to {0} at {1}.",
                ["NluSetClu"] = "NLU mode: CLU (cloud).",
                ["NluSetRegex"] = "NLU mode: Regex.",
                ["NluSetLocal"] = "NLU mode: Local.",
                ["Prompt_Contact"] = "Please enter your contact (phone or e-mail).",
                ["Confirm_ContactLine"] = "\nContact: {0}",
                ["NluIsLocal"] = "NLU is currently: LOCAL.",
                ["NluIsClu"] = "NLU is currently: CLU.",
                ["NluIsRegex"] = "NLU is currently: REGEX.",
            }
        };

        public static string T(string lang, string key) =>
            dict.TryGetValue(lang, out var m) && m.TryGetValue(key, out var v) ? v : dict["hr"][key];
        public static string F(string lang, string key, params object[] args) =>
            string.Format(T(lang, key), args);
    }


}

