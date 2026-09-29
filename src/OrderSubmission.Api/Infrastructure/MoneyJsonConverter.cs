using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderSubmission.Api.Infrastructure;

/// <summary>
/// Every decimal in this API is a money amount with at most two decimal places. Writing them with a
/// fixed scale keeps responses byte-for-byte stable whichever store they were read from
/// (SQLite keeps 10.5, SQL Server returns 10.50).
/// </summary>
internal sealed class MoneyJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Adding 0.00m gives the value a scale of at least two ("10.5" -> "10.50") without changing it.
        writer.WriteNumberValue(decimal.Round(value, 2) + 0.00m);
    }
}
