using Microsoft.AspNetCore.Mvc;

namespace MoneyFlow.Controllers
{
    public class ServiceController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
