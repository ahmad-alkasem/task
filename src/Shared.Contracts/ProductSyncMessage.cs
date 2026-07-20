namespace Shared.Contracts;

public sealed class ProductSyncMessage
{
    public Guid EventId { get; set; }
    public SyncEventType EventType { get; set; }
    public string AggregateType { get; set; } = "Product";
    public Guid AggregateId { get; set; }
    public int Version { get; set; }
    public DateTime OccurredAt { get; set; }
    public ProductPayload? Data { get; set; }
}
