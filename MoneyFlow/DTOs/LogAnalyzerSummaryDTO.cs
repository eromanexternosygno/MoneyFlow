namespace MoneyFlow.DTOs
{
    public record LogAnalyzerSummaryDTO
    (
        int TotalLogsAnalyzed,
        int TotalErrors,
        int TotalWarnings,
        int DuplicateGroupsCount,
        double AvgProcessingTimeMs,
        Dictionary<string , int> ErrorsByCode,
        Dictionary<string, int> ErrorsBySource,
        List<LogEntryDTO> RecentCriticalErrors
        );
}
