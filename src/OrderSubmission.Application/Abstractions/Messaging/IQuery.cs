using OrderSubmission.Application.Common.Results;

namespace OrderSubmission.Application.Abstractions.Messaging;

/// <summary>A side-effect-free request.</summary>
#pragma warning disable CA1040 // Marker interface binds a query to its response type.
public interface IQuery<TResponse>;
#pragma warning restore CA1040

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
