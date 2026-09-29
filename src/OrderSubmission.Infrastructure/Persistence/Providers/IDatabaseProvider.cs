using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OrderSubmission.Infrastructure.Persistence.Providers;

/// <summary>
/// Strategy for everything that differs between database engines, so the rest of the
/// infrastructure stays provider-neutral.
/// </summary>
internal interface IDatabaseProvider
{
    void AddDbContext(IServiceCollection services, Func<IServiceProvider, string> connectionString);

    bool IsUniqueConstraintViolation(DbUpdateException exception);

    /// <summary>One-off, provider-specific preparation that runs before migrations.</summary>
    Task PrepareAsync(OrderSubmissionDbContext context, CancellationToken cancellationToken);

    static IDatabaseProvider For(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.Sqlite => new SqliteDatabaseProvider(),
        DatabaseProvider.SqlServer => new SqlServerDatabaseProvider(),
        _ => throw new NotSupportedException($"Database provider '{provider}' is not supported."),
    };
}
