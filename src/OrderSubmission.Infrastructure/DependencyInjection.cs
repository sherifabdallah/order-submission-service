using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderSubmission.Application.Abstractions.Notifications;
using OrderSubmission.Application.Abstractions.Persistence;
using OrderSubmission.Application.Orders;
using OrderSubmission.Infrastructure.Notifications;
using OrderSubmission.Infrastructure.Persistence;
using OrderSubmission.Infrastructure.Persistence.Providers;

namespace OrderSubmission.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services
            .AddPersistence(configuration)
            .AddNotifications();
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName);

        var providerKind = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()?.Provider ?? DatabaseProvider.Sqlite;
        var provider = IDatabaseProvider.For(providerKind);
        services.AddSingleton(provider);

        // Resolved lazily so hosts and tests can override the connection string late in configuration.
        provider.AddDbContext(services, serviceProvider =>
            serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(DatabaseOptions.ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{DatabaseOptions.ConnectionStringName}' is not configured."));

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOrderReadService, OrderReadService>();
        services.AddScoped<INotificationOutbox, EfNotificationOutbox>();

        services.AddHealthChecks().AddDbContextCheck<OrderSubmissionDbContext>("database", tags: ["ready"]);

        return services;
    }

    private static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddOptions<FakeNotificationServiceOptions>()
            .BindConfiguration(FakeNotificationServiceOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<NotificationDispatcherOptions>()
            .BindConfiguration(NotificationDispatcherOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.PollingInterval > TimeSpan.Zero, "PollingInterval must be positive.")
            .ValidateOnStart();

        // The fake provider, wrapped by the fault-injecting decorator.
        services.AddSingleton<FakeNotificationSender>();
        services.AddSingleton<INotificationServiceSimulator, NotificationServiceSimulator>();
        services.AddSingleton<INotificationSender>(serviceProvider => new FaultInjectingNotificationSender(
            serviceProvider.GetRequiredService<FakeNotificationSender>(),
            serviceProvider.GetRequiredService<INotificationServiceSimulator>(),
            serviceProvider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<NotificationDispatchSignal>();
        services.AddSingleton<INotificationDispatchSignal>(serviceProvider => serviceProvider.GetRequiredService<NotificationDispatchSignal>());
        services.AddSingleton<NotificationDispatcher>();
        services.AddHostedService<NotificationDispatchWorker>();

        return services;
    }
}
