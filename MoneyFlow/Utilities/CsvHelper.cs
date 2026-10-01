namespace MoneyFlow.Utilities
{
    // Utilidades para generar campos CSV compatibles con Excel.
    public static class CsvHelper
    {
        // Escapa un valor como campo CSV estándar (RFC 4180).
        public static string Escapar(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        // Fuerza que Excel interprete el valor como TEXTO, evitando la conversión
        // automática a número o notación científica (caso típico de folios largos).
        // Usa el truco ="..." que devuelve la cadena literal.
        public static string EscaparComoTexto(string value)
        {
            var formula = "=\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
            return Escapar(formula);
        }
    }
}
