using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyFlow.DTOs;
using MoneyFlow.Interfaces;

namespace MoneyFlow.Controllers
{
    [Authorize]
    public class TransaccionesProcesadasController : Controller
    {
        private readonly ITransaccionesProcesadasManager _manager;

        public TransaccionesProcesadasController(ITransaccionesProcesadasManager manager)
        {
            _manager = manager;
        }

        // GET: TransaccionesProcesadas/Index
        public IActionResult Index()
        {
            return View();
        }

        // POST: TransaccionesProcesadas/CargarExcel
        // Recibe el archivo Excel, lo valida y devuelve un preview en JSON.
        [HttpPost]
        public async Task<IActionResult> CargarExcel(IFormFile archivo)
        {
            try
            {
                var resultado = await _manager.CargarExcel(archivo);
                return Json(resultado);
            }
            catch (Exception ex)
            {
                return Json(new ExcelCargaResultDTO
                {
                    Success = false,
                    Errores = { "Error procesando el archivo: " + ex.Message }
                });
            }
        }

        // POST: TransaccionesProcesadas/Guardar
        // Encola un job de Hangfire y devuelve el id para consultar el progreso.
        [HttpPost]
        public IActionResult Guardar([FromBody] List<TransaccionProcesadaDTO> items)
        {
            if (items == null || !items.Any())
            {
                return BadRequest(new { success = false, message = "No hay datos para guardar." });
            }

            try
            {
                string jobId = _manager.EncolarGuardado(items);
                if (string.IsNullOrEmpty(jobId))
                {
                    return BadRequest(new { success = false, message = "No se pudo encolar el proceso." });
                }

                return Ok(new { success = true, jobId });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "Error al encolar: " + ex.Message });
            }
        }

        // GET: TransaccionesProcesadas/GetProgreso
        [HttpGet]
        public IActionResult GetProgreso(Guid jobId)
        {
            return Ok(_manager.ObtenerProgreso(jobId));
        }

        // GET: TransaccionesProcesadas/ObtenerConteo
        [HttpGet]
        public async Task<IActionResult> ObtenerConteo()
        {
            try
            {
                int total = await _manager.ObtenerConteo();
                return Ok(new { total });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // POST: TransaccionesProcesadas/Eliminar
        // Trunca la tabla de transacciones procesadas.
        [HttpPost]
        public async Task<IActionResult> Eliminar()
        {
            try
            {
                await _manager.Truncar();
                return Ok(new { success = true, message = "Registros eliminados correctamente." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "Error al eliminar: " + ex.Message });
            }
        }

        // GET: TransaccionesProcesadas/DescargarCsv
        [HttpGet]
        public async Task<IActionResult> DescargarCsv()
        {
            try
            {
                var bytes = await _manager.ExportarCsv();
                string fileName = $"TransaccionesProcesadas_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                return File(bytes, "text/csv; charset=utf-8", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest("Error al generar CSV: " + ex.Message);
            }
        }
    }
}
