using Microsoft.EntityFrameworkCore;
using ServiceA.Api.Domain;

namespace ServiceA.Api.Data;

public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            entity.HasIndex(p => p.Sku).IsUnique();
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.Property(p => p.Description).HasMaxLength(2000);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(o => o.EventId);
            entity.Property(o => o.AggregateType).HasMaxLength(100).IsRequired();
            entity.Property(o => o.RoutingKey).HasMaxLength(100).IsRequired();
            entity.Property(o => o.EventType).HasConversion<string>().HasMaxLength(20);
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(o => o.Payload).IsRequired();
            entity.Property(o => o.LastError).HasMaxLength(2000);
            entity.HasIndex(o => new { o.ProcessedAt, o.OccurredAt });
        });
    }
}
