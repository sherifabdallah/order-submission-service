using OrderSubmission.Application.Abstractions.Messaging;
using OrderSubmission.Application.Abstractions.Notifications;
using OrderSubmission.Application.Abstractions.Persistence;
using OrderSubmission.Application.Common.Results;
using OrderSubmission.Application.Orders;
using OrderSubmission.Domain.Notifications;

namespace OrderSubmission.Application.Notifications.RetryDelivery;

/// <summary>
/// Makes an order's notification due immediately: re-queues a Failed one with a fresh retry budget,
/// or cuts the remaining back-off short for a Pending one.
/// </summary>
public sealed record RetryNotificationDeliveryCommand(Guid OrderId) : ICommand<OrderDetails>;

internal sealed class RetryNotificationDeliveryCommandHandler(
    INotificationRepository notifications,
    IOrderReadService readService,
    IUnitOfWork unitOfWork,
    INotificationDispatchSignal dispatchSignal,
    TimeProvider timeProvider)
    : ICommandHandler<RetryNotificationDeliveryCommand, OrderDetails>
{
    public async Task<Result<OrderDetails>> HandleAsync(RetryNotificationDeliveryCommand command, CancellationToken cancellationToken)
    {
        var notification = await notifications.GetByOrderIdAsync(command.OrderId, cancellationToken);
        if (notification is null)
        {
            return OrderErrors.NotFound(command.OrderId);
        }

        if (notification.Status == NotificationStatus.Delivered)
        {
            return OrderErrors.NotificationAlreadyDelivered;
        }

        notification.RetryNow(timeProvider.GetUtcNow());

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // A worker finished an attempt between our read and our write.
            return OrderErrors.NotificationBusy;
        }

        dispatchSignal.Notify();

        var order = await readService.GetOrderAsync(command.OrderId, cancellationToken);
        return order is null ? OrderErrors.NotFound(command.OrderId) : order;
    }
}
