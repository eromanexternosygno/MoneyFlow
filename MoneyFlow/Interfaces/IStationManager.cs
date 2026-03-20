using MoneyFlow.DTOs;
using MoneyFlow.Models;

namespace MoneyFlow.Interfaces
{
    public interface IStationManager
    {
        Task<IEnumerable<StationViewModel>> SearchStations(string search);
        // Get all receptions for station
        Task<IEnumerable<ReceiptViewModel>> GetReceipts(string ls, string receiptIds = null);

        // Nuevos métodos: PO
        // 1.- Obtiene los datos reales de la tabla [Purchase].[PO] de la instancia dada, filtrando por los poIds proporcionados
        Task<IEnumerable<POVViewModel>> GetDetailsFromPO(string ls, List<int> poIds);

        // 2.- Ejecuta los UPDATE uno por uno en la instancia remota
        Task<int> UpdateSpecificRemissions(string ls, List<RemissionPair> updates);

        // 3.- Guarda el registro en base de datos local con el histórico de correcciones
        Task SaveHistory(CorrectionHistory history);


        // New Function Folios
        Task<Guid> ExecuteBulkSearch(List<StationFolioDTO> request, string user);
        Task<byte[]> ExportResultsToExcel(Guid searchId);
        // NUEVO: Método para consultar el avance de la búsqueda
        object GetProgress(Guid searchId);

        // Método para obtener los datos metadata de las estaciones ne local de mi tabla StationMetadata, filtrando por el nombre de la estación o cualquier otro criterio relevante
        //Task<IEnumerable<StationViewModel>> GetStationMetadata(string search);
        Task<IEnumerable<StationViewModel>> GetStationMetadata(List<int> ids);
    }
}
