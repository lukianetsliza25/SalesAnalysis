// SalesAnalysis.Data/SalesDbContext.cs
using Microsoft.EntityFrameworkCore;
using SalesAnalysis.Core.Entities;

public class SalesDbContext : DbContext
{
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<SavedAnalysis> SavedAnalyses { get; set; }


    public SalesDbContext(DbContextOptions<SalesDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Автоматична конвертація всіх DateTime в UTC для PostgreSQL
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var properties = entityType.GetProperties()
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?));

            foreach (var property in properties)
            {
                property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
                    v => DateTime.SpecifyKind(v, DateTimeKind.Utc), // При записі в БД
                    v => DateTime.SpecifyKind(v, DateTimeKind.Utc)  // При читанні з БД
                ));
            }
        }

        // Ваші старі налаштування (Precision для Revenue тощо)
        modelBuilder.Entity<Transaction>()
            .Property(t => t.Revenue)
            .HasColumnType("decimal(18,2)");
    }
}