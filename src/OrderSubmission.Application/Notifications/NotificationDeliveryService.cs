using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderSubmission.Application.Abstractions.Notifications;
using OrderSubmission.Domain.Notifications;

namespace OrderSubmission.Application.Notifications;

public enum DeliveryOutcome
{
    Delivered,
    RetryScheduled,
    Failed,

    /// <summary>The lease was lost to another worker; nothing was recorded.</summary>
    Skipped,
}

/// <summary>
/// Makes one delivery attempt for a leased notification and records the outcome: delivered, retry
/// scheduled with back-off, or failed once retries are exhausted.
/// </summary>
public sealed partial class NotificationDeliveryService(
    INotificationOutbox outbox,
    INotificationSender sender,
    IRetryPolicy retryPolicy,
    IOptions<NotificationDeliveryOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDeliveryService> logger)
{
    private readonly NotificationDeliveryOptions _options = options.Value;

    public async Task<DeliveryOutcome> DeliverAsync(NotificationLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        var notification = await outbox.GetLeasedAsync(lease, cancellationToken);
        if (notification is null)
        {
            LogLeaseLost(lease.NotificationId);
            return DeliveryOutcome.Skipped;
        }

        var outcome = await AttemptAsync(notification, cancellationToken);

        if (!await outbox.CompleteAsync(notification, cancellationToken))
        {
            LogLeaseLost(notification.Id);
            return DeliveryOutcome.Skipped;
        }

        return outcome;
    }

    private async Task<DeliveryOutcome> AttemptAsync(Notification notification, CancellationToken cancellationToken)
    {
        var message = new NotificationMessage(notification.Id, notification.OrderId, notification.Recipient, notification.Message);

        using var timeout = new CancellationTokenSource(_options.SendTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await sender.SendAsync(message, linked.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Shutdown cancellations are not caught: the lease simply expires and another
            // worker (or this one after restart) picks the notification up again.
            return RecordFailure(notification, exception, timedOut: timeout.IsCancellationRequested);
        }

        notification.RecordDelivery(timeProvider.GetUtcNow());
        LogDelivered(notification.Id, notification.OrderId, notification.Attempts.Count);
        return DeliveryOutcome.Delivered;
    }

    private DeliveryOutcome RecordFailure(Notification notification, Exception exception, bool timedOut)
    {
        var now = timeProvider.GetUtcNow();
        var isTransient = timedOut || exception is not NotificationDeliveryException { IsTransient: false };
        var retryAt = isTransient ? retryPolicy.GetNextAttemptAt(notification.ConsecutiveFailures + 1, now) : null;

        var reason = timedOut
            ? $"Timed out after {_options.SendTimeout.TotalSeconds:0.#}s."
            : exception is NotificationDeliveryException ? exception.Message : $"{exception.GetType().Name}: {exception.Message}";

        notification.RecordFailure(reason, now, retryAt);

        if (retryAt is { } next)
        {
            LogRetryScheduled(notification.Id, notification.Attempts.Count, next, reason);
            return DeliveryOutcome.RetryScheduled;
        }

        LogFailed(notification.Id, notification.Attempts.Count, reason);
        return DeliveryOutcome.Failed;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivered notification {NotificationId} for order {OrderId} on attempt {Attempt}")]
    private partial void LogDelivered(Guid notificationId, Guid orderId, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Attempt {Attempt} for notification {NotificationId} failed ({Reason}); retrying at {RetryAt:O}")]
    private partial void LogRetryScheduled(Guid notificationId, int attempt, DateTimeOffset retryAt, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification {NotificationId} failed after {Attempt} attempt(s) ({Reason}); automatic retries exhausted")]
    private partial void LogFailed(Guid notificationId, int attempt, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lease on notification {NotificationId} was lost to another worker; outcome discarded")]
    private partial void LogLeaseLost(Guid notificationId);
}
