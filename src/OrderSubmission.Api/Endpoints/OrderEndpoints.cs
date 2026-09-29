using Microsoft.AspNetCore.Mvc;
using OrderSubmission.Api.Contracts;
using OrderSubmission.Api.Infrastructure;
using OrderSubmission.Application.Abstractions.Messaging;
using OrderSubmission.Application.Notifications.RetryDelivery;
using OrderSubmission.Application.Orders;
using OrderSubmission.Application.Orders.GetOrder;
using OrderSubmission.Application.Orders.PlaceOrder;

namespace OrderSubmission.Api.Endpoints;

internal static class OrderEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string IdempotentReplayedHeader = "Idempotent-Replayed";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders");

        orders.MapPost("/", PlaceOrderAsync)
            .WithName("PlaceOrder")
            .WithSummary("Place an order (idempotent)")
            .WithDescription(
                "Requires an Idempotency-Key header. The first request creates the order (201). Repeating the same key with the same " +
                "payload returns the original order (200, Idempotent-Replayed: true) without creating another. Reusing the key with a " +
                "different payload returns 409.")
            .Produces<OrderDetails>(StatusCodes.Status201Created)
            .Produces<OrderDetails>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RateLimiting.OrderWrites);

        orders.MapGet("/{orderId:guid}", GetOrderAsync)
            .WithName("GetOrder")
            .WithSummary("Get an order and its notification delivery status")
            .Produces<OrderDetails>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapPost("/{orderId:guid}/notification/retry", RetryNotificationAsync)
            .WithName("RetryOrderNotification")
            .WithSummary("Retry notification delivery now")
            .WithDescription("Re-queues a Failed notification with a fresh retry budget, or skips the remaining back-off of a Pending one.")
            .Produces<OrderDetails>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireRateLimiting(RateLimiting.OrderWrites);

        return app;
    }

    private static async Task<IResult> PlaceOrderAsync(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        PlaceOrderRequest request,
        ICommandHandler<PlaceOrderCommand, PlaceOrderResult> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(idempotencyKey), cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblem();
        }

        var (order, isReplay) = result.Value;
        if (isReplay)
        {
            httpContext.Response.Headers[IdempotentReplayedHeader] = "true";
            return TypedResults.Ok(order);
        }

        return TypedResults.Created($"/api/orders/{order.Id}", order);
    }

    private static async Task<IResult> GetOrderAsync(
        Guid orderId,
        IQueryHandler<GetOrderQuery, OrderDetails> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetOrderQuery(orderId), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<IResult> RetryNotificationAsync(
        Guid orderId,
        ICommandHandler<RetryNotificationDeliveryCommand, OrderDetails> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new RetryNotificationDeliveryCommand(orderId), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Accepted($"/api/orders/{orderId}", result.Value)
            : result.Error.ToProblem();
    }
}
