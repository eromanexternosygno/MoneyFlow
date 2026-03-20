using MoneyFlow.Entities;
using System.ComponentModel.DataAnnotations;

namespace MoneyFlow.DTOs
{
    public class TransactionDTO
    {
        [Required(ErrorMessage ="El tipo de Servicio es obligatorio")]
        public int ServiceId { get; set; }

        public int UserId { get; set; }
        public string Comment { get; set; }
        [Required(ErrorMessage = "La fecha es obligatoria")]
        public DateOnly Date { get; set; }
        [Required(ErrorMessage = "El monto total es obligatorio")]
        [Range(0.01, double.MaxValue, ErrorMessage = "El monto total debe ser mayor a cero")]
        public decimal TotalAmount { get; set; }
    }
}
