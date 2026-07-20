namespace ServiceB.Api.Configuration;

public sealed class ConsumerOptions
{
    public const string SectionName = "Consumer";

    public int MaxRetryCount { get; set; } = 5;
    public ushort PrefetchCount { get; set; } = 10;
}
