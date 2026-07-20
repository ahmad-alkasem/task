using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using Shared.Contracts;
using Sync.IntegrationTests.Support;

namespace Sync.IntegrationTests;

[Collection(SyncCollection.Name)]
public class SyncFlowTests
{
    private readonly SyncEnvironment _env;

    public SyncFlowTests(SyncEnvironment env)
    {
        _env = env;
    }

    [Fact]
    public async Task Created_product_is_replicated_and_marked_processed()
    {
        var request = new CreateProductBody("Widget", "W-100", 12.5m, 7, "integration");
        var response = await _env.ProducerClient.PostAsJsonAsync("/api/products", request);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<ProductBody>();
        Assert.NotNull(created);

        var replicated = await Wait.UntilAsync(async () =>
        {
            var probe = await _env.ConsumerClient.GetAsync($"/api/products/{created!.Id}");
            return probe.StatusCode == HttpStatusCode.OK
                ? await probe.Content.ReadFromJsonAsync<ProductBody>()
                : null;
        });

        Assert.Equal("Widget", replicated.Name);
        Assert.Equal("W-100", replicated.Sku);

        var status = await _env.ConsumerClient.GetFromJsonAsync<StatusBody>($"/api/sync-status/{created!.Id}");
        Assert.NotNull(status);
        Assert.Equal("Processed", status!.Status);
    }

    [Fact]
    public async Task Duplicate_delivery_is_applied_once()
    {
        var aggregateId = Guid.NewGuid();
        var message = new ProductSyncMessage
        {
            EventId = Guid.NewGuid(),
            EventType = SyncEventType.Created,
            AggregateType = "Product",
            AggregateId = aggregateId,
            Version = 1,
            OccurredAt = DateTime.UtcNow,
            Data = new ProductPayload
            {
                Id = aggregateId,
                Name = "Duplicated",
                Sku = "DUP-1",
                Price = 3m,
                Stock = 1,
                Version = 1,
                UpdatedAt = DateTime.UtcNow
            }
        };

        var body = Encoding.UTF8.GetBytes(SyncJson.Serialize(message));

        await using var connection = await _env.CreateRabbitConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var properties = new BasicProperties { Persistent = true, ContentType = "application/json" };

        await channel.BasicPublishAsync(RabbitTopology.SyncExchange, RabbitTopology.CreatedRoutingKey, mandatory: false, properties, body);
        await channel.BasicPublishAsync(RabbitTopology.SyncExchange, RabbitTopology.CreatedRoutingKey, mandatory: false, properties, body);

        await Wait.UntilAsync(async () =>
        {
            var count = await _env.QueryConsumerAsync(db => db.ProcessedEvents.CountAsync(e => e.EventId == message.EventId));
            return count == 1 ? "done" : null;
        });

        var products = await _env.QueryConsumerAsync(db => db.Products.CountAsync(p => p.Id == aggregateId));
        var processed = await _env.QueryConsumerAsync(db => db.ProcessedEvents.CountAsync(e => e.EventId == message.EventId));

        Assert.Equal(1, products);
        Assert.Equal(1, processed);
    }

    [Fact]
    public async Task Poison_message_is_dead_lettered()
    {
        var body = Encoding.UTF8.GetBytes("{ not-a-valid-message ]");

        await using var connection = await _env.CreateRabbitConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.BasicPublishAsync(
            RabbitTopology.SyncExchange,
            RabbitTopology.CreatedRoutingKey,
            mandatory: false,
            new BasicProperties { Persistent = true },
            body);

        var dead = await Wait.UntilAsync(async () =>
        {
            var result = await channel.BasicGetAsync(RabbitTopology.DeadLetterQueue, autoAck: true);
            return result is null ? null : "dead";
        });

        Assert.Equal("dead", dead);
    }

    private sealed record CreateProductBody(string Name, string Sku, decimal Price, int Stock, string? Description);

    private sealed record ProductBody(Guid Id, string Name, string Sku, decimal Price, int Stock, string? Description, int Version);

    private sealed record StatusBody(Guid AggregateId, string Status, int Attempts);
}
