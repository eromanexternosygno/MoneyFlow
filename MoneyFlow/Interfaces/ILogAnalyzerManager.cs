using MoneyFlow.DTOs;

namespace MoneyFlow.Interfaces
{
    public interface ILogAnalyzerManager
    {
        Task<PagedResult<LogEntryDTO>> SearchLogsAsync(
            LogSearchCriteria criteria,
            int page,
            int pageSize,
            CancellationToken ct
            );

        Task<List<CorrelationGroupDTO>> AnalyzeCorrelationsAsync(
            CorrelationCriteriaDTO criteria,
            CancellationToken ct
            );

        Task<List<DuplicateAnalysisDTO>> FindDuplicatesAsync(
            DuplicateDetectionConfigDTO config,
            CancellationToken ct
            );

        Task<LogAnalyzerSummaryDTO> GetSummaryAsync(
            DateTime start,
            DateTime end,
            CancellationToken ct
            );

        Task<bool> RebuildIndexAsync(string logFilePath, CancellationToken ct);
    }
}
