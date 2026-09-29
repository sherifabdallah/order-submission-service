using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderSubmission.Application.Abstractions.Notifications;
using OrderSubmission.Application.Notifications;
using OrderSubmission.Domain.Notifications;
using OrderSubmission.Infrastructure.Persistence.Providers;

namespace OrderSubmission.Infrastructure.Persistence;

/// <summary>
/// Outbox backed by the Notifications table, with lease-based competing consumers.
/// </summary>
/// <remarks>
/// Leasing is a conditional UPDATE, which is atomic per row on every relational engine, so it
/// needs no engine-specific locking hints. A worker that stalls past its lease loses ownership:
/// its late write fails the <see cref="ShadowProperties.LeaseToken"/> concurrency check and is discarded.
/// </remarks>
internal sealed class EfNotificationOutbox(
    OrderSubmissionDbContext db,
    IDatabaseProvider provider,
    IOptions<NotificationDeliveryOptions> options,
    TimeProvider timeProvider) : INotificationOutbox
{
    public async Task<IReadOnlyList<NotificationLease>> LeaseDueAsync(int maxCount, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var token = Guid.NewGuid();
        var expiresAt = now + options.Value.LeaseDuration;

        var candidates = await Available(now)
            .OrderBy(notification => notification.NextAttemptAt)
            .Select(notification => notification.Id)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        // The UPDATE re-checks availability, so a row another worker claimed after our SELECT is skipped.
        await Available(now)
            .Where(notification => candidates.Contains(notification.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(notification => EF.Property<Guid?>(notification, ShadowProperties.LeaseToken), token)
                    .SetProperty(notification => EF.Property<DateTimeOffset?>(notification, ShadowProperties.LeaseExpiresAt), expiresAt),
                cancellationToken);

        var leased = await db.Notifications
            .Where(notification => candidates.Contains(notification.Id)
                && EF.Property<Guid?>(notification, ShadowProperties.LeaseToken) == token)
            .Select(notification => notification.Id)
            .ToListAsync(cancellationToken);

        return [.. leased.Select(id => new NotificationLease(id, token, expiresAt))];
    }

    public Task<Notification?> GetLeasedAsync(NotificationLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        return db.Notifications.FirstOrDefaultAsync(
            notification => notification.Id == lease.NotificationId
                && EF.Property<Guid?>(notification, ShadowProperties.LeaseToken) == lease.Token,
            cancellationToken);
    }

    public async Task<bool> CompleteAsync(Notification notification, CancellationToken cancellationToken)
    {
        var entry = db.Entry(notification);
        entry.Property<Guid?>(ShadowProperties.LeaseToken).CurrentValue = null;
        entry.Property<DateTimeOffset?>(ShadowProperties.LeaseExpiresAt).CurrentValue = null;

        try
        {
            // UPDATE ... WHERE Id = @id AND LeaseToken = @ourToken, plus the new attempt row, in one transaction.
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || provider.IsUniqueConstraintViolation(exception))
        {
            // Another worker took the lease over (and possibly already recorded the same attempt number).
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private IQueryable<Notification> Available(DateTimeOffset now) =>
        db.Notifications.Where(notification =>
            notification.Status == NotificationStatus.Pending
            && notification.NextAttemptAt <= now
            && (EF.Property<DateTimeOffset?>(notification, ShadowProperties.LeaseExpiresAt) == null
                || EF.Property<DateTimeOffset?>(notification, ShadowProperties.LeaseExpiresAt) <= now));
}
