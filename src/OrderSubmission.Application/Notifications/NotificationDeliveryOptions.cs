using System.ComponentModel.DataAnnotations;

namespace OrderSubmission.Application.Notifications;

public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "Notifications:Delivery";

    /// <summary>Automatic attempts before a notification is marked Failed (it can then be re-queued manually).</summary>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Delay before the first retry; doubles after each further failure.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Upper bound for the exponential back-off.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Randomises each delay by ±this fraction so retries from many workers do not arrive in waves.</summary>
    [Range(0.0, 1.0)]
    public double JitterRatio { get; set; } = 0.2;

    /// <summary>A send that takes longer than this counts as a failed attempt.</summary>
    public TimeSpan SendTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a worker owns a leased notification. Must be longer than <see cref="SendTimeout"/>.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);
}
