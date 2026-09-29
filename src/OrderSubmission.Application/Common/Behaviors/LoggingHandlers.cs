using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OrderSubmission.Application.Abstractions.Messaging;
using OrderSubmission.Application.Common.Results;

namespace OrderSubmission.Application.Common.Behaviors;

/// <summary>Decorator that records the outcome and duration of every command.</summary>
internal sealed class LoggingCommandHandler<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    ILogger<LoggingCommandHandler<TCommand, TResponse>> logger)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.HandleAsync(command, cancellationToken);
        HandlerLog.Completed(logger, typeof(TCommand).Name, result.IsSuccess, result.Error?.Code, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }
}

/// <summary>Decorator that records the outcome and duration of every query.</summary>
internal sealed class LoggingQueryHandler<TQuery, TResponse>(
    IQueryHandler<TQuery, TResponse> inner,
    ILogger<LoggingQueryHandler<TQuery, TResponse>> logger)
    : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.HandleAsync(query, cancellationToken);
        HandlerLog.Completed(logger, typeof(TQuery).Name, result.IsSuccess, result.Error?.Code, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }
}

internal static partial class HandlerLog
{
    public static void Completed(ILogger logger, string request, bool succeeded, string? errorCode, double elapsedMs)
    {
        if (succeeded)
        {
            Succeeded(logger, request, elapsedMs);
        }
        else
        {
            Failed(logger, request, errorCode, elapsedMs);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Request} succeeded in {ElapsedMs:0.0} ms")]
    private static partial void Succeeded(ILogger logger, string request, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Request} failed with {ErrorCode} in {ElapsedMs:0.0} ms")]
    private static partial void Failed(ILogger logger, string request, string? errorCode, double elapsedMs);
}
