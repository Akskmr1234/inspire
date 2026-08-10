using ERP.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="AdditionalLedger"/> to the <c>additional_ledgers</c> table.</summary>
public sealed class AdditionalLedgerConfiguration : IEntityTypeConfiguration<AdditionalLedger>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AdditionalLedger> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("additional_ledgers");
        builder.HasKey(charge => charge.Id);
        builder.Property(charge => charge.Id).ValueGeneratedNever();

        builder.Property(charge => charge.Document).HasConversion<int>().IsRequired();

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(charge => charge.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        // One mapping per ledger per document type. A second would be the same charge
        // offered twice on one invoice, with two sets of flags that can disagree about
        // whether it applies.
        builder
            .HasIndex(charge => new { charge.FirmId, charge.Document, charge.LedgerId })
            .IsUnique()
            .HasDatabaseName("ix_additional_ledgers_mapping");

        // What a document entry screen asks for: the charges that belong on this kind
        // of document, in the order they are shown.
        builder
            .HasIndex(charge => new { charge.FirmId, charge.Document, charge.DisplayOrder })
            .HasDatabaseName("ix_additional_ledgers_document");

        builder.HasIndex(charge => charge.TenantId)
            .HasDatabaseName("ix_additional_ledgers_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
