namespace MoneyFlow.Models
{
    public record DuplicatePatternViewModel
    (
        string KeyFieldName,
        string KeyValue,
        DateTime FirstSeen,
        DateTime LastSeen,
        int OccurrenceCount,
        List<string> UniqueSources,
        List<LogEntryViewModel> SampleEntries
    )
    {
        public bool IsWithinWindow(DateTime checkTime, TimeSpan window) =>
            checkTime >= FirstSeen.Add(-window) && checkTime <= LastSeen.Add(window);
    }
}
