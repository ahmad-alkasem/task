namespace ServiceB.Api.Domain;

public class ReplicatedProduct
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
