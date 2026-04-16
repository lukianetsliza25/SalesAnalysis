// SalesAnalysis.Data/SalesDbContext.cs
using Microsoft.EntityFrameworkCore;
using SalesAnalysis.Core.Entities;

public class SalesDbContext : DbContext
{
    public DbSet<Transaction> Transactions { get; set; }
    

    public SalesDbContext(DbContextOptions<SalesDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Встановлюємо точність для грошових значень (для сумісності з БД)
        modelBuilder.Entity<Transaction>()
            .Property(t => t.Revenue)
            .HasColumnType("decimal(18,2)");
    }
}