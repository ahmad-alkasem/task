using Microsoft.EntityFrameworkCore;
using ServiceB.Api.Domain;

namespace ServiceB.Api.Data;

public class SyncDbContext : DbContext
{
    public SyncDbContext(DbContextOptions<SyncDbContext> options) : base(options)
    {
    }

    public DbSet<ReplicatedProduct> Products => Set<ReplicatedProduct>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<SyncStatusRecord> SyncStatuses => Set<SyncStatusRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReplicatedProduct>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(200).IsRequired();
            entity.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.Property(p => p.Description).HasMaxLength(2000);
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventType).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => e.AggregateId);
        });

        modelBuilder.Entity<SyncStatusRecord>(entity =>
        {
            entity.HasKey(s => s.AggregateId);
            entity.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(s => s.LastError).HasMaxLength(2000);
        });
    }
}
