namespace MoneyFlow.Entities
{
    public class TransaccionProcesada
    {
        public int Id { get; set; }
        public int IdEstacion { get; set; }
        public string CR { get; set; }
        public string LS { get; set; }
        public string Nombre { get; set; }
        public string FoliosTotales { get; set; }
        public DateTime FechaCarga { get; set; }
    }
}
