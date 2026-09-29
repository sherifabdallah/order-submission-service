using Microsoft.EntityFrameworkCore;
using OrderSubmission.Application.Orders;

namespace OrderSubmission.Infrastructure.Persistence;

/// <summary>
/// Query side: projects straight into the read model with no change tracking and no aggregate
/// loading. Both lookups are index seeks (primary key and the OrderId index).
/// </summary>
internal sealed class OrderReadService(OrderSubmissionDbContext db) : IOrderReadService
{
    public async Task<OrderDetails?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => new
            {
                order.Id,
                order.CustomerReference,
                order.Total,
                order.PlacedAt,
                Items = order.Lines
                    .OrderBy(line => line.LineNumber)
                    .Select(line => new OrderLineDetails(line.LineNumber, line.ProductCode, line.Quantity, line.UnitPrice, line.LineTotal))
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var notification = await db.Notifications
            .AsNoTracking()
            .Where(notification => notification.OrderId == orderId)
            .Select(notification => new NotificationDetails(
                notification.Status,
                notification.Attempts.Count,
                notification.NextAttemptAt,
                notification.DeliveredAt,
                notification.LastError,
                notification.Attempts
                    .OrderBy(attempt => attempt.Number)
                    .Select(attempt => new DeliveryAttemptDetails(attempt.Number, attempt.AttemptedAt, attempt.Succeeded, attempt.Error))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Order '{orderId}' has no notification; they are always written together.");

        return new OrderDetails(order.Id, order.CustomerReference, order.Total, order.PlacedAt, order.Items, notification);
    }
}
