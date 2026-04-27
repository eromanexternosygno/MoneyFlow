using Microsoft.Extensions.Options;
using MoneyFlow.Interfaces;
using MoneyFlow.Models;

namespace MoneyFlow.Managers
{
    public class SqliteLogIndexerManager : ILogIndexer
    {
        private readonly string _dbPath;
        private readonly ILogParser _logParser;
        private readonly LogAnalyzerOptionsViewModel _options;
        private readonly ILogger<SqliteLogIndexerManager> _logger;

        private const string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS LogIndex (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Level TEXT NOT NULL,
                Source TEXT,
                ErrorCode TEXT,
                CorrelationId TEXT,
                DispatchId TEXT,
                OrderNumber TEXT,
                FileOffset INTEGER NOT NULL,
                FileLength INTEGER NOT NULL,
                IndexedAt TEXT NOT NULL
            );
            
            CREATE INDEX IF NOT EXISTS IX_LogIndex_Timestamp ON LogIndex(Timestamp);
            CREATE INDEX IF NOT EXISTS IX_LogIndex_DispatchId ON LogIndex(DispatchId) WHERE DispatchId IS NOT NULL;
            CREATE INDEX IF NOT EXISTS IX_LogIndex_ErrorCode ON LogIndex(ErrorCode) WHERE ErrorCode IS NOT NULL;
            CREATE INDEX IF NOT EXISTS IX_LogIndex_CorrelationId ON LogIndex(CorrelationId) WHERE CorrelationId IS NOT NULL;
        ";

        // Constructor
        public SqliteLogIndexerManager(
            ILogParser parser,
            IOptions<LogAnalyzerOptionsViewModel> options,
            ILogger<SqliteLogIndexerManager> logger)
        {
            _logParser = parser;
            _options = options.Value;
            _logger = logger;
            _dbPath = _options.IndexDatabasePath;

            // Asegurar que el directorio existe
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public bool IsIndexAvailable => File.Exists(_dbPath);

    }
}
