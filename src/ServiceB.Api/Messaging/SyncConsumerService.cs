using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ServiceB.Api.Configuration;
using ServiceB.Api.Services;
using Shared.Contracts;

namespace ServiceB.Api.Messaging;

public sealed class SyncConsumerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqConnectionProvider _connectionProvider;
    private readonly ConsumerOptions _options;
    private readonly ILogger<SyncConsumerService> _logger;

    public SyncConsumerService(
        IServiceScopeFactory scopeFactory,
        RabbitMqConnectionProvider connectionProvider,
        IOptions<ConsumerOptions> options,
        ILogger<SyncConsumerService> logger)
    {
        _scopeFactory = scopeFactory;
        _connectionProvider = connectionProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Consumer disconnected, reconnecting in 5s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync(stoppingToken);

        await using var consumeChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: false, publisherConfirmationTrackingEnabled: false, consumerDispatchConcurrency: 1),
            stoppingToken);

        await using var publishChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);

        await SyncTopology.DeclareAsync(consumeChannel, stoppingToken);
        await consumeChannel.BasicQosAsync(0, _options.PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(consumeChannel);
        consumer.ReceivedAsync += (_, ea) => HandleAsync(consumeChannel, publishChannel, ea, stoppingToken);

        foreach (var (_, queue) in RabbitTopology.MainBindings)
        {
            await consumeChannel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
        }

        _logger.LogInformation("Sync consumer started");

        var completion = new TaskCompletionSource();
        await using var registration = stoppingToken.Register(() => completion.TrySetResult());
        await completion.Task;
    }

    private async Task HandleAsync(IChannel consumeChannel, IChannel publishChannel, BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        var body = ea.Body.ToArray();
        var text = Encoding.UTF8.GetString(body);

        ProductSyncMessage? message = TryDeserialize(text);

        if (message is null || !IsValid(message))
        {
            _logger.LogError("Poison message on {RoutingKey}, dead-lettering. Payload: {Payload}", ea.RoutingKey, text);
            await PublishAsync(publishChannel, RabbitTopology.DeadLetterExchange, ea.RoutingKey, body, GetRetryCount(ea), "poison", cancellationToken);
            if (message is not null)
            {
                await MarkDeadLetteredAsync(message, GetRetryCount(ea), "poison message", cancellationToken);
            }
            await consumeChannel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken);
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<SyncProcessor>();
            var outcome = await processor.ProcessAsync(message, cancellationToken);
            _logger.LogInformation("Processed {EventId} for {AggregateId}: {Outcome}", message.EventId, message.AggregateId, outcome);
            await consumeChannel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _logger.LogWarning(ex, "Transient failure for {EventId}, leaving unacknowledged for redelivery", message.EventId);
            await MarkFailedAsync(message, GetRetryCount(ea), ex.Message, cancellationToken);
            await consumeChannel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken);
        }
        catch (Exception ex)
        {
            await ScheduleRetryOrDeadLetterAsync(consumeChannel, publishChannel, ea, message, body, ex, cancellationToken);
        }
    }

    private async Task ScheduleRetryOrDeadLetterAsync(
        IChannel consumeChannel,
        IChannel publishChannel,
        BasicDeliverEventArgs ea,
        ProductSyncMessage message,
        byte[] body,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var retryCount = GetRetryCount(ea);
        var decision = RetryPolicy.Decide(retryCount, _options.MaxRetryCount);

        if (decision.Outcome == RetryOutcome.Retry)
        {
            var tier = decision.Tier!;
            _logger.LogWarning(exception, "Processing failed for {EventId}, retry {Next} via {Tier}", message.EventId, retryCount + 1, tier.Queue);
            await PublishAsync(publishChannel, tier.Exchange, ea.RoutingKey, body, retryCount + 1, null, cancellationToken);
            await MarkFailedAsync(message, retryCount + 1, exception.Message, cancellationToken);
        }
        else
        {
            _logger.LogError(exception, "Retries exhausted for {EventId}, dead-lettering", message.EventId);
            await PublishAsync(publishChannel, RabbitTopology.DeadLetterExchange, ea.RoutingKey, body, retryCount, "retry-exhausted", cancellationToken);
            await MarkDeadLetteredAsync(message, retryCount, exception.Message, cancellationToken);
        }

        await consumeChannel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken);
    }

    private static async Task PublishAsync(IChannel channel, string exchange, string routingKey, byte[] body, int retryCount, string? deadLetterReason, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, object?>
        {
            [RabbitTopology.RetryCountHeader] = retryCount
        };

        if (deadLetterReason is not null)
        {
            headers[RabbitTopology.DeadLetterReasonHeader] = deadLetterReason;
        }

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Headers = headers
        };

        await channel.BasicPublishAsync(exchange, routingKey, mandatory: false, basicProperties: properties, body: body, cancellationToken: cancellationToken);
    }

    private async Task MarkFailedAsync(ProductSyncMessage message, int attempts, string error, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<SyncStatusWriter>();
        try
        {
            await writer.MarkFailedAsync(message.AggregateId, message.EventId, message.Version, attempts, error, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to record failed status for {AggregateId}", message.AggregateId);
        }
    }

    private async Task MarkDeadLetteredAsync(ProductSyncMessage message, int attempts, string error, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<SyncStatusWriter>();
        try
        {
            await writer.MarkDeadLetteredAsync(message.AggregateId, message.EventId, message.Version, attempts, error, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to record dead-letter status for {AggregateId}", message.AggregateId);
        }
    }

    private static ProductSyncMessage? TryDeserialize(string text)
    {
        try
        {
            return SyncJson.Deserialize(text);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValid(ProductSyncMessage message)
    {
        if (message.EventId == Guid.Empty || message.AggregateId == Guid.Empty)
        {
            return false;
        }

        return message.EventType == SyncEventType.Deleted || message.Data is not null;
    }

    private static bool IsTransient(Exception exception) =>
        exception is DbUpdateException or DbException or TimeoutException;

    private static int GetRetryCount(BasicDeliverEventArgs ea)
    {
        if (ea.BasicProperties.Headers is null ||
            !ea.BasicProperties.Headers.TryGetValue(RabbitTopology.RetryCountHeader, out var value) ||
            value is null)
        {
            return 0;
        }

        return value switch
        {
            int i => i,
            long l => (int)l,
            byte[] bytes => int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) ? parsed : 0,
            string s => int.TryParse(s, out var parsed) ? parsed : 0,
            _ => 0
        };
    }
}
