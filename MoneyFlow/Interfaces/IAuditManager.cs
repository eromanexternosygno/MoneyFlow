using MoneyFlow.DTOs;
using MoneyFlow.Models;

namespace MoneyFlow.Interfaces
{
    public interface IAuditManager 
    {
        // Methods
        Task<List<AuditDTO>> GetAudits(string station);

        Task<List<ProductAuditDTO>> GetproductsByAudit(string station, int auditId);

        Task<List<ProductAuditDetailDTO>> GetAuditDetails(string station, List<int> ids);
        Task<AuditDetailViewModel> GetFullAudit(string station, int auditId);
    }
}
