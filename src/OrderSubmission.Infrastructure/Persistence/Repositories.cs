using Microsoft.EntityFrameworkCore;
using OrderSubmission.Application.Abstractions.Persistence;
using OrderSubmission.Application.Idempotency;
using OrderSubmission.Domain.Notifications;
using OrderSubmission.Domain.Orders;
using OrderSubmission.Infrastructure.Persistence.Providers;

namespace OrderSubmission.Infrastructure.Persistence;

internal sealed class OrderRepository(OrderSubmissionDbContext db) : IOrderRepository
{
    public void Add(Order order) => db.Orders.Add(order);
}

internal sealed class NotificationRepository(OrderSubmissionDbContext db) : INotificationRepository
{
    public void Add(Notification notification) => db.Notifications.Add(notification);

    public Task<Notification?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Notifications.FirstOrDefaultAsync(notification => notification.OrderId == orderId, cancellationToken);
}

internal sealed class IdempotencyStore(OrderSubmissionDbContext db) : IIdempotencyStore
{
    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(record => record.Key == key, cancellationToken);

    public void Add(IdempotencyRecord record) => db.IdempotencyRecords.Add(record);
}

internal sealed class UnitOfWork(OrderSubmissionDbContext db, IDatabaseProvider provider) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        // SaveChanges wraps every pending insert/update in one database transaction.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            db.ChangeTracker.Clear();
            throw new ConcurrencyConflictException("The data was modified by another process.", exception);
        }
        catch (DbUpdateException exception) when (provider.IsUniqueConstraintViolation(exception))
        {
            db.ChangeTracker.Clear();
            throw new UniqueConstraintViolationException("A row with the same unique key already exists.", exception);
        }
    }
}
