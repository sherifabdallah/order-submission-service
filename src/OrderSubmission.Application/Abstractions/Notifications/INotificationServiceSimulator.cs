namespace OrderSubmission.Application.Abstractions.Notifications;

public enum NotificationServiceMode
{
    /// <summary>Every delivery succeeds.</summary>
    Healthy,

    /// <summary>Each delivery fails with probability <see cref="NotificationServiceSettings.FailureRate"/>.</summary>
    Flaky,

    /// <summary>Every delivery fails with a transient error.</summary>
    Outage,
}

public sealed record NotificationServiceSettings(NotificationServiceMode Mode, double FailureRate, TimeSpan Latency)
{
    public static readonly TimeSpan MaxLatency = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Controls how the fake notification service behaves, so temporary failures can be produced on
/// demand: from configuration at start-up, or from the API/UI at runtime.
/// </summary>
public interface INotificationServiceSimulator
{
    NotificationServiceSettings Current { get; }

    /// <summary>False when runtime changes are disabled by configuration.</summary>
    bool AllowsRuntimeChanges { get; }

    void Apply(NotificationServiceSettings settings);
}
