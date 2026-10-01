using System.Globalization;
using ClosedXML.Excel;
using MoneyFlow.DTOs;

namespace MoneyFlow.Utilities
{
    // Clase de utilidad para leer archivos Excel y transformar sus filas
    // en DTOs listos para insertar. No depende de EF Core ni de la base de
    // datos, por lo que es 100% testeable de forma unitaria.
    public class ExcelTransaccionesReader
    {
        public const string ColumnaEstacionId = "EstacionId_1";
        public const string ColumnaCR = "CR_1";
        public const string ColumnaEstacion = "Estacion_1";
        public const string ColumnaFolio1 = "Folio_1";
        public const string ColumnaFolio2 = "Folio_2";

        public string[] ColumnasRequeridas => new[]
        {
            ColumnaEstacionId,
            ColumnaCR,
            ColumnaEstacion,
            ColumnaFolio1,
            ColumnaFolio2
        };

        // Lee un archivo Excel y devuelve las filas crudas junto con los
        // errores de validación de estructura (columnas requeridas).
        public ExcelLecturaDTO Leer(Stream stream)
        {
            var resultado = new ExcelLecturaDTO();

            // Copiamos a un MemoryStream para garantizar que sea seekable.
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;

            using var workbook = new XLWorkbook(ms);

            if (!workbook.Worksheets.Any())
            {
                resultado.Errores.Add("El archivo Excel no contiene hojas.");
                return resultado;
            }

            var hoja = workbook.Worksheet(1);

            int lastColumn = hoja.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastColumn == 0)
            {
                resultado.Errores.Add("El archivo Excel está vacío.");
                return resultado;
            }

            // Mapa de encabezado -> posición de columna (ignorando mayúsculas/minúsculas).
            var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int c = 1; c <= lastColumn; c++)
            {
                var header = LeerCeldaComoTexto(hoja.Row(1).Cell(c));
                if (!string.IsNullOrEmpty(header) && !headers.ContainsKey(header))
                {
                    headers[header] = c;
                }
            }

            var faltantes = ColumnasRequeridas.Where(col => !headers.ContainsKey(col)).ToList();
            if (faltantes.Any())
            {
                resultado.Errores.Add($"Faltan las columnas requeridas: {string.Join(", ", faltantes)}");
                return resultado;
            }

            int idxEstacionId = headers[ColumnaEstacionId];
            int idxCR = headers[ColumnaCR];
            int idxEstacion = headers[ColumnaEstacion];
            int idxFolio1 = headers[ColumnaFolio1];
            int idxFolio2 = headers[ColumnaFolio2];

            int lastRow = hoja.LastRowUsed()?.RowNumber() ?? 1;

            for (int r = 2; r <= lastRow; r++)
            {
                var row = hoja.Row(r);

                string estacionId = LeerCeldaComoTexto(row.Cell(idxEstacionId));
                string cr = LeerCeldaComoTexto(row.Cell(idxCR));
                string estacion = LeerCeldaComoTexto(row.Cell(idxEstacion));
                string folio1 = LeerCeldaComoTexto(row.Cell(idxFolio1));
                string folio2 = LeerCeldaComoTexto(row.Cell(idxFolio2));

                // Saltamos filas completamente vacías.
                if (string.IsNullOrEmpty(estacionId) &&
                    string.IsNullOrEmpty(cr) &&
                    string.IsNullOrEmpty(estacion) &&
                    string.IsNullOrEmpty(folio1) &&
                    string.IsNullOrEmpty(folio2))
                {
                    continue;
                }

                resultado.Filas.Add(new FilaExcelTransaccionDTO
                {
                    EstacionId = estacionId,
                    CR = cr,
                    Estacion = estacion,
                    Folio1 = folio1,
                    Folio2 = folio2
                });
            }

            return resultado;
        }

        // Transforma las filas crudas en DTOs finales:
        // - IdEstacion = EstacionId_1 (se descartan filas con id inválido)
        // - CR = CR_1
        // - LS  = valor del diccionario de mapeo IdEstacion -> LS
        // - Nombre = Estacion_1
        // - FoliosTotales = Folio_1 + Folio_2 separados por coma
        public List<TransaccionProcesadaDTO> Transformar(
            IEnumerable<FilaExcelTransaccionDTO> filas,
            IDictionary<int, string> mapaLS)
        {
            var resultado = new List<TransaccionProcesadaDTO>();

            foreach (var fila in filas)
            {
                if (string.IsNullOrWhiteSpace(fila.EstacionId) ||
                    !int.TryParse(fila.EstacionId.Trim(), out int idEstacion))
                {
                    continue;
                }

                var folios = new List<string>();
                if (!string.IsNullOrWhiteSpace(fila.Folio1)) folios.Add(fila.Folio1.Trim());
                if (!string.IsNullOrWhiteSpace(fila.Folio2)) folios.Add(fila.Folio2.Trim());

                string ls = mapaLS != null && mapaLS.TryGetValue(idEstacion, out var valorLS)
                    ? valorLS
                    : string.Empty;

                resultado.Add(new TransaccionProcesadaDTO
                {
                    IdEstacion = idEstacion,
                    CR = fila.CR ?? string.Empty,
                    LS = ls ?? string.Empty,
                    Nombre = fila.Estacion ?? string.Empty,
                    FoliosTotales = string.Join(",", folios)
                });
            }

            return resultado;
        }

        // Agrupa las filas transformadas por estación para construir un preview
        // ligero (evita renderizar miles de filas en la UI).
        public List<ResumenEstacionDTO> AgruparPorEstacion(IEnumerable<TransaccionProcesadaDTO> filas)
        {
            return filas
                .GroupBy(f => f.IdEstacion)
                .Select(g =>
                {
                    var primero = g.First();

                    var folios = g
                        .SelectMany(f => (f.FoliosTotales ?? string.Empty)
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        .Where(f => !string.IsNullOrWhiteSpace(f))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    return new ResumenEstacionDTO
                    {
                        IdEstacion = g.Key,
                        CR = primero.CR,
                        LS = primero.LS,
                        Nombre = primero.Nombre,
                        TotalFolios = folios.Count,
                        FoliosMuestra = string.Join(", ", folios.Take(3))
                    };
                })
                .OrderBy(r => r.Nombre)
                .ThenBy(r => r.IdEstacion)
                .ToList();
        }

        // Convierte el valor de una celda a texto, evitando notación científica
        // o decimales en números enteros (caso típico de folios largos).
        private static string LeerCeldaComoTexto(IXLCell celda)
        {
            var valor = celda.Value;

            if (valor.IsNumber)
            {
                double numero = valor.GetNumber();
                if (double.IsNaN(numero) || double.IsInfinity(numero))
                {
                    return string.Empty;
                }

                // Si es entero, formateamos sin decimales ni notación científica.
                if (numero == Math.Truncate(numero))
                {
                    return numero.ToString("0", CultureInfo.InvariantCulture);
                }

                return numero.ToString(CultureInfo.InvariantCulture);
            }

            return celda.GetString()?.Trim() ?? string.Empty;
        }
    }
}
