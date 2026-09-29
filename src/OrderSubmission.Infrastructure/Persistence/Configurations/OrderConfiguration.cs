using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderSubmission.Domain.Orders;

namespace OrderSubmission.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.CustomerReference).HasMaxLength(Order.CustomerReferenceMaxLength).IsRequired();
        builder.Property(order => order.Total).HasPrecision(18, 2);
        builder.HasIndex(order => order.CustomerReference);

        builder.OwnsMany(order => order.Lines, lines =>
        {
            lines.ToTable("OrderLines");
            lines.WithOwner().HasForeignKey("OrderId");
            lines.HasKey("OrderId", nameof(OrderLine.LineNumber));
            lines.Property(line => line.LineNumber).ValueGeneratedNever();
            lines.Property(line => line.ProductCode).HasMaxLength(OrderLine.ProductCodeMaxLength).IsRequired();
            lines.Property(line => line.UnitPrice).HasPrecision(18, 2);
            lines.Property(line => line.LineTotal).HasPrecision(18, 2);
        });

        builder.Navigation(order => order.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
