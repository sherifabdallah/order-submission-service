using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OrderSubmission.Infrastructure.Persistence.Providers;

internal static class DbContextOptionsDefaults
{
    /// <summary>Settings shared by every provider.</summary>
    public static DbContextOptionsBuilder ApplyDefaults(this DbContextOptionsBuilder options, IServiceProvider serviceProvider) =>
        options
            .AddInterceptors(serviceProvider.GetServices<IInterceptor>())
            // Duplicate keys and lost leases are expected under concurrency and handled by the caller;
            // anything that is not handled is still logged as an error by the API or the worker.
            .ConfigureWarnings(warnings => warnings.Log(
                (CoreEventId.SaveChangesFailed, LogLevel.Debug),
                (CoreEventId.OptimisticConcurrencyException, LogLevel.Debug),
                (RelationalEventId.CommandError, LogLevel.Debug)));
}
