using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="PurchaseInvoice"/>: what a purchase and a return share, kept in the
/// tables of each - <c>purchase_invoices</c> and <c>purchase_returns</c>.
/// </summary>
public sealed class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseInvoice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(invoice => invoice.Id);

        // Purchases and purchase returns are one model kept in two sets of tables - the
        // Purchase and Purchase Return master, details and additional - as the business's
        // own ledgers keep them. A query for purchase invoices reads both; each document
        // is written to its own.
        builder.UseTpcMappingStrategy();
        builder.Property(invoice => invoice.Id).ValueGeneratedNever();

        builder.Property(invoice => invoice.Number).HasMaxLength(50).IsRequired();
        builder.Property(invoice => invoice.Date).IsRequired();
        builder.Property(invoice => invoice.Mode).HasConversion<int>().IsRequired();
        builder.Property(invoice => invoice.Status).HasConversion<int>().IsRequired();
        builder.Property(invoice => invoice.Kind).HasConversion<int>().IsRequired();

        builder.Ignore(invoice => invoice.IsReturn);

        builder.HasIndex(invoice => invoice.ReturnsInvoiceId)
            .HasDatabaseName("ix_purchase_invoices_returns_invoice")
            .HasFilter("returns_invoice_id IS NOT NULL");

        builder.Property(invoice => invoice.Currency)
            .HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Property(invoice => invoice.SupplierInvoiceNumber).HasMaxLength(50);
        builder.Property(invoice => invoice.Narration)
            .HasMaxLength(PurchaseInvoice.MaximumNarrationLength);
        builder.Property(invoice => invoice.CancellationReason)
            .HasMaxLength(PurchaseInvoice.MaximumNarrationLength);

        // Every total is derived from the lines and the charges. Storing them as well
        // would create a second answer that can disagree with the one it came from.
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
            .HasForeignKey(invoice => invoice.SupplierLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(invoice => invoice.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // What the posting produced. Restricted, all three, for the reason a sale's are:
        // a purchase whose receipt, bill or journal could be deleted would claim to have
        // been accounted for by something that no longer exists.
        builder.HasOne<StockDocument>()
            .WithMany()
            .HasForeignKey(invoice => invoice.StockDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // The note whose goods the invoice bills. Looked up from the note's side - which
        // invoice, if any, billed it - so it is indexed.
        builder.HasOne<StockDocument>()
            .WithMany()
            .HasForeignKey(invoice => invoice.ReceiptNoteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(invoice => invoice.ReceiptNoteId)
            .HasFilter("receipt_note_id IS NOT NULL")
            .HasDatabaseName("ix_purchase_invoices_receipt_note");

        builder.HasOne<Bill>()
            .WithMany()
            .HasForeignKey(invoice => invoice.BillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Voucher>()
            .WithMany()
            .HasForeignKey(invoice => invoice.JournalVoucherId)
            .OnDelete(DeleteBehavior.Restrict);

        // The firm's own number, unique within the firm as a sale's is.
        builder
            .HasIndex(invoice => new { invoice.FirmId, invoice.Number })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_purchase_invoices_number");

        // The supplier's number, unique per supplier. A supplier's invoice entered twice
        // is input tax reclaimed twice, which is the expensive kind of duplicate: it is
        // caught by an assessor rather than by a reader of the creditors report. A
        // cancelled purchase reclaims nothing, so it gives the number up and the invoice
        // can be entered again correctly.
        builder
            .HasIndex(invoice => new
            {
                invoice.FirmId,
                invoice.SupplierLedgerId,
                invoice.SupplierInvoiceNumber,
            })
            .IsUnique()
            .HasFilter("supplier_invoice_number IS NOT NULL AND is_deleted = false AND status <> 3")
            .HasDatabaseName("ix_purchase_invoices_supplier_number");

        // The list screen's own query: a firm's purchases over a date range, newest first.
        builder
            .HasIndex(invoice => new { invoice.FirmId, invoice.Date })
            .HasDatabaseName("ix_purchase_invoices_date");

        // What a supplier's statement asks for.
        builder
            .HasIndex(invoice => new { invoice.FirmId, invoice.SupplierLedgerId, invoice.Date })
            .HasDatabaseName("ix_purchase_invoices_supplier");

        builder.HasIndex(invoice => invoice.TenantId)
            .HasDatabaseName("ix_purchase_invoices_tenant");

        builder.HasMany(invoice => invoice.Lines)
            .WithOne()
            .HasForeignKey(line => line.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(invoice => invoice.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_lines");

        builder.HasMany(invoice => invoice.Charges)
            .WithOne()
            .HasForeignKey(charge => charge.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(invoice => invoice.Charges)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_charges");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="PurchaseInvoiceLine"/>: what the lines of a purchase and of a return share.</summary>
public sealed class PurchaseInvoiceLineConfiguration
    : IEntityTypeConfiguration<PurchaseInvoiceLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseInvoiceLine> builder)
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
        builder.Property(line => line.SellingRate)
            .HasPrecision(19, StockBalance.CostScale);
        builder.Property(line => line.Discount)
            .HasPrecision(19, 4).IsRequired();

        builder.Property(line => line.LineNumber).IsRequired();

        // Text rather than a reference, because a purchase is usually the moment the
        // batch comes into existence. Same length the batch register allows, so a number
        // that fits here fits there when the receipt opens it.
        builder.Property(line => line.BatchNumber).HasMaxLength(50);

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
            .HasForeignKey(tax => tax.PurchaseInvoiceLineId)
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
            .HasIndex(line => new { line.PurchaseInvoiceId, line.LineNumber })
            .HasDatabaseName("ix_purchase_invoice_lines_invoice");

        // What was bought, and at what: the last cost of a product, and the purchase
        // analysis reports.
        builder.HasIndex(line => line.ProductId)
            .HasDatabaseName("ix_purchase_invoice_lines_product");

        builder.HasIndex(line => line.TenantId)
            .HasDatabaseName("ix_purchase_invoice_lines_tenant");

        builder.HasMany(line => line.Serials)
            .WithOne()
            .HasForeignKey(link => link.PurchaseInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(line => line.Serials)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_serials");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>
/// Maps <see cref="PurchaseInvoiceLineSerial"/>: what the serial numbers on a purchase's
/// and a return's lines share.
/// </summary>
public sealed class PurchaseInvoiceLineSerialConfiguration
    : IEntityTypeConfiguration<PurchaseInvoiceLineSerial>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseInvoiceLineSerial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseTpcMappingStrategy();

        // The number is the key alongside the line, not a reference to a register row:
        // the unit does not exist until the receipt posts. Two boxes with one number on
        // one line is refused here as well as by the aggregate.
        builder.HasKey(link => new { link.PurchaseInvoiceLineId, link.SerialNumber });

        builder.Property(link => link.SerialNumber).HasMaxLength(100).IsRequired();

        // Which purchase brought this unit in: what a warranty claim against the supplier
        // starts from, with the machine in front of somebody and no idea where it came
        // from.
        builder.HasIndex(link => new { link.TenantId, link.SerialNumber })
            .HasDatabaseName("ix_purchase_invoice_line_serials_serial");
    }
}

/// <summary>
/// Maps <see cref="PurchaseInvoiceCharge"/>: what the charges of a purchase and of a
/// return share.
/// </summary>
public sealed class PurchaseInvoiceChargeConfiguration
    : IEntityTypeConfiguration<PurchaseInvoiceCharge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseInvoiceCharge> builder)
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

        // One row per account per purchase: the aggregate refuses a second, and the
        // database says so too rather than trusting it.
        builder
            .HasIndex(charge => new { charge.PurchaseInvoiceId, charge.LedgerId })
            .IsUnique()
            .HasDatabaseName("ix_purchase_invoice_charges_ledger");

        builder.HasIndex(charge => charge.TenantId)
            .HasDatabaseName("ix_purchase_invoice_charges_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>
/// Maps <see cref="PurchaseInvoiceLineTax"/>: what the tax on a purchase's and a return's
/// lines shares.
/// </summary>
public sealed class PurchaseInvoiceLineTaxConfiguration
    : IEntityTypeConfiguration<PurchaseInvoiceLineTax>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseInvoiceLineTax> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseTpcMappingStrategy();

        // One row per head per line. The pair is the key, so a line cannot carry two
        // figures for the same component - which a return would then double-reclaim.
        builder.HasKey(tax => new { tax.PurchaseInvoiceLineId, tax.Type });

        builder.Property(tax => tax.Type).HasConversion<int>().IsRequired();
        builder.Property(tax => tax.Percentage).HasPrecision(9, 4).IsRequired();
        builder.Property(tax => tax.Amount).HasPrecision(19, 4).IsRequired();

        // What the input half of a tax return reads: every line of every purchase
        // carrying this head.
        builder.HasIndex(tax => new { tax.TenantId, tax.Type })
            .HasDatabaseName("ix_purchase_invoice_line_taxes_component");
    }
}

// Each kind's tables, and the links between them. The links are declared between the
// kinds themselves - a purchase's line to the purchase, a return's line to the return -
// since a link to what both share could point into either set of tables, and is held by
// no database key. Declared this way, the database holds each one, and a save writes a
// master before its lines and a line before its tax.

/// <summary>Maps <see cref="PurchaseEntry"/>: the Purchase master.</summary>
public sealed class PurchaseEntryConfiguration : IEntityTypeConfiguration<PurchaseEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_invoices", table => table.HasCheckConstraint("ck_purchase_invoices_kind", "kind = 1"));
    }
}

/// <summary>Maps <see cref="PurchaseEntryLine"/>: the Purchase details.</summary>
public sealed class PurchaseEntryLineConfiguration : IEntityTypeConfiguration<PurchaseEntryLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseEntryLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_invoice_lines");

        builder.HasOne<PurchaseEntry>()
            .WithMany()
            .HasForeignKey(line => line.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="PurchaseEntryLineSerial"/>: the serial numbers on a purchase's lines.</summary>
public sealed class PurchaseEntryLineSerialConfiguration : IEntityTypeConfiguration<PurchaseEntryLineSerial>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseEntryLineSerial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_invoice_line_serials");

        builder.HasOne<PurchaseEntryLine>()
            .WithMany()
            .HasForeignKey(link => link.PurchaseInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="PurchaseEntryLineTax"/>: the tax on a purchase's lines.</summary>
public sealed class PurchaseEntryLineTaxConfiguration : IEntityTypeConfiguration<PurchaseEntryLineTax>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseEntryLineTax> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_invoice_line_taxes");

        builder.HasOne<PurchaseEntryLine>()
            .WithMany()
            .HasForeignKey(tax => tax.PurchaseInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="PurchaseEntryCharge"/>: the Purchase additional table.</summary>
public sealed class PurchaseEntryChargeConfiguration : IEntityTypeConfiguration<PurchaseEntryCharge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseEntryCharge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_invoice_charges");

        builder.HasOne<PurchaseEntry>()
            .WithMany()
            .HasForeignKey(charge => charge.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="PurchaseReturn"/>: the Purchase Return master.</summary>
public sealed class PurchaseReturnConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseReturn> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_returns", table => table.HasCheckConstraint("ck_purchase_returns_kind", "kind = 2"));

        // A return points at the purchase it is against, and nothing may delete that
        // purchase out from under it - the link is how the debit finds the debt.
        builder.HasOne<PurchaseEntry>()
            .WithMany()
            .HasForeignKey(invoice => invoice.ReturnsInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <see cref="PurchaseReturnLine"/>: the Purchase Return details.</summary>
public sealed class PurchaseReturnLineConfiguration : IEntityTypeConfiguration<PurchaseReturnLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseReturnLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_return_lines", table => table.Property(line => line.PurchaseInvoiceId).HasColumnName("purchase_return_id"));

        builder.HasOne<PurchaseReturn>()
            .WithMany()
            .HasForeignKey(line => line.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="PurchaseReturnLineSerial"/>: the serial numbers on a return's lines.</summary>
public sealed class PurchaseReturnLineSerialConfiguration : IEntityTypeConfiguration<PurchaseReturnLineSerial>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseReturnLineSerial> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_return_line_serials", table => table.Property(link => link.PurchaseInvoiceLineId).HasColumnName("purchase_return_line_id"));

        builder.HasOne<PurchaseReturnLine>()
            .WithMany()
            .HasForeignKey(link => link.PurchaseInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="PurchaseReturnLineTax"/>: the tax on a return's lines.</summary>
public sealed class PurchaseReturnLineTaxConfiguration : IEntityTypeConfiguration<PurchaseReturnLineTax>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseReturnLineTax> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_return_line_taxes", table => table.Property(tax => tax.PurchaseInvoiceLineId).HasColumnName("purchase_return_line_id"));

        builder.HasOne<PurchaseReturnLine>()
            .WithMany()
            .HasForeignKey(tax => tax.PurchaseInvoiceLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="PurchaseReturnCharge"/>: the Purchase Return additional table.</summary>
public sealed class PurchaseReturnChargeConfiguration : IEntityTypeConfiguration<PurchaseReturnCharge>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PurchaseReturnCharge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("purchase_return_charges", table => table.Property(charge => charge.PurchaseInvoiceId).HasColumnName("purchase_return_id"));

        builder.HasOne<PurchaseReturn>()
            .WithMany()
            .HasForeignKey(charge => charge.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
