namespace OrderSubmission.Domain.Common;

/// <summary>
/// Raised when an operation would break a domain invariant. Input is validated at the application
/// boundary first, so reaching this exception indicates a programming error or a bypassed validator.
/// </summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
