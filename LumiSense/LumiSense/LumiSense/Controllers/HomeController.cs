using Microsoft.AspNetCore.Mvc;

namespace LumiSense.Controllers
{
    public class HomeController : Controller
    {
        public HomeController()
        {
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet("/About")]
        public IActionResult About()
        {
            return View();
        }
    }
}