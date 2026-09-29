using OrderSubmission.Application.Abstractions.Messaging;

namespace OrderSubmission.Application.Orders.PlaceOrder;

public sealed record PlaceOrderCommand(
    string IdempotencyKey,
    string CustomerReference,
    IReadOnlyList<PlaceOrderItem> Items) : ICommand<PlaceOrderResult>;

public sealed record PlaceOrderItem(string ProductCode, int Quantity, decimal UnitPrice);

/// <param name="Order">The order, as originally created by the first request with this key.</param>
/// <param name="IsReplay">True when the request was a retry and no new order was created.</param>
public sealed record PlaceOrderResult(OrderDetails Order, bool IsReplay);
