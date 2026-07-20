namespace ServiceB.Api.Data;

public sealed class DatabaseInitializerService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DatabaseInitializerService> _logger;

    public DatabaseInitializerService(IServiceProvider services, ILogger<DatabaseInitializerService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        DatabaseInitializer.InitializeAsync(_services, _logger, stoppingToken);
}
