using ERP.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="TaxAccountMap"/> to the <c>tax_account_maps</c> table.
/// </summary>
public sealed class TaxAccountMapConfiguration : IEntityTypeConfiguration<TaxAccountMap>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TaxAccountMap> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tax_account_maps");
        builder.HasKey(map => map.Id);
        builder.Property(map => map.Id).ValueGeneratedNever();

        // One map per firm. Two would be two answers to "where does output VAT post",
        // and a posting would take whichever the query happened to read first.
        builder
            .HasIndex(map => map.FirmId)
            .IsUnique()
            .HasDatabaseName("ix_tax_account_maps_firm");

        builder.HasIndex(map => map.TenantId)
            .HasDatabaseName("ix_tax_account_maps_tenant");

        builder.HasMany(map => map.Accounts)
            .WithOne()
            .HasForeignKey(entry => entry.TaxAccountMapId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(map => map.Accounts)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_accounts");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>
/// Maps <see cref="TaxAccountAssignment"/> to the <c>tax_account_assignments</c> table.
/// </summary>
public sealed class TaxAccountAssignmentConfiguration
    : IEntityTypeConfiguration<TaxAccountAssignment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TaxAccountAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tax_account_assignments");

        // The triple is the key, and the direction is part of it deliberately: one
        // account per head per direction, so the database itself refuses to hold both
        // halves of a return in one place.
        builder.HasKey(entry =>
            new { entry.TaxAccountMapId, entry.Component, entry.Direction });

        builder.Property(entry => entry.Component).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.Direction).HasConversion<int>().IsRequired();

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(entry => entry.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => entry.LedgerId)
            .HasDatabaseName("ix_tax_account_assignments_ledger");

        builder.HasIndex(entry => entry.TenantId)
            .HasDatabaseName("ix_tax_account_assignments_tenant");
    }
}
