using OrderSubmission.Application.Common.Results;

namespace OrderSubmission.Application.Abstractions.Messaging;

/// <summary>A request that changes state.</summary>
#pragma warning disable CA1040 // Marker interface binds a command to its response type.
public interface ICommand<TResponse>;
#pragma warning restore CA1040

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
