using Shared.Contracts;

namespace ServiceB.Api.Messaging;

public enum RetryOutcome
{
    Retry,
    DeadLetter
}

public sealed record RetryDecision(RetryOutcome Outcome, RetryTier? Tier);

public static class RetryPolicy
{
    public static RetryDecision Decide(int retryCount, int maxRetryCount)
    {
        if (retryCount >= maxRetryCount)
        {
            return new RetryDecision(RetryOutcome.DeadLetter, null);
        }

        var tiers = RabbitTopology.RetryTiers;
        var index = Math.Min(retryCount, tiers.Count - 1);
        return new RetryDecision(RetryOutcome.Retry, tiers[index]);
    }
}
