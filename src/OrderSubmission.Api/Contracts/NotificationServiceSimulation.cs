using OrderSubmission.Application.Abstractions.Notifications;

namespace OrderSubmission.Api.Contracts;

public sealed record NotificationServiceSimulationRequest(NotificationServiceMode? Mode, double? FailureRate, int? LatencyMs);

public sealed record NotificationServiceSimulationResponse(NotificationServiceMode Mode, double FailureRate, int LatencyMs, bool AllowsRuntimeChanges)
{
    public static NotificationServiceSimulationResponse From(INotificationServiceSimulator simulator)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        var current = simulator.Current;
        return new(current.Mode, current.FailureRate, (int)current.Latency.TotalMilliseconds, simulator.AllowsRuntimeChanges);
    }
}
