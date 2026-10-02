using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using MoneyFlow.DTOs;
using MoneyFlow.Interfaces;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MoneyFlow.Managers
{
    public class VolumetricoManager : IVolumetricoManager
    {
        // Conexión a la base de datos "Volumetric" (distinta de MoneyFlowDb).
        // Aquí viven [volumetric].[clarifications], [dbo].[dim_estaciones] y las
        // tablas temporales que este módulo crea/carga dinámicamente.
        private readonly string _volumetricConnString;
        private readonly ILogger<VolumetricoManager> _logger;

        private const string HojaFinal = "FINAL";

        public VolumetricoManager(IConfiguration configuration, ILogger<VolumetricoManager> logger)
        {
            _volumetricConnString = configuration.GetConnectionString("VolumetricDb");
            _logger = logger;
        }

        private SqlConnection VolumetricConnection => new SqlConnection(_volumetricConnString);

        public async Task<VolumetricoCargaDTO> CargarExcelAsync(IFormFile archivo, int mes, int año)
        {
            var resultado = new VolumetricoCargaDTO();

            if (archivo == null || archivo.Length == 0)
            {
                resultado.Errores.Add("No se recibió ningún archivo.");
                return resultado;
            }

            var extension = Path.GetExtension(archivo.FileName)?.ToLowerInvariant();
            if (extension != ".xlsx")
            {
                resultado.Errores.Add("El archivo debe ser un Excel con extensión .xlsx.");
                return resultado;
            }

            string nombreTabla;
            try
            {
                nombreTabla = SanitizarNombreTabla(Path.GetFileNameWithoutExtension(archivo.FileName));
            }
            catch (Exception ex)
            {
                resultado.Errores.Add(ex.Message);
                return resultado;
            }

            _logger.LogInformation("Volumetrico: cargando archivo {Archivo} -> tabla {Tabla} (mes {Mes}, año {Año})",
                archivo.FileName, nombreTabla, mes, año);

            List<ColumnaInfo> columnas;
            IXLWorksheet? hoja;

            // El MemoryStream y el workbook deben vivir hasta terminar de leer las filas.
            // Antes se declaraban dentro del try, por lo que se desechaban al salir del bloque
            // y fallaba "Cannot access a disposed object: XLWorksheetInternals".
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms);
            ms.Position = 0;

            using var workbook = new XLWorkbook(ms);

            try
            {
                hoja = workbook.Worksheets.FirstOrDefault(w =>
                           string.Equals(w.Name, HojaFinal, StringComparison.OrdinalIgnoreCase))
                       ?? workbook.Worksheets.FirstOrDefault(w =>
                           w.Name.Trim().EndsWith(HojaFinal, StringComparison.OrdinalIgnoreCase))
                       ?? (workbook.Worksheets.Any() ? workbook.Worksheet(1) : null);

                if (hoja == null)
                {
                    resultado.Errores.Add("El archivo no contiene hojas.");
                    return resultado;
                }

                _logger.LogInformation("Volumetrico: usando hoja '{Hoja}'", hoja.Name);

                columnas = LeerColumnas(hoja);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Volumetrico: error leyendo el archivo Excel");
                resultado.Errores.Add("No se pudo leer el archivo Excel: " + ex.Message);
                return resultado;
            }

            var colCR = BuscarColumna(columnas, "CR");
            var colDescripcion = BuscarColumna(columnas, "DESCRIPCION");
            var colAclaraciones = BuscarColumnaAclaraciones(columnas);
            var colEESS = BuscarColumna(columnas, "EESS");
            var colAuditor = BuscarColumna(columnas, "AUDITOR");

            if (colCR == null)
                resultado.Errores.Add("Falta la columna requerida: CR.");
            if (colDescripcion == null)
                resultado.Errores.Add("Falta la columna requerida: Descripcion.");
            if (colAclaraciones == null)
                resultado.Errores.Add("Falta la columna requerida: Aclaraciones Json (o Aclaraciones_Json).");

            if (resultado.Errores.Any())
                return resultado;

            // Construye el DataTable con las mismas columnas (todas como texto) y lee las filas.
            var dt = new DataTable();
            foreach (var col in columnas)
                dt.Columns.Add(col.SqlNombre, typeof(string));

            AdvertirColumnasSinEncabezado(hoja, columnas);

            int filasLeidas = 0;
            int totalConAclaracion = 0;
            var preview = new List<VolumetricoFilaDTO>();

            try
            {
                int lastRow = hoja.LastRowUsed()?.RowNumber() ?? 1;

                for (int r = 2; r <= lastRow; r++)
                {
                    var row = hoja.Row(r);

                    var valores = new object[columnas.Count];
                    bool vacia = true;

                    for (int i = 0; i < columnas.Count; i++)
                    {
                        string texto = CeldaATexto(row.Cell(columnas[i].Indice));
                        if (texto.Length > 0) vacia = false;
                        valores[i] = texto.Length == 0 ? DBNull.Value : texto;
                    }

                    if (vacia) continue;

                    dt.Rows.Add(valores);
                    filasLeidas++;

                    if (colAclaraciones != null)
                    {
                        string acl = CeldaATexto(row.Cell(colAclaraciones.Indice));
                        if (acl.Length > 0) totalConAclaracion++;
                    }

                    if (preview.Count < 50 && colCR != null && colDescripcion != null)
                    {
                        preview.Add(new VolumetricoFilaDTO
                        {
                            RowNumber = filasLeidas,
                            CR = CeldaATexto(row.Cell(colCR.Indice)),
                            EESS = colEESS != null ? CeldaATexto(row.Cell(colEESS.Indice)) : string.Empty,
                            Descripcion = CeldaATexto(row.Cell(colDescripcion.Indice)),
                            AclaracionesJson = colAclaraciones != null ? CeldaATexto(row.Cell(colAclaraciones.Indice)) : string.Empty,
                            Auditor = colAuditor != null ? CeldaATexto(row.Cell(colAuditor.Indice)) : string.Empty
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Volumetrico: error leyendo las filas del archivo {Archivo}", archivo.FileName);
                resultado.Errores.Add("Error al leer las filas del archivo: " + ex.Message);
                return resultado;
            }

            try
            {
                using var conn = VolumetricConnection;
                await conn.OpenAsync();

                var sbColumnas = new StringBuilder();
                foreach (var col in columnas)
                    sbColumnas.Append($"[{col.SqlNombre}] NVARCHAR(MAX) NULL,");
                sbColumnas.Length -= 1; // quita la última coma

                // Se elimina la tabla si ya existía (idempotente) y se crea de nuevo.
                await conn.ExecuteAsync(
                    $"IF OBJECT_ID('dbo.[{nombreTabla}]','U') IS NOT NULL DROP TABLE dbo.[{nombreTabla}]; " +
                    $"CREATE TABLE dbo.[{nombreTabla}] ({sbColumnas});");

                _logger.LogInformation("Volumetrico: tabla temporal {Tabla} creada con {N} columnas", nombreTabla, columnas.Count);

                using (var bulk = new SqlBulkCopy(conn))
                {
                    bulk.DestinationTableName = $"dbo.[{nombreTabla}]";
                    bulk.BatchSize = 500;
                    bulk.BulkCopyTimeout = 300;
                    foreach (DataColumn dc in dt.Columns)
                        bulk.ColumnMappings.Add(dc.ColumnName, dc.ColumnName);
                    await bulk.WriteToServerAsync(dt);
                }

                await conn.ExecuteAsync($"ALTER TABLE dbo.[{nombreTabla}] ADD [Row#] INT IDENTITY(1,1);");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Volumetrico: error creando/cargando la tabla {Tabla}", nombreTabla);
                resultado.Errores.Add("Error al crear o cargar la tabla temporal: " + ex.Message);
                return resultado;
            }

            resultado.Success = true;
            resultado.FilasLeidas = filasLeidas;
            resultado.NombreTabla = nombreTabla;
            resultado.ColumnasDetectadas = columnas.Select(c => c.SqlNombre).ToList();
            resultado.TotalConAclaracion = totalConAclaracion;
            resultado.FilasPreview = preview;

            _logger.LogInformation("Volumetrico: {Tabla} cargada con {Filas} filas", nombreTabla, filasLeidas);

            return resultado;
        }

        public async Task<VolumetricoProcesamientoDTO> ProcesarTransformacionesAsync(string nombreTabla, int mes, int año)
        {
            ValidarNombreTabla(nombreTabla);

            var resultado = new VolumetricoProcesamientoDTO();

            using var conn = VolumetricConnection;
            await conn.OpenAsync();

            bool tablaExiste = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @t",
                new { t = nombreTabla }) > 0;

            if (!tablaExiste)
            {
                resultado.Mensaje = $"La tabla temporal '{nombreTabla}' no existe.";
                return resultado;
            }

            bool clarificationsExiste = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'volumetric' AND TABLE_NAME = 'clarifications'") > 0;

            if (!clarificationsExiste)
            {
                resultado.Mensaje = "La tabla [volumetric].[clarifications] no existe en la base de datos.";
                return resultado;
            }

            string? colAclaraciones = await ObtenerColumnaAclaracionesAsync(conn, nombreTabla);
            if (colAclaraciones == null)
            {
                resultado.Mensaje = "No se encontró la columna de aclaraciones en la tabla temporal.";
                return resultado;
            }

            using var tx = conn.BeginTransaction();
            try
            {
                // 3.1 Columnas de control (idempotente).
                await AgregarColumnaSiNoExiste(conn, tx, nombreTabla, "stationId", "int");
                await AgregarColumnaSiNoExiste(conn, tx, nombreTabla, "Has_Clarification_SI_NO", "int");
                await AgregarColumnaSiNoExiste(conn, tx, nombreTabla, "Product_Id", "int");
                await AgregarColumnaSiNoExiste(conn, tx, nombreTabla, "Producto", "VARCHAR(100)");

                // 3.2 stationId desde dim_estaciones.
                await conn.ExecuteAsync($@"
                    UPDATE DD
                    SET DD.stationId = ES.CodGas
                    FROM dbo.[{nombreTabla}] DD
                    INNER JOIN [dbo].[dim_estaciones] ES ON ES.CrEstacion = DD.CR
                    WHERE DD.CR IS NOT NULL;", transaction: tx);

                // 3.3 Product_Id (maneja "MAGNA (LT)", "PREMIUM (LT)", "DIESEL (LT)" y sin sufijo).
                await conn.ExecuteAsync($@"
                    UPDATE dbo.[{nombreTabla}]
                    SET Product_Id = CASE
                        WHEN Descripcion LIKE '%MAGNA%' THEN 1
                        WHEN Descripcion LIKE '%PREMIUM%' THEN 2
                        ELSE 3
                    END;", transaction: tx);

                // 3.4 Producto.
                await conn.ExecuteAsync($@"
                    UPDATE dbo.[{nombreTabla}]
                    SET Producto = CASE
                        WHEN Product_Id = 1 THEN 'MAGNA'
                        WHEN Product_Id = 2 THEN 'PREMIUM'
                        ELSE 'DIESEL'
                    END;", transaction: tx);

                // 3.5 Has_Clarification_SI_NO (columna con nombre variable).
                await conn.ExecuteAsync($@"
                    UPDATE dbo.[{nombreTabla}]
                    SET Has_Clarification_SI_NO = CASE
                        WHEN [{colAclaraciones}] IS NOT NULL AND [{colAclaraciones}] != '' THEN 1
                        ELSE 0
                    END;", transaction: tx);

                // 4.1 Limpiar tabla destino.
                await conn.ExecuteAsync("DELETE FROM [volumetric].[clarifications];", transaction: tx);

                // 4.2 Insertar desde la tabla temporal.
                await conn.ExecuteAsync($@"
                    INSERT INTO [volumetric].[clarifications]
                      ([StationCr],[ProductId],[ProductName],[Eds],[Month],[Year],
                       [HasClarification],[Description],[RegistrationDatetime],
                       [EvidencePath],[UploadType],[Id],[CreatedBy],[Created],
                       [LastModifiedBy],[LastModified])
                    SELECT
                      CR,
                      Product_Id,
                      Producto,
                      0,
                      @Month,
                      @Year,
                      Has_Clarification_SI_NO,
                      SUBSTRING([{colAclaraciones}], 1, 255),
                      GETDATE(),
                      '',
                      '',
                      [Row#],
                      'system',
                      GETDATE(),
                      NULL,
                      NULL
                    FROM dbo.[{nombreTabla}]
                    WHERE CR IS NOT NULL;", new { Month = mes, Year = año }, tx);

                resultado.RegistrosConAclaracion = await conn.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM dbo.[{nombreTabla}] WHERE Has_Clarification_SI_NO = 1;", transaction: tx);
                resultado.RegistrosInsertados = await conn.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM dbo.[{nombreTabla}] WHERE CR IS NOT NULL;", transaction: tx);
                resultado.RegistrosTransformados = await conn.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM dbo.[{nombreTabla}];", transaction: tx);

                tx.Commit();
                resultado.Success = true;
                resultado.Mensaje = $"Proceso completado: {resultado.RegistrosTransformados} registros transformados, " +
                                    $"{resultado.RegistrosInsertados} insertados, {resultado.RegistrosConAclaracion} con aclaración.";

                _logger.LogInformation("Volumetrico: {Tabla} procesada ({Insertados} insertados en clarifications)",
                    nombreTabla, resultado.RegistrosInsertados);
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger.LogError(ex, "Volumetrico: error procesando transformaciones de {Tabla}", nombreTabla);
                resultado.Mensaje = "Error al procesar: " + ex.Message;
            }

            return resultado;
        }

        public async Task<byte[]> GenerarScriptInsertsAsync(int clarificationIdInicio)
        {
            if (clarificationIdInicio < 1)
                throw new Exception("El ClarificationId inicial debe ser mayor o igual a 1.");

            using var conn = VolumetricConnection;
            await conn.OpenAsync();

            var filas = (await conn.QueryAsync<ClarificationRow>(@"
                SELECT [StationCr], [ProductId], [ProductName], [Eds],
                       [Month], [Year], [HasClarification], [Description],
                       [RegistrationDatetime], [EvidencePath], [UploadType],
                       [Id], [CreatedBy], [Created], [LastModifiedBy], [LastModified]
                FROM [volumetric].[clarifications]
                ORDER BY [ClarificationId];")).ToList();

            if (filas.Count > 0 && (long)clarificationIdInicio + filas.Count - 1 > int.MaxValue)
                throw new Exception("El rango de ClarificationId supera el valor máximo de int.");

            int ultimo = filas.Count == 0 ? clarificationIdInicio - 1 : clarificationIdInicio + filas.Count - 1;

            var sb = new StringBuilder();
            sb.AppendLine("-- ============================================================");
            sb.AppendLine("-- Inserts para [volumetric].[clarifications]");
            sb.AppendLine($"-- Filas exportadas: {filas.Count}");
            sb.AppendLine($"-- Rango de ClarificationId: {clarificationIdInicio} - {ultimo}");
            sb.AppendLine("-- Generado Automate volumetrico");
            sb.AppendLine("-- ============================================================");
            sb.AppendLine();

            if (filas.Count == 0)
            {
                sb.AppendLine("-- (No hay registros en [volumetric].[clarifications])");
            }
            else
            {
                sb.AppendLine($"IF EXISTS (SELECT 1 FROM [volumetric].[clarifications] WHERE [ClarificationId] >= {clarificationIdInicio})");
                sb.AppendLine($"    THROW 50000, 'Ya existen ClarificationId >= {clarificationIdInicio} en destino. No ejecutar.', 1;");
                sb.AppendLine();
                sb.AppendLine("SET IDENTITY_INSERT [volumetric].[clarifications] ON;");
                sb.AppendLine();

                int idActual = clarificationIdInicio;
                foreach (var f in filas)
                {
                    sb.AppendLine("INSERT INTO [volumetric].[clarifications]");
                    sb.AppendLine("  ([ClarificationId],[StationCr],[ProductId],[ProductName],[Eds],[Month],[Year],");
                    sb.AppendLine("   [HasClarification],[Description],[RegistrationDatetime],[EvidencePath],[UploadType],");
                    sb.AppendLine("   [Id],[CreatedBy],[Created],[LastModifiedBy],[LastModified])");
                    sb.Append("VALUES (");
                    sb.Append(SqlInt(idActual)).Append(", ");
                    sb.Append(SqlText(f.StationCr)).Append(", ");
                    sb.Append(SqlInt(f.ProductId)).Append(", ");
                    sb.Append(SqlText(f.ProductName)).Append(", ");
                    sb.Append(SqlInt(f.Eds)).Append(", ");
                    sb.Append(SqlInt(f.Month)).Append(", ");
                    sb.Append(SqlInt(f.Year)).Append(", ");
                    sb.Append(SqlBit(f.HasClarification)).Append(", ");
                    sb.Append(SqlText(f.Description)).Append(", ");
                    sb.Append(SqlDate(f.RegistrationDatetime)).Append(", ");
                    sb.Append(SqlText(f.EvidencePath)).Append(", ");
                    sb.Append(SqlText(f.UploadType)).Append(", ");
                    sb.Append(SqlInt(f.Id)).Append(", ");
                    sb.Append(SqlText(f.CreatedBy)).Append(", ");
                    sb.Append(SqlDate(f.Created)).Append(", ");
                    sb.Append(SqlText(f.LastModifiedBy)).Append(", ");
                    sb.Append(SqlDate(f.LastModified));
                    sb.AppendLine(");");
                    sb.AppendLine();
                    idActual++;
                }

                sb.AppendLine("SET IDENTITY_INSERT [volumetric].[clarifications] OFF;");
            }

            var contenido = Encoding.UTF8.GetBytes(sb.ToString());
            var preamble = Encoding.UTF8.GetPreamble();
            var bytes = new byte[preamble.Length + contenido.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(contenido, 0, bytes, preamble.Length, contenido.Length);

            return bytes;
        }

        public async Task<int> TruncarClarificationsAsync()
        {
            using var conn = VolumetricConnection;
            await conn.OpenAsync();

            bool existe = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'volumetric' AND TABLE_NAME = 'clarifications'") > 0;

            if (!existe)
                throw new Exception("La tabla [volumetric].[clarifications] no existe.");

            int previos = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM [volumetric].[clarifications];");
            await conn.ExecuteAsync("TRUNCATE TABLE [volumetric].[clarifications];");

            _logger.LogInformation("Volumetrico: clarifications truncada ({Registros} registros eliminados)", previos);

            return previos;
        }

        public async Task<List<VolumetricoTablaDTO>> ListarTablasVolumetricasAsync()
        {
            const string query = @"
                SELECT
                    t.name AS Name,
                    t.create_date AS CreateDate,
                    p.row_count AS [RowCount]
                FROM sys.tables t
                LEFT JOIN sys.dm_db_partition_stats p
                    ON p.object_id = t.object_id AND p.index_id IN (0, 1)
                WHERE t.name LIKE '%Volumetrico%'
                ORDER BY t.create_date DESC;";

            using var conn = VolumetricConnection;
            var tablas = await conn.QueryAsync<VolumetricoTablaDTO>(query);
            return tablas.ToList();
        }

        public async Task<bool> EliminarTablaTemporalAsync(string nombreTabla)
        {
            ValidarNombreTabla(nombreTabla);

            using var conn = VolumetricConnection;

            bool existe = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @t",
                new { t = nombreTabla }) > 0;

            if (existe)
            {
                await conn.ExecuteAsync($"DROP TABLE dbo.[{nombreTabla}];");
                _logger.LogInformation("Volumetrico: tabla temporal {Tabla} eliminada", nombreTabla);
            }

            return existe;
        }

        public async Task<int> ObtenerConteoRegistrosAsync(string nombreTabla)
        {
            ValidarNombreTabla(nombreTabla);

            using var conn = VolumetricConnection;

            bool existe = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @t",
                new { t = nombreTabla }) > 0;

            if (!existe)
                return 0;

            return await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.[{nombreTabla}];");
        }

        // ----- Helpers -----

        private List<ColumnaInfo> LeerColumnas(IXLWorksheet hoja)
        {
            int lastColumn = hoja.LastColumnUsed()?.ColumnNumber() ?? 0;

            var columnas = new List<ColumnaInfo>();
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int c = 1; c <= lastColumn; c++)
            {
                var original = CeldaATexto(hoja.Cell(1, c));
                if (string.IsNullOrEmpty(original))
                    continue;

                var sqlNombre = SanitizarNombreColumna(original);
                var final = sqlNombre;
                int n = 2;
                while (usados.Contains(final))
                {
                    final = $"{sqlNombre}_{n++}";
                }
                usados.Add(final);

                columnas.Add(new ColumnaInfo { Original = original, SqlNombre = final, Indice = c });
            }

            return columnas;
        }

        private void AdvertirColumnasSinEncabezado(IXLWorksheet hoja, List<ColumnaInfo> columnas)
        {
            int lastColumn = hoja.LastColumnUsed()?.ColumnNumber() ?? 0;
            int lastRow = hoja.LastRowUsed()?.RowNumber() ?? 1;

            var conEncabezado = new HashSet<int>(columnas.Select(c => c.Indice));
            var sinEncabezado = new List<string>();

            for (int c = 1; c <= lastColumn; c++)
            {
                if (conEncabezado.Contains(c)) continue;

                for (int r = 2; r <= lastRow; r++)
                {
                    if (CeldaATexto(hoja.Cell(r, c)).Length > 0)
                    {
                        sinEncabezado.Add(hoja.Cell(2, c).Address.ColumnLetter);
                        break;
                    }
                }
            }

            if (sinEncabezado.Count > 0)
                _logger.LogWarning(
                    "Volumetrico: se ignorarán columnas sin encabezado que contienen datos: {Columnas}",
                    string.Join(", ", sinEncabezado));
        }

        private static string NormalizarEncabezado(string nombre) => Regex.Replace(nombre, @"[\s_]", "");

        private static ColumnaInfo? BuscarColumna(List<ColumnaInfo> columnas, string nombre)
        {
            return columnas.FirstOrDefault(c =>
                string.Equals(NormalizarEncabezado(c.Original), nombre, StringComparison.OrdinalIgnoreCase));
        }

        private static ColumnaInfo? BuscarColumnaAclaraciones(List<ColumnaInfo> columnas)
        {
            return columnas.FirstOrDefault(c =>
            {
                string n = NormalizarEncabezado(c.Original);
                return string.Equals(n, "ACLARACIONESJSON", StringComparison.OrdinalIgnoreCase);
            });
        }

        private async Task<string?> ObtenerColumnaAclaracionesAsync(SqlConnection conn, string nombreTabla)
        {
            var columnas = await conn.QueryAsync<string>(
                "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @t",
                new { t = nombreTabla });

            return columnas.FirstOrDefault(c =>
                string.Equals(NormalizarEncabezado(c), "ACLARACIONESJSON", StringComparison.OrdinalIgnoreCase));
        }

        private async Task AgregarColumnaSiNoExiste(SqlConnection conn, SqlTransaction tx, string tabla, string columna, string tipoSql)
        {
            bool existe = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @t AND COLUMN_NAME = @c",
                new { t = tabla, c = columna }, tx) > 0;

            if (!existe)
            {
                await conn.ExecuteAsync($"ALTER TABLE dbo.[{tabla}] ADD [{columna}] {tipoSql};", transaction: tx);
            }
        }

        private string SanitizarNombreTabla(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
                throw new Exception("Nombre de archivo inválido para generar el nombre de tabla.");

            var sanitizado = Regex.Replace(nombre, @"[^a-zA-Z0-9_]", "_");
            sanitizado = Regex.Replace(sanitizado, @"_+", "_");
            sanitizado = sanitizado.Trim('_');

            if (string.IsNullOrWhiteSpace(sanitizado))
                throw new Exception("Nombre de tabla inválido.");

            return sanitizado;
        }

        private static void ValidarNombreTabla(string nombreTabla)
        {
            if (string.IsNullOrWhiteSpace(nombreTabla) || !Regex.IsMatch(nombreTabla, @"^[a-zA-Z0-9_]+$"))
                throw new Exception("Nombre de tabla inválido.");
        }

        private static string SqlText(string? s)
            => s is null ? "NULL" : $"N'{s.Replace("'", "''")}'";

        private static string SqlInt(int? i)
            => i is null ? "NULL" : i.Value.ToString(CultureInfo.InvariantCulture);

        private static string SqlBit(bool? b)
            => b is null ? "NULL" : (b.Value ? "1" : "0");

        private static string SqlDate(DateTime? d)
            => d is null ? "NULL" : $"'{d.Value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)}'";

        private static string SanitizarNombreColumna(string nombre)
        {
            var sanitizado = Regex.Replace(nombre.Trim(), @"[^a-zA-Z0-9_ ]", "_").Trim();
            return string.IsNullOrWhiteSpace(sanitizado) ? "Columna" : sanitizado;
        }

        private static string CeldaATexto(IXLCell celda)
        {
            var valor = celda.Value;

            if (valor.IsNumber)
            {
                double numero = valor.GetNumber();
                if (double.IsNaN(numero) || double.IsInfinity(numero))
                    return string.Empty;

                if (numero == Math.Truncate(numero))
                    return numero.ToString("0", CultureInfo.InvariantCulture);

                return numero.ToString(CultureInfo.InvariantCulture);
            }

            if (valor.IsText)
                return celda.GetString()?.Trim() ?? string.Empty;

            return celda.GetFormattedString()?.Trim() ?? string.Empty;
        }

        private class ColumnaInfo
        {
            public string Original { get; set; } = string.Empty;
            public string SqlNombre { get; set; } = string.Empty;
            public int Indice { get; set; }
        }

        private class ClarificationRow
        {
            public string StationCr { get; set; } = string.Empty;
            public int ProductId { get; set; }
            public string? ProductName { get; set; }
            public int? Eds { get; set; }
            public int Month { get; set; }
            public int Year { get; set; }
            public bool HasClarification { get; set; }
            public string? Description { get; set; }
            public DateTime RegistrationDatetime { get; set; }
            public string? EvidencePath { get; set; }
            public string? UploadType { get; set; }
            public int Id { get; set; }
            public string CreatedBy { get; set; } = string.Empty;
            public DateTime Created { get; set; }
            public string? LastModifiedBy { get; set; }
            public DateTime? LastModified { get; set; }
        }
    }
}

