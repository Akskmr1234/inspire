using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="SalesOrder"/> to the <c>sales_orders</c> table.</summary>
public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_orders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();

        builder.Property(order => order.Number).HasMaxLength(50).IsRequired();
        builder.Property(order => order.Date).IsRequired();
        builder.Property(order => order.Mode).HasConversion<int>().IsRequired();
        builder.Property(order => order.Status).HasConversion<int>().IsRequired();

        builder.Property(order => order.Currency)
            .HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Property(order => order.ReferenceNumber)
            .HasMaxLength(SalesOrder.MaximumNarrationLength);
        builder.Property(order => order.Narration)
            .HasMaxLength(SalesOrder.MaximumNarrationLength);
        builder.Property(order => order.ClosureReason)
            .HasMaxLength(SalesOrder.MaximumNarrationLength);

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
            .HasForeignKey(order => order.CustomerLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(order => order.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // The number is what the order is called in a customer's own records, so two of
        // them in one firm would be two documents with one name.
        builder
            .HasIndex(order => new { order.FirmId, order.Number })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_sales_orders_number");

        // The list screen's own query: a firm's orders over a date range, newest first.
        builder
            .HasIndex(order => new { order.FirmId, order.Date })
            .HasDatabaseName("ix_sales_orders_date");

        // What an outstanding-orders report asks for: this firm's open orders. Narrow on
        // purpose - a firm's finished orders outnumber its open ones within months, and
        // this is the question somebody asks every morning.
        builder
            .HasIndex(order => new { order.FirmId, order.Status, order.ExpectedOn })
            .HasDatabaseName("ix_sales_orders_open");

        builder
            .HasIndex(order => new { order.FirmId, order.CustomerLedgerId, order.Date })
            .HasDatabaseName("ix_sales_orders_customer");

        builder.HasIndex(order => order.TenantId)
            .HasDatabaseName("ix_sales_orders_tenant");

        builder.HasMany(order => order.Lines)
            .WithOne()
            .HasForeignKey(line => line.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_lines");

        builder.HasMany(order => order.Charges)
            .WithOne()
            .HasForeignKey(charge => charge.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(order => order.Charges)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_charges");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="SalesOrderLine"/> to the <c>sales_order_lines</c> table.</summary>
public sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_order_lines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Quantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();
        builder.Property(line => line.StockQuantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();

        // Stored rather than summed from the invoices raised, because the question it
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
            .HasForeignKey(tax => tax.SalesOrderLineId)
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
            .HasIndex(line => new { line.SalesOrderId, line.LineNumber })
            .HasDatabaseName("ix_sales_order_lines_order");

        // What is on order for a product: the other half of a shortage report.
        builder.HasIndex(line => line.ProductId)
            .HasDatabaseName("ix_sales_order_lines_product");

        builder.HasIndex(line => line.TenantId)
            .HasDatabaseName("ix_sales_order_lines_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>Maps <see cref="SalesOrderCharge"/> to the <c>sales_order_charges</c> table.</summary>
public sealed class SalesOrderChargeConfiguration : IEntityTypeConfiguration<SalesOrderCharge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesOrderCharge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_order_charges");
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
            .HasIndex(charge => new { charge.SalesOrderId, charge.LedgerId })
            .IsUnique()
            .HasDatabaseName("ix_sales_order_charges_ledger");

        builder.HasIndex(charge => charge.TenantId)
            .HasDatabaseName("ix_sales_order_charges_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>Maps <see cref="SalesOrderLineTax"/> to the <c>sales_order_line_taxes</c> table.</summary>
public sealed class SalesOrderLineTaxConfiguration : IEntityTypeConfiguration<SalesOrderLineTax>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesOrderLineTax> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_order_line_taxes");

        // One row per head per line, as on an invoice: a line cannot carry two figures
        // for the same component.
        builder.HasKey(tax => new { tax.SalesOrderLineId, tax.Type });

        builder.Property(tax => tax.Type).HasConversion<int>().IsRequired();
        builder.Property(tax => tax.Percentage).HasPrecision(9, 4).IsRequired();
        builder.Property(tax => tax.Amount).HasPrecision(19, 4).IsRequired();

        builder.HasIndex(tax => tax.TenantId)
            .HasDatabaseName("ix_sales_order_line_taxes_tenant");
    }
}
