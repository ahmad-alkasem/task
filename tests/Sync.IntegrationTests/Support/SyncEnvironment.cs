using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using ServiceB.Api.Data;
using Testcontainers.MySql;
using Testcontainers.RabbitMq;

namespace Sync.IntegrationTests.Support;

public sealed class SyncEnvironment : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3.13-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.0")
        .WithUsername("root")
        .WithPassword("root")
        .WithDatabase("taskdb")
        .Build();

    private WebApplicationFactory<ServiceA.Api.ApiMarker>? _producer;
    private WebApplicationFactory<ServiceB.Api.ApiMarker>? _consumer;

    public HttpClient ProducerClient { get; private set; } = default!;
    public HttpClient ConsumerClient { get; private set; } = default!;

    public string RabbitHost => _rabbit.Hostname;
    public int RabbitPort => _rabbit.GetMappedPublicPort(5672);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_rabbit.StartAsync(), _mysql.StartAsync());

        await using (var connection = await CreateRabbitConnectionAsync())
        await using (var channel = await connection.CreateChannelAsync())
        {
            await ServiceB.Api.Messaging.SyncTopology.DeclareAsync(channel, CancellationToken.None);
        }

        _producer = BuildFactory<ServiceA.Api.ApiMarker>(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ProductDb"] = MySqlConnectionString("taskdb"),
            ["RabbitMq:HostName"] = RabbitHost,
            ["RabbitMq:Port"] = RabbitPort.ToString(),
            ["Outbox:PollingIntervalSeconds"] = "1"
        });

        _consumer = BuildFactory<ServiceB.Api.ApiMarker>(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SyncDb"] = MySqlConnectionString("taskdb_consumer"),
            ["RabbitMq:HostName"] = RabbitHost,
            ["RabbitMq:Port"] = RabbitPort.ToString()
        });

        ProducerClient = _producer.CreateClient();
        ConsumerClient = _consumer.CreateClient();
    }

    public async Task DisposeAsync()
    {
        ProducerClient?.Dispose();
        ConsumerClient?.Dispose();
        if (_producer is not null)
        {
            await _producer.DisposeAsync();
        }
        if (_consumer is not null)
        {
            await _consumer.DisposeAsync();
        }
        await _rabbit.DisposeAsync();
        await _mysql.DisposeAsync();
    }

    public async Task<IConnection> CreateRabbitConnectionAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = RabbitHost,
            Port = RabbitPort,
            UserName = "guest",
            Password = "guest"
        };

        return await factory.CreateConnectionAsync();
    }

    public async Task<T> QueryConsumerAsync<T>(Func<SyncDbContext, Task<T>> query)
    {
        using var scope = _consumer!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        return await query(db);
    }

    private WebApplicationFactory<TMarker> BuildFactory<TMarker>(Dictionary<string, string?> settings) where TMarker : class =>
        new WebApplicationFactory<TMarker>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });

    private string MySqlConnectionString(string database) =>
        $"server={_mysql.Hostname};port={_mysql.GetMappedPublicPort(3306)};database={database};user=root;password=root;SslMode=None";
}

[CollectionDefinition(Name)]
public sealed class SyncCollection : ICollectionFixture<SyncEnvironment>
{
    public const string Name = "sync-environment";
}
