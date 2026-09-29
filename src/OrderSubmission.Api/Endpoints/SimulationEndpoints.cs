using OrderSubmission.Api.Contracts;
using OrderSubmission.Application.Abstractions.Notifications;

namespace OrderSubmission.Api.Endpoints;

/// <summary>Runtime control of the fake notification service, used to demonstrate failure and recovery.</summary>
internal static class SimulationEndpoints
{
    public static IEndpointRouteBuilder MapSimulationEndpoints(this IEndpointRouteBuilder app)
    {
        var simulation = app.MapGroup("/api/simulation/notification-service").WithTags("Simulation");

        simulation.MapGet("/", (INotificationServiceSimulator simulator) =>
                TypedResults.Ok(NotificationServiceSimulationResponse.From(simulator)))
            .WithName("GetNotificationServiceSimulation")
            .WithSummary("Get the fake notification service behaviour");

        simulation.MapPut("/", Update)
            .WithName("UpdateNotificationServiceSimulation")
            .WithSummary("Change the fake notification service behaviour (Healthy, Flaky or Outage)")
            .Produces<NotificationServiceSimulationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static IResult Update(NotificationServiceSimulationRequest request, INotificationServiceSimulator simulator)
    {
        if (!simulator.AllowsRuntimeChanges)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Runtime changes disabled",
                detail: "Set Notifications:FakeService:AllowRuntimeChanges to true to change the simulation at runtime.");
        }

        var current = simulator.Current;
        var failureRate = request.FailureRate ?? current.FailureRate;
        var latency = request.LatencyMs is { } ms ? TimeSpan.FromMilliseconds(ms) : current.Latency;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.Mode is null)
        {
            errors["mode"] = ["Mode is required (Healthy, Flaky or Outage)."];
        }

        if (failureRate is < 0 or > 1)
        {
            errors["failureRate"] = ["Failure rate must be between 0 and 1."];
        }

        if (latency < TimeSpan.Zero || latency > NotificationServiceSettings.MaxLatency)
        {
            errors["latencyMs"] = [$"Latency must be between 0 and {NotificationServiceSettings.MaxLatency.TotalMilliseconds} ms."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        simulator.Apply(new NotificationServiceSettings(request.Mode!.Value, failureRate, latency));
        return TypedResults.Ok(NotificationServiceSimulationResponse.From(simulator));
    }
}
