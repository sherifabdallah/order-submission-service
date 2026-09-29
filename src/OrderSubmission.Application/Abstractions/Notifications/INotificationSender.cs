namespace OrderSubmission.Application.Abstractions.Notifications;

/// <summary>Port to the external notification service (email, SMS, push, ...).</summary>
public interface INotificationSender
{
    /// <summary>
    /// Delivers the message or throws. <see cref="NotificationMessage.NotificationId"/> stays the same
    /// across retries, so a real provider can use it to de-duplicate at-least-once deliveries.
    /// </summary>
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}

public sealed record NotificationMessage(Guid NotificationId, Guid OrderId, string Recipient, string Body);

/// <summary>
/// Thrown by a sender when delivery fails. Transient failures are retried with back-off; permanent
/// ones fail the notification straight away (it can still be re-queued manually).
/// </summary>
public sealed class NotificationDeliveryException : Exception
{
    public NotificationDeliveryException()
    {
    }

    public NotificationDeliveryException(string message)
        : base(message)
    {
    }

    public NotificationDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public NotificationDeliveryException(string message, bool isTransient)
        : base(message)
    {
        IsTransient = isTransient;
    }

    public bool IsTransient { get; } = true;
}
