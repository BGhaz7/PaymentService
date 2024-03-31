using System.ComponentModel.DataAnnotations;

namespace PaymentService.Models
{
    public class PaymentWallet
    {
        [Key]
        public int id { get; set; }
        public decimal Balance { get; set; }
        public decimal topUp { get; set; }
    }
}

