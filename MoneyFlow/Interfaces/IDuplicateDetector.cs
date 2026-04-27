using MoneyFlow.DTOs;

namespace MoneyFlow.Interfaces
{
    public interface IDuplicateDetector
    {
        Task<List<DuplicateAnalysisDTO>> FindDuplicatesAsync(
            DuplicateDetectionConfigDTO config,
            CancellationToken ct
            );

        Task<bool> IsPotentialDuplicateAsync(
            string keyFieldValue,
            string keyFieldName,
            DateTime timestamp,
            TimeSpan timeWindow,
            CancellationToken ct
            );
    }
}
