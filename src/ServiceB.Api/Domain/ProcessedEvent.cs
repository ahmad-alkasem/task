using Shared.Contracts;

namespace ServiceB.Api.Domain;

public class ProcessedEvent
{
    public Guid EventId { get; set; }
    public Guid AggregateId { get; set; }
    public SyncEventType EventType { get; set; }
    public int Version { get; set; }
    public DateTime ProcessedAt { get; set; }
}
