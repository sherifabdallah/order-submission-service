using OrderSubmission.Domain.Common;

namespace OrderSubmission.Domain.Orders;

public sealed class OrderLine
{
    public const int ProductCodeMaxLength = 50;
    public const int MaxQuantity = 10_000;
    public const decimal MaxUnitPrice = 1_000_000m;
    public const int UnitPriceDecimals = 2;

    // Required by EF Core.
    private OrderLine()
    {
        ProductCode = string.Empty;
    }

    private OrderLine(int lineNumber, string productCode, int quantity, decimal unitPrice)
    {
        LineNumber = lineNumber;
        ProductCode = productCode;
        Quantity = quantity;
        UnitPrice = unitPrice;
        LineTotal = quantity * unitPrice;
    }

    /// <summary>1-based position of the line, preserving the order in which items were submitted.</summary>
    public int LineNumber { get; private set; }

    public string ProductCode { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal LineTotal { get; private set; }

    internal static OrderLine Create(int lineNumber, NewOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (string.IsNullOrWhiteSpace(line.ProductCode))
        {
            throw new DomainException("order_line.product_code_required", "Product code is required.");
        }

        var productCode = line.ProductCode.Trim();
        if (productCode.Length > ProductCodeMaxLength)
        {
            throw new DomainException(
                "order_line.product_code_too_long",
                $"Product code must be at most {ProductCodeMaxLength} characters.");
        }

        if (line.Quantity is <= 0 or > MaxQuantity)
        {
            throw new DomainException(
                "order_line.quantity_out_of_range",
                $"Quantity must be between 1 and {MaxQuantity}.");
        }

        if (line.UnitPrice is <= 0 or > MaxUnitPrice)
        {
            throw new DomainException(
                "order_line.unit_price_out_of_range",
                $"Unit price must be greater than 0 and at most {MaxUnitPrice}.");
        }

        if (decimal.Round(line.UnitPrice, UnitPriceDecimals) != line.UnitPrice)
        {
            throw new DomainException(
                "order_line.unit_price_precision",
                $"Unit price must have at most {UnitPriceDecimals} decimal places.");
        }

        return new OrderLine(lineNumber, productCode, line.Quantity, line.UnitPrice);
    }
}
