using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="Branch"/> to the <c>branches</c> table.</summary>
public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("branches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.Code).HasMaxLength(20).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.NameArabic).HasMaxLength(200);
        builder.Property(b => b.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(b => b.AddressLine1).HasMaxLength(200);
        builder.Property(b => b.AddressLine2).HasMaxLength(200);
        builder.Property(b => b.Phone).HasMaxLength(32);
        builder.Property(b => b.Email).HasMaxLength(256);

        builder.Property(b => b.IsHeadOffice).IsRequired();
        builder.Property(b => b.IsActive).IsRequired();
        builder.Property(b => b.IsDeleted).IsRequired();

        builder
            .HasIndex(b => new { b.FirmId, b.Code })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_branches_firm_code");

        builder.HasIndex(b => b.TenantId).HasDatabaseName("ix_branches_tenant");

        // Enforces the single-head-office rule in the database as well as the
        // domain. The domain check cannot see a concurrent transaction adding a
        // second head office at the same moment; a partial unique index can.
        builder
            .HasIndex(b => b.FirmId)
            .IsUnique()
            .HasFilter("is_head_office = true AND is_deleted = false")
            .HasDatabaseName("ix_branches_single_head_office");

        ConfigurationConventions.ApplyAuditConventions(builder);
    }
}
