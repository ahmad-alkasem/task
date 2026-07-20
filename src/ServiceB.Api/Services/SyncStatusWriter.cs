using Microsoft.EntityFrameworkCore;
using ServiceB.Api.Data;
using ServiceB.Api.Domain;

namespace ServiceB.Api.Services;

public sealed class SyncStatusWriter
{
    private readonly SyncDbContext _db;

    public SyncStatusWriter(SyncDbContext db)
    {
        _db = db;
    }

    public Task MarkFailedAsync(Guid aggregateId, Guid eventId, int version, int attempts, string? error, CancellationToken cancellationToken) =>
        WriteAsync(aggregateId, eventId, version, SyncStatus.Failed, attempts, error, cancellationToken);

    public Task MarkDeadLetteredAsync(Guid aggregateId, Guid eventId, int version, int attempts, string? error, CancellationToken cancellationToken) =>
        WriteAsync(aggregateId, eventId, version, SyncStatus.DeadLettered, attempts, error, cancellationToken);

    private async Task WriteAsync(Guid aggregateId, Guid eventId, int version, SyncStatus status, int attempts, string? error, CancellationToken cancellationToken)
    {
        if (aggregateId == Guid.Empty)
        {
            return;
        }

        var record = await _db.SyncStatuses.FirstOrDefaultAsync(s => s.AggregateId == aggregateId, cancellationToken);
        if (record is null)
        {
            _db.SyncStatuses.Add(new SyncStatusRecord
            {
                AggregateId = aggregateId,
                LastEventId = eventId,
                Version = version,
                Status = status,
                Attempts = attempts,
                LastError = Truncate(error),
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            record.LastEventId = eventId;
            record.Version = version;
            record.Status = status;
            record.Attempts = attempts;
            record.LastError = Truncate(error);
            record.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? error) =>
        error is { Length: > 2000 } ? error[..2000] : error;
}
