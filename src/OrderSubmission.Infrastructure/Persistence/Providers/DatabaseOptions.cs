namespace OrderSubmission.Infrastructure.Persistence.Providers;

public enum DatabaseProvider
{
    Sqlite,
    SqlServer,
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string ConnectionStringName = "Orders";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>
    /// Convenient for local development and single-instance deployments. For multi-instance
    /// deployments, run migrations as a release step instead (see README).
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;
}
