using Microsoft.AspNetCore.Http;
using MoneyFlow.DTOs;

namespace MoneyFlow.Interfaces
{
    public interface ITransaccionesProcesadasManager
    {
        // Lee y transforma un archivo Excel devolviendo un preview listo para guardar.
        Task<ExcelCargaResultDTO> CargarExcel(IFormFile archivo);

        // Encola un job de Hangfire para insertar las transacciones en segundo plano.
        // Devuelve el id del job (Guid) para poder consultar su progreso.
        string EncolarGuardado(IEnumerable<TransaccionProcesadaDTO> items);

        // Consulta el progreso de un job de inserción.
        object ObtenerProgreso(Guid jobId);

        // Devuelve el total de registros insertados en TransaccionesProcesadas.
        Task<int> ObtenerConteo();

        // Elimina (trunca) todos los registros de TransaccionesProcesadas.
        Task Truncar();

        // Exporta los registros insertados en formato CSV.
        Task<byte[]> ExportarCsv();
    }
}
