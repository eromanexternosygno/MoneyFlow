using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MoneyFlow.Controllers
{
    [Authorize]
    public class SearchController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
