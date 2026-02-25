using Microsoft.AspNetCore.Mvc;
using MoneyFlow.Context;
using MoneyFlow.Managers;
using MoneyFlow.Models;

namespace MoneyFlow.Controllers
{
    public class ServiceController(ServiceManager _serviceManager) : Controller
    {
        public IActionResult Index()
        {
            //TODO: Get the userId from the session
            var listServices = _serviceManager.GetallServices(1); // Temporary userId = 1
            return View(listServices);
        }

        // GET: Service/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Service/Create
        [HttpPost]
        public IActionResult CreateNewService(ServiceViewModel sm)
        {
            // Send information to the view using ViewBag
            ViewBag.Message = "Error";
            //Validate the model
            if (!ModelState.IsValid)
            {
                ViewBag.Message = "Invalid data";
                return View("Create", sm);
            }
            //Now try to save the new service
            var result = _serviceManager.SaveNewService(sm);
            if (result > 0)
            {
                TempData["Message"] = "Service created successfully";
                TempData["MessageType"] = "success";

                return RedirectToAction("Index");
            }
            else
            {
                ViewBag.Message = "Error creating the service";
            }
            return View("Create", sm);
        }

        // GET: Service/Details/5
        [HttpGet]
        public IActionResult Detail(int id)
        {
            var serviceDetail = _serviceManager.GetServiceById(id);

            if (serviceDetail == null)
                return NotFound();

            return View("Detail", serviceDetail);
        }

        // POST: Update Service/5
        [HttpPost]
        public IActionResult Updateservice(ServiceViewModel sm)
        {
            //valid model
            ViewBag.Message = "Error";
            if (!ModelState.IsValid)
            {
                ViewBag.Message = "Invalid data";
                return View("Detail", sm);
            }

            var resultService = _serviceManager.UpdateService(sm);
            //Validate result
            if (resultService > 0) {
                TempData["Message"] = "Service updated successfully";
                TempData["MessageType"] = "success";
                return RedirectToAction("Index");
            }
            else
            {
                ViewBag.Message = "Error updating the service";
            }
            return View("Detail", sm);
        }
        // Delete: Delete Service
        [HttpPost]
        public IActionResult Delete(int id) {

            var eliminado = _serviceManager.DelteTask(id);
            if (eliminado > 0)
            {
                TempData["Message"] = "Service deleted successfully";
                TempData["MessageType"] = "success";

                return RedirectToAction("Index");
            }
            else
            {
                ViewBag.Message = "Error deleting the service";
            }

            return RedirectToAction("Index");
        }

    }
}
