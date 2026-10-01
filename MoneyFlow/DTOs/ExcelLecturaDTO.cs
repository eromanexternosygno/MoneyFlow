namespace MoneyFlow.DTOs
{
    public class ExcelLecturaDTO
    {
        public List<FilaExcelTransaccionDTO> Filas { get; set; } = new();
        public List<string> Errores { get; set; } = new();
        public bool Success => Errores.Count == 0;
    }
}
