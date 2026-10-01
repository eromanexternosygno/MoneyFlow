using MoneyFlow.Utilities;
using Xunit;

namespace MoneyFlow.Tests
{
    public class CsvHelperTests
    {
        [Fact]
        public void Escapar_CitaCamposConComa()
        {
            Assert.Equal("\"a,b\"", CsvHelper.Escapar("a,b"));
        }

        [Fact]
        public void Escapar_NoCitaCamposSimples()
        {
            Assert.Equal("simple", CsvHelper.Escapar("simple"));
        }

        [Fact]
        public void Escapar_ValorNuloDevuelveVacio()
        {
            Assert.Equal(string.Empty, CsvHelper.Escapar(null));
        }

        [Fact]
        public void EscaparComoTexto_DevuelveFormulaDeTexto()
        {
            // ="467429920,467429750" citado como campo CSV:
            // "=""467429920,467429750"""
            Assert.Equal("\"=\"\"467429920,467429750\"\"\"", CsvHelper.EscaparComoTexto("467429920,467429750"));
        }

        [Fact]
        public void EscaparComoTexto_DevuelveFormulaDeTextoSinComa()
        {
            Assert.Equal("\"=\"\"467429920\"\"\"", CsvHelper.EscaparComoTexto("467429920"));
        }
    }
}
