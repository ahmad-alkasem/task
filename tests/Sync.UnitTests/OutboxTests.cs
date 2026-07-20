using ServiceA.Api.Domain;
using ServiceA.Api.Dtos;
using ServiceA.Api.Services;
using Shared.Contracts;
using Sync.UnitTests.Support;

namespace Sync.UnitTests;

public class OutboxTests
{
    [Fact]
    public async Task Create_persists_product_and_single_outbox_message()
    {
        using var db = new ProducerDatabase();
        var service = new ProductService(db.Create());

        var created = await service.CreateAsync(new CreateProductRequest
        {
            Name = "Keyboard",
            Sku = "KB-1",
            Price = 49.90m,
            Stock = 5,
            Description = "mechanical"
        }, CancellationToken.None);

        await using var verify = db.Create();
        var product = Assert.Single(verify.Products);
        Assert.Equal(created.Id, product.Id);
        Assert.Equal(1, product.Version);

        var outbox = Assert.Single(verify.OutboxMessages);
        Assert.Equal(SyncEventType.Created, outbox.EventType);
        Assert.Equal(RabbitTopology.CreatedRoutingKey, outbox.RoutingKey);
        Assert.Equal(product.Id, outbox.AggregateId);
        Assert.Null(outbox.ProcessedAt);
        Assert.Equal(OutboxStatus.Pending, outbox.Status);

        var payload = SyncJson.Deserialize(outbox.Payload);
        Assert.NotNull(payload);
        Assert.Equal(outbox.EventId, payload!.EventId);
        Assert.Equal("Keyboard", payload.Data!.Name);
    }

    [Fact]
    public async Task Update_bumps_version_and_emits_updated_event()
    {
        using var db = new ProducerDatabase();
        var created = await new ProductService(db.Create()).CreateAsync(new CreateProductRequest
        {
            Name = "Mouse",
            Sku = "MO-1",
            Price = 20m,
            Stock = 3
        }, CancellationToken.None);

        await new ProductService(db.Create()).UpdateAsync(created.Id, new UpdateProductRequest
        {
            Name = "Mouse Pro",
            Sku = "MO-1",
            Price = 25m,
            Stock = 4
        }, CancellationToken.None);

        await using var verify = db.Create();
        var product = Assert.Single(verify.Products);
        Assert.Equal(2, product.Version);
        Assert.Equal("Mouse Pro", product.Name);

        Assert.Equal(2, verify.OutboxMessages.Count());
        Assert.Contains(verify.OutboxMessages, m => m.EventType == SyncEventType.Updated && m.Version == 2);
    }

    [Fact]
    public async Task Delete_removes_product_and_emits_deleted_event()
    {
        using var db = new ProducerDatabase();
        var created = await new ProductService(db.Create()).CreateAsync(new CreateProductRequest
        {
            Name = "Cable",
            Sku = "CA-1",
            Price = 5m,
            Stock = 100
        }, CancellationToken.None);

        var deleted = await new ProductService(db.Create()).DeleteAsync(created.Id, CancellationToken.None);

        Assert.True(deleted);

        await using var verify = db.Create();
        Assert.Empty(verify.Products);
        Assert.Contains(verify.OutboxMessages, m => m.EventType == SyncEventType.Deleted);
    }
}
