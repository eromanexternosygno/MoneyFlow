namespace MoneyFlow.DTOs
{
    public class AuditDTO
    {
        public int AuditId { get; set; }
        public int IdEstacion { get; set; }
        public string StatusName { get; set; }
        public string Folio {  get; set; }
        public string Comments { get; set; }
        public string MotiveAuditAdjustmentName { get; set; }
	    public string AuditType { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

    }
}
