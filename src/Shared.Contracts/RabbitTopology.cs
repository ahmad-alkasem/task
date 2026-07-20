namespace Shared.Contracts;

public static class RabbitTopology
{
    public const string SyncExchange = "data.sync";
    public const string DeadLetterExchange = "sync.dlx";
    public const string DeadLetterQueue = "sync.dlq";

    public const string CreatedRoutingKey = "product.created";
    public const string UpdatedRoutingKey = "product.updated";
    public const string DeletedRoutingKey = "product.deleted";

    public const string CreatedQueue = "sync.created";
    public const string UpdatedQueue = "sync.updated";
    public const string DeletedQueue = "sync.deleted";

    public const string RetryCountHeader = "x-retry-count";
    public const string DeadLetterReasonHeader = "x-dead-letter-reason";

    public static string RoutingKeyFor(SyncEventType eventType) => eventType switch
    {
        SyncEventType.Created => CreatedRoutingKey,
        SyncEventType.Updated => UpdatedRoutingKey,
        SyncEventType.Deleted => DeletedRoutingKey,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null)
    };

    public static IReadOnlyList<(string RoutingKey, string Queue)> MainBindings { get; } = new[]
    {
        (CreatedRoutingKey, CreatedQueue),
        (UpdatedRoutingKey, UpdatedQueue),
        (DeletedRoutingKey, DeletedQueue)
    };

    public static IReadOnlyList<RetryTier> RetryTiers { get; } = new[]
    {
        new RetryTier(1, "sync.retry.5s", "sync.retry.5s.queue", 5000),
        new RetryTier(2, "sync.retry.30s", "sync.retry.30s.queue", 30000),
        new RetryTier(3, "sync.retry.5m", "sync.retry.5m.queue", 300000)
    };
}

public sealed record RetryTier(int Level, string Exchange, string Queue, int TtlMilliseconds);
