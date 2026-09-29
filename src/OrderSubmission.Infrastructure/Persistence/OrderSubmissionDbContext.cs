using Microsoft.EntityFrameworkCore;
using OrderSubmission.Application.Idempotency;
using OrderSubmission.Domain.Notifications;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Infrastructure.Persistence;

/// <summary>
/// Provider-neutral model. Each database provider has a small derived context
/// (<see cref="Providers.SqliteOrderSubmissionDbContext"/>, <see cref="Providers.SqlServerOrderSubmissionDbContext"/>)
/// that owns its own migrations and provider-specific mapping tweaks.
/// </summary>
public abstract class OrderSubmissionDbContext : DbContext
{
    protected OrderSubmissionDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderSubmissionDbContext).Assembly);
    }
}

/// <summary>
/// Infrastructure-only columns that are deliberately kept out of the domain model.
/// </summary>
internal static class ShadowProperties
{
    /// <summary>Identifies the worker that currently owns a notification; also the optimistic concurrency token.</summary>
    public const string LeaseToken = "LeaseToken";

    /// <summary>When the current lease expires and another worker may take the notification over.</summary>
    public const string LeaseExpiresAt = "LeaseExpiresAt";
}
