using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="SalesInvoice"/>: what a sale and a return share, kept in the tables of
/// each - <c>sales_invoices</c> and <c>sales_returns</c>.
/// </summary>
public sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesInvoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(invoice => invoice.Id);

        // Sales and sales returns are one model kept in two sets of tables - the Sales
        // and Sales Return master, details and additional - as the business's own ledgers
        // keep them. A query for sales invoices reads both; each document is written to
        // its own.
        builder.UseTpcMappingStrategy();
        builder.Property(invoice => invoice.Id).ValueGeneratedNever();

        builder.Property(invoice => invoice.Number).HasMaxLength(50).IsRequired();
        builder.Property(invoice => invoice.Date).IsRequired();
        builder.Property(invoice => invoice.Mode).HasConversion<int>().IsRequired();
        builder.Property(invoice => invoice.Status).HasConversion<int>().IsRequired();
        builder.Property(invoice => invoice.Kind).HasConversion<int>().IsRequired();

        builder.Ignore(invoice => invoice.IsReturn);

        builder.HasIndex(invoice => invoice.ReturnsInvoiceId)
            .HasDatabaseName("ix_sales_invoices_returns_invoice")
            .HasFilter("returns_invoice_id IS NOT NULL");

        // The order this was raised from. Restricted for the same reason a return's link
        // is: cancelling the invoice reads it to put the quantities back.
        builder.HasOne<SalesOrder>()
            .WithMany()
            .HasForeignKey(invoice => invoice.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(invoice => invoice.SalesOrderId)
            .HasDatabaseName("ix_sales_invoices_order")
            .HasFilter("sales_order_id IS NOT NULL");

        builder.Property(invoice => invoice.Currency)
            .HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Property(invoice => invoice.ReferenceNumber)
            .HasMaxLength(SalesInvoice.MaximumNarrationLength);
        builder.Property(invoice => invoice.Narration)
            .HasMaxLength(SalesInvoice.MaximumNarrationLength);
        builder.Property(invoice => invoice.CancellationReason)
            .HasMaxLength(SalesInvoice.MaximumNarrationLength);

        // Every total is derived from the lines and the charges. The domain's properties
        // stay the one answer; the stored copies below are written from them on save.
        builder.Ignore(invoice => invoice.IsEditable);
        builder.Ignore(invoice => invoice.Taxable);
        builder.Ignore(invoice => invoice.Tax);
        builder.Ignore(invoice => invoice.ChargeTotal);
        builder.Ignore(invoice => invoice.GrossTotal);
        builder.Ignore(invoice => invoice.RoundingDifference);
        builder.Ignore(invoice => invoice.Total);

        // Kept on the master as well, for reports and anything reading the table: written
        // from the same figures on every save, never typed in.
        StoredTotals.ConfigureMaster(builder);

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

        // Invoices entered before the column existed were all on account; the migration
        // fills it in for them as credit.
        builder.Property(invoice => invoice.PaymentMode).HasConversion<int>().IsRequired();

        // What was paid with the sale, and where it went. Zero and empty for every sale
        // entered before the columns existed, which were all left on account.
        builder.Property(invoice => invoice.AmountReceived)
            .HasPrecision(19, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(invoice => invoice.PaymentReference)
            .HasMaxLength(SalesInvoice.MaximumPaymentReferenceLength);

        // How an online payment was made, and the name the bill is made out to: empty for
        // every sale entered before the columns existed, which said neither.
        builder.Property(invoice => invoice.OnlinePaymentType)
            .HasMaxLength(SalesInvoice.MaximumOnlinePaymentTypeLength);
        builder.Property(invoice => invoice.BillingName)
            .HasMaxLength(SalesInvoice.MaximumBillingNameLength);
        builder.Property(invoice => invoice.BillingMobile)
            .HasMaxLength(SalesInvoice.MaximumBillingMobileLength);
        builder.Property(invoice => invoice.BillingAddress)
            .HasMaxLength(SalesInvoice.MaximumBillingAddressLength);

        // Zero - the currency's own rounding, and nothing more - for every sale entered
        // before a firm could round to a unit of its own.
        builder.Property(invoice => invoice.RoundTo)
            .HasPrecision(19, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(invoice => invoice.ReceivedIntoLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(invoice => invoice.ReceiptVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(invoice => invoice.SalesmanLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(invoice => invoice.BillingManLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        // What the posting produced. Restricted, all three: a sale whose issue, bill or
        // journal could be deleted would be a sale claiming to have been accounted for
        // by something that no longer exists.
        builder.HasOne<StockDocument>()
            .WithMany()
            .HasForeignKey(invoice => invoice.StockDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // The note whose goods the invoice bills. Looked up from the note's side - which
        // invoice, if any, billed it - so it is indexed.
        builder.HasOne<StockDocument>()
            .WithMany()
            .HasForeignKey(invoice => invoice.DeliveryNoteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(invoice => invoice.DeliveryNoteId)
            .HasFilter("delivery_note_id IS NOT NULL")
            .HasDatabaseName("ix_sales_invoices_delivery_note");

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

/// <summary>Maps <see cref="SalesInvoiceLine"/>: what the lines of a sale and of a return share.</summary>
public sealed class SalesInvoiceLineConfiguration : IEntityTypeConfiguration<SalesInvoiceLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesInvoiceLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(line => line.Id);
        builder.UseTpcMappingStrategy();
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

        // The amounts as their figures and currencies, rather than as complex properties:
        // a return's lines are kept in a table of their own, and EF Core leaves a complex
        // property's columns out of a derived type's table.
        builder.Ignore(line => line.TaxableAmount);
        builder.Ignore(line => line.TaxAmount);
        builder.Property(line => line.TaxableValue)
            .HasColumnName("taxable_amount").HasPrecision(19, 4).IsRequired();
        builder.Property(line => line.Currency)
            .HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(line => line.TaxValue)
            .HasColumnName("tax_amount").HasPrecision(19, 4).IsRequired();
        builder.Property(line => line.TaxCurrency)
            .HasColumnName("tax_currency").HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Ignore(line => line.LineTotal);
        StoredTotals.ConfigureLine(builder);

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
/// Maps <see cref="SalesInvoiceLineSerial"/>: what the serial numbers on a sale's and a
/// return's lines share.
/// </summary>
public sealed class SalesInvoiceLineSerialConfiguration
    : IEntityTypeConfiguration<SalesInvoiceLineSerial>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesInvoiceLineSerial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseTpcMappingStrategy();
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

/// <summary>Maps <see cref="SalesInvoiceCharge"/>: what the charges of a sale and of a return share.</summary>
public sealed class SalesInvoiceChargeConfiguration : IEntityTypeConfiguration<SalesInvoiceCharge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesInvoiceCharge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(charge => charge.Id);
        builder.UseTpcMappingStrategy();
        builder.Property(charge => charge.Id).ValueGeneratedNever();

        // As its figure and currency, for the same reason as a line's amounts.
        builder.Ignore(charge => charge.Amount);
        builder.Property(charge => charge.AmountValue)
            .HasColumnName("amount").HasPrecision(19, 4).IsRequired();
        builder.Property(charge => charge.Currency)
            .HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();

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
/// Maps <see cref="SalesInvoiceLineTax"/>: what the tax on a sale's and a return's lines
/// shares.
/// </summary>
public sealed class SalesInvoiceLineTaxConfiguration
    : IEntityTypeConfiguration<SalesInvoiceLineTax>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesInvoiceLineTax> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseTpcMappingStrategy();

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

// Each kind's tables, and the links between them. The links are declared between the
// kinds themselves - a sale's line to the sale, a return's line to the return - since a
// link to what both share could point into either set of tables, and is held by no
// database key. Declared this way, the database holds each one, and a save writes a
// master before its lines and a line before its tax.

/// <summary>Maps <see cref="Sale"/>: the Sales master.</summary>
public sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_invoices", table => table.HasCheckConstraint("ck_sales_invoices_kind", "kind = 1"));
    }
}

/// <summary>Maps <see cref="SaleLine"/>: the Sales details.</summary>
public sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_invoice_lines");

        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(line => line.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="SaleLineSerial"/>: the serial numbers on a sale's lines.</summary>
public sealed class SaleLineSerialConfiguration : IEntityTypeConfiguration<SaleLineSerial>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SaleLineSerial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_invoice_line_serials");

        builder.HasOne<SaleLine>()
            .WithMany()
            .HasForeignKey(link => link.SalesInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="SaleLineTax"/>: the tax on a sale's lines.</summary>
public sealed class SaleLineTaxConfiguration : IEntityTypeConfiguration<SaleLineTax>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SaleLineTax> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_invoice_line_taxes");

        builder.HasOne<SaleLine>()
            .WithMany()
            .HasForeignKey(tax => tax.SalesInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="SaleCharge"/>: the Sales additional table.</summary>
public sealed class SaleChargeConfiguration : IEntityTypeConfiguration<SaleCharge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SaleCharge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_invoice_charges");

        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(charge => charge.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="SalesReturn"/>: the Sales Return master.</summary>
public sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_returns", table => table.HasCheckConstraint("ck_sales_returns_kind", "kind = 2"));

        // A return points at the sale it is against, and nothing may delete that sale out
        // from under it - the link is how the credit finds the debt.
        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(invoice => invoice.ReturnsInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <see cref="SalesReturnLine"/>: the Sales Return details.</summary>
public sealed class SalesReturnLineConfiguration : IEntityTypeConfiguration<SalesReturnLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesReturnLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_return_lines", table => table.Property(line => line.SalesInvoiceId).HasColumnName("sales_return_id"));

        builder.HasOne<SalesReturn>()
            .WithMany()
            .HasForeignKey(line => line.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="SalesReturnLineSerial"/>: the serial numbers on a return's lines.</summary>
public sealed class SalesReturnLineSerialConfiguration : IEntityTypeConfiguration<SalesReturnLineSerial>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesReturnLineSerial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "sales_return_line_serials",
            table => table.Property(link => link.SalesInvoiceLineId).HasColumnName("sales_return_line_id"));

        builder.HasOne<SalesReturnLine>()
            .WithMany()
            .HasForeignKey(link => link.SalesInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="SalesReturnLineTax"/>: the tax on a return's lines.</summary>
public sealed class SalesReturnLineTaxConfiguration : IEntityTypeConfiguration<SalesReturnLineTax>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesReturnLineTax> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "sales_return_line_taxes",
            table => table.Property(tax => tax.SalesInvoiceLineId).HasColumnName("sales_return_line_id"));

        builder.HasOne<SalesReturnLine>()
            .WithMany()
            .HasForeignKey(tax => tax.SalesInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="SalesReturnCharge"/>: the Sales Return additional table.</summary>
public sealed class SalesReturnChargeConfiguration : IEntityTypeConfiguration<SalesReturnCharge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SalesReturnCharge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sales_return_charges", table => table.Property(charge => charge.SalesInvoiceId).HasColumnName("sales_return_id"));

        builder.HasOne<SalesReturn>()
            .WithMany()
            .HasForeignKey(charge => charge.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
