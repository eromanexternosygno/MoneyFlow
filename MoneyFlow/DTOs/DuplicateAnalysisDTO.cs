namespace MoneyFlow.DTOs
{
    public record DuplicateAnalysisDTO
    (
        string KeyValue,
        string KeyFiledName,
        int Occurrences,
        DateTime FirstOccurrence,
        DateTime LastOccurrence,
        List<string> Sources,
        List<LogEntryDTO> OccurrencesList,
        string? SuggestedAction
        );
}
