namespace OrderSubmission.Application.Idempotency;

/// <summary>
/// Remembers which order an <c>Idempotency-Key</c> produced and a fingerprint of the request
/// that created it. The key is the primary key, so the database itself guarantees that two
/// concurrent requests with the same key can never both commit.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int KeyMaxLength = 128;

    // Required by EF Core.
    private IdempotencyRecord()
    {
        Key = string.Empty;
        RequestFingerprint = string.Empty;
    }

    private IdempotencyRecord(string key, string requestFingerprint, Guid orderId, DateTimeOffset createdAt)
    {
        Key = key;
        RequestFingerprint = requestFingerprint;
        OrderId = orderId;
        CreatedAt = createdAt;
    }

    public string Key { get; private set; }

    public string RequestFingerprint { get; private set; }

    public Guid OrderId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static IdempotencyRecord Create(string key, string requestFingerprint, Guid orderId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        return new IdempotencyRecord(key, requestFingerprint, orderId, createdAt);
    }

    public bool Matches(string requestFingerprint) =>
        string.Equals(RequestFingerprint, requestFingerprint, StringComparison.Ordinal);
}
