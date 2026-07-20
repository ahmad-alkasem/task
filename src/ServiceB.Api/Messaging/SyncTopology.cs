using RabbitMQ.Client;
using Shared.Contracts;

namespace ServiceB.Api.Messaging;

public static class SyncTopology
{
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(RabbitTopology.SyncExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(RabbitTopology.DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(RabbitTopology.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(RabbitTopology.DeadLetterQueue, RabbitTopology.DeadLetterExchange, string.Empty, cancellationToken: cancellationToken);

        foreach (var (routingKey, queue) in RabbitTopology.MainBindings)
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(queue, RabbitTopology.SyncExchange, routingKey, cancellationToken: cancellationToken);
        }

        foreach (var tier in RabbitTopology.RetryTiers)
        {
            await channel.ExchangeDeclareAsync(tier.Exchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancellationToken);

            var arguments = new Dictionary<string, object?>
            {
                ["x-message-ttl"] = tier.TtlMilliseconds,
                ["x-dead-letter-exchange"] = RabbitTopology.SyncExchange
            };

            await channel.QueueDeclareAsync(tier.Queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(tier.Queue, tier.Exchange, string.Empty, cancellationToken: cancellationToken);
        }
    }
}
