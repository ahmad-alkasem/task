using Shared.Contracts;

namespace Sync.UnitTests.Support;

public static class MessageFactory
{
    public static ProductSyncMessage Create(Guid aggregateId, int version, SyncEventType eventType = SyncEventType.Created, Guid? eventId = null)
    {
        return new ProductSyncMessage
        {
            EventId = eventId ?? Guid.NewGuid(),
            EventType = eventType,
            AggregateType = "Product",
            AggregateId = aggregateId,
            Version = version,
            OccurredAt = DateTime.UtcNow,
            Data = eventType == SyncEventType.Deleted ? null : new ProductPayload
            {
                Id = aggregateId,
                Name = $"Product {version}",
                Sku = $"SKU-{aggregateId:N}",
                Price = 10m * version,
                Stock = version,
                Description = "sample",
                Version = version,
                UpdatedAt = DateTime.UtcNow
            }
        };
    }
}
