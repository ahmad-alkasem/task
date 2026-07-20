using Microsoft.EntityFrameworkCore;

namespace ServiceB.Api.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < 10)
            {
                logger.LogWarning(ex, "Database not ready (attempt {Attempt}), retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
    }
}
