using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderSubmission.Application.Abstractions.Notifications;

namespace OrderSubmission.Infrastructure.Notifications;

public sealed class FakeNotificationServiceOptions
{
    public const string SectionName = "Notifications:FakeService";

    public NotificationServiceMode Mode { get; set; } = NotificationServiceMode.Healthy;

    /// <summary>Probability that a delivery fails while <see cref="Mode"/> is Flaky.</summary>
    [Range(0.0, 1.0)]
    public double FailureRate { get; set; } = 0.5;

    /// <summary>Simulated network latency per delivery.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Whether the simulation can be changed at runtime through the API.</summary>
    public bool AllowRuntimeChanges { get; set; } = true;
}

/// <summary>
/// Stand-in for a real notification provider: "delivers" by logging, and keeps a bounded
/// in-memory journal of what it sent for diagnostics and tests.
/// </summary>
public sealed partial class FakeNotificationSender(ILogger<FakeNotificationSender> logger) : INotificationSender
{
    private const int JournalCapacity = 1_000;
    private readonly ConcurrentQueue<NotificationMessage> _journal = new();

    public IReadOnlyCollection<NotificationMessage> Sent => _journal;

    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        _journal.Enqueue(message);
        while (_journal.Count > JournalCapacity && _journal.TryDequeue(out _))
        {
        }

        LogSent(message.NotificationId, message.Recipient, message.Body);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[fake notification service] Sent {NotificationId} to {Recipient}: {Body}")]
    private partial void LogSent(Guid notificationId, string recipient, string body);
}

/// <summary>
/// Decorator that injects latency and transient failures in front of any sender, driven by
/// <see cref="INotificationServiceSimulator"/>. Because it wraps the port rather than the fake,
/// it could just as well sit in front of a real provider for chaos testing.
/// </summary>
internal sealed class FaultInjectingNotificationSender(
    INotificationSender inner,
    INotificationServiceSimulator simulator,
    TimeProvider timeProvider) : INotificationSender
{
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        var settings = simulator.Current;

        if (settings.Latency > TimeSpan.Zero)
        {
            await Task.Delay(settings.Latency, timeProvider, cancellationToken);
        }

        var fails = settings.Mode switch
        {
            NotificationServiceMode.Outage => true,
#pragma warning disable CA5394 // Not security sensitive: simulated failure sampling.
            NotificationServiceMode.Flaky => Random.Shared.NextDouble() < settings.FailureRate,
#pragma warning restore CA5394
            _ => false,
        };

        if (fails)
        {
            throw new NotificationDeliveryException(
                settings.Mode == NotificationServiceMode.Outage
                    ? "503 Service Unavailable: notification service is down (simulated outage)."
                    : "504 Gateway Timeout: notification service did not respond (simulated flakiness).",
                isTransient: true);
        }

        await inner.SendAsync(message, cancellationToken);
    }
}

/// <summary>Holds the current simulation settings; swapped atomically so readers never see a torn update.</summary>
internal sealed class NotificationServiceSimulator(IOptions<FakeNotificationServiceOptions> options) : INotificationServiceSimulator
{
    private NotificationServiceSettings _current = new(options.Value.Mode, options.Value.FailureRate, options.Value.Latency);

    public NotificationServiceSettings Current => Volatile.Read(ref _current);

    public bool AllowsRuntimeChanges { get; } = options.Value.AllowRuntimeChanges;

    public void Apply(NotificationServiceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.FailureRate, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.FailureRate, 1.0);
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.Latency, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.Latency, NotificationServiceSettings.MaxLatency);

        Volatile.Write(ref _current, settings);
    }
}
