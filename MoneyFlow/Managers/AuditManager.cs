using Dapper;
using Microsoft.Data.SqlClient;
using MoneyFlow.DTOs;
using MoneyFlow.Interfaces;
using MoneyFlow.Models;
using System.Data;

namespace MoneyFlow.Managers
{
    public class AuditManager : IAuditManager
    {
       private readonly IConfiguration _stationConfig; //Esto es para:
                                                        
        private readonly ILogger<AuditManager> _logger;

        //Constructor
        public AuditManager(IConfiguration configuration, ILogger<AuditManager> logger)
        {
            _logger = logger;
            _stationConfig = configuration;
        }

        private IDbConnection GetConnection()
        {
            return new SqlConnection(_stationConfig.GetConnectionString("StationsDb"));
        }

        public async Task<List<AuditDTO>> GetAudits(string station)
        {
            using var conn = GetConnection();

            var sqlAudit = $@"
            SELECT AuditId, StationId, StatusName, Folio, Comments, MotiveAuditAdjustmentName, AuditType,
            StartDate, EndDate
                    FROM [{station}].[GAXPOS].[Inventory].[Audit]
                    ORDER BY Created DESC";
            
            return (await conn.QueryAsync<AuditDTO>(sqlAudit)).ToList();
        }


        // 2. Obtener productos por auditoria
        public async Task<List<ProductAuditDTO>> GetproductsByAudit(string station, int auditId)
        {

            using var conn = GetConnection();

            var sql = $@"
            SELECT ProductAuditInventoryId, ProductId, ProductName,
                    TheoreticalInventory, ActualInventory, InventoryDifference
            FROM [{station}].[GAXPOS].[Inventory].[ProductAuditInventory]
            WHERE AuditId = @auditId";

            try
            {
                return (await conn.QueryAsync<ProductAuditDTO>(sql, new { auditId })).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consultando GetproductsByAudit en {Instance}", station);
                throw new Exception($"Error en la instancia remota: {ex.Message}");
            }
        }


        // 3. Obtener Detalle
        public async Task<List<ProductAuditDetailDTO>> GetAuditDetails(string station, List<int> ids)
        {
            using var conn = GetConnection();

            var idsString = string.Join(",", ids);

            var sql = $@"
                SELECT 
                    pd.ProductAuditInventoryDetailId,
                    pd.LocationId,
                    l.LocationName,
                    pd.TheoreticalInventory,
                    pd.ActualInventory,
                    pd.ProductAuditInventoryId
                FROM [{station}].[GAXPOS].[Inventory].[ProductAuditInventoryDetail] pd
                INNER JOIN [{station}].[GAXPOS].[Inventory].[Location] l 
                    ON pd.LocationId = l.LocationId
                WHERE pd.ProductAuditInventoryId IN @Ids
                ORDER BY pd.ProductAuditInventoryId DESC";

            try
            {
                return (await conn.QueryAsync<ProductAuditDetailDTO>(sql, new { Ids = ids })).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consultando GetAuditDetails en {Instance}", station);
                throw new Exception($"Error en la instancia remota: {ex.Message}");
            }
        }

        // 3. Get Full to print pdf
        public async Task<AuditDetailViewModel> GetFullAudit(string station, int auditId)
        {
            // 1. Obtener inventario (tabla principal)
            var inventory = await GetproductsByAudit(station, auditId);

            if (inventory == null || !inventory.Any())
            {
                _logger.LogWarning($"No inventory found for AuditId: {auditId}");
                return new AuditDetailViewModel
                {
                    AuditId = auditId,
                    Station = station,
                    Inventory = new List<ProductAuditDTO>(),
                    Details = new List<ProductAuditDetailDTO>()
                };
            }

            // 2. Obtener IDs para detalle
            var ids = inventory
                .Select(x => x.ProductAuditInventoryId)
                .Distinct()
                .ToList();

            // 3. Obtener detalle (movimientos por ubicación)
            var details = await GetAuditDetails(station, ids);

            // 4. Armar ViewModel
            var result = new AuditDetailViewModel
            {
                AuditId = auditId,
                Station = station,
                Inventory = inventory,
                Details = details
            };

            return result;
        }
    }
}
