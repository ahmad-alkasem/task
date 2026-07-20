using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using ServiceA.Api.Configuration;
using ServiceA.Api.Data;
using ServiceA.Api.Domain;
using Shared.Contracts;

namespace ServiceA.Api.Messaging;

public sealed class OutboxPublisherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxPublisherService> _logger;

    public OutboxPublisherService(
        IServiceScopeFactory scopeFactory,
        RabbitMqConnectionProvider connectionProvider,
        IOptions<OutboxOptions> options,
        ILogger<OutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollingIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox publishing cycle failed, will retry in {Interval}", interval);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PublishPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();

        var pending = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await channel.ExchangeDeclareAsync(RabbitTopology.SyncExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        foreach (var (routingKey, queue) in RabbitTopology.MainBindings)
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(queue, RabbitTopology.SyncExchange, routingKey, cancellationToken: cancellationToken);
        }

        foreach (var message in pending)
        {
            try
            {
                var body = Encoding.UTF8.GetBytes(message.Payload);
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/json",
                    MessageId = message.EventId.ToString(),
                    Type = message.EventType.ToString(),
                    Timestamp = new AmqpTimestamp(new DateTimeOffset(message.OccurredAt, TimeSpan.Zero).ToUnixTimeSeconds())
                };

                await channel.BasicPublishAsync(
                    RabbitTopology.SyncExchange,
                    message.RoutingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: cancellationToken);

                message.ProcessedAt = DateTime.UtcNow;
                message.Status = OutboxStatus.Published;
                message.Attempts += 1;
                message.LastError = null;
            }
            catch (Exception ex)
            {
                message.Attempts += 1;
                message.LastError = ex.Message;
                if (message.Attempts >= _options.MaxPublishAttempts)
                {
                    message.Status = OutboxStatus.Failed;
                }

                _logger.LogWarning(ex, "Failed to publish outbox message {EventId} (attempt {Attempts})", message.EventId, message.Attempts);
                await db.SaveChangesAsync(cancellationToken);
                throw;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
