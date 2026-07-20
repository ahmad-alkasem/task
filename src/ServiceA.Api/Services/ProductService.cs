using Microsoft.EntityFrameworkCore;
using ServiceA.Api.Data;
using ServiceA.Api.Domain;
using ServiceA.Api.Dtos;
using Shared.Contracts;

namespace ServiceA.Api.Services;

public sealed class ProductService
{
    private readonly ProductDbContext _db;

    public ProductService(ProductDbContext db)
    {
        _db = db;
    }

    public async Task<List<ProductResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var products = await _db.Products
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(cancellationToken);

        return products.Select(Map).ToList();
    }

    public async Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return product is null ? null : Map(product);
    }

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Sku = request.Sku,
            Price = request.Price,
            Stock = request.Stock,
            Description = request.Description,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Products.Add(product);
        _db.OutboxMessages.Add(BuildOutbox(product, SyncEventType.Created));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Map(product);
    }

    public async Task<ProductResponse?> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.Name = request.Name;
        product.Sku = request.Sku;
        product.Price = request.Price;
        product.Stock = request.Stock;
        product.Description = request.Description;
        product.Version += 1;
        product.UpdatedAt = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.OutboxMessages.Add(BuildOutbox(product, SyncEventType.Updated));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Map(product);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return false;
        }

        product.Version += 1;
        product.UpdatedAt = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Products.Remove(product);
        _db.OutboxMessages.Add(BuildOutbox(product, SyncEventType.Deleted));

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private static OutboxMessage BuildOutbox(Product product, SyncEventType eventType)
    {
        var message = new ProductSyncMessage
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            AggregateType = "Product",
            AggregateId = product.Id,
            Version = product.Version,
            OccurredAt = DateTime.UtcNow,
            Data = eventType == SyncEventType.Deleted ? null : new ProductPayload
            {
                Id = product.Id,
                Name = product.Name,
                Sku = product.Sku,
                Price = product.Price,
                Stock = product.Stock,
                Description = product.Description,
                Version = product.Version,
                UpdatedAt = product.UpdatedAt
            }
        };

        return new OutboxMessage
        {
            EventId = message.EventId,
            AggregateId = product.Id,
            AggregateType = "Product",
            EventType = eventType,
            RoutingKey = RabbitTopology.RoutingKeyFor(eventType),
            Payload = SyncJson.Serialize(message),
            Version = product.Version,
            OccurredAt = message.OccurredAt,
            Status = OutboxStatus.Pending,
            Attempts = 0
        };
    }

    private static ProductResponse Map(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Sku = product.Sku,
        Price = product.Price,
        Stock = product.Stock,
        Description = product.Description,
        Version = product.Version,
        CreatedAt = product.CreatedAt,
        UpdatedAt = product.UpdatedAt
    };
}
