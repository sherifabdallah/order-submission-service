using OrderSubmission.Application.Orders.PlaceOrder;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Application.UnitTests.Orders;

public sealed class PlaceOrderCommandValidatorTests
{
    private static readonly PlaceOrderCommand Valid = new("0b9a3f2e-key", "CUST-1", [new("SKU-1", 1, 9.99m)]);

    private readonly PlaceOrderCommandValidator _validator = new();

    [Fact]
    public void Accepts_a_well_formed_command()
    {
        Assert.True(_validator.Validate(Valid).IsValid);
    }

    public static TheoryData<PlaceOrderCommand, string> InvalidCommands => new()
    {
        { Valid with { IdempotencyKey = "" }, "IdempotencyKey" },
        { Valid with { IdempotencyKey = "has space" }, "IdempotencyKey" },
        { Valid with { IdempotencyKey = new string('k', 129) }, "IdempotencyKey" },
        { Valid with { CustomerReference = "" }, "CustomerReference" },
        { Valid with { CustomerReference = new string('c', Order.CustomerReferenceMaxLength + 1) }, "CustomerReference" },
        { Valid with { Items = [] }, "Items" },
        { Valid with { Items = [new("", 1, 1m)] }, "Items[0].ProductCode" },
        { Valid with { Items = [new("SKU", 0, 1m)] }, "Items[0].Quantity" },
        { Valid with { Items = [new("SKU", -3, 1m)] }, "Items[0].Quantity" },
        { Valid with { Items = [new("SKU", 1, 0m)] }, "Items[0].UnitPrice" },
        { Valid with { Items = [new("SKU", 1, 1.999m)] }, "Items[0].UnitPrice" },
        { Valid with { Items = [.. Enumerable.Repeat(new PlaceOrderItem("SKU", 1, 1m), Order.MaxLines + 1)] }, "Items" },
    };

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public void Rejects_invalid_input_on_the_offending_field(PlaceOrderCommand command, string property)
    {
        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == property);
    }
}
