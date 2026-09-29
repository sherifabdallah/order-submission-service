using OrderSubmission.Application.Orders.PlaceOrder;

namespace OrderSubmission.Application.UnitTests.Orders;

public sealed class RequestFingerprintTests
{
    private static readonly PlaceOrderCommand Original = new(
        "key-1",
        "CUST-1",
        [new("SKU-1", 2, 10.5m), new("SKU-2", 1, 4.99m)]);

    [Fact]
    public void Is_a_sha256_hex_string()
    {
        var fingerprint = RequestFingerprint.Compute(Original);

        Assert.Equal(64, fingerprint.Length);
        Assert.True(fingerprint.All(Uri.IsHexDigit));
    }

    [Fact]
    public void Ignores_the_idempotency_key_insignificant_whitespace_and_trailing_zeros()
    {
        var equivalent = new PlaceOrderCommand(
            "another-key",
            "  CUST-1 ",
            [new(" SKU-1", 2, 10.50m), new("SKU-2 ", 1, 4.990m)]);

        Assert.Equal(RequestFingerprint.Compute(Original), RequestFingerprint.Compute(equivalent));
    }

    public static TheoryData<PlaceOrderCommand> DifferentPayloads => new()
    {
        Original with { CustomerReference = "CUST-2" },
        Original with { Items = [new("SKU-1", 3, 10.5m), new("SKU-2", 1, 4.99m)] },
        Original with { Items = [new("SKU-1", 2, 10.51m), new("SKU-2", 1, 4.99m)] },
        Original with { Items = [new("sku-1", 2, 10.5m), new("SKU-2", 1, 4.99m)] },
        Original with { Items = [new("SKU-2", 1, 4.99m), new("SKU-1", 2, 10.5m)] },
        Original with { Items = [new("SKU-1", 2, 10.5m)] },
    };

    [Theory]
    [MemberData(nameof(DifferentPayloads))]
    public void Changes_when_the_business_content_changes(PlaceOrderCommand changed)
    {
        Assert.NotEqual(RequestFingerprint.Compute(Original), RequestFingerprint.Compute(changed));
    }
}
