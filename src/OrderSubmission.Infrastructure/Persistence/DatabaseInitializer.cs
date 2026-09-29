using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OrderSubmission.Infrastructure.Persistence.Providers;

namespace OrderSubmission.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies pending migrations when <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/> is on.
    /// EF Core takes a database-wide migration lock, so concurrent instances starting together are safe.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (!options.ApplyMigrationsOnStartup)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<OrderSubmissionDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IDatabaseProvider>();

        await provider.PrepareAsync(db, cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
    }
}
