using OrderSubmission.Application.Idempotency;
using OrderSubmission.Domain.Notifications;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Application.Abstractions.Persistence;

public interface IOrderRepository
{
    void Add(Order order);
}

public interface INotificationRepository
{
    void Add(Notification notification);

    Task<Notification?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);
}

public interface IIdempotencyStore
{
    /// <summary>Reads a committed record from the store, never from pending uncommitted changes.</summary>
    Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken);

    void Add(IdempotencyRecord record);
}

/// <summary>
/// Commits every change tracked in the current scope as one atomic transaction.
/// </summary>
/// <remarks>
/// Store errors the application reacts to surface as <see cref="UniqueConstraintViolationException"/>
/// or <see cref="ConcurrencyConflictException"/>. When a commit fails, its pending changes are discarded.
/// </remarks>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
