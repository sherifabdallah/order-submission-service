namespace OrderSubmission.Domain.Notifications;

/// <summary>Immutable record of a single delivery attempt, kept for operational visibility.</summary>
public sealed class DeliveryAttempt
{
    // Required by EF Core.
    private DeliveryAttempt()
    {
    }

    private DeliveryAttempt(int number, DateTimeOffset attemptedAt, bool succeeded, string? error)
    {
        Number = number;
        AttemptedAt = attemptedAt;
        Succeeded = succeeded;
        Error = error;
    }

    public int Number { get; private set; }

    public DateTimeOffset AttemptedAt { get; private set; }

    public bool Succeeded { get; private set; }

    public string? Error { get; private set; }

    internal static DeliveryAttempt Success(int number, DateTimeOffset attemptedAt) =>
        new(number, attemptedAt, succeeded: true, error: null);

    internal static DeliveryAttempt Failure(int number, DateTimeOffset attemptedAt, string error) =>
        new(number, attemptedAt, succeeded: false, error);
}
