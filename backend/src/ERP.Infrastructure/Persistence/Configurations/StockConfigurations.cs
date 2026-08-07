using ERP.Domain.Inventory;
using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="StockDocument"/> to the <c>stock_documents</c> table.</summary>
public sealed class StockDocumentConfiguration : IEntityTypeConfiguration<StockDocument>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_documents");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).ValueGeneratedNever();

        builder.Property(document => document.Number).HasMaxLength(50).IsRequired();
        builder.Property(document => document.Date).IsRequired();
        builder.Property(document => document.Type).HasConversion<int>().IsRequired();
        builder.Property(document => document.Status).HasConversion<int>().IsRequired();

        builder.Property(document => document.ReferenceNumber)
            .HasMaxLength(StockDocument.MaximumReferenceLength);
        builder.Property(document => document.Narration)
            .HasMaxLength(StockDocument.MaximumNarrationLength);
        builder.Property(document => document.CancellationReason)
            .HasMaxLength(StockDocument.MaximumNarrationLength);

        // Derived from the type. Storing them as well would create a second answer
        // that can disagree with the one it came from.
        builder.Ignore(document => document.IsEditable);
        builder.Ignore(document => document.IsTransfer);
        builder.Ignore(document => document.CarriesRate);
        builder.Ignore(document => document.AllowsSignedQuantity);

        builder.HasOne<FinancialYear>()
            .WithMany()
            .HasForeignKey(document => document.FinancialYearId)
            .OnDelete(DeleteBehavior.Restrict);

        // Both warehouse references restricted. Deleting a warehouse that stock has
        // moved through would leave movements pointing at a place that no longer
        // exists, which is a stock ledger nobody can reconcile.
        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(document => document.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(document => document.DestinationWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // The number is what the document is called on paper, so two of them within
        // one firm and one type would be two documents with the same name.
        builder
            .HasIndex(document => new { document.FirmId, document.Type, document.Number })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_stock_documents_number");

        // The list screen's own query: a firm's documents over a date range, newest
        // first.
        builder
            .HasIndex(document => new { document.FirmId, document.Date })
            .HasDatabaseName("ix_stock_documents_date");

        builder
            .HasIndex(document => new { document.FirmId, document.WarehouseId, document.Date })
            .HasDatabaseName("ix_stock_documents_warehouse");

        builder.HasIndex(document => document.TenantId)
            .HasDatabaseName("ix_stock_documents_tenant");

        builder.HasMany(document => document.Lines)
            .WithOne()
            .HasForeignKey(line => line.StockDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(document => document.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_lines");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="StockDocumentLine"/> to the <c>stock_document_lines</c> table.</summary>
public sealed class StockDocumentLineConfiguration
    : IEntityTypeConfiguration<StockDocumentLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockDocumentLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_document_lines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        // Six places on the quantities, matching the widest a unit of measure may
        // declare. A narrower column here would silently truncate a quantity the unit
        // itself considers valid.
        builder.Property(line => line.Quantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();
        builder.Property(line => line.StockQuantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();

        builder.Property(line => line.Rate)
            .HasPrecision(19, StockBalance.CostScale).IsRequired();

        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.Remarks)
            .HasMaxLength(StockDocument.MaximumNarrationLength);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(line => line.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(line => new { line.StockDocumentId, line.LineNumber })
            .HasDatabaseName("ix_stock_document_lines_document");

        builder.HasIndex(line => line.ProductId)
            .HasDatabaseName("ix_stock_document_lines_product");

        builder.HasIndex(line => line.TenantId)
            .HasDatabaseName("ix_stock_document_lines_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>Maps <see cref="StockBalance"/> to the <c>stock_balances</c> table.</summary>
public sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_balances");
        builder.HasKey(balance => balance.Id);
        builder.Property(balance => balance.Id).ValueGeneratedNever();

        builder.Property(balance => balance.Quantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();

        // Six places, not the currency's two. An average is a quotient, and rounding
        // it to the currency on every receipt would push the error into the valuation
        // in one direction for as long as the product exists.
        builder.Property(balance => balance.AverageCost)
            .HasPrecision(19, StockBalance.CostScale).IsRequired();

        builder.Property(balance => balance.Currency)
            .HasMaxLength(3).IsFixedLength().IsRequired();

        builder.Ignore(balance => balance.Value);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(balance => balance.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(balance => balance.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // One position per product per warehouse, enforced by the database rather
        // than by the code that opens them. Two rows for the same pair would each
        // hold half the stock and disagree about the cost, and nothing downstream
        // would notice - the valuation would simply be wrong by whichever row a query
        // happened to read.
        builder
            .HasIndex(balance => new { balance.FirmId, balance.ProductId, balance.WarehouseId })
            .IsUnique()
            .HasDatabaseName("ix_stock_balances_position");

        builder
            .HasIndex(balance => new { balance.FirmId, balance.WarehouseId })
            .HasDatabaseName("ix_stock_balances_warehouse");

        builder.HasIndex(balance => balance.TenantId)
            .HasDatabaseName("ix_stock_balances_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="StockLedgerEntry"/> to the <c>stock_ledger_entries</c> table.</summary>
public sealed class StockLedgerEntryConfiguration : IEntityTypeConfiguration<StockLedgerEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StockLedgerEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_ledger_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.Date).IsRequired();
        builder.Property(entry => entry.DocumentType).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.DocumentNumber).HasMaxLength(50).IsRequired();
        builder.Property(entry => entry.PostedAtUtc).IsRequired();
        builder.Property(entry => entry.Narration)
            .HasMaxLength(StockDocument.MaximumNarrationLength);

        builder.Property(entry => entry.Quantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();
        builder.Property(entry => entry.UnitCost)
            .HasPrecision(19, StockBalance.CostScale).IsRequired();
        builder.Property(entry => entry.BalanceQuantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();
        builder.Property(entry => entry.BalanceAverageCost)
            .HasPrecision(19, StockBalance.CostScale).IsRequired();

        builder.ComplexProperty(entry => entry.Value, value =>
        {
            value.Property(money => money.Amount)
                .HasColumnName("value").HasPrecision(19, 4).IsRequired();
            value.Property(money => money.Currency)
                .HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        });

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(entry => entry.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(entry => entry.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StockDocument>()
            .WithMany()
            .HasForeignKey(entry => entry.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // The stock ledger report's own query: one product in one warehouse, in the
        // order the movements were posted. Posting order rather than date order,
        // because that is the order the running balance column was computed in and
        // the only order in which it reads correctly.
        builder
            .HasIndex(entry => new
            {
                entry.FirmId,
                entry.ProductId,
                entry.WarehouseId,
                entry.PostedAtUtc,
            })
            .HasDatabaseName("ix_stock_ledger_position");

        // Cancellation reads every movement a document made, which is how a reversal
        // finds the cost each was valued at.
        builder.HasIndex(entry => entry.DocumentId)
            .HasDatabaseName("ix_stock_ledger_document");

        builder
            .HasIndex(entry => new { entry.FirmId, entry.Date })
            .HasDatabaseName("ix_stock_ledger_date");

        builder.HasIndex(entry => entry.TenantId)
            .HasDatabaseName("ix_stock_ledger_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
