using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyFlow.Models;

namespace MoneyFlow.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        // Logout
        public async Task<IActionResult> Logout()
        {
            // Clear the user's session and sign them out
            //HttpContext.Session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("User logged out successfully.");
            return RedirectToAction("Login", "Account");
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)] // This is to prevent caching of the error page
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier }); // This is to get the current request ID for logging and debugging purposes
        }



    }
}
