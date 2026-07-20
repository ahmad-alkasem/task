using ServiceB.Api.Domain;
using ServiceB.Api.Services;
using Sync.UnitTests.Support;

namespace Sync.UnitTests;

public class StatusTransitionTests
{
    [Fact]
    public async Task Mark_failed_sets_failed_status_with_attempts()
    {
        using var db = new ConsumerDatabase();
        var aggregateId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await new SyncStatusWriter(db.Create()).MarkFailedAsync(aggregateId, eventId, 1, attempts: 2, "boom", CancellationToken.None);

        await using var verify = db.Create();
        var status = Assert.Single(verify.SyncStatuses);
        Assert.Equal(SyncStatus.Failed, status.Status);
        Assert.Equal(2, status.Attempts);
        Assert.Equal("boom", status.LastError);
    }

    [Fact]
    public async Task Failed_can_transition_to_dead_lettered()
    {
        using var db = new ConsumerDatabase();
        var aggregateId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        await new SyncStatusWriter(db.Create()).MarkFailedAsync(aggregateId, eventId, 1, 3, "temporary", CancellationToken.None);
        await new SyncStatusWriter(db.Create()).MarkDeadLetteredAsync(aggregateId, eventId, 1, 5, "permanent", CancellationToken.None);

        await using var verify = db.Create();
        var status = Assert.Single(verify.SyncStatuses);
        Assert.Equal(SyncStatus.DeadLettered, status.Status);
        Assert.Equal(5, status.Attempts);
    }

    [Fact]
    public async Task Processed_then_failed_updates_same_record()
    {
        using var db = new ConsumerDatabase();
        var aggregateId = Guid.NewGuid();

        await new SyncProcessor(db.Create()).ProcessAsync(MessageFactory.Create(aggregateId, 1), CancellationToken.None);
        await new SyncStatusWriter(db.Create()).MarkFailedAsync(aggregateId, Guid.NewGuid(), 2, 1, "later failure", CancellationToken.None);

        await using var verify = db.Create();
        var status = Assert.Single(verify.SyncStatuses);
        Assert.Equal(SyncStatus.Failed, status.Status);
    }
}
