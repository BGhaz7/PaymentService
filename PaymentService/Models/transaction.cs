using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using PaymentService.Data;

namespace PaymentService.Models
{
    
    public class transaction
    {
        [Key] public Guid transactionId { get; set; } = Guid.NewGuid();
        public int userid { get; set; }
        public decimal amount { get; set;}
        public DateTime date { get; set; }
    }
}