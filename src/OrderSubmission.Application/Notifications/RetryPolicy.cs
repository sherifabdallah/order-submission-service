using Microsoft.Extensions.Options;

namespace OrderSubmission.Application.Notifications;

/// <summary>Strategy that decides when a failed delivery is attempted again.</summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Returns when the next attempt is due after <paramref name="consecutiveFailures"/> failures,
    /// or null when automatic retries are exhausted.
    /// </summary>
    DateTimeOffset? GetNextAttemptAt(int consecutiveFailures, DateTimeOffset failedAt);
}

/// <summary>Capped exponential back-off with jitter: 2s, 4s, 8s, ... up to <see cref="NotificationDeliveryOptions.MaxRetryDelay"/>.</summary>
internal sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly NotificationDeliveryOptions _options;
    private readonly Func<double> _nextRandom;

    public ExponentialBackoffRetryPolicy(IOptions<NotificationDeliveryOptions> options)
        : this(options, Random.Shared.NextDouble)
    {
    }

    internal ExponentialBackoffRetryPolicy(IOptions<NotificationDeliveryOptions> options, Func<double> nextRandom)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _nextRandom = nextRandom;
    }

    public DateTimeOffset? GetNextAttemptAt(int consecutiveFailures, DateTimeOffset failedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consecutiveFailures);

        if (consecutiveFailures >= _options.MaxAttempts)
        {
            return null;
        }

        var exponent = Math.Min(consecutiveFailures - 1, 30);
        var backoffMs = Math.Min(
            _options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, exponent),
            _options.MaxRetryDelay.TotalMilliseconds);

        var jitter = 1 + (((_nextRandom() * 2) - 1) * _options.JitterRatio);
        return failedAt + TimeSpan.FromMilliseconds(backoffMs * jitter);
    }
}
