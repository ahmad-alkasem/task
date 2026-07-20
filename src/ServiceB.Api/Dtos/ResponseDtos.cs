namespace ServiceB.Api.Dtos;

public sealed class ProductResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string? Description { get; set; }
    public int Version { get; set; }
    public DateTime LastSyncedAt { get; set; }
}

public sealed class SyncStatusResponse
{
    public Guid AggregateId { get; set; }
    public Guid LastEventId { get; set; }
    public int Version { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAt { get; set; }
}
