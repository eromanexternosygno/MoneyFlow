namespace MoneyFlow.DTOs
{
    public class ExcelCargaResultDTO
    {
        public bool Success { get; set; }
        public List<string> Errores { get; set; } = new();
        public List<TransaccionProcesadaDTO> Filas { get; set; } = new();
        public List<ResumenEstacionDTO> Resumen { get; set; } = new();
    }
}
