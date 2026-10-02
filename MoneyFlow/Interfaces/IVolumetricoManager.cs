using Microsoft.AspNetCore.Http;
using MoneyFlow.DTOs;

namespace MoneyFlow.Interfaces
{
    public interface IVolumetricoManager
    {
        // Lee el Excel "FINAL", crea la tabla temporal en dbo y carga los datos con SqlBulkCopy.
        Task<VolumetricoCargaDTO> CargarExcelAsync(IFormFile archivo, int mes, int año);

        // Ejecuta las transformaciones (ALTER/UPDATE) e inserta en volumetric.clarifications.
        Task<VolumetricoProcesamientoDTO> ProcesarTransformacionesAsync(string nombreTabla, int mes, int año);

        // Genera un script .sql con un INSERT literal por cada fila de volumetric.clarifications.
        // El ClarificationId se calcula partiendo de clarificationIdInicio y se incrementa en +1 por fila.
        Task<byte[]> GenerarScriptInsertsAsync(int clarificationIdInicio);

        // Trunca (vacía) la tabla volumetric.clarifications y devuelve cuántos registros había.
        Task<int> TruncarClarificationsAsync();

        // Lista las tablas temporales volumétricas existentes (nombre, fecha, filas).
        Task<List<VolumetricoTablaDTO>> ListarTablasVolumetricasAsync();

        // Elimina una tabla temporal (DROP TABLE) con validación del nombre.
        Task<bool> EliminarTablaTemporalAsync(string nombreTabla);

        // Devuelve el total de registros de una tabla temporal.
        Task<int> ObtenerConteoRegistrosAsync(string nombreTabla);
    }
}
