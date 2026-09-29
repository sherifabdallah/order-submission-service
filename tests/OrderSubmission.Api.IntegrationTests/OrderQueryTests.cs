using System.Net;
using Microsoft.EntityFrameworkCore;
using OrderSubmission.Api.IntegrationTests.Infrastructure;
using OrderSubmission.Application.Orders;
using OrderSubmission.Domain.Notifications;

namespace OrderSubmission.Api.IntegrationTests;

public sealed class OrderQueryTests(OrderApiFactory factory) : IClassFixture<OrderApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_returns_the_order_with_its_server_calculated_total_and_delivery_status()
    {
        using var created = await _client.PlaceOrderAsync(Guid.NewGuid().ToString(), new
        {
            customerReference = "CUST-42",
            items = new[]
            {
                new { productCode = "SKU-1", quantity = 3, unitPrice = 19.99m },
                new { productCode = "SKU-2", quantity = 2, unitPrice = 0.50m },
            },
        });
        var id = (await created.ReadAsync<OrderDetails>()).Id;

        using var response = await _client.GetAsync($"/api/orders/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.ReadAsync<OrderDetails>();
        Assert.Equal("CUST-42", order.CustomerReference);
        Assert.Equal(60.97m, order.Total);
        Assert.Equal([59.97m, 1.00m], order.Items.Select(item => item.LineTotal));
        Assert.Equal(["SKU-1", "SKU-2"], order.Items.Select(item => item.ProductCode));
        Assert.Equal(NotificationStatus.Pending, order.Notification.Status);
        Assert.Equal(0, order.Notification.AttemptCount);
        Assert.Equal(factory.Time.GetUtcNow(), order.Notification.NextAttemptAt);
    }

    [Fact]
    public async Task Get_for_an_unknown_order_returns_404()
    {
        using var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Order_and_pending_notification_are_saved_atomically()
    {
        var key = Guid.NewGuid().ToString();
        var customer = $"CUST-{Guid.NewGuid():N}";
        var body = new { customerReference = customer, items = new[] { new { productCode = "SKU-1", quantity = 1, unitPrice = 1m } } };

        // The notification INSERT executes, then fails: the order written in the same transaction must roll back.
        factory.Faults.FailNotificationInserts = true;
        try
        {
            using var failed = await _client.PlaceOrderAsync(key, body);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }
        finally
        {
            factory.Faults.FailNotificationInserts = false;
        }

        Assert.Equal(0, await factory.CountOrdersAsync(customer));
        Assert.False(await factory.QueryAsync(db => db.IdempotencyRecords.AnyAsync(record => record.Key == key, TestContext.Current.CancellationToken)));

        // Nothing was committed, so the client's retry with the same key simply succeeds.
        using var retried = await _client.PlaceOrderAsync(key, body);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        var order = await retried.ReadAsync<OrderDetails>();
        Assert.Equal(1, await factory.QueryAsync(db => db.Notifications.CountAsync(n => n.OrderId == order.Id, TestContext.Current.CancellationToken)));
    }
}
