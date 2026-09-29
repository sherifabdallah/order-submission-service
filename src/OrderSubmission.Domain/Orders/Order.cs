using OrderSubmission.Domain.Common;

namespace OrderSubmission.Domain.Orders;

/// <summary>
/// Order aggregate root. Orders are immutable once placed and the total is always calculated
/// from the lines on the server; clients never supply it.
/// </summary>
public sealed class Order
{
    public const int CustomerReferenceMaxLength = 64;
    public const int MaxLines = 100;

    private readonly List<OrderLine> _lines = [];

    // Required by EF Core.
    private Order()
    {
        CustomerReference = string.Empty;
    }

    private Order(Guid id, string customerReference, DateTimeOffset placedAt)
    {
        Id = id;
        CustomerReference = customerReference;
        PlacedAt = placedAt;
    }

    public Guid Id { get; private set; }

    public string CustomerReference { get; private set; }

    public decimal Total { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public static Order Place(string customerReference, IReadOnlyCollection<NewOrderLine> lines, DateTimeOffset placedAt)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new DomainException("order.customer_reference_required", "Customer reference is required.");
        }

        var reference = customerReference.Trim();
        if (reference.Length > CustomerReferenceMaxLength)
        {
            throw new DomainException(
                "order.customer_reference_too_long",
                $"Customer reference must be at most {CustomerReferenceMaxLength} characters.");
        }

        if (lines.Count == 0)
        {
            throw new DomainException("order.lines_required", "An order must contain at least one line item.");
        }

        if (lines.Count > MaxLines)
        {
            throw new DomainException("order.too_many_lines", $"An order can contain at most {MaxLines} line items.");
        }

        // Version 7 GUIDs are time-ordered, which keeps primary-key inserts append-mostly.
        var order = new Order(Guid.CreateVersion7(placedAt), reference, placedAt);

        var lineNumber = 0;
        foreach (var line in lines)
        {
            order._lines.Add(OrderLine.Create(++lineNumber, line));
        }

        order.Total = order._lines.Sum(line => line.LineTotal);
        return order;
    }
}
