namespace MoneyFlow.DTOs
{
    public record DuplicateDetectionConfigDTO(
        DateTime StartTime,
        DateTime EndTime,
        string KeyFieldName, // "DispatchId", "OrderNumber", etc.
        int TimeWindowSeconds = 5,
        int MiniOcurrences = 2,
        string? StationFilter = null
    );
}
