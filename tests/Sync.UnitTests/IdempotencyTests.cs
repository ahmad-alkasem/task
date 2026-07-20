using ServiceB.Api.Services;
using Shared.Contracts;
using Sync.UnitTests.Support;

namespace Sync.UnitTests;

public class IdempotencyTests
{
    [Fact]
    public async Task Duplicate_event_is_applied_once()
    {
        using var db = new ConsumerDatabase();
        var aggregateId = Guid.NewGuid();
        var message = MessageFactory.Create(aggregateId, version: 1);

        var first = await new SyncProcessor(db.Create()).ProcessAsync(message, CancellationToken.None);
        var second = await new SyncProcessor(db.Create()).ProcessAsync(message, CancellationToken.None);

        Assert.Equal(SyncOutcome.Applied, first);
        Assert.Equal(SyncOutcome.Duplicate, second);

        await using var verify = db.Create();
        Assert.Single(verify.Products);
        Assert.Single(verify.ProcessedEvents);
    }

    [Fact]
    public async Task Applied_event_marks_status_processed()
    {
        using var db = new ConsumerDatabase();
        var aggregateId = Guid.NewGuid();

        await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 1), CancellationToken.None);

        await using var verify = db.Create();
        var status = Assert.Single(verify.SyncStatuses);
        Assert.Equal(ServiceB.Api.Domain.SyncStatus.Processed, status.Status);
        Assert.Equal(aggregateId, status.AggregateId);
    }
}
