using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using OrderSubmission.Domain.Common;

namespace OrderSubmission.Api.Infrastructure;

/// <summary>Last line of defence: every unhandled exception becomes a problem-details response.</summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            BadHttpRequestException { InnerException: JsonException json } => (
                StatusCodes.Status400BadRequest,
                "Malformed request body",
                $"The request body is not valid JSON for this endpoint (at '{json.Path ?? "$"}')."),
            BadHttpRequestException badRequest => (badRequest.StatusCode, "Bad request", badRequest.Message),
            DomainException domain => (StatusCodes.Status400BadRequest, "Business rule violated", domain.Message),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, "Client closed request", null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", null),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = status, Title = title, Detail = detail },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private partial void LogUnhandled(Exception exception, string method, PathString path);
}
