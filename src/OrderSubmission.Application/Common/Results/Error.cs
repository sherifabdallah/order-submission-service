namespace OrderSubmission.Application.Common.Results;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
}

/// <summary>An expected, business-level failure. Unexpected failures are exceptions.</summary>
public record Error(string Code, string Message, ErrorType Type)
{
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
}

public sealed record ValidationError(IReadOnlyDictionary<string, string[]> Errors)
    : Error("validation.failed", "One or more validation errors occurred.", ErrorType.Validation);
