namespace MoneyFlow.DTOs
{
    public record LogEntryDTO
    (
        DateTime Timestamp,
        string Level,
        string Source,
        string Message,
        string? ExceptionType,
        string? ErrorCode,
        string? CorrelationId,
        string? DispatchId,
        string? OrderNumber,
        string? RawLine = null
    );
}
