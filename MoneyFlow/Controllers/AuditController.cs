using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyFlow.DTOs;
using MoneyFlow.Interfaces;
using MoneyFlow.Managers;
using MoneyFlow.Models;
using Rotativa.AspNetCore;

namespace MoneyFlow.Controllers
{
    [Authorize]
    public class AuditController : Controller
    {
        private readonly IAuditManager _auditManager;
        // Inicializamos el constructor para inyectar el Manager
        public AuditController(IAuditManager auditManager)
        {
            _auditManager = auditManager;
        }

        // Vista principal
        [HttpGet]
        public async Task<IActionResult> GetAudits(string ls)
        {
            var audits = await _auditManager.GetAudits(ls);

            ViewBag.Station = ls;

            return View("Index", audits);
        }

        [HttpGet]
        public async Task<IActionResult> AuditDetail(string ls, int auditId)
        {
            var products = await _auditManager.GetproductsByAudit(ls, auditId);

            var ids = products.Select(x => x.ProductAuditInventoryId).ToList();

            var details = ids.Any()
                ? await _auditManager.GetAuditDetails(ls, ids)
                : new List<ProductAuditDetailDTO>();

            var model = new AuditDetailViewModel
            {
                Station = ls,
                AuditId = auditId,
                Inventory = products,
                Details = details
            };

            return View("AuditDetail", model);
        }

        public async Task<IActionResult> ExportPdf(int auditId, string station)
        {
            var model = await _auditManager.GetFullAudit(station, auditId);
            return new ViewAsPdf("PdfReport", model)
            {
                FileName = $"Audit_{auditId}.pdf",
                PageSize = Rotativa.AspNetCore.Options.Size.A4,
                PageOrientation = Rotativa.AspNetCore.Options.Orientation.Landscape,
                PageMargins = new Rotativa.AspNetCore.Options.Margins(5, 5, 5, 5)
            };

        }

    }
}
