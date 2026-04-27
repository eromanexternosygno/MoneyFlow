using MoneyFlow.DTOs;
using MoneyFlow.Models;

namespace MoneyFlow.Interfaces
{
    public interface IEventCorrelator
    {
        Task<List<CorrelationGroupViewModel>> CorrelateAsync(
            CorrelationCriteriaDTO criteria,
            CancellationToken ct
            );

        Task<CorrelationGroupViewModel> GetGroupByIdAsync(Guid groupId, CancellationToken ct);
    }
}
