using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceB.Api.Data;
using ServiceB.Api.Dtos;

namespace ServiceB.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly SyncDbContext _db;

    public ProductsController(SyncDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var products = await _db.Products
            .AsNoTracking()
            .OrderByDescending(p => p.LastSyncedAt)
            .Select(p => new ProductResponse
            {
                Id = p.Id,
                Name = p.Name,
                Sku = p.Sku,
                Price = p.Price,
                Stock = p.Stock,
                Description = p.Description,
                Version = p.Version,
                LastSyncedAt = p.LastSyncedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(products);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductResponse
            {
                Id = p.Id,
                Name = p.Name,
                Sku = p.Sku,
                Price = p.Price,
                Stock = p.Stock,
                Description = p.Description,
                Version = p.Version,
                LastSyncedAt = p.LastSyncedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        return product is null ? NotFound() : Ok(product);
    }
}
