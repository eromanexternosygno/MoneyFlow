using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using MoneyFlow.Context;
using MoneyFlow.DTOs;
using MoneyFlow.Entities;
using MoneyFlow.Interfaces;
using MoneyFlow.Models;
using System.Data;
using static ClosedXML.Excel.XLPredefinedFormat;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MoneyFlow.Managers;

public class StationManager : IStationManager
{
    private readonly string _stationsConnString;
    private readonly string _localConnString;
    private readonly ILogger<StationManager> _logger;
    private static readonly Dictionary<Guid, (int current, int total)> ProgressTracker = new();

    // Constructor: Inyectamos el connection string y el logger
    public StationManager(IConfiguration configuration, ILogger<StationManager> logger)
    {
        _stationsConnString = configuration.GetConnectionString("StationsDb");
        _localConnString = configuration.GetConnectionString("LocalDb");
        _logger = logger;
    }

    // Propiedad para la base de datos de Estaciones (Linked Server)
    private SqlConnection StationsConnection => new SqlConnection(_stationsConnString);
    // Propiedad para la base de datos local (MoneyFlowDb)
    private SqlConnection LocalConnection => new SqlConnection(_localConnString);

    // function to get all stations for a user
    public async Task<IEnumerable<StationViewModel>> SearchStations(string search)
    {
        var query = @"
        SELECT 
            ER.IdEstacion,
            ER.cr AS CR,
            REPLACE(RE.nombre,' ','') AS LS,
            ER.nombre AS Nombre,
            RE.Estacion,
            RE.ACTIVO AS Activo
        FROM oxxogas..EstacionesReportes ER
        INNER JOIN oxxogas..relacionestaciones RE 
            ON ER.IdEstacion = RE.EstacionID
        WHERE RE.ACTIVO = 1
        AND (
            ER.Nombre LIKE '%' + @search + '%' OR
            ER.cr LIKE '%' + @search + '%'
        )
        ORDER BY ER.Nombre DESC
        ";

        try
        {
            using var conn = StationsConnection;
            return await conn.QueryAsync<StationViewModel>(query, new
            {
                search = $"%{search}%"
            });
        }
        catch (Exception ex)
        {

            throw new Exception("Error while searching into Stations" + ex.Message);
        }
    }

    // function to get all receptions for station
    public async Task<IEnumerable<ReceiptViewModel>> GetReceipts(string ls, string receiptIds = null)
    {
        // 1. Validaciones de seguridad
        if (string.IsNullOrEmpty(ls) || ls.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
            throw new Exception("Nombre de instancia inválida");

        // 2. Base de la consulta
        string selectClause = $@"
    SELECT 
        R.ReceiptId, R.RecordTypeId, RT.Description, R.Quantity, 
        R.ReceiptStatusId, R.StatusId, R.POId, R.Notes, R.IsFuel, 
        R.CreatedBy, R.Created, R.LastModifiedBy, R.LastModified, 
        R.CancellationDate, R.InventoryAssignationType
    FROM [{ls}].gaxpos.[Purchase].[Receipt] R
    INNER JOIN [{ls}].gaxpos.[Catalog].[RecordType] RT
        ON R.RecordTypeId = RT.RecordTypeId";

        string orderBy = " ORDER BY R.ReceiptId DESC";

        try
        {
            using var conn = StationsConnection;
            using var connLocal = LocalConnection;

            // Definimos una variable para guardar los resultados de la DB remota
            IEnumerable<ReceiptViewModel> receipts;

            // 3. Lógica Condicional para obtener los datos remotos
            if (string.IsNullOrEmpty(receiptIds))
            {
                receipts = await conn.QueryAsync<ReceiptViewModel>(selectClause + orderBy);
            }
            else
            {
                var idsList = receiptIds.Split(',')
                    .Select(s => int.TryParse(s.Trim(), out int id) ? id : (int?)null)
                    .Where(i => i.HasValue)
                    .Select(i => i.Value)
                    .ToList();

                if (!idsList.Any())
                {
                    receipts = await conn.QueryAsync<ReceiptViewModel>(selectClause + orderBy);
                }
                else
                {
                    string finalQuery = selectClause + " WHERE R.ReceiptId IN @Ids" + orderBy;
                    receipts = await conn.QueryAsync<ReceiptViewModel>(finalQuery, new { Ids = idsList });
                }
            }

            // --- NUEVA LÓGICA DE CONCILIACIÓN LOCAL ---

            // 4. Traemos los POIds que ya existen en nuestra tabla histórica local para esta instancia
            // Usamos DISTINCT para que la lista sea más ligera
            var processedPOIds = await connLocal.QueryAsync<int>(
                "SELECT DISTINCT POId FROM CorrectionHistory WHERE Instance = @ls",
                new { ls });

            // Convertimos a HashSet para que la búsqueda sea ultra rápida (O(1))
            var processedSet = new HashSet<int>(processedPOIds);

            // 5. Marcamos los recibos procesados
            foreach (var item in receipts)
            {
                if (processedSet.Contains(item.POId))
                {
                    item.IsProcessed = true;
                }
            }

            return receipts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consultando recibos en {Instance}", ls);
            throw new Exception($"Error en la instancia remota: {ex.Message}");
        }
    }

    // 1.- Consultar detalles de PO
    public async Task<IEnumerable<POVViewModel>> GetDetailsFromPO(string ls, List<int> poIds)
    {
        if (poIds == null || !poIds.Any()) return new List<POVViewModel>();


        string query = $@"
        SELECT 
            POId,NumOC,Subtotal,Tax,Total,[Status],CreatedBy,Created,CarrierName,Clave,FromERP,Remission,RemissionDate,StationId
        FROM [{ls}].gaxpos.[Purchase].[PO]
        WHERE POId IN @Ids";
        try
        {
            using var conn = StationsConnection;
            return await conn.QueryAsync<POVViewModel>(query, new { Ids = poIds });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consultando detalles de PO en {Instance}", ls);
            throw new Exception($"Error en la instancia remota: {ex.Message}");
        }
    }

    // 2.- Actualizar remisiones con TransactionScope
    public async Task<int> UpdateSpecificRemissions(string ls, List<RemissionPair> updates)
    {
        // 1. Abrimos la conexión normalmente
        using var conn = StationsConnection;
        await conn.OpenAsync(); // Es mejor usar el Open asíncrono si ya estamos en un Task

        try
        {
            int totalUpdated = 0;

            // Definimos la query
            string updateQuery = $@"
            UPDATE [{ls}].gaxpos.[Purchase].[PO]
            SET Remission = @Remission
            WHERE POId = @POId";

            // Ejecutamos cada actualización. 
            // SQL Server tratará cada comando como una transacción implícita.
            foreach (var update in updates)
            {
                int affectedRows = await conn.ExecuteAsync(updateQuery, new
                {
                    Remission = update.Remission,
                    POId = update.POId
                });
                totalUpdated += affectedRows;
            }

            return totalUpdated;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error actualizando remisiones en {Instance}", ls);
            // Al no haber 'transaction.Rollback()', simplemente lanzamos la excepción.
            // Los registros que se alcanzaron a actualizar antes del error se quedarán guardados.
            throw new Exception($"Error en la instancia remota: {ex.Message}");
        }
    }

    // 3.- Guardar histórico local
    public async Task SaveHistory(CorrectionHistory history)
    {
        // Partimos los strings por la coma
        //Convertimos history.POIds  en una lista de enteros para iterar fácilmente, aunque en la tabla local los guardaremos como string separado por comas para mantener la trazabilidad exacta de lo que se actualizó (en caso de que el orden importe)

        var ids =  history.POIds.Select(id => id.ToString()).ToArray(); // Convertimos a string para guardar como "1,2,3"
        var oldRems = history.OldRemission.Split(',');
        var newRems = history.NewRemission.Split(',');

        string insertQuery = @"
        INSERT INTO CorrectionHistory (Instance, POId, OldRemission ,NewRemission, AppliedAt, AppliedBy)
        VALUES (@Instance, @POId, @OldRemission ,@NewRemission, @AppliedAt, @AppliedBy)";

        using var conn = LocalConnection;
        await conn.OpenAsync();
        using var trans = conn.BeginTransaction();

        try
        {
            for (int i = 0; i < ids.Length; i++)
            {
                await conn.ExecuteAsync(insertQuery, new
                {
                    Instance = history.Instance,
                    POId = int.Parse(ids[i].Trim()), // Guardamos los IDs como string separado por comas
                    OldRemission = oldRems[i], // Aquí podrías mejorar obteniendo el valor anterior antes de actualizar, pero por simplicidad lo dejamos como N/A
                    NewRemission = newRems[i],
                    AppliedAt = System.DateTime.Now,
                    AppliedBy = "Admin" // Guardamos quién aplicó la corrección
                }, trans);
            }
            trans.Commit();
        }
        catch (Exception ex)
        {
            trans.Rollback();
            _logger.LogError(ex, "Error guardando desglose de histórico");
            throw;
        }
    }

    public object GetProgress(Guid searchId)
    {
        
        //Adaptamos para saber cuales fallaron y cuales no, para mostrar en el frontend
        // 1. Obtenemos los números del diccionario (el progreso en memoria)
        int current = 0;
        int total = 0;

        if (ProgressTracker.TryGetValue(searchId, out var progress))
        {
            current = progress.current;
            total = progress.total;
        }

        // 2. Consultamos la tabla de errores para ver CUÁLES fallaron
        // Esto es lo que usaremos en JS para poner el botón de "Reintentar"
        using var conn = LocalConnection;
        var failedLS = conn.Query<string>(
            "SELECT LS FROM BulkSearchErrors WHERE SearchId = @searchId",
            new { searchId }
        ).ToList();

        // 3. Devolvemos un objeto anónimo que Dapper/ASP.NET convertirá a JSON automáticamente
        return new
        {
            current = current,
            total = total,
            percentage = total > 0 ? (int)((double)current / total * 100) : 0,
            failedList = failedLS // Ejemplo: ["LS_ALFAREROS", "LS_SUR"]
        };
    }

    public async Task<Guid> ExecuteBulkSearch(List<StationFolioDTO> request, string user)
    {
        Guid searchId = Guid.NewGuid();
        var filteredRequest = request.Where(x => !string.IsNullOrEmpty(x.FoliosCsv)).ToList();
        int totalStations = filteredRequest.Count;
        int processedCount = 0;

        // Inicializamos el progreso
        ProgressTracker[searchId] = (0, totalStations);

        var semaphore = new SemaphoreSlim(15);
        var tasks = filteredRequest.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                // ... (Toda tu lógica de búsqueda e inserción que ya tenemos) ...
                var foliosList = item.FoliosCsv.Split(',')
                   .Select(f => f.Trim())
                   .Where(f => !string.IsNullOrEmpty(f))
                   .ToList();

                if (!foliosList.Any()) return;

                string remoteQuery = $@"
                DECLARE @CR_Remote VARCHAR(20), @Estacion_Remote VARCHAR(100);
                SELECT TOP 1 @CR_Remote = StationCROracle, @Estacion_Remote = @NombreEstacion
                FROM [{item.LS}].[GAXPOS].[System].[Stations];

                SELECT 
                    O.Folio AS Folio,
                    O.OrderId, 
                    O.[Type] AS Tipo, 
                    O.Total,
                    ISNULL(CONCAT(U.[Name], ' ' + U.LastName), 'Sin Nombre') AS NombreEmpleado,
                    CASE WHEN U.Disabled = 1 THEN 'Deshabilitado' ELSE 'Habilitado' END AS EstadoEmpleado,
                    O.EmployeeNumber AS EmpleadoEstacion,
                    ISNULL(R.Name, 'Sin Rol') AS RoleName,
                    @CR_Remote AS CR, 
                    @Estacion_Remote AS Estacion, 
                    O.Created
                FROM [{item.LS}].[gaxpos].[Sale].[Order] O
                LEFT JOIN [{item.LS}].[GAXPOS].Security.Users U ON U.EmployeeNumber = O.EmployeeNumber
                LEFT JOIN [{item.LS}].[GAXPOS].Security.Roles R ON R.Id = U.RoleId
                WHERE O.Folio IN @Folios";

                IEnumerable<dynamic> remoteResults;
                using (var connStations = StationsConnection)
                {
                    remoteResults = await connStations.QueryAsync(remoteQuery, new
                    {
                        Folios = foliosList,
                        NombreEstacion = item.Nombre
                    }, commandTimeout: 60);
                }

                if (remoteResults != null && remoteResults.Any())
                {
                    using var connLocal = LocalConnection;
                    string insertQuery = @"
                    INSERT INTO LocalBulkSearchResults (Folio, OrderId, Tipo, Total, NombreEmpleado, EstadoEmpleado, 
                                                     EmpleadoEstacion, RoleName, CR, Estacion, Created, SearchId, UserExecution)
                    VALUES (@Folio, @OrderId, @Tipo, @Total, @NombreEmpleado, @EstadoEmpleado, 
                            @EmpleadoEstacion, @RoleName, @CR, @Estacion, @Created, @SearchId, @UserExecution)";

                    await connLocal.ExecuteAsync(insertQuery, remoteResults.Select(r => new {
                        r.Folio,
                        r.OrderId,
                        r.Tipo,
                        r.Total,
                        r.NombreEmpleado,
                        r.EstadoEmpleado,
                        r.EmpleadoEstacion,
                        r.RoleName,
                        r.CR,
                        r.Estacion,
                        r.Created,
                        SearchId = searchId,
                        UserExecution = user
                    }));
                }
                // Al finalizar la inserción de esta estación, actualizamos el contador
                //Interlocked.Increment(ref processedCount);
                //ProgressTracker[searchId] = (processedCount, totalStations);
                _logger.LogInformation($"Estacion: {item.LS} procesada-.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en {item.LS}: {ex.Message}");
                // Almacenamos en BulkError si quieres tener un histórico de qué estaciones fallaron y por qué
                 await Task.Run(async () =>
                {
                    using var connLocal = LocalConnection;
                    string errorInsertQuery = @"
                    INSERT INTO BulkSearchErrors(SearchId, LS, NombreEstacion, ErrorMsg)
                    VALUES(@searchId, @Instance, @nombre, @error)";
                    await connLocal.ExecuteAsync(errorInsertQuery, new
                    {
                        searchId = searchId,
                        Instance = item.LS,
                        nombre = item.Nombre,
                        error = ex.Message
                    });
                });
            }
            finally
            {
                // Actualizar progreso después de cada estación (éxito o fallo)
                Interlocked.Increment(ref processedCount);
                ProgressTracker[searchId] = (processedCount, totalStations);
                semaphore.Release();
            }
        });

        // IMPORTANTE: No bloquees el hilo principal aquí si quieres que GetProgress responda.
        // Task.WhenAll se encargará de correr todo.
        _ = Task.Run(async () => {
            await Task.WhenAll(tasks);
            // Opcional: Remover del tracker tras 5 minutos de haber terminado para no saturar memoria
            await Task.Delay(TimeSpan.FromMinutes(5));
            ProgressTracker.Remove(searchId);
        });

        return searchId;
    }

    public async Task<byte[]> ExportResultsToExcel(Guid searchId)
    {
        using var connLocal = LocalConnection;

        // Consultamos la tabla local
        string query = @"
        SELECT Folio, OrderId, Tipo, Total, NombreEmpleado, EstadoEmpleado, 
               EmpleadoEstacion, RoleName, CR, Estacion, Created
        FROM LocalBulkSearchResults 
        WHERE SearchId = @searchId
        ORDER BY Estacion, Created DESC";

        var results = await connLocal.QueryAsync<BulkSearchResultDTO>(query, new { searchId });

        if (!results.Any()) throw new Exception("No hay datos para exportar.");

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Resultados Masivos");

            // Insertamos los datos y ClosedXML crea la tabla automáticamente
            var table = worksheet.Cell(1, 1).InsertTable(results);

            // Formatos estéticos
            worksheet.Columns().AdjustToContents();
            worksheet.Column(4).Style.NumberFormat.Format = "$ #,##0.00"; // Columna Total
            worksheet.Column(11).Style.NumberFormat.Format = "dd/MM/yyyy HH:mm:ss"; // Columna Created

            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                return stream.ToArray();
            }
        }
    }

    // Método para consultar los datos de la estación en local (IdEstacion, CR, LS, Nombre) dado un Id de estación específico.
    // Esto es útil para mostrar detalles en el frontend o para validar que la estación existe antes de hacer otras operaciones 
    public async Task<IEnumerable<StationViewModel>> GetStationMetadata(List<int> ids)
    {
        string query = @"
        SELECT 
            IdEstacion,
            cr AS CR,
            REPLACE(LS,' ','') AS LS,
            nombre AS Nombre,
            IdEstacion AS Estacion,
            ACtiva AS Activo
        FROM EstacionesMaestras
        WHERE IdEstacion IN @ids
        ORDER BY Nombre DESC";

        using var conn = LocalConnection;
        // Dapper ejecutará la consulta de forma asíncrona
        return await conn.QueryAsync<StationViewModel>(query, new { ids });
    }

    //object IStationManager.GetStationMetadata(List<int> ids)
    //{
    //    return GetStationMetadata(ids);
    //}
}
