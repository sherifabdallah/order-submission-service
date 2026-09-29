using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OrderSubmission.Api.IntegrationTests.Infrastructure;
using OrderSubmission.Application.Abstractions.Notifications;
using OrderSubmission.Application.Orders;
using OrderSubmission.Domain.Notifications;

namespace OrderSubmission.Api.IntegrationTests;

/// <summary>
/// Notification delivery driven one dispatch cycle at a time, with a fake clock. Each test gets its
/// own application instance and database so failure modes cannot leak between tests.
/// </summary>
public sealed class NotificationDeliveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pending_notification_is_delivered_on_the_next_dispatch_cycle()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var orderId = await PlaceOrderAsync(client);

        Assert.Equal(1, await app.DispatchAsync());

        var order = await GetOrderAsync(client, orderId);
        Assert.Equal(NotificationStatus.Delivered, order.Notification.Status);
        Assert.Equal(app.Time.GetUtcNow(), order.Notification.DeliveredAt);
        Assert.True(Assert.Single(order.Notification.Attempts).Succeeded);
        Assert.Single(app.NotificationService.Sent, message => message.OrderId == orderId);
    }

    [Fact]
    public async Task A_failed_delivery_stays_retryable_and_recovers_once_the_service_is_back()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        app.SetNotificationService(NotificationServiceMode.Outage);
        var orderId = await PlaceOrderAsync(client);

        // Attempt 1 fails: still Pending, retry scheduled 2s later (InitialRetryDelay, no jitter).
        await app.DispatchAsync();
        var afterFirstFailure = (await GetOrderAsync(client, orderId)).Notification;
        Assert.Equal(NotificationStatus.Pending, afterFirstFailure.Status);
        Assert.Equal(1, afterFirstFailure.AttemptCount);
        Assert.Contains("503", afterFirstFailure.LastError, StringComparison.Ordinal);
        Assert.Equal(app.Time.GetUtcNow().AddSeconds(2), afterFirstFailure.NextAttemptAt);

        // Back-off is respected: nothing is attempted before the retry is due.
        Assert.Equal(0, await app.DispatchAsync());
        app.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, await app.DispatchAsync());

        // Attempt 2 fails too; the delay doubles.
        app.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, await app.DispatchAsync());
        var afterSecondFailure = (await GetOrderAsync(client, orderId)).Notification;
        Assert.Equal(2, afterSecondFailure.AttemptCount);
        Assert.Equal(app.Time.GetUtcNow().AddSeconds(4), afterSecondFailure.NextAttemptAt);

        // The service recovers; the next due attempt delivers.
        app.SetNotificationService(NotificationServiceMode.Healthy);
        app.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, await app.DispatchAsync());

        var recovered = (await GetOrderAsync(client, orderId)).Notification;
        Assert.Equal(NotificationStatus.Delivered, recovered.Status);
        Assert.Null(recovered.LastError);
        Assert.Null(recovered.NextAttemptAt);
        Assert.Equal([false, false, true], recovered.Attempts.Select(attempt => attempt.Succeeded));
        Assert.Single(app.NotificationService.Sent, message => message.OrderId == orderId);
    }

    [Fact]
    public async Task Exhausted_retries_mark_the_notification_failed_and_a_manual_retry_requeues_it()
    {
        await using var app = await StartAsync(factory => factory.UseSetting("Notifications:Delivery:MaxAttempts", "3"));
        var client = app.CreateClient();
        app.SetNotificationService(NotificationServiceMode.Outage);
        var orderId = await PlaceOrderAsync(client);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(1, await app.DispatchAsync());
            app.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var failed = (await GetOrderAsync(client, orderId)).Notification;
        Assert.Equal(NotificationStatus.Failed, failed.Status);
        Assert.Equal(3, failed.AttemptCount);
        Assert.Null(failed.NextAttemptAt);

        // Failed notifications are not retried automatically...
        app.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, await app.DispatchAsync());

        // ...but remain retryable on demand, with a fresh retry budget.
        app.SetNotificationService(NotificationServiceMode.Healthy);
        using var retry = await client.PostAsync($"/api/orders/{orderId}/notification/retry", content: null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(NotificationStatus.Pending, (await retry.ReadAsync<OrderDetails>()).Notification.Status);

        Assert.Equal(1, await app.DispatchAsync());
        var delivered = (await GetOrderAsync(client, orderId)).Notification;
        Assert.Equal(NotificationStatus.Delivered, delivered.Status);
        Assert.Equal(4, delivered.AttemptCount);
    }

    [Fact]
    public async Task Retry_now_skips_the_remaining_backoff_of_a_pending_notification()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        app.SetNotificationService(NotificationServiceMode.Outage);
        var orderId = await PlaceOrderAsync(client);
        await app.DispatchAsync();
        app.SetNotificationService(NotificationServiceMode.Healthy);
        Assert.Equal(0, await app.DispatchAsync()); // not due yet

        using var retry = await client.PostAsync($"/api/orders/{orderId}/notification/retry", content: null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);

        Assert.Equal(1, await app.DispatchAsync());
        Assert.Equal(NotificationStatus.Delivered, (await GetOrderAsync(client, orderId)).Notification.Status);
    }

    [Fact]
    public async Task Retrying_a_delivered_notification_is_a_conflict_and_unknown_orders_are_404()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var orderId = await PlaceOrderAsync(client);
        await app.DispatchAsync();

        using var delivered = await client.PostAsync($"/api/orders/{orderId}/notification/retry", content: null, Ct);
        using var unknown = await client.PostAsync($"/api/orders/{Guid.NewGuid()}/notification/retry", content: null, Ct);

        Assert.Equal(HttpStatusCode.Conflict, delivered.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Competing_dispatchers_deliver_every_notification_exactly_once()
    {
        const int orders = 40;
        await using var app = await StartAsync(factory => factory.UseSetting("Notifications:Dispatcher:BatchSize", "5"));
        var client = app.CreateClient();
        var orderIds = new HashSet<Guid>();
        for (var i = 0; i < orders; i++)
        {
            orderIds.Add(await PlaceOrderAsync(client));
        }

        // Four "worker instances" drain the same outbox concurrently.
        var leasedPerWorker = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            var leased = 0;
            int batch;
            while ((batch = await app.DispatchAsync()) > 0)
            {
                leased += batch;
            }

            return leased;
        }, Ct)));

        Assert.Equal(orders, leasedPerWorker.Sum());
        var sent = app.NotificationService.Sent.Where(message => orderIds.Contains(message.OrderId)).ToList();
        Assert.Equal(orders, sent.Count);
        Assert.Equal(orders, sent.Select(message => message.NotificationId).Distinct().Count());
    }

    [Fact]
    public async Task A_worker_that_lost_its_lease_cannot_overwrite_the_new_owners_outcome()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        var orderId = await PlaceOrderAsync(client);

        // Worker A leases the notification and loads it...
        await using var workerA = app.Services.CreateAsyncScope();
        var outboxA = workerA.ServiceProvider.GetRequiredService<INotificationOutbox>();
        var leaseA = Assert.Single(await outboxA.LeaseDueAsync(10, Ct));
        var stale = await outboxA.GetLeasedAsync(leaseA, Ct);
        Assert.NotNull(stale);

        // ...then stalls past its lease. Another worker takes over and delivers.
        app.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await app.DispatchAsync());

        // Worker A wakes up and tries to record a failure: rejected, nothing written.
        stale.RecordFailure("late and stale", app.Time.GetUtcNow(), retryAt: null);
        Assert.False(await outboxA.CompleteAsync(stale, Ct));
        Assert.Null(await outboxA.GetLeasedAsync(leaseA, Ct));

        var notification = (await GetOrderAsync(client, orderId)).Notification;
        Assert.Equal(NotificationStatus.Delivered, notification.Status);
        Assert.Equal(1, notification.AttemptCount);
    }

    private static async Task<OrderApiFactory> StartAsync(Action<OrderApiFactory>? configure = null)
    {
        var factory = new OrderApiFactory();
        configure?.Invoke(factory);
        await ((IAsyncLifetime)factory).InitializeAsync();
        return factory;
    }

    private static async Task<Guid> PlaceOrderAsync(HttpClient client)
    {
        using var response = await client.PlaceOrderAsync(Guid.NewGuid().ToString(), new
        {
            customerReference = "CUST-1",
            items = new[] { new { productCode = "SKU-1", quantity = 1, unitPrice = 9.99m } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<OrderDetails>()).Id;
    }

    private static async Task<OrderDetails> GetOrderAsync(HttpClient client, Guid orderId)
    {
        using var response = await client.GetAsync($"/api/orders/{orderId}", Ct);
        response.EnsureSuccessStatusCode();
        return await response.ReadAsync<OrderDetails>();
    }
}
