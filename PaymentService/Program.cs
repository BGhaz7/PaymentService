    using System.IdentityModel.Tokens.Jwt;
    using System.Security.Claims;
    using System.Text;
    using System.Text.Json;
    using System.Security.Cryptography;
    using Azure.Identity;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Infrastructure;
    using Microsoft.AspNetCore.Cryptography.KeyDerivation;
    using Microsoft.AspNetCore.Identity.Data;
    using Microsoft.IdentityModel.Tokens;
    using PaymentService.Data;
    using PaymentService.Data;
    using PaymentService.Models;
    using Npgsql.EntityFrameworkCore.PostgreSQL;
    using System.ComponentModel.DataAnnotations;

    internal class Program
    {
        private static string GenerateJwtToken(string username, List<Claim> claims)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("sGQ7+cHIYRyCJoq1l0F9utfBhCG4jxDVq9DKhrWyXys="));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            //Issuer and Audience?
            var token = new JwtSecurityToken(
                issuer: null, 
                audience: null, 
                claims: claims,
                expires: DateTime.Now.AddHours(1), // Token expiration time
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static string extractIdFromJWT(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            try
            {
                var jwtToken = tokenHandler.ReadJwtToken(token);
        
                // Assuming the user ID is stored in a claim with type 'nameidentifier' or a custom type.
                // Adjust the claim type according to how the user ID is stored in your tokens.s
                var idClaim = jwtToken.Claims.FirstOrDefault(claim => claim.Type == JwtRegisteredClaimNames.NameId);

                if (idClaim == null)
                {
                    return "Id does not exist";
                }

                var id = idClaim.Value;
                return id;
            }
            catch (Exception ex)
            {
                return "invalid token";
            }
        }

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddDbContext<paymentContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("PostGresConnectionString")));
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey =
                            new SymmetricSecurityKey(
                                Encoding.UTF8.GetBytes("sGQ7+cHIYRyCJoq1l0F9utfBhCG4jxDVq9DKhrWyXys=")),
                        ValidateIssuer = false,
                        ValidateAudience = false
                    };
                });

            builder.Services.AddControllers();
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization(); //This also applies to minimal apis

            app.MapPost("v1/createRecord", async (HttpContext httpContext, paymentContext db) =>
            {
                var authorizationHeader = httpContext.Request.Headers["Authorization"].ToString();

                if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer "))
                {
                    return Results.BadRequest("Missing or invalid Authorization header.");
                }

                var token = authorizationHeader.Substring("Bearer ".Length).Trim();
                var userId = int.Parse(extractIdFromJWT(token));

                var paymentWallet = new PaymentWallet
                {
                    id = userId,
                    Balance = 0,
                    topUp = 0
                    
                };
                db.PaymentWallets.Add(paymentWallet);
                try
                {
                    await db.SaveChangesAsync();
                    return Results.Created();
                }
                catch (DbUpdateException)
                {
                    return Results.Problem(
                        "An error occurred saving the user, please make sure you have entered valid information", "500");
                }
            });
            
            
            app.MapPost("v1/ApplePayTopUp", async (paymentContext db, topUpDto topUp) =>
            {
                var userId = int.Parse(extractIdFromJWT(topUp.jwtToken));
                var record = await db.PaymentWallets.FindAsync(userId);
                if (record != null)
                {
                    record.Balance += topUp.amount;
                    record.topUp = topUp.amount;
                }
                else
                {
                    Results.BadRequest("No record found!");
                }
                var transaction = new transaction
                {
                    userid = userId,
                    amount = topUp.amount,
                    date = DateTime.UtcNow,
                };
                db.Transactions.Add(transaction);
                await db.SaveChangesAsync();
                return Results.Ok();
            });
            
            app.MapGet("/v1/transactions", async (HttpContext httpContext, paymentContext db, int pageNumber = 1, int pageSize = 10) =>
            {
                var authorizationHeader = httpContext.Request.Headers["Authorization"].ToString();

                if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer "))
                {
                    return Results.BadRequest("Missing or invalid Authorization header.");
                }

                var token = authorizationHeader.Substring("Bearer ".Length).Trim();
                var userId = int.Parse(extractIdFromJWT(token));
                Console.Write(userId);
                
                var transactions = await db.Transactions
                    .Where(t => t.userid == userId)
                    .OrderByDescending(t => t.date)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var totalTransactions = await db.Transactions.CountAsync();
                var totalPages = (int)Math.Ceiling(totalTransactions / (double)pageSize);

                return Results.Ok(new { Transactions = transactions, TotalPages = totalPages });
            });

            app.MapGet("/v1/balance", async (HttpContext httpContext, paymentContext db) =>
                {
                    var authorizationHeader = httpContext.Request.Headers["Authorization"].ToString();

                    if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer "))
                    {
                        return Results.BadRequest("Missing or invalid Authorization header.");
                    }

                    var token = authorizationHeader.Substring("Bearer ".Length).Trim();
                    var userId = int.Parse(extractIdFromJWT(token));
                    Console.Write(userId);

                    var record = await db.PaymentWallets.FindAsync(userId);
                    return record == null ? Results.BadRequest("Invalid Username or Password") : Results.Ok(record.Balance);
                });
            app.Run();
        }
    }
