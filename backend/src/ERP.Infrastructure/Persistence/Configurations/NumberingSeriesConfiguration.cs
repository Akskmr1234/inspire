using ERP.Domain.Numbering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="NumberingSeries"/> to the <c>numbering_series</c> table.</summary>
public sealed class NumberingSeriesConfiguration : IEntityTypeConfiguration<NumberingSeries>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NumberingSeries> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("numbering_series");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DocumentType).HasMaxLength(60).IsRequired();
        builder.Property(s => s.Prefix).HasMaxLength(20);
        builder.Property(s => s.Suffix).HasMaxLength(20);
        builder.Property(s => s.Separator).HasMaxLength(5).IsRequired();
        builder.Property(s => s.FinancialYearLabel).HasMaxLength(20);
        builder.Property(s => s.StartingNumber).IsRequired();
        builder.Property(s => s.NumberLength).IsRequired();
        builder.Property(s => s.NextNumber).IsRequired();
        builder.Property(s => s.IsActive).IsRequired();

        // At most one series per document type, branch, and year. Without this, a
        // duplicate row would silently create two counters for the same documents
        // and the sequence would appear to jump about at random.
        //
        // NULLS NOT DISTINCT is essential: PostgreSQL treats NULLs as distinct by
        // default, so two firm-wide series (both with a NULL branch) would otherwise
        // both be allowed.
        builder
            .HasIndex(s => new { s.FirmId, s.DocumentType, s.BranchId, s.FinancialYearId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ix_numbering_series_scope");

        builder.HasIndex(s => s.TenantId).HasDatabaseName("ix_numbering_series_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
