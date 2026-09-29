using System.Globalization;
using OrderSubmission.Domain.Common;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Domain.Notifications;

/// <summary>
/// A notification that must be delivered for an order. It is written in the same transaction as
/// the order (transactional outbox) and moved through its lifecycle by the delivery worker:
/// <c>Pending → Delivered</c>, or <c>Pending → Failed</c> once automatic retries are exhausted.
/// A failed notification can be re-queued, so a delivery failure is never final.
/// </summary>
public sealed class Notification
{
    public const int RecipientMaxLength = Order.CustomerReferenceMaxLength;
    public const int MessageMaxLength = 500;
    public const int ErrorMaxLength = 500;

    private readonly List<DeliveryAttempt> _attempts = [];

    // Required by EF Core.
    private Notification()
    {
        Recipient = string.Empty;
        Message = string.Empty;
    }

    private Notification(Guid id, Guid orderId, NotificationType type, string recipient, string message, DateTimeOffset createdAt)
    {
        Id = id;
        OrderId = orderId;
        Type = type;
        Recipient = recipient;
        Message = message;
        CreatedAt = createdAt;
        Status = NotificationStatus.Pending;
        NextAttemptAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public NotificationType Type { get; private set; }

    public string Recipient { get; private set; }

    public string Message { get; private set; }

    public NotificationStatus Status { get; private set; }

    /// <summary>Failures since the last success or manual retry; drives the retry back-off.</summary>
    public int ConsecutiveFailures { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the next delivery attempt is due. Only set while <see cref="Status"/> is Pending.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public string? LastError { get; private set; }

    public IReadOnlyList<DeliveryAttempt> Attempts => _attempts;

    public static Notification OrderConfirmation(Order order, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(order);

        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"Order {order.Id} received: {order.Lines.Count} item(s), total {order.Total:0.00}.");

        return new Notification(
            Guid.CreateVersion7(createdAt),
            order.Id,
            NotificationType.OrderConfirmation,
            order.CustomerReference,
            message,
            createdAt);
    }

    public void RecordDelivery(DateTimeOffset deliveredAt)
    {
        EnsurePending();

        _attempts.Add(DeliveryAttempt.Success(_attempts.Count + 1, deliveredAt));
        Status = NotificationStatus.Delivered;
        DeliveredAt = deliveredAt;
        NextAttemptAt = null;
        LastError = null;
        ConsecutiveFailures = 0;
    }

    /// <summary>
    /// Records a failed attempt. When <paramref name="retryAt"/> is null, automatic retries are
    /// exhausted and the notification moves to <see cref="NotificationStatus.Failed"/>.
    /// </summary>
    public void RecordFailure(string error, DateTimeOffset attemptedAt, DateTimeOffset? retryAt)
    {
        EnsurePending();

        var reason = Truncate(string.IsNullOrWhiteSpace(error) ? "Unknown delivery error." : error.Trim(), ErrorMaxLength);

        _attempts.Add(DeliveryAttempt.Failure(_attempts.Count + 1, attemptedAt, reason));
        ConsecutiveFailures++;
        LastError = reason;

        if (retryAt is { } next)
        {
            NextAttemptAt = next;
        }
        else
        {
            Status = NotificationStatus.Failed;
            NextAttemptAt = null;
        }
    }

    /// <summary>
    /// Makes the notification due immediately. A failed notification gets a fresh retry budget;
    /// a pending one skips its remaining back-off. Delivered notifications cannot be retried.
    /// </summary>
    public void RetryNow(DateTimeOffset now)
    {
        switch (Status)
        {
            case NotificationStatus.Delivered:
                throw new DomainException("notification.already_delivered", "The notification has already been delivered.");
            case NotificationStatus.Failed:
                Status = NotificationStatus.Pending;
                ConsecutiveFailures = 0;
                NextAttemptAt = now;
                break;
            case NotificationStatus.Pending:
                NextAttemptAt = NextAttemptAt is { } due && due < now ? due : now;
                break;
            default:
                throw new InvalidOperationException($"Unsupported notification status '{Status}'.");
        }
    }

    private void EnsurePending()
    {
        if (Status != NotificationStatus.Pending)
        {
            throw new DomainException(
                "notification.not_pending",
                $"Only pending notifications can be attempted; current status is {Status}.");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
