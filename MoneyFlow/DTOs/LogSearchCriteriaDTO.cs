namespace MoneyFlow.DTOs
{
    // Clase declarada como record por que: 
    public record LogSearchCriteriaDTO (
        DateTime? StartDate,
        DateTime? EndDate,
        string? LogLEvel,               // "ERR", "FTL", "INF", etc.
        string? SourceContains,         // Filtro por nombre de clase/source
        string? MEssageContains,        // Búsqueda full-text simple
        string? CorrelationId,          // Búsqueda por ID de correlación
        string? DispatchId,             // Filtro específico de tu dominio
        string? ErrorCode               //Filtrar por código de error específico
    );
}
