using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyFlow.DTOs;
using MoneyFlow.Interfaces;
using MoneyFlow.Managers;
using System.Security.Claims;

namespace MoneyFlow.Controllers
{
    [Authorize]
    public class TransactionController : Controller
    {
        private readonly ServiceManager _serviceManager;
        private readonly ITransactionManager _transactionManager;
        public TransactionController(ServiceManager serviceManager, ITransactionManager transactionManager)
        {
            _serviceManager = serviceManager;
            _transactionManager = transactionManager;
        }
        public IActionResult Index()
        {
            return View();
        }

        // Get Services for the dropdown list in the view
        [HttpGet]
        public IActionResult GetServicesByType(string type)
        {
            //Get userId from the session
            var userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)!.Value;
            var services = _serviceManager.GetByType(int.Parse(userId), type);
            return Ok(services);
        }

        // Save a new transaction
        [HttpPost]
        public IActionResult SaveNewTransaction([FromBody] TransactionDTO transactionDTO)
        {
            // Validate the incoming data
            if (!ModelState.IsValid)
            {
                return BadRequest(new { success = false, message = "Invalid data.", errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
            }

            var result = _transactionManager.SaveNew(transactionDTO);
            if (result > 0)
            {
                return Ok(new { success = true, message = "Transaction saved successfully." });
            }
            else
            {
                return BadRequest(new { success = false, message = "Failed to save transaction." });
            }
        }

        // TODO: Region para el histórico de transacciones
        public IActionResult HistoryTransactions()
        {
            return View("History");
        }

        // Get the transaction history for a user and a date range, range will be provided by the view but
        // if range is not provided, it will default to the current month
        [HttpGet]
        public async Task<IActionResult> GetHistoryTransactions(DateOnly? startDate, DateOnly? endDate)
         {
            var userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)!.Value; // TODO: Get the userId from the session
            // If startDate or endDate is not provided, default to the current month
            if (!startDate.HasValue || !endDate.HasValue)
            {
                var now = DateTime.Now;
                startDate = new DateOnly(now.Year, now.Month, 1);
                endDate = startDate.Value.AddMonths(1).AddDays(-1);
            }
            var transactions = await _transactionManager.GetTransactionsHistory(startDate.Value, endDate.Value, int.Parse(userId));
            return Ok(new {
                    data = transactions
                });
        }
        /*public async Task<IActionResult> GetHistoryTransactions(DateOnly startDate =  , DateOnly endDate)
        {
            var userId = 1; // TODO: Get the userId from the session
            var transactions = _transactionManager.GetTransactionsHistory(startDate, endDate, userId);
            return Ok(new {
                    data = transactions
                });
        }*/
    }
}
