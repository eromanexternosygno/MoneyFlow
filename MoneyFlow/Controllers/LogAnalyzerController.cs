using Microsoft.AspNetCore.Mvc;

namespace MoneyFlow.Controllers
{
    public class LogAnalyzerController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
