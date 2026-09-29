using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderSubmission.Application.Idempotency;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public const int FingerprintLength = 64; // SHA-256, hex encoded.

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        // The primary key is what makes concurrent duplicates impossible: only one insert can win.
        builder.HasKey(record => record.Key);
        builder.Property(record => record.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength).IsUnicode(false);

        builder.Property(record => record.RequestFingerprint)
            .HasMaxLength(FingerprintLength)
            .IsFixedLength()
            .IsUnicode(false)
            .IsRequired();

        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(record => record.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Supports a retention job that expires old keys.
        builder.HasIndex(record => record.CreatedAt);
    }
}
