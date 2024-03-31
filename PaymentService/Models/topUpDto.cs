using System.ComponentModel.DataAnnotations;

namespace PaymentService.Models;

public class topUpDto
{
    [Required]
    public decimal amount { get; set; }
    [Required]
    public int user_id { get; set; }
}