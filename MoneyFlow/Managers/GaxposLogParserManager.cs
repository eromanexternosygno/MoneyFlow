using MoneyFlow.Interfaces;
using MoneyFlow.Models;
using System.Text.RegularExpressions;

namespace MoneyFlow.Managers
{
    public partial class GaxposLogParserManager : ILogParser
    {
        [GeneratedRegex(
            @"^(?<timestamp>\d{4}-\d{2}-\d{2}T[\d:.]+[-+]\d{2}:\d{2})\s+\[(?<level>\w{3})\]\s+\((?<source>[^)]+)\)\s+(?<message>.*)$",
            RegexOptions.Compiled)]

        private static partial Regex LogLinePattern();

        [GeneratedRegex(@"DispatchId[=:\s]*([a-f0-9\-]{36})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex DispatchIdPattern();

        [GeneratedRegex(@"CorrelationId[=:\s]*([a-f0-9\-]{36})", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex CorrelationIdPattern();

        [GeneratedRegex(@"ErrorCode[=:\s]*([^\s,;\]]+)", RegexOptions.Compiled)]
        private static partial Regex ErrorCodePattern();

        [GeneratedRegex(@"OrderNumber[=:\s]*(\d+)", RegexOptions.Compiled)]
        private static partial Regex OrderNumberPattern();

        public bool CanParse(string logLine) => LogLinePattern().IsMatch(logLine?.Trim() ?? string.Empty);

        public LogEntryViewModel? Parse(string logLine)
        {
            if (string.IsNullOrWhiteSpace(logLine)) return null;

            var match = LogLinePattern().Match(logLine.Trim());
            if (!match.Success) return null;

            var timestamp = DateTime.Parse(match.Groups["timestamp"].Value);
            var level = match.Groups["level"].Value;
            var source = match.Groups["source"].Value;
            var message = match.Groups["message"].Value;

            // Extraer campos específicos de tu dominio
            var dispatchId = ExtractGuid(DispatchIdPattern(), message);
            var correlationId = ExtractGuid(CorrelationIdPattern(), message);
            var errorCode = ErrorCodePattern().Match(message)?.Groups[1]?.Value;
            var orderNumber = OrderNumberPattern().Match(message)?.Groups[1]?.Value;

            // Extraer tipo de excepción si existe
            var exceptionType = ExtractExceptionType(message);

            return new LogEntryViewModel(
                Timestamp: timestamp,
                Level: level,
                Source: source,
                Message: message,
                ExceptionType: exceptionType,
                ErrorCode: errorCode,
                CorrelationId: correlationId,
                DispatchId: dispatchId,
                OrderNumber: orderNumber,
                TransactionReference: null, // Implementar si lo necesitas
                PumpId: null, // Implementar si lo necesitas
                RawLine: logLine
            );
        }

        private static Guid? ExtractGuid(Regex pattern, string text)
        {
            var match = pattern.Match(text);
            return match.Success && Guid.TryParse(match.Groups[1].Value, out var guid)
                ? guid
                : null;
        }

        private static string? ExtractExceptionType(string message)
        {
            // Busca patrones como: " ---> System.Net.Sockets.SocketException"
            var match = Regex.Match(message, @"-->\s*([\w.]+Exception)");
            return match.Success ? match.Groups[1].Value : null;
        }


    }
}
