using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Firm"/> to the <c>firms</c> table.</summary>
public sealed class FirmConfiguration : IEntityTypeConfiguration<Firm>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Firm> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("firms");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Code).HasMaxLength(20).IsRequired();
        builder.Property(f => f.Name).HasMaxLength(200).IsRequired();
        builder.Property(f => f.NameArabic).HasMaxLength(200);
        builder.Property(f => f.TaxRegistrationNumber).HasMaxLength(50);
        builder.Property(f => f.StateCode).HasMaxLength(10);
        builder.Property(f => f.TimeZoneId).HasMaxLength(64).IsRequired();

        // Persisted as an integer rather than a string. The value is compared in
        // row-level-security predicates and report SQL, and an int comparison
        // avoids any collation or casing question.
        builder.Property(f => f.TaxRegime).HasConversion<int>().IsRequired();

        builder.Property(f => f.IsActive).IsRequired();
        builder.Property(f => f.IsDeleted).IsRequired();

        // A firm code must be unique within its tenant, not globally - two
        // unrelated customers may both have a firm called "HO". Filtered so a
        // soft-deleted firm does not permanently reserve its code.
        builder
            .HasIndex(f => new { f.TenantId, f.Code })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_firms_tenant_code");

        builder.HasIndex(f => f.TenantId).HasDatabaseName("ix_firms_tenant");

        // Branches are reached through the firm, which owns the uniqueness and
        // single-head-office invariants across the whole set.
        builder
            .HasMany(f => f.Branches)
            .WithOne()
            .HasForeignKey(b => b.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(f => f.Branches)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_branches");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
