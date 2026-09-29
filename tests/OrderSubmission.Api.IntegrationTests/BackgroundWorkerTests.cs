using System.Net;
using OrderSubmission.Api.IntegrationTests.Infrastructure;
using OrderSubmission.Application.Abstractions.Notifications;
using OrderSubmission.Application.Orders;
using OrderSubmission.Domain.Notifications;

namespace OrderSubmission.Api.IntegrationTests;

/// <summary>End-to-end: the real hosted worker, real time, no manual dispatching.</summary>
public sealed class BackgroundWorkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task The_background_worker_delivers_new_orders_without_any_manual_step()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();

        var orderId = await PlaceOrderAsync(client);

        var order = await WaitForAsync(client, orderId, order => order.Notification.Status == NotificationStatus.Delivered);
        Assert.Equal(1, order.Notification.AttemptCount);
    }

    [Fact]
    public async Task The_background_worker_keeps_retrying_through_an_outage_and_delivers_after_recovery()
    {
        await using var app = await StartAsync();
        var client = app.CreateClient();
        app.SetNotificationService(NotificationServiceMode.Outage);

        var orderId = await PlaceOrderAsync(client);
        var failing = await WaitForAsync(client, orderId, order => order.Notification.AttemptCount >= 2);
        Assert.Equal(NotificationStatus.Pending, failing.Notification.Status);
        Assert.All(failing.Notification.Attempts, attempt => Assert.False(attempt.Succeeded));

        app.SetNotificationService(NotificationServiceMode.Healthy);

        var recovered = await WaitForAsync(client, orderId, order => order.Notification.Status == NotificationStatus.Delivered);
        Assert.True(recovered.Notification.Attempts[^1].Succeeded);
        Assert.Single(app.NotificationService.Sent, message => message.OrderId == orderId);
    }

    private static async Task<OrderApiFactory> StartAsync()
    {
        var factory = new OrderApiFactory { UseRealTime = true }
            .UseSetting("Notifications:Dispatcher:Enabled", "true")
            .UseSetting("Notifications:Dispatcher:PollingInterval", "00:00:00.050")
            .UseSetting("Notifications:Delivery:InitialRetryDelay", "00:00:00.100")
            .UseSetting("Notifications:Delivery:MaxRetryDelay", "00:00:00.200")
            .UseSetting("Notifications:Delivery:MaxAttempts", "100");
        await ((IAsyncLifetime)factory).InitializeAsync();
        return factory;
    }

    private static async Task<Guid> PlaceOrderAsync(HttpClient client)
    {
        using var response = await client.PlaceOrderAsync(Guid.NewGuid().ToString(), new
        {
            customerReference = "CUST-1",
            items = new[] { new { productCode = "SKU-1", quantity = 1, unitPrice = 1m } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadAsync<OrderDetails>()).Id;
    }

    private static async Task<OrderDetails> WaitForAsync(HttpClient client, Guid orderId, Func<OrderDetails, bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            using var response = await client.GetAsync($"/api/orders/{orderId}", TestContext.Current.CancellationToken);
            var order = await response.ReadAsync<OrderDetails>();
            if (condition(order))
            {
                return order;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Condition not met within {Timeout}. Last notification state: {order.Notification}");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
