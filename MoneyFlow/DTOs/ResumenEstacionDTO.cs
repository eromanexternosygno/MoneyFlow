namespace MoneyFlow.DTOs
{
    // Resumen agrupado por estación para mostrar un preview ligero en la UI.
    public class ResumenEstacionDTO
    {
        public int IdEstacion { get; set; }
        public string CR { get; set; }
        public string LS { get; set; }
        public string Nombre { get; set; }
        public int TotalFolios { get; set; }
        public string FoliosMuestra { get; set; }
    }
}
