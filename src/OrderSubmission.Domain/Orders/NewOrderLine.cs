namespace OrderSubmission.Domain.Orders;

/// <summary>Input used to place an order line; the domain derives the line total itself.</summary>
public sealed record NewOrderLine(string ProductCode, int Quantity, decimal UnitPrice);
