using System;

namespace MoneyFlow.Models
{
    public class POVViewModel
    {
        public int POId { get; set; }                       // [POId] [int] IDENTITY(1,1) NOT NULL
        public string? NumOC { get; set; }                  // [NumOC] [nvarchar](50) NULL
        public int? ProviderId { get; set; }                // [ProviderId] [int] NULL
        public string? Buyer { get; set; }                  // [Buyer] [nvarchar](50) NULL
        public string? Currency { get; set; }               // [Currency] [nvarchar](50) NULL
        public decimal? Subtotal { get; set; }              // [Subtotal] [decimal](18, 6) NULL
        public decimal? Tax { get; set; }                   // [Tax] [decimal](18, 6) NULL
        public decimal? Total { get; set; }                 // [Total] [decimal](18, 6) NULL
        public string? Status { get; set; }                 // [Status] [nvarchar](50) NULL
        public string? CreatedBy { get; set; }              // [CreatedBy] [nvarchar](50) NULL
        public DateTime Created { get; set; }               // [Created] [datetime2](7) NOT NULL

        public string? LastModifiedBy { get; set; }         // [LastModifiedBy] [nvarchar](50) NULL
        public DateTime? LastModified { get; set; }         // [LastModified] [datetime2](7) NULL
        public string? CarrierName { get; set; }            // [CarrierName] [nvarchar](100) NULL
        public int? CarrierProviderId { get; set; }         // [CarrierproviderId] [int] NULL (renamed to PascalCase)
        public string? Clave { get; set; }                  // [Clave] [nvarchar](100) NULL
        public bool? FromERP { get; set; }                  // [FromERP] [bit] NULL
        public DateTime? LastERPUpdate { get; set; }        // [LastERPUpdate] [datetime2](7) NULL
        public string? Remission { get; set; }              // [Remission] [nvarchar](100) NULL
        public DateTime? RemissionDate { get; set; }        // [RemissionDate] [datetime2](7) NULL
        public int? StationId { get; set; }                 // [StationId] [int] NULL
        public DateTime? CancellationDate { get; set; }     // [CancellationDate] [datetime2](7) NULL
    }

    // 2. Para el proceso de guardado (lo que explicamos antes)
    public class RemissionPair
    {
        public int POId { get; set; }
        public string Remission { get; set; } // Nuevo valor a asignar
    }

    //public class BulkRemissionUpdateDTO
    //{
    //    public string Instance { get; set; }
    //    public List<RemissionPair> Updates { get; set; }
    //}

    public class BulkRemissionUpdateDTO
    {
        public string Instance { get; set; } // Nombre del Linked Server
        public List<int> POIds { get; set; }
        public string NewRemission { get; set; }
        public string? OldRemission { get; set; }
    }

    // Para tu histórico local (Tabla en MoneyFlowDb)
    public class CorrectionHistory
    {
        //Instance,POId, OldRemission, NewRemission, AppliedAt, AppliedBy
        public int Id { get; set; }
        public string Instance { get; set; }
        public List<int> POIds { get; set; }
        public int POId { get; set; } // Guardaremos los IDs como string separado por comas
        public string OldRemission { get; set; }
        public string NewRemission { get; set; }
        public DateTime AppliedAt { get; set; } = DateTime.Now;
        public string AppliedBy { get; set; }
    }

    public class EvidenceRequest
    {
        public string Instance { get; set; }
        public string Moment { get; set; } // "Antes" o "Despues"
        public string ImageData { get; set; } // Base64 de la imagen
    }
}
