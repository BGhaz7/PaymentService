using System.ComponentModel.DataAnnotations;

namespace PaymentService.Models
{
    public class PaymentWallet
    {
        [Key]
        public int id { get; set; }
        public int Balance { get; set; }
        public int topUp { get; set; }
    }
}

