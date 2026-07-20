using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceB.Api.Data;
using ServiceB.Api.Domain;
using ServiceB.Api.Dtos;

namespace ServiceB.Api.Controllers;

[ApiController]
[Route("api/sync-status")]
public class SyncStatusController : ControllerBase
{
    private readonly SyncDbContext _db;

    public SyncStatusController(SyncDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SyncStatusResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var records = await _db.SyncStatuses
            .AsNoTracking()
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(cancellationToken);

        return Ok(records.Select(Map));
    }

    [HttpGet("{aggregateId:guid}")]
    public async Task<ActionResult<SyncStatusResponse>> GetById(Guid aggregateId, CancellationToken cancellationToken)
    {
        var record = await _db.SyncStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AggregateId == aggregateId, cancellationToken);

        return record is null ? NotFound() : Ok(Map(record));
    }

    private static SyncStatusResponse Map(SyncStatusRecord s) => new()
    {
        AggregateId = s.AggregateId,
        LastEventId = s.LastEventId,
        Version = s.Version,
        Status = s.Status.ToString(),
        Attempts = s.Attempts,
        LastError = s.LastError,
        UpdatedAt = s.UpdatedAt
    };
}
