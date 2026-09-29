using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderSubmission.Application.Abstractions.Notifications;

namespace OrderSubmission.Infrastructure.Notifications;

/// <summary>
/// Background worker that drains the notification outbox. It runs back-to-back cycles while there is
/// a backlog, and otherwise sleeps until the polling interval elapses or a new order wakes it up.
/// </summary>
internal sealed partial class NotificationDispatchWorker(
    NotificationDispatcher dispatcher,
    NotificationDispatchSignal signal,
    IOptions<NotificationDispatcherOptions> options,
    ILogger<NotificationDispatchWorker> logger) : BackgroundService
{
    private readonly NotificationDispatcherOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            LogDisabled();
            return;
        }

        LogStarted(_options.BatchSize, _options.MaxConcurrency, _options.PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            var leased = 0;
            try
            {
                leased = await dispatcher.DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // e.g. database briefly unreachable: keep the worker alive and try again.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogCycleFailed(exception);
            }

            if (leased < _options.BatchSize)
            {
                await signal.WaitAsync(_options.PollingInterval, stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification dispatcher disabled on this instance")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification dispatcher started (batch {BatchSize}, concurrency {MaxConcurrency}, polling every {PollingInterval})")]
    private partial void LogStarted(int batchSize, int maxConcurrency, TimeSpan pollingInterval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification dispatch cycle failed; retrying after the polling interval")]
    private partial void LogCycleFailed(Exception exception);
}

/// <summary>In-process wake-up signal. Multiple notifications coalesce into a single wake-up.</summary>
internal sealed class NotificationDispatchSignal : INotificationDispatchSignal, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A wake-up is already pending.
        }
    }

    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await _signal.WaitAsync(timeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    public void Dispose() => _signal.Dispose();
}
