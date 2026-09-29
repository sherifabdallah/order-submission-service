using OrderSubmission.Domain.Notifications;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Application.Orders;

/// <summary>Read model returned by the order endpoints: the order plus its notification delivery status.</summary>
public sealed record OrderDetails(
    Guid Id,
    string CustomerReference,
    decimal Total,
    DateTimeOffset PlacedAt,
    IReadOnlyList<OrderLineDetails> Items,
    NotificationDetails Notification)
{
    public static OrderDetails From(Order order, Notification notification)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(notification);

        return new OrderDetails(
            order.Id,
            order.CustomerReference,
            order.Total,
            order.PlacedAt,
            [.. order.Lines.Select(line => new OrderLineDetails(line.LineNumber, line.ProductCode, line.Quantity, line.UnitPrice, line.LineTotal))],
            NotificationDetails.From(notification));
    }
}

public sealed record OrderLineDetails(int LineNumber, string ProductCode, int Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record NotificationDetails(
    NotificationStatus Status,
    int AttemptCount,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? DeliveredAt,
    string? LastError,
    IReadOnlyList<DeliveryAttemptDetails> Attempts)
{
    public static NotificationDetails From(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new NotificationDetails(
            notification.Status,
            notification.Attempts.Count,
            notification.NextAttemptAt,
            notification.DeliveredAt,
            notification.LastError,
            [.. notification.Attempts.Select(attempt => new DeliveryAttemptDetails(attempt.Number, attempt.AttemptedAt, attempt.Succeeded, attempt.Error))]);
    }
}

public sealed record DeliveryAttemptDetails(int Number, DateTimeOffset AttemptedAt, bool Succeeded, string? Error);

/// <summary>Query-side port: reads are projected straight from the store without loading aggregates.</summary>
public interface IOrderReadService
{
    Task<OrderDetails?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);
}
