using ERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Batch"/> to the <c>batches</c> table.</summary>
public sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Batch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("batches");
        builder.HasKey(batch => batch.Id);
        builder.Property(batch => batch.Id).ValueGeneratedNever();

        builder.Property(batch => batch.Number)
            .HasMaxLength(Batch.MaximumNumberLength).IsRequired();

        builder.Property(batch => batch.PurchaseRate)
            .HasPrecision(19, StockBalance.CostScale).IsRequired();

        builder.Ignore(batch => batch.IsSequenced);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(batch => batch.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // One batch number per product, enforced by the database. Two rows for the
        // same pair would each hold part of the lot and disagree about its expiry
        // date, and a picker would have no way to tell which one the carton in their
        // hand belongs to.
        builder
            .HasIndex(batch => new { batch.FirmId, batch.ProductId, batch.Number })
            .IsUnique()
            .HasDatabaseName("ix_batches_number");

        // Generation reads the highest sequence a product has reached, which is this
        // index read backwards over one product.
        builder
            .HasIndex(batch => new { batch.FirmId, batch.ProductId, batch.AutoSequence })
            .HasFilter("auto_sequence IS NOT NULL")
            .HasDatabaseName("ix_batches_sequence");

        // The expiry report's own query: a firm's batches by the date they run out.
        builder
            .HasIndex(batch => new { batch.FirmId, batch.ExpiresOn })
            .HasFilter("expires_on IS NOT NULL")
            .HasDatabaseName("ix_batches_expiry");

        builder.HasIndex(batch => batch.TenantId)
            .HasDatabaseName("ix_batches_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="BatchBalance"/> to the <c>batch_balances</c> table.</summary>
public sealed class BatchBalanceConfiguration : IEntityTypeConfiguration<BatchBalance>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BatchBalance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("batch_balances");
        builder.HasKey(balance => balance.Id);
        builder.Property(balance => balance.Id).ValueGeneratedNever();

        builder.Property(balance => balance.Quantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();

        builder.Property(balance => balance.UnitCost)
            .HasPrecision(19, StockBalance.CostScale).IsRequired();

        builder.Property(balance => balance.Currency)
            .HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Ignore(balance => balance.Value);

        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(balance => balance.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(balance => balance.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(balance => balance.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // One position per batch per warehouse, for the same reason the product has
        // one per warehouse: two rows would each hold half the lot, and a valuation
        // would report whichever the query happened to read.
        builder
            .HasIndex(balance => new { balance.FirmId, balance.BatchId, balance.WarehouseId })
            .IsUnique()
            .HasDatabaseName("ix_batch_balances_position");

        // The batch-wise stock report reads a warehouse, or a product across
        // warehouses, and the sales screen reads one product to offer a choice of lot.
        builder
            .HasIndex(balance => new { balance.FirmId, balance.ProductId, balance.WarehouseId })
            .HasDatabaseName("ix_batch_balances_product");

        builder.HasIndex(balance => balance.TenantId)
            .HasDatabaseName("ix_batch_balances_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
