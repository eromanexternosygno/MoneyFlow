using MoneyFlow.DTOs;

namespace MoneyFlow.Models
{
    public class AuditDetailViewModel
    {
        public string Station {  get; set; }
        public int AuditId { get; set; }

        public List<ProductAuditDTO> Inventory { get; set; }
        public List<ProductAuditDetailDTO> Details { get; set; }
    }
}
