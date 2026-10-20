using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Service;
using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="JobCard"/> to the <c>job_cards</c> table.</summary>
public sealed class JobCardConfiguration : IEntityTypeConfiguration<JobCard>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<JobCard> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("job_cards");
        builder.HasKey(card => card.Id);
        builder.Property(card => card.Id).ValueGeneratedNever();

        builder.Property(card => card.Number).HasMaxLength(50).IsRequired();
        builder.Property(card => card.Status).HasConversion<int>().IsRequired();
        builder.Property(card => card.PaymentMode).HasConversion<int>().IsRequired();

        builder.Property(card => card.CustomerName).HasMaxLength(JobCard.MaximumShortLength).IsRequired();
        builder.Property(card => card.Mobile).HasMaxLength(JobCard.MaximumShortLength);
        builder.Property(card => card.AlternateMobile).HasMaxLength(JobCard.MaximumShortLength);
        builder.Property(card => card.Email).HasMaxLength(JobCard.MaximumShortLength);
        builder.Property(card => card.AddressLine1).HasMaxLength(JobCard.MaximumLongLength);
        builder.Property(card => card.AddressLine2).HasMaxLength(JobCard.MaximumLongLength);
        builder.Property(card => card.ReferenceNumber).HasMaxLength(JobCard.MaximumShortLength);

        builder.Property(card => card.ItemName).HasMaxLength(JobCard.MaximumShortLength).IsRequired();
        builder.Property(card => card.BrandName).HasMaxLength(JobCard.MaximumShortLength);
        builder.Property(card => card.ModelNumber).HasMaxLength(JobCard.MaximumShortLength);
        builder.Property(card => card.Colour).HasMaxLength(JobCard.MaximumShortLength);
        builder.Property(card => card.SerialNumber).HasMaxLength(JobCard.MaximumShortLength);
        builder.Property(card => card.Accessories).HasMaxLength(JobCard.MaximumLongLength);

        builder.Property(card => card.ServiceType).HasMaxLength(JobCard.MaximumShortLength);
        builder.Property(card => card.Fault).HasMaxLength(JobCard.MaximumLongLength);
        builder.Property(card => card.Problem).HasMaxLength(JobCard.MaximumLongLength);
        builder.Property(card => card.Remarks).HasMaxLength(JobCard.MaximumLongLength);
        builder.Property(card => card.Notes).HasMaxLength(JobCard.MaximumLongLength);
        builder.Property(card => card.EstimatedTime).HasMaxLength(JobCard.MaximumShortLength);
        builder.Property(card => card.EstimatedCharge).HasPrecision(19, 4).IsRequired();
        builder.Property(card => card.CancellationReason).HasMaxLength(JobCard.MaximumLongLength);

        builder.Ignore(card => card.IsBillable);

        builder.HasOne<FinancialYear>()
            .WithMany()
            .HasForeignKey(card => card.FinancialYearId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(card => card.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(card => card.CustomerLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(card => card.SalesmanLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(card => card.TechnicianLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SalesInvoice>()
            .WithMany()
            .HasForeignKey(card => card.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(card => new { card.FirmId, card.Number })
            .IsUnique()
            .HasDatabaseName("ix_job_cards_number");

        // The counter finds a customer's jobs by the number they give.
        builder.HasIndex(card => new { card.FirmId, card.Mobile })
            .HasDatabaseName("ix_job_cards_mobile");

        builder.HasIndex(card => card.SalesInvoiceId)
            .HasFilter("sales_invoice_id IS NOT NULL")
            .HasDatabaseName("ix_job_cards_invoice");

        builder.HasIndex(card => card.TenantId).HasDatabaseName("ix_job_cards_tenant");

        builder.HasMany(card => card.Lines)
            .WithOne()
            .HasForeignKey(line => line.JobCardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(card => card.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_lines");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="JobCardLine"/> to the <c>job_card_lines</c> table.</summary>
public sealed class JobCardLineConfiguration : IEntityTypeConfiguration<JobCardLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<JobCardLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("job_card_lines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Quantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();
        builder.Property(line => line.Rate).HasPrecision(19, 4).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.Remarks).HasMaxLength(JobCard.MaximumLongLength);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(line => line.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.JobCardId, line.LineNumber })
            .HasDatabaseName("ix_job_card_lines_card");

        builder.HasIndex(line => line.ProductId).HasDatabaseName("ix_job_card_lines_product");
        builder.HasIndex(line => line.TenantId).HasDatabaseName("ix_job_card_lines_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>Maps <see cref="Rma"/> to the <c>rmas</c> table.</summary>
public sealed class RmaConfiguration : IEntityTypeConfiguration<Rma>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Rma> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rmas");
        builder.HasKey(rma => rma.Id);
        builder.Property(rma => rma.Id).ValueGeneratedNever();

        builder.Property(rma => rma.Number).HasMaxLength(50).IsRequired();
        builder.Property(rma => rma.Direction).HasConversion<int>().IsRequired();
        builder.Property(rma => rma.Status).HasConversion<int>().IsRequired();
        builder.Property(rma => rma.Condition).HasConversion<int?>();
        builder.Property(rma => rma.InvoiceReference).HasMaxLength(Rma.MaximumTextLength);
        builder.Property(rma => rma.Reason).HasMaxLength(Rma.MaximumTextLength).IsRequired();
        builder.Property(rma => rma.Narration).HasMaxLength(Rma.MaximumTextLength);
        builder.Property(rma => rma.InspectionNotes).HasMaxLength(Rma.MaximumTextLength);

        builder.Ignore(rma => rma.IsEditable);
        builder.Ignore(rma => rma.AwaitsOutcome);
        builder.Ignore(rma => rma.IsFinished);

        builder.HasOne<FinancialYear>()
            .WithMany()
            .HasForeignKey(rma => rma.FinancialYearId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(rma => rma.PartyLedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(rma => rma.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SalesInvoice>()
            .WithMany()
            .HasForeignKey(rma => rma.SalesInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PurchaseInvoice>()
            .WithMany()
            .HasForeignKey(rma => rma.PurchaseInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // What settled it. Restricted, like every other link to a posted document: a return
        // claiming a refund whose note could vanish would be a refund nobody can find.
        builder.HasOne<StockDocument>()
            .WithMany()
            .HasForeignKey(rma => rma.ReplacementDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SalesInvoice>()
            .WithMany()
            .HasForeignKey(rma => rma.CreditNoteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PurchaseInvoice>()
            .WithMany()
            .HasForeignKey(rma => rma.DebitNoteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(rma => new { rma.FirmId, rma.Number })
            .IsUnique()
            .HasDatabaseName("ix_rmas_number");

        builder.HasIndex(rma => new { rma.FirmId, rma.PartyLedgerId })
            .HasDatabaseName("ix_rmas_party");

        builder.HasIndex(rma => rma.TenantId).HasDatabaseName("ix_rmas_tenant");

        builder.HasMany(rma => rma.Lines)
            .WithOne()
            .HasForeignKey(line => line.RmaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(rma => rma.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_lines");

        builder.HasMany(rma => rma.Photos)
            .WithOne()
            .HasForeignKey(photo => photo.RmaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(rma => rma.Photos)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_photos");

        builder.HasMany(rma => rma.Events)
            .WithOne()
            .HasForeignKey(entry => entry.RmaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(rma => rma.Events)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_events");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="RmaLine"/> to the <c>rma_lines</c> table.</summary>
public sealed class RmaLineConfiguration : IEntityTypeConfiguration<RmaLine>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RmaLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rma_lines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Id).ValueGeneratedNever();

        builder.Property(line => line.Quantity)
            .HasPrecision(18, StockBalance.QuantityScale).IsRequired();
        builder.Property(line => line.LineNumber).IsRequired();
        builder.Property(line => line.Reason).HasMaxLength(Rma.MaximumTextLength);
        builder.Property(line => line.SerialNumber).HasMaxLength(RmaLine.MaximumSerialLength);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(line => line.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Batch>()
            .WithMany()
            .HasForeignKey(line => line.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.RmaId, line.LineNumber })
            .HasDatabaseName("ix_rma_lines_rma");

        // A unit is looked up by its serial number when it comes back a second time.
        builder.HasIndex(line => line.SerialNumber)
            .HasFilter("serial_number IS NOT NULL")
            .HasDatabaseName("ix_rma_lines_serial");

        builder.HasIndex(line => line.ProductId).HasDatabaseName("ix_rma_lines_product");
        builder.HasIndex(line => line.TenantId).HasDatabaseName("ix_rma_lines_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>Maps <see cref="RmaPhoto"/> to the <c>rma_photos</c> table.</summary>
public sealed class RmaPhotoConfiguration : IEntityTypeConfiguration<RmaPhoto>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RmaPhoto> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rma_photos");
        builder.HasKey(photo => photo.Id);
        builder.Property(photo => photo.Id).ValueGeneratedNever();

        builder.Property(photo => photo.Content).HasMaxLength(RmaPhoto.MaximumLength).IsRequired();
        builder.Property(photo => photo.Caption).HasMaxLength(RmaLine.MaximumSerialLength);

        builder.HasIndex(photo => photo.RmaId).HasDatabaseName("ix_rma_photos_rma");
        builder.HasIndex(photo => photo.TenantId).HasDatabaseName("ix_rma_photos_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}

/// <summary>Maps <see cref="RmaEvent"/> to the <c>rma_events</c> table.</summary>
public sealed class RmaEventConfiguration : IEntityTypeConfiguration<RmaEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RmaEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("rma_events");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.Status).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.At).IsRequired();
        builder.Property(entry => entry.UserId).IsRequired();
        builder.Property(entry => entry.UserName).HasMaxLength(200).IsRequired();
        builder.Property(entry => entry.Note).HasMaxLength(Rma.MaximumTextLength);

        builder.HasIndex(entry => new { entry.RmaId, entry.At }).HasDatabaseName("ix_rma_events_rma");
        builder.HasIndex(entry => entry.TenantId).HasDatabaseName("ix_rma_events_tenant");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}
