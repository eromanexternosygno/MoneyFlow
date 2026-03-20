namespace MoneyFlow.DTOs
{
    public class BulkSearchResultDTO
    {
        public string Folio { get; set; }
        public Guid OrderId { get; set; }
        public string Tipo { get; set; }
        public decimal Total { get; set; }
        public string NombreEmpleado { get; set; }
        public string EstadoEmpleado { get; set; }
        public string EmpleadoEstacion { get; set; }
        public string RoleName { get; set; }
        public string CR { get; set; }
        public string Estacion { get; set; }
        public DateTime Created { get; set; }
    }
}
