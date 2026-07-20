using Microsoft.EntityFrameworkCore;
using ServiceB.Api.Data;
using ServiceB.Api.Domain;
using Shared.Contracts;

namespace ServiceB.Api.Services;

public sealed class SyncProcessor
{
    private readonly SyncDbContext _db;

    public SyncProcessor(SyncDbContext db)
    {
        _db = db;
    }

    public async Task<SyncOutcome> ProcessAsync(ProductSyncMessage message, CancellationToken cancellationToken)
    {
        var alreadyProcessed = await _db.ProcessedEvents
            .AnyAsync(e => e.EventId == message.EventId, cancellationToken);

        if (alreadyProcessed)
        {
            return SyncOutcome.Duplicate;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var replica = await _db.Products.FirstOrDefaultAsync(p => p.Id == message.AggregateId, cancellationToken);
        var outcome = Apply(message, replica);

        _db.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = message.EventId,
            AggregateId = message.AggregateId,
            EventType = message.EventType,
            Version = message.Version,
            ProcessedAt = DateTime.UtcNow
        });

        var status = await _db.SyncStatuses.FirstOrDefaultAsync(s => s.AggregateId == message.AggregateId, cancellationToken);
        UpsertStatus(message, status);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return outcome;
    }

    private SyncOutcome Apply(ProductSyncMessage message, ReplicatedProduct? replica)
    {
        if (message.EventType == SyncEventType.Deleted)
        {
            if (replica is null)
            {
                return SyncOutcome.Applied;
            }

            if (replica.Version >= message.Version)
            {
                return SyncOutcome.Stale;
            }

            _db.Products.Remove(replica);
            return SyncOutcome.Applied;
        }

        var payload = message.Data!;

        if (replica is null)
        {
            _db.Products.Add(new ReplicatedProduct
            {
                Id = message.AggregateId,
                Name = payload.Name,
                Sku = payload.Sku,
                Price = payload.Price,
                Stock = payload.Stock,
                Description = payload.Description,
                Version = message.Version,
                LastSyncedAt = DateTime.UtcNow
            });
            return SyncOutcome.Applied;
        }

        if (replica.Version >= message.Version)
        {
            return SyncOutcome.Stale;
        }

        replica.Name = payload.Name;
        replica.Sku = payload.Sku;
        replica.Price = payload.Price;
        replica.Stock = payload.Stock;
        replica.Description = payload.Description;
        replica.Version = message.Version;
        replica.LastSyncedAt = DateTime.UtcNow;
        return SyncOutcome.Applied;
    }

    private void UpsertStatus(ProductSyncMessage message, SyncStatusRecord? record)
    {
        if (record is null)
        {
            _db.SyncStatuses.Add(new SyncStatusRecord
            {
                AggregateId = message.AggregateId,
                LastEventId = message.EventId,
                Version = message.Version,
                Status = SyncStatus.Processed,
                Attempts = 0,
                LastError = null,
                UpdatedAt = DateTime.UtcNow
            });
            return;
        }

        record.LastEventId = message.EventId;
        record.Version = Math.Max(record.Version, message.Version);
        record.Status = SyncStatus.Processed;
        record.Attempts = 0;
        record.LastError = null;
        record.UpdatedAt = DateTime.UtcNow;
    }
}
