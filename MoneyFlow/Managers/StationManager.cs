using Dapper;
using Microsoft.Data.SqlClient;
using MoneyFlow.Context;
using MoneyFlow.Models;
using System.Data;

namespace MoneyFlow.Managers;

public class StationManager
{
    private readonly string _configuration;

    // This Class is a station manager and this is for: separating the logic from controller to manager
    public StationManager(IConfiguration configuration)
    {
        _configuration = configuration.GetConnectionString("StationsDb");
    }

    // function to get all stations for a user
    private SqlConnection Connection =>
        new SqlConnection(_configuration);

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
            using var conn = Connection;
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
}
