using DocumentFormat.OpenXml.Bibliography;
using MoneyFlow.DTOs;

namespace MoneyFlow.Interfaces
{
    public interface ILogIndexer
    {
        bool IsIndexAvailable { get; }
        Task<bool> BuildIndexAsync(string logFilePath, CancellationToken ct);
        Task<BookAuthor> UpdateIndexAsync(string logFilePath, CancellationToken ct);
        Task<PagedResult<LogEntryDTO>> QueryAsync(
            LogSearchCriteriaDTO criteria,
            int page,
            int pageSize,
            CancellationToken ct);
        Task<bool> IsIndexStaleAsync(string logFilePath, CancellationToken ct);
    }
}
