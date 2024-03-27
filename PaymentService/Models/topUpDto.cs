using System.ComponentModel.DataAnnotations;

namespace PaymentService.Models;

public class topUpDto
{
    [Required]
    public int amount { get; set; }
}