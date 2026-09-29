using System.Text.Json;
using OrderSubmission.Application.Common.Results;

namespace OrderSubmission.Api.Infrastructure;

/// <summary>Translates application errors into RFC 9457 problem details.</summary>
internal static class ProblemResults
{
    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (error is ValidationError validation)
        {
            var errors = validation.Errors.ToDictionary(pair => ToJsonPath(pair.Key), pair => pair.Value, StringComparer.Ordinal);
            return TypedResults.ValidationProblem(errors, title: error.Message, extensions: extensions);
        }

        var (status, title) = error.Type switch
        {
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Resource not found"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status400BadRequest, "Bad request"),
        };

        return TypedResults.Problem(detail: error.Message, statusCode: status, title: title, extensions: extensions);
    }

    // "Items[0].UnitPrice" -> "items[0].unitPrice", matching the JSON the client sent.
    private static string ToJsonPath(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
