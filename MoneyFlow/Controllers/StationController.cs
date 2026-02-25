using Microsoft.AspNetCore.Mvc;
using MoneyFlow.Managers;
using MoneyFlow.Models;

namespace MoneyFlow.Controllers
{
    public class StationController : Controller
    {
        private readonly StationManager _stationManager;

        public StationController(StationManager stationManager)
        {
            _stationManager = stationManager;
        }

        public IActionResult Index()
        {
            return View(new List<StationViewModel>());
        }
        [HttpPost]
        public async Task<IActionResult> Index(string search)
        {
            try
            {
                var stations = await _stationManager.SearchStations(search ?? "");
                return View(stations);
            }
            catch (Exception ex)
            {
                ViewBag.Message = ex.Message;
                return View(new List<StationViewModel>());

            }
        }
    }
}
