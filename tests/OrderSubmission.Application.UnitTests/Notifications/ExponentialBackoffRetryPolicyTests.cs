using Microsoft.Extensions.Options;
using OrderSubmission.Application.Notifications;

namespace OrderSubmission.Application.UnitTests.Notifications;

public sealed class ExponentialBackoffRetryPolicyTests
{
    private static readonly DateTimeOffset FailedAt = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    [InlineData(7, 60)] // 128s capped at MaxRetryDelay
    public void Delay_doubles_after_each_failure_up_to_the_cap(int consecutiveFailures, int expectedSeconds)
    {
        var policy = Policy(jitterRatio: 0);

        var next = policy.GetNextAttemptAt(consecutiveFailures, FailedAt);

        Assert.Equal(FailedAt.AddSeconds(expectedSeconds), next);
    }

    [Fact]
    public void Returns_null_once_max_attempts_are_used_up()
    {
        var policy = Policy(jitterRatio: 0, maxAttempts: 3);

        Assert.NotNull(policy.GetNextAttemptAt(2, FailedAt));
        Assert.Null(policy.GetNextAttemptAt(3, FailedAt));
    }

    [Theory]
    [InlineData(0.0, 8)]  // lowest random value -> -20%
    [InlineData(1.0, 12)] // highest random value -> +20%
    [InlineData(0.5, 10)]
    public void Jitter_spreads_the_delay_within_the_configured_ratio(double random, int expectedSeconds)
    {
        var policy = Policy(jitterRatio: 0.2, random: random, initialDelay: TimeSpan.FromSeconds(10));

        var next = policy.GetNextAttemptAt(1, FailedAt);

        Assert.Equal(FailedAt.AddSeconds(expectedSeconds), next);
    }

    private static ExponentialBackoffRetryPolicy Policy(
        double jitterRatio,
        int maxAttempts = 10,
        double random = 0.5,
        TimeSpan? initialDelay = null) =>
        new(
            Options.Create(new NotificationDeliveryOptions
            {
                MaxAttempts = maxAttempts,
                InitialRetryDelay = initialDelay ?? TimeSpan.FromSeconds(2),
                MaxRetryDelay = TimeSpan.FromSeconds(60),
                JitterRatio = jitterRatio,
            }),
            () => random);
}
