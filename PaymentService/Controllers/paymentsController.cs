using Microsoft.AspNetCore.Mvc;
using PaymentService.Models;
using PaymentService.Data;
namespace PaymentService.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AccountsController : ControllerBase
{
    private readonly paymentContext _context;
}