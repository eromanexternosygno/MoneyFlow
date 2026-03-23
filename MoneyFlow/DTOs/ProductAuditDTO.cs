namespace MoneyFlow.DTOs
{
    public class ProductAuditDTO
    {
        public int ProductAuditInventoryId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public decimal ProductPrice { get; set; }
        public decimal TheoreticalInventory {  get; set; }
        public decimal ActualInventory { get; set; }
        public decimal InventoryDifference { get; set; }
        public decimal TheoreticalAmount { get; set; }
        public decimal ActualAmount { get; set; }
        public decimal DifferenceAmount { get; set; }
        public int AuditId { get; set; }
    }
}
