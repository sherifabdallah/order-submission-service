using Microsoft.Extensions.Logging;
using OrderSubmission.Application.Abstractions.Messaging;
using OrderSubmission.Application.Abstractions.Notifications;
using OrderSubmission.Application.Abstractions.Persistence;
using OrderSubmission.Application.Common.Results;
using OrderSubmission.Application.Idempotency;
using OrderSubmission.Domain.Notifications;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Application.Orders.PlaceOrder;

/// <summary>
/// Places an order exactly once per <c>Idempotency-Key</c>.
/// </summary>
/// <remarks>
/// The order, its pending notification (outbox) and the idempotency record are committed in a single
/// transaction. No external call happens inside that transaction, so the whole operation is one
/// short atomic write and the unique key on the idempotency record is enough to settle races between
/// concurrent duplicates, across any number of API instances, with no distributed lock.
/// </remarks>
internal sealed partial class PlaceOrderCommandHandler(
    IIdempotencyStore idempotencyStore,
    IOrderRepository orders,
    INotificationRepository notifications,
    IOrderReadService readService,
    IUnitOfWork unitOfWork,
    INotificationDispatchSignal dispatchSignal,
    TimeProvider timeProvider,
    ILogger<PlaceOrderCommandHandler> logger)
    : ICommandHandler<PlaceOrderCommand, PlaceOrderResult>
{
    public async Task<Result<PlaceOrderResult>> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var fingerprint = RequestFingerprint.Compute(command);

        // Fast path: a retry of a request that has already been committed.
        var existing = await idempotencyStore.FindAsync(command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await ReplayAsync(existing, fingerprint, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        var order = Order.Place(
            command.CustomerReference,
            [.. command.Items.Select(item => new NewOrderLine(item.ProductCode, item.Quantity, item.UnitPrice))],
            now);
        var notification = Notification.OrderConfirmation(order, now);

        orders.Add(order);
        notifications.Add(notification);
        idempotencyStore.Add(IdempotencyRecord.Create(command.IdempotencyKey, fingerprint, order.Id, now));

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // A concurrent request with the same key committed first; our transaction was rolled back
            // as a whole. Answer with the winner's outcome.
            var winner = await idempotencyStore.FindAsync(command.IdempotencyKey, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            LogConcurrentDuplicate(command.IdempotencyKey, winner.OrderId);
            return await ReplayAsync(winner, fingerprint, cancellationToken);
        }

        dispatchSignal.Notify();
        LogOrderPlaced(order.Id, order.Lines.Count, order.Total);

        return new PlaceOrderResult(OrderDetails.From(order, notification), IsReplay: false);
    }

    private async Task<Result<PlaceOrderResult>> ReplayAsync(IdempotencyRecord record, string fingerprint, CancellationToken cancellationToken)
    {
        if (!record.Matches(fingerprint))
        {
            LogKeyReused(record.Key, record.OrderId);
            return OrderErrors.IdempotencyKeyReused;
        }

        var order = await readService.GetOrderAsync(record.OrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Idempotency record '{record.Key}' points to missing order '{record.OrderId}'.");

        return new PlaceOrderResult(order, IsReplay: true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Placed order {OrderId} with {LineCount} line(s), total {Total}")]
    private partial void LogOrderPlaced(Guid orderId, int lineCount, decimal total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Concurrent duplicate for idempotency key {IdempotencyKey} resolved to order {OrderId}")]
    private partial void LogConcurrentDuplicate(string idempotencyKey, Guid orderId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency key {IdempotencyKey} (order {OrderId}) was reused with a different payload")]
    private partial void LogKeyReused(string idempotencyKey, Guid orderId);
}
