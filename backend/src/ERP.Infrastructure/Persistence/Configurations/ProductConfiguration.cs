using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Product"/> to the <c>products</c> table.</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("products");
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).ValueGeneratedNever();

        builder.Property(product => product.Code)
            .HasMaxLength(Product.MaximumCodeLength).IsRequired();
        builder.Property(product => product.Description)
            .HasMaxLength(Product.MaximumDescriptionLength).IsRequired();
        builder.Property(product => product.DescriptionArabic)
            .HasMaxLength(Product.MaximumDescriptionLength);
        builder.Property(product => product.ShortDescription)
            .HasMaxLength(Product.MaximumShortDescriptionLength);

        string[] attributes =
        [
            nameof(Product.ItemName),
            nameof(Product.Manufacturer),
            nameof(Product.Label),
            nameof(Product.Size),
            nameof(Product.Origin),
            nameof(Product.Rack),
            nameof(Product.Bin),
        ];

        foreach (string attribute in attributes)
        {
            builder.Property(attribute).HasMaxLength(Product.MaximumAttributeLength);
        }

        builder.Property(product => product.ItemType).HasConversion<int>().IsRequired();
        builder.Property(product => product.CostingMethod).HasConversion<int>().IsRequired();
        builder.Property(product => product.Movement).HasConversion<int>().IsRequired();

        builder.Property(product => product.Currency)
            .HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Property(product => product.TracksBatches).IsRequired();
        builder.Property(product => product.TracksSerialNumbers).IsRequired();
        builder.Property(product => product.IsPacking).IsRequired();
        builder.Property(product => product.IsActive).IsRequired();
        builder.Property(product => product.IsDiscontinued).IsRequired();

        ConfigureRates(builder, product => product.Rates);

        builder.ComplexProperty(product => product.Levels, levels =>
        {
            levels.Property(l => l.Minimum)
                .HasColumnName("minimum_level").HasPrecision(18, 6).IsRequired();
            levels.Property(l => l.Reorder)
                .HasColumnName("reorder_level").HasPrecision(18, 6).IsRequired();
            levels.Property(l => l.Maximum)
                .HasColumnName("maximum_level").HasPrecision(18, 6).IsRequired();
        });

        builder.ComplexProperty(product => product.Device, device =>
        {
            device.Property(d => d.Device)
                .HasColumnName("device").HasMaxLength(DeviceAttributes.MaximumLength);
            device.Property(d => d.Colour)
                .HasColumnName("colour").HasMaxLength(DeviceAttributes.MaximumLength);
            device.Property(d => d.Battery)
                .HasColumnName("battery").HasMaxLength(DeviceAttributes.MaximumLength);
            device.Property(d => d.Ram)
                .HasColumnName("ram").HasMaxLength(DeviceAttributes.MaximumLength);
            device.Property(d => d.Storage)
                .HasColumnName("storage").HasMaxLength(DeviceAttributes.MaximumLength);
        });

        // Derived from the fields behind them. Storing either as well would create a
        // third answer that can disagree with the two it came from.
        builder.Ignore(product => product.IsTransactable);
        builder.Ignore(product => product.IsStocked);
        builder.Ignore(product => product.Cost);
        builder.Ignore(product => product.RetailRate);
        builder.Ignore(product => product.WholesaleRate);
        builder.Ignore(product => product.MaximumRetailPrice);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(product => product.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Brand>()
            .WithMany()
            .HasForeignKey(product => product.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        // Three separate references to the same table, and all restricted. Deleting a
        // unit that products are stocked in would leave every quantity recorded
        // against it unreadable - there would be nothing left saying what the number
        // counted.
        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(product => product.StockUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(product => product.PurchaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(product => product.SalesUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(product => product.DefaultSupplierLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        // The code is how a product is referred to on every document, so it has to be
        // unique. Scoped to the firm, like the masters above it: two companies under
        // one group number their own ranges independently.
        builder
            .HasIndex(product => new { product.FirmId, product.Code })
            .IsUnique()
            .HasDatabaseName("ix_products_code");

        // The lookup every transaction screen does: active products of a firm, by
        // description. Filtered, because a deleted product must never be offered and
        // the index would otherwise carry every one ever created.
        builder
            .HasIndex(product => new { product.FirmId, product.Description })
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_products_description");

        builder
            .HasIndex(product => new { product.FirmId, product.CategoryId })
            .HasDatabaseName("ix_products_category");

        builder.HasIndex(product => product.TenantId).HasDatabaseName("ix_products_tenant");

        builder.HasMany(product => product.Barcodes)
            .WithOne()
            .HasForeignKey(barcode => barcode.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(product => product.Barcodes)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_barcodes");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }

    /// <summary>Maps a rate block onto its seven columns.</summary>
    /// <typeparam name="TEntity">The entity carrying the rates.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="rates">The rate block.</param>
    /// <remarks>
    /// Shared by the product and its barcodes, which price the same way. Two copies
    /// of seven column definitions would eventually differ in precision, and a
    /// barcode that rounded differently from the product it belongs to would produce
    /// a till receipt that disagrees with the invoice.
    /// </remarks>
    internal static void ConfigureRates<TEntity>(
        EntityTypeBuilder<TEntity> builder,
        System.Linq.Expressions.Expression<Func<TEntity, ProductRates?>> rates)
        where TEntity : class
    {
        builder.ComplexProperty(rates, block =>
        {
            block.Property(r => r.Cost)
                .HasColumnName("cost").HasPrecision(19, 4).IsRequired();
            block.Property(r => r.ProfitPercentage)
                .HasColumnName("profit_percentage").HasPrecision(9, 4).IsRequired();
            block.Property(r => r.CorPercentage)
                .HasColumnName("cor_percentage").HasPrecision(9, 4).IsRequired();
            block.Property(r => r.RetailRate)
                .HasColumnName("retail_rate").HasPrecision(19, 4).IsRequired();
            block.Property(r => r.WholesaleRate)
                .HasColumnName("wholesale_rate").HasPrecision(19, 4).IsRequired();
            block.Property(r => r.OtherRate)
                .HasColumnName("other_rate").HasPrecision(19, 4).IsRequired();
            block.Property(r => r.MaximumRetailPrice)
                .HasColumnName("maximum_retail_price").HasPrecision(19, 4).IsRequired();
        });
    }
}

/// <summary>Maps <see cref="ProductBarcode"/> to the <c>product_barcodes</c> table.</summary>
public sealed class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_barcodes");
        builder.HasKey(barcode => barcode.Id);
        builder.Property(barcode => barcode.Id).ValueGeneratedNever();

        builder.Property(barcode => barcode.Barcode)
            .HasMaxLength(ProductBarcode.MaximumLength).IsRequired();

        ProductConfiguration.ConfigureRates(builder, barcode => barcode.Rates);

        // A barcode identifies one product, or it identifies nothing. The till scans
        // it and expects a single answer; two products sharing one would make the
        // scan ambiguous at the worst possible moment.
        //
        // Scoped to the tenant rather than the firm, deliberately and unlike the
        // product code. A barcode is the manufacturer's global identifier for the
        // physical item, so the same code appearing under two firms of one group is a
        // data-entry error rather than an independent numbering choice.
        builder
            .HasIndex(barcode => new { barcode.TenantId, barcode.Barcode })
            .IsUnique()
            .HasDatabaseName("ix_product_barcodes_code");

        builder
            .HasIndex(barcode => barcode.ProductId)
            .HasDatabaseName("ix_product_barcodes_product");
    }
}
