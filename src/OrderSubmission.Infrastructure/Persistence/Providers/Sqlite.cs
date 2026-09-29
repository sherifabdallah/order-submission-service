using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OrderSubmission.Infrastructure.Persistence.Providers;

public sealed class SqliteOrderSubmissionDbContext(DbContextOptions<SqliteOrderSubmissionDbContext> options)
    : OrderSubmissionDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // SQLite has no date/time type and EF Core cannot translate DateTimeOffset comparisons for it.
        // Storing UTC ticks keeps "is it due?" and "has the lease expired?" checks in SQL and index-friendly.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        value => value.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
}

internal sealed class SqliteDatabaseProvider : IDatabaseProvider
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintUnique = 2067;

    public void AddDbContext(IServiceCollection services, Func<IServiceProvider, string> connectionString) =>
        services.AddDbContextPool<OrderSubmissionDbContext, SqliteOrderSubmissionDbContext>((serviceProvider, options) =>
            options
                .UseSqlite(AnchorToContentRoot(connectionString(serviceProvider), serviceProvider))
                .ApplyDefaults(serviceProvider));

    // Relative database paths resolve against the app's content root, not the shell's current directory.
    private static string AnchorToContentRoot(string connectionString, IServiceProvider serviceProvider)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        var isFile = !string.IsNullOrEmpty(dataSource)
            && dataSource != ":memory:"
            && !dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

        if (isFile && !Path.IsPathRooted(dataSource) && serviceProvider.GetService<IHostEnvironment>() is { } environment)
        {
            builder.DataSource = Path.Combine(environment.ContentRootPath, dataSource);
        }

        return builder.ToString();
    }

    public bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception?.InnerException is SqliteException
        {
            SqliteErrorCode: SqliteConstraint,
            SqliteExtendedErrorCode: SqliteConstraintPrimaryKey or SqliteConstraintUnique,
        };

    public async Task PrepareAsync(OrderSubmissionDbContext context, CancellationToken cancellationToken)
    {
        var dataSource = new SqliteConnectionStringBuilder(context.Database.GetConnectionString()).DataSource;
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write-ahead logging lets readers proceed while a writer commits. The setting is persistent.
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}

/// <summary>Used by <c>dotnet ef</c> to create SQLite migrations.</summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteOrderSubmissionDbContext>
{
    public SqliteOrderSubmissionDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteOrderSubmissionDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
