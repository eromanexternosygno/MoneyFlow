namespace MoneyFlow.DTOs
{
    public class VolumetricoCargaDTO
    {
        public bool Success { get; set; }
        public List<string> Errores { get; set; } = new();
        public int FilasLeidas { get; set; }
        public string NombreTabla { get; set; } = string.Empty;
        public List<string> ColumnasDetectadas { get; set; } = new();
        public int TotalConAclaracion { get; set; }
        public List<VolumetricoFilaDTO> FilasPreview { get; set; } = new();
    }
}
