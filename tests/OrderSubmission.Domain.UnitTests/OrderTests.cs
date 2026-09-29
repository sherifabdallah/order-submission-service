using OrderSubmission.Domain.Common;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Domain.UnitTests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Place_calculates_line_totals_and_order_total_on_the_server()
    {
        var order = Order.Place("CUST-1", [new("SKU-1", 3, 19.99m), new("SKU-2", 1, 0.01m)], Now);

        Assert.Equal(59.97m, order.Lines[0].LineTotal);
        Assert.Equal(0.01m, order.Lines[1].LineTotal);
        Assert.Equal(59.98m, order.Total);
        Assert.Equal(Now, order.PlacedAt);
        Assert.NotEqual(Guid.Empty, order.Id);
    }

    [Fact]
    public void Place_numbers_lines_in_submission_order_and_trims_text()
    {
        var order = Order.Place("  CUST-1  ", [new(" B ", 1, 1m), new("A", 1, 1m)], Now);

        Assert.Equal("CUST-1", order.CustomerReference);
        Assert.Equal([1, 2], order.Lines.Select(line => line.LineNumber));
        Assert.Equal(["B", "A"], order.Lines.Select(line => line.ProductCode));
    }

    [Fact]
    public void Place_without_lines_is_rejected()
    {
        var exception = Assert.Throws<DomainException>(() => Order.Place("CUST-1", [], Now));

        Assert.Equal("order.lines_required", exception.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Place_without_customer_reference_is_rejected(string reference)
    {
        var exception = Assert.Throws<DomainException>(() => Order.Place(reference, [new("SKU", 1, 1m)], Now));

        Assert.Equal("order.customer_reference_required", exception.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(OrderLine.MaxQuantity + 1)]
    public void Place_rejects_quantity_out_of_range(int quantity)
    {
        var exception = Assert.Throws<DomainException>(() => Order.Place("CUST-1", [new("SKU", quantity, 1m)], Now));

        Assert.Equal("order_line.quantity_out_of_range", exception.Code);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Place_rejects_non_positive_unit_price(string price)
    {
        var exception = Assert.Throws<DomainException>(() => Order.Place("CUST-1", [new("SKU", 1, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture))], Now));

        Assert.Equal("order_line.unit_price_out_of_range", exception.Code);
    }

    [Fact]
    public void Place_rejects_sub_cent_prices_but_accepts_trailing_zeros()
    {
        var exception = Assert.Throws<DomainException>(() => Order.Place("CUST-1", [new("SKU", 1, 1.005m)], Now));
        Assert.Equal("order_line.unit_price_precision", exception.Code);

        var order = Order.Place("CUST-1", [new("SKU", 2, 1.500m)], Now);
        Assert.Equal(3m, order.Total);
    }

    [Fact]
    public void Place_rejects_more_lines_than_allowed()
    {
        var lines = Enumerable.Range(0, Order.MaxLines + 1).Select(i => new NewOrderLine($"SKU-{i}", 1, 1m)).ToList();

        var exception = Assert.Throws<DomainException>(() => Order.Place("CUST-1", lines, Now));

        Assert.Equal("order.too_many_lines", exception.Code);
    }
}
