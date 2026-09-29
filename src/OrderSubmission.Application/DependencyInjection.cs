using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderSubmission.Application.Abstractions.Messaging;
using OrderSubmission.Application.Common.Behaviors;
using OrderSubmission.Application.Notifications;

namespace OrderSubmission.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.Scan(scan => scan
            .FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
            .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        // Pipeline, outermost first: logging -> validation -> handler.
        services.Decorate(typeof(ICommandHandler<,>), typeof(ValidatingCommandHandler<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingCommandHandler<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingQueryHandler<,>));

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Singleton, includeInternalTypes: true);

        services.AddOptions<NotificationDeliveryOptions>()
            .Bind(configuration.GetSection(NotificationDeliveryOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.LeaseDuration > options.SendTimeout, "LeaseDuration must be longer than SendTimeout.")
            .Validate(options => options.InitialRetryDelay > TimeSpan.Zero && options.MaxRetryDelay >= options.InitialRetryDelay, "Retry delays must be positive and MaxRetryDelay >= InitialRetryDelay.")
            .ValidateOnStart();

        services.AddSingleton<IRetryPolicy, ExponentialBackoffRetryPolicy>();
        services.AddScoped<NotificationDeliveryService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
