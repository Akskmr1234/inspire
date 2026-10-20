using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
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
