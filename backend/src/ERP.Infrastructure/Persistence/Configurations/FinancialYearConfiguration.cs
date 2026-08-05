using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="FinancialYear"/> to the <c>financial_years</c> table.</summary>
public sealed class FinancialYearConfiguration : IEntityTypeConfiguration<FinancialYear>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FinancialYear> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("financial_years");
        builder.HasKey(y => y.Id);

        builder.Property(y => y.Id).ValueGeneratedNever();

        builder.Property(y => y.Code).HasMaxLength(20).IsRequired();
        builder.Property(y => y.StartDate).IsRequired();
        builder.Property(y => y.EndDate).IsRequired();
        builder.Property(y => y.Status).HasConversion<int>().IsRequired();

        builder
            .HasIndex(y => new { y.FirmId, y.Code })
            .IsUnique()
            .HasDatabaseName("ix_financial_years_firm_code");

        // Resolving which year a document date falls into happens on every
        // posting, so the range lookup is indexed.
        builder
            .HasIndex(y => new { y.FirmId, y.StartDate, y.EndDate })
            .HasDatabaseName("ix_financial_years_firm_range");

        builder.HasIndex(y => y.TenantId).HasDatabaseName("ix_financial_years_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
