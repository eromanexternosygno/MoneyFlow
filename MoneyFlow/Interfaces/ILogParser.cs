using MoneyFlow.DTOs;

namespace MoneyFlow.Interfaces
{
    public interface ILogParser
    {
        LogEntryDTO? Parse(string logLine);
        bool CanParse(string logLine);
    }
}
