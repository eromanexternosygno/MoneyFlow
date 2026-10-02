namespace MoneyFlow.Models
{
    public class VolumetricoViewModel
    {
        public int Mes { get; set; } = DateTime.Now.Month;
        public int Año { get; set; } = DateTime.Now.Year;
        public string NombreArchivo { get; set; } = string.Empty;
        public string NombreTabla { get; set; } = string.Empty;
        public int TotalFilas { get; set; }
        public int TotalConAclaracion { get; set; }
        public List<string> TablasExistentes { get; set; } = new();
    }
}
