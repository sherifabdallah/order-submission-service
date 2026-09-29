using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderSubmission.Application.Abstractions.Notifications;
using OrderSubmission.Application.Notifications;

namespace OrderSubmission.Infrastructure.Notifications;

public sealed class NotificationDispatcherOptions
{
    public const string SectionName = "Notifications:Dispatcher";

    /// <summary>
    /// Runs the delivery worker in this process. Turn it off on API-only nodes and run dedicated
    /// worker nodes when API and delivery need to scale independently.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Notifications leased per polling cycle.</summary>
    [Range(1, 1_000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>Deliveries in flight at once per worker instance.</summary>
    [Range(1, 256)]
    public int MaxConcurrency { get; set; } = 8;

    /// <summary>How often to look for due work when idle (new orders on this node wake the worker immediately).</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Runs one dispatch cycle: lease a batch of due notifications, then deliver them concurrently,
/// each in its own DI scope (and therefore its own DbContext).
/// </summary>
public sealed partial class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationDispatcherOptions> options,
    ILogger<NotificationDispatcher> logger)
{
    private readonly NotificationDispatcherOptions _options = options.Value;

    /// <returns>The number of notifications leased in this cycle.</returns>
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationLease> leases;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<INotificationOutbox>();
            leases = await outbox.LeaseDueAsync(_options.BatchSize, cancellationToken);
        }

        if (leases.Count == 0)
        {
            return 0;
        }

        var parallelism = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxConcurrency,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(leases, parallelism, DeliverAsync);
        return leases.Count;
    }

    private async ValueTask DeliverAsync(NotificationLease lease, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var delivery = scope.ServiceProvider.GetRequiredService<NotificationDeliveryService>();
            await delivery.DeliverAsync(lease, cancellationToken);
        }
#pragma warning disable CA1031 // One bad notification must not abort the rest of the batch.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // Nothing was recorded; the lease expires and the notification is picked up again.
            LogDeliveryCrashed(exception, lease.NotificationId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error while delivering notification {NotificationId}; it will be retried when its lease expires")]
    private partial void LogDeliveryCrashed(Exception exception, Guid notificationId);
}
