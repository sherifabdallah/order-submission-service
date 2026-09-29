using OrderSubmission.Application.Orders.PlaceOrder;

namespace OrderSubmission.Api.Contracts;

/// <summary>Body of <c>POST /api/orders</c>. The total is not accepted from clients; the server calculates it.</summary>
public sealed record PlaceOrderRequest(string? CustomerReference, IReadOnlyList<PlaceOrderItemRequest?>? Items)
{
    /// <summary>
    /// Maps the transport shape to the use-case command, trimming insignificant whitespace.
    /// Missing values become empty values so the validator reports them uniformly.
    /// </summary>
    public PlaceOrderCommand ToCommand(string? idempotencyKey) => new(
        idempotencyKey ?? string.Empty,
        CustomerReference?.Trim() ?? string.Empty,
        Items?.Select(item => new PlaceOrderItem(item?.ProductCode?.Trim() ?? string.Empty, item?.Quantity ?? 0, item?.UnitPrice ?? 0m)).ToList() ?? []);
}

public sealed record PlaceOrderItemRequest(string? ProductCode, int Quantity, decimal UnitPrice);
