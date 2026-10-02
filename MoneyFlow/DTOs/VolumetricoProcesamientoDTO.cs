namespace MoneyFlow.DTOs
{
    public class VolumetricoProcesamientoDTO
    {
        public bool Success { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public int RegistrosTransformados { get; set; }
        public int RegistrosInsertados { get; set; }
        public int RegistrosConAclaracion { get; set; }
    }
}
