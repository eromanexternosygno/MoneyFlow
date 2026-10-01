using ClosedXML.Excel;
using MoneyFlow.DTOs;
using MoneyFlow.Utilities;
using Xunit;

namespace MoneyFlow.Tests
{
    public class ExcelTransaccionesReaderTests
    {
        private readonly ExcelTransaccionesReader _reader = new();

        [Fact]
        public void Leer_ConColumnasCorrectas_DevuelveFilasYSinErrores()
        {
            // Arrange
            var stream = CrearExcel(
                new[] { "EstacionId_1", "CR_1", "Estacion_1", "Folio_1", "Folio_2" },
                new object[][]
                {
                    new object[] { 10, "CR-001", "Estacion A", "F100", "F101" },
                    new object[] { 20, "CR-002", "Estacion B", "F200", "" }
                });

            // Act
            var resultado = _reader.Leer(stream);

            // Assert
            Assert.True(resultado.Success);
            Assert.Empty(resultado.Errores);
            Assert.Equal(2, resultado.Filas.Count);
            Assert.Equal("10", resultado.Filas[0].EstacionId);
            Assert.Equal("CR-001", resultado.Filas[0].CR);
        }

        [Fact]
        public void Leer_SinColumnasRequeridas_DevuelveErrores()
        {
            // Arrange
            var stream = CrearExcel(
                new[] { "EstacionId_1", "CR_1" },
                new object[][]
                {
                    new object[] { 10, "CR-001" }
                });

            // Act
            var resultado = _reader.Leer(stream);

            // Assert
            Assert.False(resultado.Success);
            Assert.NotEmpty(resultado.Errores);
            Assert.Contains(resultado.Errores, e => e.Contains("Estacion_1"));
        }

        [Fact]
        public void Transformar_ConcatenaFoliosYSeparaPorComa()
        {
            // Arrange
            var filas = new List<FilaExcelTransaccionDTO>
            {
                new() { EstacionId = "10", CR = "CR-001", Estacion = "Estacion A", Folio1 = "F100", Folio2 = "F101" },
                new() { EstacionId = "20", CR = "CR-002", Estacion = "Estacion B", Folio1 = "F200", Folio2 = "" }
            };
            var mapa = new Dictionary<int, string>();

            // Act
            var resultado = _reader.Transformar(filas, mapa);

            // Assert
            Assert.Equal(2, resultado.Count);
            Assert.Equal("F100,F101", resultado[0].FoliosTotales);
            Assert.Equal("F200", resultado[1].FoliosTotales);
        }

        [Fact]
        public void Transformar_AsignaLSSegunMapaYDejaVacioSiNoExiste()
        {
            // Arrange
            var filas = new List<FilaExcelTransaccionDTO>
            {
                new() { EstacionId = "10", CR = "CR-001", Estacion = "Estacion A", Folio1 = "F100" },
                new() { EstacionId = "99", CR = "CR-099", Estacion = "Estacion X", Folio1 = "F900" }
            };
            var mapa = new Dictionary<int, string>
            {
                { 10, "LS_A" }
            };

            // Act
            var resultado = _reader.Transformar(filas, mapa);

            // Assert
            Assert.Equal("LS_A", resultado[0].LS);
            Assert.Equal(string.Empty, resultado[1].LS);
        }

        [Fact]
        public void Transformar_IgnoraFilasConIdEstacionInvalido()
        {
            // Arrange
            var filas = new List<FilaExcelTransaccionDTO>
            {
                new() { EstacionId = "abc", CR = "CR-001", Estacion = "Estacion A", Folio1 = "F100" },
                new() { EstacionId = "20", CR = "CR-002", Estacion = "Estacion B", Folio1 = "F200" },
                new() { EstacionId = "", CR = "CR-003", Estacion = "Estacion C", Folio1 = "F300" }
            };
            var mapa = new Dictionary<int, string>();

            // Act
            var resultado = _reader.Transformar(filas, mapa);

            // Assert
            Assert.Single(resultado);
            Assert.Equal(20, resultado[0].IdEstacion);
        }

        [Fact]
        public void Leer_FolioNumericoLargo_NoDevuelveNotacionCientifica()
        {
            // Arrange: folio almacenado como número en Excel (no como texto)
            using var workbook = new XLWorkbook();
            var hoja = workbook.Worksheets.Add("Hoja1");

            hoja.Cell(1, 1).Value = "EstacionId_1";
            hoja.Cell(1, 2).Value = "CR_1";
            hoja.Cell(1, 3).Value = "Estacion_1";
            hoja.Cell(1, 4).Value = "Folio_1";
            hoja.Cell(1, 5).Value = "Folio_2";

            hoja.Cell(2, 1).Value = 10;
            hoja.Cell(2, 2).Value = "CR-001";
            hoja.Cell(2, 3).Value = "Estacion A";
            hoja.Cell(2, 4).Value = 1000000000000000d;
            hoja.Cell(2, 5).Value = "";

            var ms = new MemoryStream();
            workbook.SaveAs(ms);
            ms.Position = 0;

            // Act
            var resultado = _reader.Leer(ms);

            // Assert
            Assert.True(resultado.Success);
            Assert.Equal("1000000000000000", resultado.Filas[0].Folio1);
        }

        [Fact]
        public void AgruparPorEstacion_AgrupaYCuentaFolios()
        {
            // Arrange
            var filas = new List<TransaccionProcesadaDTO>
            {
                new() { IdEstacion = 10, CR = "CR-001", LS = "LS_A", Nombre = "Estacion A", FoliosTotales = "F100,F101" },
                new() { IdEstacion = 10, CR = "CR-001", LS = "LS_A", Nombre = "Estacion A", FoliosTotales = "F102" },
                new() { IdEstacion = 20, CR = "CR-002", LS = "LS_B", Nombre = "Estacion B", FoliosTotales = "F200" }
            };

            // Act
            var resultado = _reader.AgruparPorEstacion(filas);

            // Assert
            Assert.Equal(2, resultado.Count);

            var estacionA = resultado.First(r => r.IdEstacion == 10);
            Assert.Equal(3, estacionA.TotalFolios);
            Assert.Contains("F100", estacionA.FoliosMuestra);
        }

        private static MemoryStream CrearExcel(string[] encabezados, object[][] filas)
        {
            using var workbook = new XLWorkbook();
            var hoja = workbook.Worksheets.Add("Hoja1");

            for (int c = 0; c < encabezados.Length; c++)
            {
                hoja.Cell(1, c + 1).Value = encabezados[c];
            }

            for (int r = 0; r < filas.Length; r++)
            {
                for (int c = 0; c < filas[r].Length; c++)
                {
                    hoja.Cell(r + 2, c + 1).Value = filas[r][c]?.ToString() ?? string.Empty;
                }
            }

            var ms = new MemoryStream();
            workbook.SaveAs(ms);
            ms.Position = 0;
            return ms;
        }
    }
}
