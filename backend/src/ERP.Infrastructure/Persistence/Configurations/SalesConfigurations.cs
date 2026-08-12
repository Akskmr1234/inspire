using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="SalesInvoice"/> to the <c>sales_invoices</c> table.</summary>
public sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesInvoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_invoices");
        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.Id).ValueGeneratedNever();

        builder.Property(invoice => invoice.Number).HasMaxLength(50).IsRequired();
        builder.Property(invoice => invoice.Date).IsRequired();
        builder.Property(invoice => invoice.Mode).HasConversion<int>().IsRequired();
        builder.Property(invoice => invoice.Status).HasConversion<int>().IsRequired();
        builder.Property(invoice => invoice.Kind).HasConversion<int>().IsRequired();

        builder.Ignore(invoice => invoice.IsReturn);

        // A return points at the invoice it is against, and nothing may delete that
        // invoice out from under it - the link is how the credit finds the debt.
        builder.HasOne<SalesInvoice>()
            .WithMany()
            .HasForeignKey(invoice => invoice.ReturnsInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(invoice => invoice.ReturnsInvoiceId)
            .HasDatabaseName("ix_sales_invoices_returns_invoice")
            .HasFilter("returns_invoice_id IS NOT NULL");

        builder.Property(invoice => invoice.Currency)
            .HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Property(invoice => invoice.ReferenceNumber)
            .HasMaxLength(SalesInvoice.MaximumNarrationLength);
        builder.Property(invoice => invoice.Narration)
            .HasMaxLength(SalesInvoice.MaximumNarrationLength);
        builder.Property(invoice => invoice.CancellationReason)
            .HasMaxLength(SalesInvoice.MaximumNarrationLength);

        // Every total is derived from the lines and the charges. Storing them as well
        // would create a second answer that can disagree with the one it came from -
        // and on an invoice, the two disagreeing is the customer's problem, not ours.
        builder.Ignore(invoice => invoice.IsEditable);
        builder.Ignore(invoice => invoice.Taxable);
        builder.Ignore(invoice => invoice.Tax);
        builder.Ignore(invoice => invoice.ChargeTotal);
        builder.Ignore(invoice => invoice.GrossTotal);
        builder.Ignore(invoice => invoice.RoundingDifference);
        builder.Ignore(invoice => invoice.Total);

        builder.HasOne<FinancialYear>()
            .WithMany()
            .HasForeignKey(invoice => invoice.FinancialYearId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(invoice => invoice.CustomerLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(invoice => invoice.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // What the posting produced. Restricted, all three: a sale whose issue, bill or
        // journal could be deleted would be a sale claiming to have been accounted for
        // by something that no longer exists.
        builder.HasOne<StockDocument>()
            .WithMany()
            .HasForeignKey(invoice => invoice.StockDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Bill>()
            .WithMany()
            .HasForeignKey(invoice => invoice.BillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(invoice => invoice.JournalVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // The number is what the invoice is called on paper and in a customer's own
        // records, so two of them in one firm would be two documents with one name.
        builder
            .HasIndex(invoice => new { invoice.FirmId, invoice.Number })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_sales_invoices_number");

        // The list screen's own query, and the sales reports': a firm's invoices over a
        // date range, newest first.
        builder
            .HasIndex(invoice => new { invoice.FirmId, invoice.Date })
            .HasDatabaseName("ix_sales_invoices_date");

        // What a customer's statement asks for.
        builder
            .HasIndex(invoice => new { invoice.FirmId, invoice.CustomerLedgerId, invoice.Date })
            .HasDatabaseName("ix_sales_invoices_customer");

        builder.HasIndex(invoice => invoice.TenantId)
            .HasDatabaseName("ix_sales_invoices_tenant");

        builder.HasMany(invoice => invoice.Lines)
            .WithOne()
            .HasForeignKey(line => line.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(invoice => invoice.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_lines");

        builder.HasMany(invoice => invoice.Charges)
            .WithOne()
            .HasForeignKey(charge => charge.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(invoice => invoice.Charges)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_charges");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="SalesInvoiceLine"/> to the <c>sales_invoice_lines</c> table.</summary>
public sealed class SalesInvoiceLineConfiguration : IEntityTypeConfiguration<SalesInvoiceLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesInvoiceLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_invoice_lines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Quantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();
        builder.Property(line => line.StockQuantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();

        builder.Property(line => line.Rate)
            .HasPrecision(19, StockBalance.CostScale).IsRequired();
        builder.Property(line => line.Discount)
            .HasPrecision(19, 4).IsRequired();

        builder.Property(line => line.LineNumber).IsRequired();

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

        builder.Ignore(line => line.LineTotal);

        builder.HasMany(line => line.Components)
            .WithOne()
            .HasForeignKey(tax => tax.SalesInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(line => line.Components)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_components");

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(line => line.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(line => line.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(line => new { line.SalesInvoiceId, line.LineNumber })
            .HasDatabaseName("ix_sales_invoice_lines_invoice");

        // What sold, and how much of it: the sales analysis reports read by product.
        builder.HasIndex(line => line.ProductId)
            .HasDatabaseName("ix_sales_invoice_lines_product");

        builder.HasIndex(line => line.TenantId)
            .HasDatabaseName("ix_sales_invoice_lines_tenant");

        builder.HasMany(line => line.Serials)
            .WithOne()
            .HasForeignKey(link => link.SalesInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(line => line.Serials)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_serials");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>
/// Maps <see cref="SalesInvoiceLineSerial"/> to the <c>sales_invoice_line_serials</c> table.
/// </summary>
public sealed class SalesInvoiceLineSerialConfiguration
    : IEntityTypeConfiguration<SalesInvoiceLineSerial>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesInvoiceLineSerial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_invoice_line_serials");
        builder.HasKey(link => new { link.SalesInvoiceLineId, link.SerialNumberId });

        builder.HasOne<SerialNumber>()
            .WithMany()
            .HasForeignKey(link => link.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);

        // Which invoice sold this unit: the question a warranty claim starts from, with
        // the machine in front of somebody and no idea who bought it.
        builder.HasIndex(link => link.SerialNumberId)
            .HasDatabaseName("ix_sales_invoice_line_serials_serial");

        builder.HasIndex(link => link.TenantId)
            .HasDatabaseName("ix_sales_invoice_line_serials_tenant");
    }
}

/// <summary>Maps <see cref="SalesInvoiceCharge"/> to the <c>sales_invoice_charges</c> table.</summary>
public sealed class SalesInvoiceChargeConfiguration : IEntityTypeConfiguration<SalesInvoiceCharge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesInvoiceCharge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_invoice_charges");
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

        // One row per account per invoice: the aggregate refuses a second, and the
        // database says so too rather than trusting it.
        builder
            .HasIndex(charge => new { charge.SalesInvoiceId, charge.LedgerId })
            .IsUnique()
            .HasDatabaseName("ix_sales_invoice_charges_ledger");

        builder.HasIndex(charge => charge.TenantId)
            .HasDatabaseName("ix_sales_invoice_charges_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>
/// Maps <see cref="SalesInvoiceLineTax"/> to the <c>sales_invoice_line_taxes</c> table.
/// </summary>
public sealed class SalesInvoiceLineTaxConfiguration
    : IEntityTypeConfiguration<SalesInvoiceLineTax>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesInvoiceLineTax> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_invoice_line_taxes");

        // One row per head per line. The pair is the key, so a line cannot carry two
        // figures for the same component - which a return would then double-count.
        builder.HasKey(tax => new { tax.SalesInvoiceLineId, tax.Type });

        builder.Property(tax => tax.Type).HasConversion<int>().IsRequired();
        builder.Property(tax => tax.Percentage).HasPrecision(9, 4).IsRequired();
        builder.Property(tax => tax.Amount).HasPrecision(19, 4).IsRequired();

        // What a tax return reads: every line of every invoice carrying this head.
        builder.HasIndex(tax => new { tax.TenantId, tax.Type })
            .HasDatabaseName("ix_sales_invoice_line_taxes_component");
    }
}
