using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyFlow.DTOs;
using MoneyFlow.Interfaces;
using MoneyFlow.Models;

namespace MoneyFlow.Controllers
{
    [Authorize]
    public class VolumetricoController : Controller
    {
        private readonly IVolumetricoManager _manager;
        private readonly ILogger<VolumetricoController> _logger;

        public VolumetricoController(IVolumetricoManager manager, ILogger<VolumetricoController> logger)
        {
            _manager = manager;
            _logger = logger;
        }

        // GET: Volumetrico/Index
        public IActionResult Index()
        {
            return View(new VolumetricoViewModel());
        }

        // POST: Volumetrico/CargarExcel
        // Sube el archivo .xlsx, crea la tabla temporal y devuelve un preview.
        [HttpPost]
        public async Task<IActionResult> CargarExcel(IFormFile archivo, int mes, int año)
        {
            try
            {
                var resultado = await _manager.CargarExcelAsync(archivo, mes, año);
                return Json(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Volumetrico: error al procesar el archivo {Archivo}", archivo?.FileName);
                return Json(new VolumetricoCargaDTO
                {
                    Success = false,
                    Errores = { "Error procesando el archivo: " + ex.Message }
                });
            }
        }

        // POST: Volumetrico/Procesar
        // Ejecuta las transformaciones e inserta en volumetric.clarifications.
        [HttpPost]
        public async Task<IActionResult> Procesar(string tabla, int mes, int año)
        {
            try
            {
                var resultado = await _manager.ProcesarTransformacionesAsync(tabla, mes, año);

                if (!resultado.Success)
                    return BadRequest(new { success = false, message = resultado.Mensaje });

                return Ok(new
                {
                    success = true,
                    message = resultado.Mensaje,
                    registrosTransformados = resultado.RegistrosTransformados,
                    registrosInsertados = resultado.RegistrosInsertados,
                    registrosConAclaracion = resultado.RegistrosConAclaracion
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // GET: Volumetrico/GenerarScript?clarificationIdInicio=X
        // Descarga un script .sql con un INSERT por cada fila de clarifications,
        // asignando ClarificationId consecutivo a partir de clarificationIdInicio.
        [HttpGet]
        public async Task<IActionResult> GenerarScript(int clarificationIdInicio)
        {
            try
            {
                if (clarificationIdInicio < 1)
                    return BadRequest("El ClarificationId inicial debe ser mayor o igual a 1.");

                var bytes = await _manager.GenerarScriptInsertsAsync(clarificationIdInicio);
                return File(bytes, "application/sql", $"clarifications_inserts_desde_{clarificationIdInicio}.sql");
            }
            catch (Exception ex)
            {
                return BadRequest("Error: " + ex.Message);
            }
        }

        // POST: Volumetrico/TruncarClarifications
        // Vacía la tabla volumetric.clarifications.
        [HttpPost]
        public async Task<IActionResult> TruncarClarifications()
        {
            try
            {
                int eliminados = await _manager.TruncarClarificationsAsync();
                return Ok(new { success = true, message = $"Tabla clarifications truncada. {eliminados} registro(s) eliminados." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // GET: Volumetrico/ObtenerTablas
        // Lista las tablas temporales volumétricas existentes.
        [HttpGet]
        public async Task<IActionResult> ObtenerTablas()
        {
            try
            {
                var tablas = await _manager.ListarTablasVolumetricasAsync();
                return Json(tablas);
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // DELETE: Volumetrico/LimpiarTabla?tabla=X
        // Elimina una tabla temporal.
        [HttpDelete]
        public async Task<IActionResult> LimpiarTabla(string tabla)
        {
            try
            {
                bool eliminada = await _manager.EliminarTablaTemporalAsync(tabla);
                return Ok(new { success = true, message = eliminada ? "Tabla temporal eliminada." : "La tabla no existía." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "Error: " + ex.Message });
            }
        }

        // GET: Volumetrico/ObtenerConteo?tabla=X
        // Cuenta los registros de una tabla temporal.
        [HttpGet]
        public async Task<IActionResult> ObtenerConteo(string tabla)
        {
            try
            {
                int total = await _manager.ObtenerConteoRegistrosAsync(tabla);
                return Ok(new { total });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}
