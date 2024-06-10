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
    using RabbitMQ.Client;

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

        private static string extractToken(HttpContext context)
        {
            var authorizationHeader = context.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Missing or invalid Authorization header.");
            }
            return authorizationHeader["Bearer ".Length..].Trim();
        }

        private static int extractIdFromJWT(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            var userIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.NameId);
            if (userIdClaim == null)
            {
                throw new InvalidOperationException("Token does not contain a user ID.");
            }
            return int.Parse(userIdClaim.Value);
        }

        public static void Main(string[] args)
        {
            var accountSender = new ConnectionFactory {Uri = new Uri("amqp://guest:guest@localhost:5672")};
            using var connection = accountSender.CreateConnection();
            using var channel = connection.CreateModel();
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
                    options.Events = new JwtBearerEvents
                    {
                        OnAuthenticationFailed = context =>
                        {
                            if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                            {
                                context.Response.Headers.Append("Token-Expired", "true");
                            }

                            if (context.Exception.GetType() == typeof(SecurityTokenInvalidSignatureException))
                            {
                                context.Response.Headers.Append("Token-Invalid", "true");
                            }
                            return Task.CompletedTask;
                        }
                    };
                    
                    options.Events = new JwtBearerEvents
                    {
                        OnChallenge = context =>
                        {
                            context.HandleResponse();
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.ContentType = "application/json";
                            return context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "You are not authorized or your token has expired." }));
                        }
                    };
                });
            

            builder.Services.AddControllers();
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization(); //This also applies to minimal apis
            app.Use(async (context, next) =>
            {
                await next();

                if (context.Response is { StatusCode: 401, HasStarted: false })
                {
                    // Handle the case where the token is invalid or expired
                    // Modify the response if needed
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Invalid or expired token provided." }));
                }
            });


            app.MapPost("v1/createRecord", async (HttpContext httpContext, paymentContext db) =>
            {
                channel.QueueDeclare(queue: "recordCreate",
                    durable: false,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);
                var authorizationHeader = httpContext.Request.Headers["Authorization"].ToString();

                if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer "))
                {
                    return Results.BadRequest("Missing or invalid Authorization header.");
                }

                var token = extractToken(httpContext);
                var userId = extractIdFromJWT(token);
                

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
            
            
            app.MapPost("v1/ApplePayTopUp", async (HttpContext httpContext,paymentContext db, topUpDto topUp) =>
            {
                var token = extractToken(httpContext);
                var userId = extractIdFromJWT(token);
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

                var token = extractToken(httpContext);
                var userId = extractIdFromJWT(token);
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

                    var token = extractToken(httpContext);
                    var userId = extractIdFromJWT(token);
                    Console.Write(userId);

                    var record = await db.PaymentWallets.FindAsync(userId);
                    return record == null ? Results.BadRequest("Invalid Username or Password") : Results.Ok(record.Balance);
                });
            app.Run();
        }
    }
