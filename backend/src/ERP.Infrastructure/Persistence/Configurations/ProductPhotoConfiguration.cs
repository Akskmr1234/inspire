using ERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ProductPhoto"/>.</summary>
public sealed class ProductPhotoConfiguration : IEntityTypeConfiguration<ProductPhoto>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductPhoto> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_photos");
        builder.HasKey(photo => photo.Id);
        builder.Property(photo => photo.Id).ValueGeneratedNever();

        builder.Property(photo => photo.Content).HasMaxLength(ProductPhoto.MaximumLength).IsRequired();
        builder.Property(photo => photo.Slot).IsRequired();

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(photo => photo.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Each picture in a slot of its own; the first is the product's main one.
        builder.HasIndex(photo => new { photo.ProductId, photo.Slot })
            .IsUnique()
            .HasDatabaseName("ix_product_photos_product");
        builder.HasIndex(photo => photo.TenantId).HasDatabaseName("ix_product_photos_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
