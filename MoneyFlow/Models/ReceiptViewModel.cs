namespace MoneyFlow.Models
{
    public class ReceiptViewModel
    {
        public int ReceiptId { get; set; }

        public int RecordTypeId { get; set; }
        public string Description { get; set; }
        public decimal Quantity { get; set; }
        public byte ReceiptStatusId { get; set; }
        public int StatusId { get; set; }
        public int POId { get; set; }
        public string Notes { get; set; }
        public bool IsFuel { get; set; }
        public string CreatedBy { get; set; }
        public DateTime Created { get; set; }
        public string LastModifiedBy { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime CancellationDate  { get; set; }
        public string InventoryAssignationType { get; set; } //field nvarchar(5)
        public bool IsProcessed { get; set; }
    }
}
