using ServiceB.Api.Services;
using Shared.Contracts;
using Sync.UnitTests.Support;

namespace Sync.UnitTests;

public class OrderingTests
{
    [Fact]
    public async Task Stale_update_is_discarded()
    {
        using var db = new ConsumerDatabase();
        var aggregateId = Guid.NewGuid();

        await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 2, SyncEventType.Updated), CancellationToken.None);
        var outcome = await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 1, SyncEventType.Updated), CancellationToken.None);

        Assert.Equal(SyncOutcome.Stale, outcome);

        await using var verify = db.Create();
        var product = Assert.Single(verify.Products);
        Assert.Equal(2, product.Version);
    }

    [Fact]
    public async Task Update_before_create_upserts_then_discards_stale_create()
    {
        using var db = new ConsumerDatabase();
        var aggregateId = Guid.NewGuid();

        var update = await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 2, SyncEventType.Updated), CancellationToken.None);
        var create = await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 1, SyncEventType.Created), CancellationToken.None);

        Assert.Equal(SyncOutcome.Applied, update);
        Assert.Equal(SyncOutcome.Stale, create);

        await using var verify = db.Create();
        var product = Assert.Single(verify.Products);
        Assert.Equal(2, product.Version);
    }

    [Fact]
    public async Task Stale_update_does_not_regress_status_version()
    {
        using var db = new ConsumerDatabase();
        var aggregateId = Guid.NewGuid();

        await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 5, SyncEventType.Updated), CancellationToken.None);
        await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 2, SyncEventType.Updated), CancellationToken.None);

        await using var verify = db.Create();
        var status = Assert.Single(verify.SyncStatuses);
        Assert.Equal(5, status.Version);
    }

    [Fact]
    public async Task Newer_update_overwrites_existing_replica()
    {
        using var db = new ConsumerDatabase();
        var aggregateId = Guid.NewGuid();

        await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 1, SyncEventType.Created), CancellationToken.None);
        await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 3, SyncEventType.Updated), CancellationToken.None);

        await using var verify = db.Create();
        var product = Assert.Single(verify.Products);
        Assert.Equal(3, product.Version);
    }

    [Fact]
    public async Task Stale_delete_does_not_remove_newer_replica()
    {
        using var db = new ConsumerDatabase();
        var aggregateId = Guid.NewGuid();

        await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 5, SyncEventType.Updated), CancellationToken.None);
        var delete = await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 3, SyncEventType.Deleted), CancellationToken.None);

        Assert.Equal(SyncOutcome.Stale, delete);

        await using var verify = db.Create();
        Assert.Single(verify.Products);
    }
}
