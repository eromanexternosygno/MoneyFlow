namespace MoneyFlow.DTOs
{
    public record CorrelationGroupDTO
    (
        Guid Id,
        string? CorrelationKey,
        List<LogEntryDTO> Events,
        DateTime FirstTimestamp,
        DateTime LastTimestamp,
        TimeSpan Duration,
        bool HasDuplicates,
        string? PrimaryDispatchId,
        string? PrimaryErrorCode
    );
}
