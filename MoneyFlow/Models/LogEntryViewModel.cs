namespace MoneyFlow.Models
{
    public record LogEntryViewModel
    (
        DateTime Timestamp,
        string Level,
        string Source,
        string Message,
        string? ExceptionType,
        string? ErrorCode,
        Guid? CorrelationId,
        Guid? DispatchId,
        string? OrderNumber,
        string? TransactionReference,
        string? PumpId,
        string RawLine

    )
    {
        public bool IsError => Level is "ERR" or "FTL";
        public bool IsWarning => Level == "WRN";
        public bool HasDispatchId => DispatchId.HasValue;
        public bool HasCorrelationId => CorrelationId.HasValue;
    }
}
