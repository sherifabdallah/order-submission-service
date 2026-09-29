using OrderSubmission.Domain.Notifications;

namespace OrderSubmission.Application.Abstractions.Notifications;

/// <summary>
/// The transactional outbox as seen by the delivery side. Workers lease due notifications, so any
/// number of worker instances can compete for work without delivering the same one twice.
/// </summary>
public interface INotificationOutbox
{
    /// <summary>Atomically leases up to <paramref name="maxCount"/> notifications that are due for an attempt.</summary>
    Task<IReadOnlyList<NotificationLease>> LeaseDueAsync(int maxCount, CancellationToken cancellationToken);

    /// <summary>Loads the notification for update if the lease is still held; otherwise returns null.</summary>
    Task<Notification?> GetLeasedAsync(NotificationLease lease, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the attempt outcome and releases the lease. Returns false, without writing anything,
    /// if another worker has taken the lease over in the meantime.
    /// </summary>
    Task<bool> CompleteAsync(Notification notification, CancellationToken cancellationToken);
}

public sealed record NotificationLease(Guid NotificationId, Guid Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Lets the write side wake the local delivery worker when new work is committed, so delivery starts
/// immediately rather than on the next polling cycle. This only reduces latency: polling alone is correct.
/// </summary>
public interface INotificationDispatchSignal
{
    void Notify();
}
