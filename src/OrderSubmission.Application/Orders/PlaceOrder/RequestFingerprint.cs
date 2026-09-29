using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace OrderSubmission.Application.Orders.PlaceOrder;

/// <summary>
/// Stable hash of the business content of a place-order request, used to tell a genuine retry
/// (same key, same payload) from a key reused for a different order (same key, new payload).
/// </summary>
/// <remarks>
/// The hash is taken over a canonical form of the command, not the raw HTTP body, so JSON
/// formatting, property order, surrounding whitespace and numerically equal prices (10.5 and
/// 10.50) do not produce false conflicts. Line order is significant: lines are an ordered list.
/// </remarks>
internal static class RequestFingerprint
{
    private const int Version = 1;

    public static string Compute(PlaceOrderCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var canonical = new CanonicalOrder(
            Version,
            command.CustomerReference.Trim(),
            [.. command.Items.Select(item => new CanonicalItem(item.ProductCode.Trim(), item.Quantity, Normalize(item.UnitPrice)))]);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    // "10.50" and "10.5" are the same price; strip insignificant trailing zeros.
    private static string Normalize(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private sealed record CanonicalOrder(int V, string Customer, CanonicalItem[] Items);

    private sealed record CanonicalItem(string Code, int Qty, string Price);
}
