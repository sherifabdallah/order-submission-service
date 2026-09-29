using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderSubmission.Api.IntegrationTests.Infrastructure;
using OrderSubmission.Application.Orders;

namespace OrderSubmission.Api.IntegrationTests;

/// <summary>Duplicate submission, conflicting payloads and concurrent duplicates.</summary>
public sealed class IdempotencyTests(OrderApiFactory factory) : IClassFixture<OrderApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Repeating_the_same_key_and_payload_returns_the_original_order_without_creating_another()
    {
        var (key, customer) = NewIdentity();
        var body = Order(customer, ("SKU-1", 2, 10.50m), ("SKU-2", 1, 4.99m));

        using var first = await _client.PlaceOrderAsync(key, body);
        using var second = await _client.PlaceOrderAsync(key, body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal("true", Assert.Single(second.Headers.GetValues("Idempotent-Replayed")));

        var original = await first.ReadAsync<OrderDetails>();
        var replayed = await second.ReadAsync<OrderDetails>();
        Assert.Equal(original.Id, replayed.Id);
        Assert.Equal(original.Total, replayed.Total);
        Assert.Equal(original.PlacedAt, replayed.PlacedAt);
        Assert.Equal(original.Items, replayed.Items);
        Assert.Equal($"/api/orders/{original.Id}", first.Headers.Location?.OriginalString);

        Assert.Equal(1, await factory.CountOrdersAsync(customer));
        Assert.Equal(1, await factory.QueryAsync(db => db.Notifications.CountAsync(n => n.OrderId == original.Id, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_retry_that_differs_only_in_formatting_is_still_the_same_request()
    {
        var (key, customer) = NewIdentity();

        using var first = await _client.PlaceOrderAsync(key, Order(customer, ("SKU-1", 2, 10.50m)));
        using var retry = await _client.PlaceOrderAsync(key, new
        {
            items = new[] { new { unitPrice = 10.5m, quantity = 2, productCode = " SKU-1 " } },
            customerReference = $"  {customer}  ",
        });

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((await first.ReadAsync<OrderDetails>()).Id, (await retry.ReadAsync<OrderDetails>()).Id);
        Assert.Equal(1, await factory.CountOrdersAsync(customer));
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_payload_returns_409_and_changes_nothing()
    {
        var (key, customer) = NewIdentity();
        using var first = await _client.PlaceOrderAsync(key, Order(customer, ("SKU-1", 1, 5m)));
        var original = await first.ReadAsync<OrderDetails>();

        using var conflicting = await _client.PlaceOrderAsync(key, Order(customer, ("SKU-1", 99, 5m)));

        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        var problem = await conflicting.ReadAsync<ProblemDetails>();
        Assert.Equal("idempotency.key_reused", problem.Extensions["code"]?.ToString());

        Assert.Equal(1, await factory.CountOrdersAsync(customer));
        using var stored = await _client.GetAsync($"/api/orders/{original.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(5m, (await stored.ReadAsync<OrderDetails>()).Total);
    }

    [Fact]
    public async Task Different_keys_create_different_orders_even_with_identical_payloads()
    {
        var customer = $"CUST-{Guid.NewGuid():N}";
        var body = Order(customer, ("SKU-1", 1, 5m));

        using var first = await _client.PlaceOrderAsync(Guid.NewGuid().ToString(), body);
        using var second = await _client.PlaceOrderAsync(Guid.NewGuid().ToString(), body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(2, await factory.CountOrdersAsync(customer));
    }

    [Fact]
    public async Task Concurrent_requests_with_the_same_key_create_exactly_one_order()
    {
        const int concurrentRequests = 32;
        var (key, customer) = NewIdentity();
        var body = Order(customer, ("SKU-1", 3, 7.25m));
        using var start = new SemaphoreSlim(0, concurrentRequests);

        var requests = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            await start.WaitAsync(TestContext.Current.CancellationToken);
            using var response = await _client.PlaceOrderAsync(key, body);
            return (response.StatusCode, Order: await response.ReadAsync<OrderDetails>());
        }).ToList();
        start.Release(concurrentRequests);
        var results = await Task.WhenAll(requests);

        Assert.All(results, result => Assert.True(result.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"Unexpected {result.StatusCode}"));
        Assert.Single(results, result => result.StatusCode == HttpStatusCode.Created);
        Assert.Single(results.Select(result => result.Order.Id).Distinct());
        Assert.Equal(1, await factory.CountOrdersAsync(customer));
    }

    [Fact]
    public async Task Concurrent_requests_racing_with_different_payloads_create_one_order_and_conflict_the_rest()
    {
        var (key, customer) = NewIdentity();
        var bodies = new[] { Order(customer, ("SKU-A", 1, 1m)), Order(customer, ("SKU-B", 2, 2m)) };

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            using var response = await _client.PlaceOrderAsync(key, bodies[i % 2]);
            return (Payload: i % 2, response.StatusCode);
        }));

        Assert.Equal(1, await factory.CountOrdersAsync(customer));
        var winningPayload = Assert.Single(results, result => result.StatusCode == HttpStatusCode.Created).Payload;
        foreach (var (payload, statusCode) in results)
        {
            if (payload == winningPayload)
            {
                Assert.Contains(statusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK });
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, statusCode);
            }
        }
    }

    [Fact]
    public async Task A_request_without_an_idempotency_key_is_rejected()
    {
        var customer = $"CUST-{Guid.NewGuid():N}";

        using var response = await _client.PlaceOrderAsync(idempotencyKey: null, Order(customer, ("SKU-1", 1, 1m)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadAsync<ValidationProblemDetails>();
        Assert.Contains("idempotencyKey", problem.Errors.Keys);
        Assert.Equal(0, await factory.CountOrdersAsync(customer));
    }

    [Fact]
    public async Task Invalid_line_items_are_rejected_with_field_level_errors()
    {
        using var response = await _client.PlaceOrderAsync(Guid.NewGuid().ToString(), new
        {
            customerReference = "CUST-1",
            items = new[] { new { productCode = "", quantity = 0, unitPrice = -1m } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadAsync<ValidationProblemDetails>();
        Assert.Contains("items[0].productCode", problem.Errors.Keys);
        Assert.Contains("items[0].quantity", problem.Errors.Keys);
        Assert.Contains("items[0].unitPrice", problem.Errors.Keys);
    }

    [Fact]
    public async Task A_rejected_request_does_not_consume_its_key()
    {
        var (key, customer) = NewIdentity();

        using var invalid = await _client.PlaceOrderAsync(key, Order(customer, ("SKU-1", 0, 1m)));
        using var corrected = await _client.PlaceOrderAsync(key, Order(customer, ("SKU-1", 1, 1m)));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
    }

    private static (string Key, string Customer) NewIdentity() => (Guid.NewGuid().ToString(), $"CUST-{Guid.NewGuid():N}");

    private static object Order(string customer, params (string Code, int Quantity, decimal Price)[] lines) => new
    {
        customerReference = customer,
        items = lines.Select(line => new { productCode = line.Code, quantity = line.Quantity, unitPrice = line.Price }).ToArray(),
    };
}
