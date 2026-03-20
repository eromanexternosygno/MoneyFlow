using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyFlow.Interfaces;
using MoneyFlow.Models;
using System.Security.Claims;

namespace MoneyFlow.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        //Inyectamos la instancia del UserManager
        private readonly IUserManager _userManager;

        // Constructor
        public AccountController(IUserManager userManager)
        {
            _userManager = userManager;
        }

        // GET: Account/Login
        [HttpGet]
        public IActionResult Login()
        {
            // Return view with empty LoginViewModel
            var loginViewModel = new LoginViewModel();
            return View(loginViewModel);
        }


        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // If the model state is invalid, return the view with the current model to show validation errors
                return View(model);
            }
            var user = await _userManager.GetByEmail(model.Email);
            if (user == null) {
                TempData["ErrorMessage"] = "Invalid email or password. Please try again.";
                return View(model);
            }

            // Validate Password Hashed
            var userData = await _userManager.ValidatePassword(model.Email,model.Password);

            if (userData == null) {
                TempData["ErrorMessage"] = "Invalid email or password. Please try again.";
                return View(model);
            }

            // Return view with empty LoginViewModel
            var loginViewModel = await _userManager.Login(model);
            if (loginViewModel == null) {
                // SI fallo el login, se muestra el mismo modelo con los errores de validación
                TempData["ErrorMessage"] = "Invalid email or password. Please try again.";
                return View(model);
            }

            // Si el login es exitoso, se generan los claims y se autentica al usuario
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Email, user.Email)
            };

            var claimsIdentity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme
            );

            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            // Cookie Creation
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                claimsPrincipal,
                new AuthenticationProperties
                {
                    IsPersistent = true, // Mantener la sesión activa incluso después de cerrar el navegador
                    AllowRefresh = true, // Permitir la renovación de la cookie
                    ExpiresUtc = DateTime.UtcNow.AddHours(1) // Expiración de la cookie en 1 hora
                }
            );

            return RedirectToAction("Index","Home");

        }
    }
}
