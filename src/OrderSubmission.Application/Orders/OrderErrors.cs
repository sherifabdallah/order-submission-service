using OrderSubmission.Application.Common.Results;

namespace OrderSubmission.Application.Orders;

public static class OrderErrors
{
    public static Error NotFound(Guid orderId) =>
        Error.NotFound("order.not_found", $"Order '{orderId}' was not found.");

    public static readonly Error IdempotencyKeyReused = Error.Conflict(
        "idempotency.key_reused",
        "This Idempotency-Key was already used with a different request payload. Use a new key for a new order.");

    public static readonly Error NotificationAlreadyDelivered = Error.Conflict(
        "notification.already_delivered",
        "The order notification has already been delivered.");

    public static readonly Error NotificationBusy = Error.Conflict(
        "notification.busy",
        "The notification is being delivered right now. Refresh and try again.");
}
