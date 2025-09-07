using Microsoft.AspNetCore.Mvc;
namespace TerminBot.Controllers
{
    public class ChatController : Controller
    {
        public IActionResult Index() => View();
    }
}
