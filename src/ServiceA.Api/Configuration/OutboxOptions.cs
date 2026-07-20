namespace ServiceA.Api.Configuration;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
    public int MaxPublishAttempts { get; set; } = 10;
}
