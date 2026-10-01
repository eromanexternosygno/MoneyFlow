using Dapper;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MoneyFlow.Context;
using MoneyFlow.DTOs;
using MoneyFlow.Interfaces;
using MoneyFlow.Utilities;
using System.Text;

namespace MoneyFlow.Managers
{
    public class TransaccionesProcesadasManager : ITransaccionesProcesadasManager
    {
        private readonly AppDbContext _dbContext;
        private readonly string _localConnString;
        private readonly ILogger<TransaccionesProcesadasManager> _logger;
        private readonly ExcelTransaccionesReader _excelReader;

        public TransaccionesProcesadasManager(
            AppDbContext dbContext,
            IConfiguration configuration,
            ILogger<TransaccionesProcesadasManager> logger)
        {
            _dbContext = dbContext;
            _localConnString = configuration.GetConnectionString("LocalDb");
            _logger = logger;
            _excelReader = new ExcelTransaccionesReader();
        }

        public async Task<ExcelCargaResultDTO> CargarExcel(IFormFile archivo)
        {
            var resultado = new ExcelCargaResultDTO();

            if (archivo == null || archivo.Length == 0)
            {
                resultado.Errores.Add("No se recibió ningún archivo.");
                return resultado;
            }

            var extension = Path.GetExtension(archivo.FileName)?.ToLowerInvariant();
            if (extension != ".xlsx")
            {
                resultado.Errores.Add("El archivo debe ser un Excel con extensión .xlsx.");
                return resultado;
            }

            ExcelLecturaDTO lectura;
            try
            {
                using var stream = archivo.OpenReadStream();
                lectura = _excelReader.Leer(stream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al leer el archivo Excel.");
                resultado.Errores.Add("No se pudo leer el archivo Excel: " + ex.Message);
                return resultado;
            }

            resultado.Errores.AddRange(lectura.Errores);

            if (!lectura.Success)
            {
                return resultado;
            }

            var mapaLS = await ObtenerMapaEstaciones();

            resultado.Filas = _excelReader.Transformar(lectura.Filas, mapaLS);
            resultado.Resumen = _excelReader.AgruparPorEstacion(resultado.Filas);
            resultado.Success = true;

            return resultado;
        }

        public string EncolarGuardado(IEnumerable<TransaccionProcesadaDTO> items)
        {
            var lista = items?.ToList() ?? new List<TransaccionProcesadaDTO>();
            if (!lista.Any())
            {
                return string.Empty;
            }

            var jobId = Guid.NewGuid();
            TransaccionesProcesadasJob.Inicializar(jobId, lista.Count);

            BackgroundJob.Enqueue<TransaccionesProcesadasJob>(job => job.Procesar(jobId, lista));

            return jobId.ToString();
        }

        public object ObtenerProgreso(Guid jobId)
        {
            return TransaccionesProcesadasJob.ObtenerProgreso(jobId);
        }

        public async Task<int> ObtenerConteo()
        {
            return await _dbContext.TransaccionesProcesadas.CountAsync();
        }

        public async Task Truncar()
        {
            await _dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE [dbo].[TransaccionesProcesadas]");
        }

        public async Task<byte[]> ExportarCsv()
        {
            var datos = await _dbContext.TransaccionesProcesadas
                .OrderBy(d => d.Id)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Id,IdEstacion,CR,LS,Nombre,FoliosTotales,FechaCarga");

            foreach (var d in datos)
            {
                sb.AppendLine(string.Join(",",
                    CsvHelper.Escapar(d.Id.ToString()),
                    CsvHelper.Escapar(d.IdEstacion.ToString()),
                    CsvHelper.Escapar(d.CR),
                    CsvHelper.Escapar(d.LS),
                    CsvHelper.Escapar(d.Nombre),
                    CsvHelper.EscaparComoTexto(d.FoliosTotales),
                    CsvHelper.Escapar(d.FechaCarga.ToString("yyyy-MM-dd HH:mm:ss"))
                ));
            }

            // UTF-8 con BOM para que Excel detecte la codificación correctamente.
            var contenido = Encoding.UTF8.GetBytes(sb.ToString());
            var preamble = Encoding.UTF8.GetPreamble();
            var bytes = new byte[preamble.Length + contenido.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(contenido, 0, bytes, preamble.Length, contenido.Length);

            return bytes;
        }

        // Construye el diccionario IdEstacion -> LS consultando la tabla local EstacionesMaestras.
        private async Task<Dictionary<int, string>> ObtenerMapaEstaciones()
        {
            var mapa = new Dictionary<int, string>();

            const string query = "SELECT IdEstacion, LS FROM EstacionesMaestras";

            using var conn = new SqlConnection(_localConnString);
            var filas = await conn.QueryAsync<EstacionLS>(query);

            foreach (var fila in filas)
            {
                mapa[fila.IdEstacion] = fila.LS ?? string.Empty;
            }

            return mapa;
        }

        private class EstacionLS
        {
            public int IdEstacion { get; set; }
            public string LS { get; set; }
        }
    }
}
