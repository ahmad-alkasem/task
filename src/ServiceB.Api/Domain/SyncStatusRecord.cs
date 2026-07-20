namespace ServiceB.Api.Domain;

public enum SyncStatus
{
    Pending,
    Processed,
    Failed,
    DeadLettered
}

public class SyncStatusRecord
{
    public Guid AggregateId { get; set; }
    public Guid LastEventId { get; set; }
    public int Version { get; set; }
    public SyncStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAt { get; set; }
}
