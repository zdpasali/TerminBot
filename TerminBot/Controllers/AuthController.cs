using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using TerminBot.Data;

namespace TerminBot.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _db;
        public AuthController(AppDbContext db) { _db = db; }

        private const string SessionKey = "ADMIN_USER";

        [HttpGet("/auth/login")]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View("Login", model: null);
        }

        [HttpPost("/auth/login")]
        public async Task<IActionResult> LoginPost(string username, string password, string? returnUrl = null)
        {
            username ??= ""; password ??= "";
            var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
            if (user != null && TerminBot.Security.PasswordHasher.Verify(password, user.PasswordHash))
            {
                HttpContext.Session.SetString(SessionKey, username);
                return Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/admin" : returnUrl);
            }

            ViewBag.ReturnUrl = returnUrl;
            ViewBag.Error = "Neispravni podaci za prijavu.";
            return View("Login", model: null);
        }

        [HttpGet("/auth/logout")]
        public IActionResult Logout(string? returnUrl = null)
        {
            HttpContext.Session.Remove(SessionKey);
            return Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/auth/login" : returnUrl);
        }

        public static bool IsLoggedIn(HttpContext ctx)
            => ctx.Session.TryGetValue(SessionKey, out _);

        public static string? CurrentUsername(HttpContext ctx)
            => ctx.Session.GetString(SessionKey);
    }
}
