using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OrderSubmission.Api.Endpoints;
using OrderSubmission.Api.Infrastructure;
using Scalar.AspNetCore;

namespace OrderSubmission.Api;

internal static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier));
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.Converters.Add(new MoneyJsonConverter());
        });

        services.AddOpenApi();
        services.AddOrderRateLimiting();

        return services;
    }

    public static WebApplication UsePresentation(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        // Serves the Angular client when it has been built into wwwroot (single deployable; see README).
        var hostsClient = app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;
        if (hostsClient)
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }

        app.UseRateLimiter();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options => options.WithTitle("Order Submission API"));
        }

        app.MapOrderEndpoints();
        app.MapSimulationEndpoints();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

        // Unknown API routes are real 404s; any other route belongs to the client-side router.
        app.MapFallback("/api/{**path}", () => TypedResults.NotFound());
        if (hostsClient)
        {
            app.MapFallbackToFile("index.html");
        }

        return app;
    }
}
