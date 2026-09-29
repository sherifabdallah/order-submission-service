using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using OrderSubmission.Application.Idempotency;
using OrderSubmission.Domain.Notifications;

namespace OrderSubmission.Infrastructure.Persistence.Providers;

public sealed class SqlServerOrderSubmissionDbContext(DbContextOptions<SqlServerOrderSubmissionDbContext> options)
    : OrderSubmissionDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Idempotency keys are opaque and case-sensitive; the default SQL Server collation is not.
        modelBuilder.Entity<IdempotencyRecord>()
            .Property(record => record.Key)
            .UseCollation("Latin1_General_100_BIN2");

        // Keep the outbox index small: delivered/failed rows are the vast majority over time.
        modelBuilder.Entity<Notification>()
            .HasIndex(notification => new { notification.Status, notification.NextAttemptAt })
            .HasFilter("[Status] = 'Pending'");
    }
}

internal sealed class SqlServerDatabaseProvider : IDatabaseProvider
{
    private const int UniqueIndexViolation = 2601;
    private const int PrimaryKeyViolation = 2627;

    public void AddDbContext(IServiceCollection services, Func<IServiceProvider, string> connectionString) =>
        services.AddDbContextPool<OrderSubmissionDbContext, SqlServerOrderSubmissionDbContext>((serviceProvider, options) =>
            options
                .UseSqlServer(connectionString(serviceProvider), sql => sql.EnableRetryOnFailure())
                .ApplyDefaults(serviceProvider));

    public bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception?.InnerException is SqlException { Number: UniqueIndexViolation or PrimaryKeyViolation };

    public Task PrepareAsync(OrderSubmissionDbContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Used by <c>dotnet ef</c> to create SQL Server migrations.</summary>
public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerOrderSubmissionDbContext>
{
    public SqlServerOrderSubmissionDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqlServerOrderSubmissionDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=OrderSubmission;Trusted_Connection=True")
            .Options);
}
