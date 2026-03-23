namespace MoneyFlow.DTOs
{
    public class ProductAuditDetailDTO
    {
        public int ProductAuditInventoryDetailId { get; set; }
        public int LocationId { get; set; }
        public string LocationName { get; set; }
        public decimal TheoreticalInventory {  get; set; }
        public decimal ActualInventory { get; set; }
        public int ProductAuditInventoryId { get; set; }
    }
}
