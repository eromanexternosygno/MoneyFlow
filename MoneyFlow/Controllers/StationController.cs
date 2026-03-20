using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyFlow.DTOs;
using MoneyFlow.Interfaces;
using MoneyFlow.Managers;
using MoneyFlow.Models;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace MoneyFlow.Controllers
{
    [Authorize]
    public class StationController : Controller
    {
        private readonly IStationManager _stationManager;

        public StationController(IStationManager stationManager)
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

        // Get all receptions for station
        [HttpGet]
        public async Task<IActionResult> GetReceipts(string ls, string receiptIds = null) {
            ViewBag.LS = ls;
            ViewBag.CurrentSearch = receiptIds; // Para mantener el valor en el input
            try
            {
                var receipts = await _stationManager.GetReceipts(ls, receiptIds);
                return View("ViewReceipts",receipts);
            }
            catch (Exception ex)
            {
                // geet Model blank
                ReceiptViewModel sm = new ReceiptViewModel();
                TempData["Message"] = ex.Message;
                TempData["MessageType"] = "danger";
                return RedirectToAction("ViewReceipts", sm); // Regresa a la búsqueda de estaciones si falla
            }

        }

        // Nuevos métodos: PO show details in a ViewReceipts view
        [HttpPost]
        public async Task<IActionResult> GetPODetails([FromBody] BulkRemissionUpdateDTO request)
        {
            // NoTa: Usamos la lista de poIds para obtener los detalles de las PO, y luego retornamos esos detalles al cliente
            try
            {
                var poDetails = await _stationManager.GetDetailsFromPO(request.Instance, request.POIds);
                return Json(poDetails);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // Nuevo Método: ProcessFinalUpdate Masive en PO
        [HttpPost]
        public async Task<IActionResult> ProcessFinalUpdate([FromBody] BulkRemissionUpdateDTO data)
        {
            try
            {
                // 1.- Ejecutar los UPDATE uno por uno en la instancia remota
                //var updateResult = await _stationManager.UpdateSpecificRemissions(data.Instance, data.POIds.Select(id => new RemissionPair { POId = id, Remission = data.NewRemission }).ToList());

                // 1.- data.Updates contiene los pares {POId, Remission, oldRemision}
                var newRems =  data.NewRemission.Split(',').Select(r => r.Trim()).ToList();
                var oldRems = data.OldRemission?.Split(',').Select(r => r.Trim()).ToList() ?? new List<string>();
                var updateResult = data.POIds.Zip(data.NewRemission.Split(','), (id, rem) => new RemissionPair { POId = id, Remission = rem.Trim() }).ToList();

                await _stationManager.UpdateSpecificRemissions(data.Instance, updateResult);
                // 2.- Guardar el registro en base de datos local con el histórico de correcciones
                var history = new CorrectionHistory
                {
                    Instance = data.Instance,
                    POIds = data.POIds, // Guardamos los IDs como string separado por comas
                    OldRemission = data?.OldRemission ?? "N/A", // Aquí podrías mejorar obteniendo el valor anterior antes de actualizar, pero por simplicidad lo dejamos como N/A
                    NewRemission = data.NewRemission,
                    AppliedAt = DateTime.Now,
                    AppliedBy = User.Identity?.Name ?? "Admin" // Guardamos quién aplicó la corrección
                };
                await _stationManager.SaveHistory(history);
                return Ok(new { success = true, message = $"{updateResult} records updated successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // SaveEvidence
        [HttpPost]
        public IActionResult SaveEvidence([FromBody] EvidenceRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.ImageData)) return BadRequest("No image data");

                // 1. Limpiar el prefijo data:image/png;base64,
                var base64Data = request.ImageData.Contains(",")
                    ? request.ImageData.Split(',')[1]
                    : request.ImageData;

                byte[] imageBytes = Convert.FromBase64String(base64Data);

                // 2. Configurar la ruta raíz en C:
                string rootPath = @"C:\AppReceiptsEvidenceStations";
                string stationPath = Path.Combine(rootPath, request.Instance);

                // 3. Crear carpetas si no existen
                if (!Directory.Exists(stationPath))
                {
                    Directory.CreateDirectory(stationPath);
                }

                // 4. Nombre del archivo: Fecha_Hora_Momento.png
                string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{request.Moment}.png";
                string fullPath = Path.Combine(stationPath, fileName);

                // 5. Guardar físicamente
                System.IO.File.WriteAllBytes(fullPath, imageBytes);

                return Ok();
            }
            catch (Exception ex)
            {
                // Loguear el error para saber si es por permisos de Windows
                return BadRequest("Error al guardar en C:: " + ex.Message);
            }
        }

        // New functions: Search Folios Bulk

        [HttpGet]
        public IActionResult GetProgress(Guid searchId)
        {
            //var progress = _stationManager.GetProgress(searchId);
            
            var progressInfo = _stationManager.GetProgress(searchId);
            return Ok(progressInfo);
        }
        [HttpPost]
        public async Task<IActionResult> ProcessBulk([FromBody] List<StationFolioDTO> request)
        {

            if (request == null || !request.Any()) return BadRequest("El listado de folios está vacío o tiene un formato incorrecto.");

            string user = User.Identity?.Name ?? "Admin";
            Guid searchId = await _stationManager.ExecuteBulkSearch(request, user);

            return Ok(new { searchId });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadExcel(Guid searchId)
        {
            try
            {
                var fileBytes = await _stationManager.ExportResultsToExcel(searchId);
                string fileName = $"Reporte_Masivo_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error al generar Excel: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetStationsMetadata([FromBody] List<int> ids)
        {
            // Obtener la metadata de mi tabla local de estaciones para los ids proporcionados
            var metadata = await _stationManager.GetStationMetadata(ids);
            return Ok(metadata);
        }
    }
}
