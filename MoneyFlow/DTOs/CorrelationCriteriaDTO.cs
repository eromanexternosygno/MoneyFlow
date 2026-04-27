namespace MoneyFlow.DTOs
{
    public record CorrelationCriteriaDTO
    (
        DateTime Start,
        DateTime End,
        Guid? DispatchId,
        string? CorrelationIdFilter,
        string? EventType,
        int MinEventsInGroup = 2,
        int MaxTimeSpanSeconds = 30
    );
}
