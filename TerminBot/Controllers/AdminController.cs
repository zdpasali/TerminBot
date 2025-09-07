using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using TerminBot.Data;
using static DateTimeUtils;
using static BusinessRules;
using TerminBot.Models;

namespace TerminBot.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _db;
        public AdminController(AppDbContext db) { _db = db; }

        private IActionResult EnsureLogin(string? returnUrl = null)
        {
            if (!AuthController.IsLoggedIn(HttpContext))
                return Redirect($"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl ?? Request.Path + Request.QueryString)}");
            return null!;
        }

        // GET /admin
        [HttpGet("/admin")]
        public async Task<IActionResult> Index(string? from = null, string? to = null, string? service = null, string? q = null, int page = 1, int pageSize = 50)
        {
            var guard = EnsureLogin();
            if (guard != null) return guard;

            // normalizacija parametra
            var fromIso = DateTimeUtils.ToIsoDate(from, DateTime.Now.Year);
            var toIso = DateTimeUtils.ToIsoDate(to, DateTime.Now.Year);
            var serviceKey = string.IsNullOrWhiteSpace(service) ? null : BusinessRules.NormalizeService(service.Trim());

            // bazni query
            IQueryable<Appointment> query = _db.Appointments.AsNoTracking();

            // datum od/do - ISO stringovi
            if (!string.IsNullOrEmpty(fromIso))
                query = query.Where(a => a.DayIso != null && a.DayIso.CompareTo(fromIso) >= 0);

            if (!string.IsNullOrEmpty(toIso))
                query = query.Where(a => a.DayIso != null && a.DayIso.CompareTo(toIso) <= 0);

            // usluga
            if (!string.IsNullOrWhiteSpace(serviceKey))
                query = query.Where(a => a.ServiceType == serviceKey);

            // trazilica
            if (!string.IsNullOrWhiteSpace(q))
            {
                var qq = q.Trim();
                var qUpper = qq.ToUpperInvariant();

                query = query.Where(a =>
                    (a.UserName != null && a.UserName.Contains(qq)) ||
                    (a.UserId != null && a.UserId.Contains(qq)) ||
                    (a.BookingCode != null && a.BookingCode.ToUpper().Contains(qUpper))
                );
            }

            // sort
            query = query.OrderBy(a => a.DayIso).ThenBy(a => a.TimeIso);

            // statistika
            var stats = await query
                .GroupBy(a => a.ServiceType)
                .Select(g => new AdminController.StatRow { ServiceKey = g.Key, Count = g.Count() })
                .ToListAsync();

            var todayIso = DateTime.Today.ToString("yyyy-MM-dd");
            var todayCount = await query.CountAsync(a => a.DayIso == todayIso);

            // paginacija
            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            ViewBag.Total = total;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TodayCount = todayCount;

            var vm = new AdminController.AdminVm
            {
                From = from,
                To = to,
                Service = service,
                Q = q,
                Items = items,
                Stats = stats
            };
            return View(vm);

        }

        // GET /admin/edit/{id}
        [HttpGet("/admin/edit/{id:int}")]
        public async Task<IActionResult> Edit(int id)
        {
            var guard = EnsureLogin();
            if (guard != null) return guard;

            var a = await _db.Appointments.FindAsync(id);
            if (a == null) return NotFound();
            return View(a);
        }

        // POST /admin/edit/{id}
        [HttpPost("/admin/edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSave(int id, string? name, string? contact, string? day, string? time, string? service)
        {
            var guard = EnsureLogin();
            if (guard != null) return guard;

            var a = await _db.Appointments.FindAsync(id);
            if (a == null) return NotFound();

            var dayIso = string.IsNullOrWhiteSpace(day) ? a.DayIso : DateTimeUtils.ToIsoDate(day, DateTime.Now.Year) ?? a.DayIso;
            var timeIso = string.IsNullOrWhiteSpace(time) ? a.TimeIso : DateTimeUtils.ToIsoTime(time) ?? a.TimeIso;
            var svcKey = string.IsNullOrWhiteSpace(service) ? a.ServiceType : BusinessRules.NormalizeService(service) ?? a.ServiceType;

            a.UserName = string.IsNullOrWhiteSpace(name) ? a.UserName : name.Trim();
            a.Contact = string.IsNullOrWhiteSpace(contact) ? a.Contact : contact.Trim();
            a.DayIso = dayIso;
            a.TimeIso = timeIso;
            a.ServiceType = svcKey;

            await _db.SaveChangesAsync();
            TempData["Msg"] = "Promjene spremljene.";
            return RedirectToAction(nameof(Index));
        }

        // POST /admin/cancel/{id}
        [HttpPost("/admin/cancel/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelById(int id, string? returnUrl = null)
        {
            var guard = EnsureLogin();
            if (guard != null) return guard;

            var appt = await _db.Appointments.FirstOrDefaultAsync(a => a.Id == id);
            if (appt != null)
            {
                _db.Appointments.Remove(appt);
                await _db.SaveChangesAsync();
                TempData["Msg"] = $"Otkazano: {PrettyDate(appt.DayIso)} {appt.TimeIso} – {appt.UserName}.";
            }
            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }

        // POST /admin/cancel-by-code
        [HttpPost("/admin/cancel-by-code")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelByCode(string code, string? returnUrl = null)
        {
            var guard = EnsureLogin();
            if (guard != null) return guard;

            code ??= "";
            var norm = code.ToUpperInvariant().Replace(" ", "");
            var appt = await _db.Appointments
                .FirstOrDefaultAsync(x => (x.BookingCode ?? "").ToUpper().Replace(" ", "") == norm);

            if (appt != null)
            {
                _db.Appointments.Remove(appt);
                await _db.SaveChangesAsync();
                TempData["Msg"] = $"Otkazano: {PrettyDate(appt.DayIso)} {appt.TimeIso} – {appt.UserName} ({appt.BookingCode}).";
            }
            else
            {
                TempData["Msg"] = "Nisam pronašao rezervaciju za taj kod.";
            }
            return Redirect(returnUrl ?? Url.Action(nameof(Index))!);
        }

        public class AdminVm
        {
            public string? From { get; set; }
            public string? To { get; set; }
            public string? Service { get; set; }
            public string? Q { get; set; }
            public System.Collections.Generic.List<TerminBot.Models.Appointment> Items { get; set; } = new();
            public System.Collections.Generic.List<StatRow> Stats { get; set; } = new();
        }
        public class StatRow
        {
            public string ServiceKey { get; set; } = "";
            public int Count { get; set; }
        }
    }
}
