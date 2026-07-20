namespace Sync.IntegrationTests.Support;

public static class Wait
{
    public static async Task<T> UntilAsync<T>(Func<Task<T?>> probe, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("Condition was not satisfied within the allotted time.");
    }
}
