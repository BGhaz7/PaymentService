using System.ComponentModel.DataAnnotations;

namespace PaymentService.Models;

public class topUpDto
{
    [Required]
    public decimal amount { get; set; }
    [Required]
    public string jwtToken { get; set; }
}