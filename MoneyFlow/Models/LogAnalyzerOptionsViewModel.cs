namespace MoneyFlow.Models
{
    public class LogAnalyzerOptionsViewModel
    {
        public const string SectionName = "Loganalyzer";

        public string LogFilePath { get; set; } = string.Empty;
        public bool UseIndexing { get; set; } = true;
        public string IndexDatabasePath { get; set; } = "Data/LogIndex.sqlite";
        public int StreamingBufferSize { get; set; } = 81920;
        public int MaxResultsPerPage { get; set; } = 100;
        public int DefaultTimeWindowHours { get; set; } = 24;

        public DuplicateDetectionOptions DuplicateDetection { get; set; } = new();
        public CorrelationOptions Correlation { get; set; } = new();

        public class DuplicateDetectionOptions
        {
            public int DefaultTimeWindowSeconds { get; set; } = 5;
            public List<string> DefaultKeyFileds { get; set; } = new()
            {
                "DispatchId",
                "OrderNumber",
                "TransactionInternalReference"
            };

            public int MaxAllowedRetries { get; set; } = 1;
        }

        public class CorrelationOptions
        {
            public int DefaultMaxTimeSpanSeconds { get; set; } = 30;
            public int MinEventsInGroup { get; set; } = 2;
            public List<string> CorrelationFields { get; set; } = new()
            {
                "CorrelationId", "DispatchId","PumpId"
            };
        }
    }
}
