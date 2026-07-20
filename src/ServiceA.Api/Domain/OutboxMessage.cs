using Shared.Contracts;

namespace ServiceA.Api.Domain;

public enum OutboxStatus
{
    Pending,
    Published,
    Failed
}

public class OutboxMessage
{
    public Guid EventId { get; set; }
    public Guid AggregateId { get; set; }
    public string AggregateType { get; set; } = "Product";
    public SyncEventType EventType { get; set; }
    public string RoutingKey { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public OutboxStatus Status { get; set; }
    public string? LastError { get; set; }
}
