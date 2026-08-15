using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="PurchaseOrder"/> to the <c>purchase_orders</c> table.</summary>
public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_orders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();

        builder.Property(order => order.Number).HasMaxLength(50).IsRequired();
        builder.Property(order => order.Date).IsRequired();
        builder.Property(order => order.Mode).HasConversion<int>().IsRequired();
        builder.Property(order => order.Status).HasConversion<int>().IsRequired();

        builder.Property(order => order.Currency)
            .HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Property(order => order.ReferenceNumber)
            .HasMaxLength(PurchaseOrder.MaximumNarrationLength);
        builder.Property(order => order.Narration)
            .HasMaxLength(PurchaseOrder.MaximumNarrationLength);
        builder.Property(order => order.ClosureReason)
            .HasMaxLength(PurchaseOrder.MaximumNarrationLength);

        // Every total is derived from the lines and the charges, and so is every question
        // about what is still owed. Storing them as well would create a second answer.
        builder.Ignore(order => order.IsEditable);
        builder.Ignore(order => order.IsOpen);
        builder.Ignore(order => order.IsPartlyInvoiced);
        builder.Ignore(order => order.Taxable);
        builder.Ignore(order => order.Tax);
        builder.Ignore(order => order.ChargeTotal);
        builder.Ignore(order => order.GrossTotal);
        builder.Ignore(order => order.RoundingDifference);
        builder.Ignore(order => order.Total);

        builder.HasOne<FinancialYear>()
            .WithMany()
            .HasForeignKey(order => order.FinancialYearId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(order => order.SupplierLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(order => order.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // The number is what the order is called in the supplier's own records, so two of
        // them in one firm would be two documents with one name.
        builder
            .HasIndex(order => new { order.FirmId, order.Number })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_purchase_orders_number");

        // The list screen's own query: a firm's orders over a date range, newest first.
        builder
            .HasIndex(order => new { order.FirmId, order.Date })
            .HasDatabaseName("ix_purchase_orders_date");

        // What a buyer asks every morning: this firm's open orders, soonest promised
        // first. Narrow on purpose - a firm's finished orders outnumber its open ones
        // within months.
        builder
            .HasIndex(order => new { order.FirmId, order.Status, order.ExpectedOn })
            .HasDatabaseName("ix_purchase_orders_open");

        builder
            .HasIndex(order => new { order.FirmId, order.SupplierLedgerId, order.Date })
            .HasDatabaseName("ix_purchase_orders_supplier");

        builder.HasIndex(order => order.TenantId)
            .HasDatabaseName("ix_purchase_orders_tenant");

        builder.HasMany(order => order.Lines)
            .WithOne()
            .HasForeignKey(line => line.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_lines");

        builder.HasMany(order => order.Charges)
            .WithOne()
            .HasForeignKey(charge => charge.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.Charges)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_charges");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="PurchaseOrderLine"/> to the <c>purchase_order_lines</c> table.</summary>
public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_order_lines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Quantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();
        builder.Property(line => line.StockQuantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();

        // Stored rather than summed from the purchases raised, because the question it
        // answers - what is still owed - is asked of the order and would otherwise mean
        // reading every document that ever pointed at it.
        builder.Property(line => line.InvoicedQuantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();

        builder.Property(line => line.Rate)
            .HasPrecision(19, StockBalance.CostScale).IsRequired();
        builder.Property(line => line.Discount)
            .HasPrecision(19, 4).IsRequired();

        builder.Property(line => line.LineNumber).IsRequired();

        builder.Ignore(line => line.OutstandingQuantity);
        builder.Ignore(line => line.IsFulfilled);
        builder.Ignore(line => line.LineTotal);

        builder.ComplexProperty(line => line.TaxableAmount, amount =>
        {
            amount.Property(money => money.Amount)
                .HasColumnName("taxable_amount").HasPrecision(19, 4).IsRequired();
            amount.Property(money => money.Currency)
                .HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.ComplexProperty(line => line.TaxAmount, amount =>
        {
            amount.Property(money => money.Amount)
                .HasColumnName("tax_amount").HasPrecision(19, 4).IsRequired();
            amount.Property(money => money.Currency)
                .HasColumnName("tax_currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.HasMany(line => line.Components)
            .WithOne()
            .HasForeignKey(tax => tax.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(line => line.Components)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_components");

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(line => line.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(line => new { line.PurchaseOrderId, line.LineNumber })
            .HasDatabaseName("ix_purchase_order_lines_order");

        // What is on order from suppliers for a product: the half of a shortage report the
        // sales side cannot answer.
        builder.HasIndex(line => line.ProductId)
            .HasDatabaseName("ix_purchase_order_lines_product");

        builder.HasIndex(line => line.TenantId)
            .HasDatabaseName("ix_purchase_order_lines_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>Maps <see cref="PurchaseOrderCharge"/> to the <c>purchase_order_charges</c> table.</summary>
public sealed class PurchaseOrderChargeConfiguration : IEntityTypeConfiguration<PurchaseOrderCharge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseOrderCharge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_order_charges");
        builder.HasKey(charge => charge.Id);
        builder.Property(charge => charge.Id).ValueGeneratedNever();

        builder.ComplexProperty(charge => charge.Amount, amount =>
        {
            amount.Property(money => money.Amount)
                .HasColumnName("amount").HasPrecision(19, 4).IsRequired();
            amount.Property(money => money.Currency)
                .HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.Ignore(charge => charge.SignedAmount);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(charge => charge.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(charge => new { charge.PurchaseOrderId, charge.LedgerId })
            .IsUnique()
            .HasDatabaseName("ix_purchase_order_charges_ledger");

        builder.HasIndex(charge => charge.TenantId)
            .HasDatabaseName("ix_purchase_order_charges_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>Maps <see cref="PurchaseOrderLineTax"/> to the <c>purchase_order_line_taxes</c> table.</summary>
public sealed class PurchaseOrderLineTaxConfiguration
    : IEntityTypeConfiguration<PurchaseOrderLineTax>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseOrderLineTax> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_order_line_taxes");

        // One row per head per line, as on an invoice: a line cannot carry two figures for
        // the same component.
        builder.HasKey(tax => new { tax.PurchaseOrderLineId, tax.Type });

        builder.Property(tax => tax.Type).HasConversion<int>().IsRequired();
        builder.Property(tax => tax.Percentage).HasPrecision(9, 4).IsRequired();
        builder.Property(tax => tax.Amount).HasPrecision(19, 4).IsRequired();

        builder.HasIndex(tax => tax.TenantId)
            .HasDatabaseName("ix_purchase_order_line_taxes_tenant");
    }
}
