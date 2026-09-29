using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderSubmission.Domain.Notifications;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).ValueGeneratedNever();

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(notification => notification.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // One notification of each type per order.
        builder.HasIndex(notification => new { notification.OrderId, notification.Type }).IsUnique();

        // Serves the outbox poll: WHERE Status = 'Pending' AND NextAttemptAt <= @now ORDER BY NextAttemptAt.
        builder.HasIndex(notification => new { notification.Status, notification.NextAttemptAt });

        builder.Property(notification => notification.Type).HasConversion<string>().HasMaxLength(32).IsUnicode(false);
        builder.Property(notification => notification.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        builder.Property(notification => notification.Recipient).HasMaxLength(Notification.RecipientMaxLength).IsRequired();
        builder.Property(notification => notification.Message).HasMaxLength(Notification.MessageMaxLength).IsRequired();
        builder.Property(notification => notification.LastError).HasMaxLength(Notification.ErrorMaxLength);

        builder.Property<Guid?>(ShadowProperties.LeaseToken).IsConcurrencyToken();
        builder.Property<DateTimeOffset?>(ShadowProperties.LeaseExpiresAt);

        builder.OwnsMany(notification => notification.Attempts, attempts =>
        {
            attempts.ToTable("NotificationDeliveryAttempts");
            attempts.WithOwner().HasForeignKey("NotificationId");
            attempts.HasKey("NotificationId", nameof(DeliveryAttempt.Number));
            attempts.Property(attempt => attempt.Number).ValueGeneratedNever();
            attempts.Property(attempt => attempt.Error).HasMaxLength(Notification.ErrorMaxLength);
        });

        builder.Navigation(notification => notification.Attempts).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
