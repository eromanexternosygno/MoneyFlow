using Hangfire;
using MoneyFlow.Context;
using MoneyFlow.DTOs;
using MoneyFlow.Entities;

namespace MoneyFlow.Managers
{
    // Job de Hangfire encargado de insertar las transacciones procesadas
    // en segundo plano, por lotes y con reintentos automáticos.
    public class TransaccionesProcesadasJob
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private static readonly Dictionary<Guid, ProgresoCarga> _progreso = new();

        public TransaccionesProcesadasJob(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public static void Inicializar(Guid jobId, int total)
        {
            _progreso[jobId] = new ProgresoCarga { Current = 0, Total = total, Estado = "En cola" };
        }

        [AutomaticRetry(Attempts = 3)]
        public async Task Procesar(Guid jobId, List<TransaccionProcesadaDTO> items)
        {
            try
            {
                _progreso[jobId] = new ProgresoCarga { Current = 0, Total = items.Count, Estado = "Procesando" };

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                const int batchSize = 500;
                int procesados = 0;

                foreach (var lote in items.Chunk(batchSize))
                {
                    var entidades = lote.Select(dto => new TransaccionProcesada
                    {
                        IdEstacion = dto.IdEstacion,
                        CR = dto.CR,
                        LS = dto.LS,
                        Nombre = dto.Nombre,
                        FoliosTotales = dto.FoliosTotales
                    }).ToList();

                    db.TransaccionesProcesadas.AddRange(entidades);
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear(); // Liberamos memoria entre lotes

                    procesados += entidades.Count;
                    _progreso[jobId] = new ProgresoCarga { Current = procesados, Total = items.Count, Estado = "Procesando" };
                }

                _progreso[jobId] = new ProgresoCarga { Current = procesados, Total = items.Count, Estado = "Completado" };
            }
            catch (Exception ex)
            {
                _progreso[jobId] = new ProgresoCarga
                {
                    Current = _progreso.TryGetValue(jobId, out var p) ? p.Current : 0,
                    Total = items.Count,
                    Estado = "Error",
                    Mensaje = ex.Message
                };
                throw;
            }
        }

        public static object ObtenerProgreso(Guid jobId)
        {
            if (_progreso.TryGetValue(jobId, out var p))
            {
                return new
                {
                    current = p.Current,
                    total = p.Total,
                    percentage = p.Total > 0 ? (int)((double)p.Current / p.Total * 100) : 0,
                    estado = p.Estado,
                    mensaje = p.Mensaje
                };
            }

            return new { current = 0, total = 0, percentage = 0, estado = "Desconocido", mensaje = string.Empty };
        }

        private class ProgresoCarga
        {
            public int Current { get; set; }
            public int Total { get; set; }
            public string Estado { get; set; }
            public string Mensaje { get; set; }
        }
    }
}
