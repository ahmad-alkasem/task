using ServiceB.Api.Messaging;

namespace Sync.UnitTests;

public class RetryPolicyTests
{
    [Theory]
    [InlineData(0, 5000)]
    [InlineData(1, 30000)]
    [InlineData(2, 300000)]
    [InlineData(3, 300000)]
    [InlineData(4, 300000)]
    public void Retries_escalate_through_tiers(int retryCount, int expectedTtl)
    {
        var decision = RetryPolicy.Decide(retryCount, maxRetryCount: 5);

        Assert.Equal(RetryOutcome.Retry, decision.Outcome);
        Assert.NotNull(decision.Tier);
        Assert.Equal(expectedTtl, decision.Tier!.TtlMilliseconds);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Dead_letters_once_max_retries_reached(int retryCount)
    {
        var decision = RetryPolicy.Decide(retryCount, maxRetryCount: 5);

        Assert.Equal(RetryOutcome.DeadLetter, decision.Outcome);
        Assert.Null(decision.Tier);
    }
}
