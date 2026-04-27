using MoneyFlow.DTOs;

namespace MoneyFlow.Models
{
    public record CorrelationGroupViewModel
    (
        Guid Id,
        string? CorrelationKey,
        List<LogEntryViewModel> Events,
        DateTime FirstTimestamp,
        DateTime LastTimestamp
        )
    {
        public TimeSpan Duration => LastTimestamp - FirstTimestamp;
        public bool HasDuplicates => Events.GroupBy(e => e.DispatchId)
            .Any(g => g.Count() > 1);
        public LogEntryViewModel? FirstError => Events.FirstOrDefault(e => e.IsError);
        public string? PrimaryDispatchId => Events.FirstOrDefault(e => e.HasDispatchId)?.DispatchId?.ToString();
    }
}
