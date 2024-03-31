using Microsoft.EntityFrameworkCore;
using PaymentService.Models;



namespace PaymentService.Data
{
    public class paymentContext : DbContext
    {
        public paymentContext(DbContextOptions<paymentContext> options) : base(options)
        {
        }
        public DbSet<transaction> Transactions { get; set; }
        public DbSet<PaymentWallet> PaymentWallets { get; set; }

        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PaymentWallet>()
                .HasIndex(p => p.id)
                .IsUnique();
        }
    }
}
