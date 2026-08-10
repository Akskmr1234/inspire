using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="InventoryAccountMap"/> to the <c>inventory_account_maps</c> table.
/// </summary>
public sealed class InventoryAccountMapConfiguration
    : IEntityTypeConfiguration<InventoryAccountMap>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InventoryAccountMap> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("inventory_account_maps");
        builder.HasKey(map => map.Id);
        builder.Property(map => map.Id).ValueGeneratedNever();

        builder.Ignore(map => map.IsComplete);

        // One map per firm. Two would be two answers to "where does stock post", and a
        // posting would take whichever the query happened to read.
        builder
            .HasIndex(map => map.FirmId)
            .IsUnique()
            .HasDatabaseName("ix_inventory_account_maps_firm");

        builder.HasIndex(map => map.TenantId)
            .HasDatabaseName("ix_inventory_account_maps_tenant");

        builder.HasMany(map => map.Accounts)
            .WithOne()
            .HasForeignKey(entry => entry.InventoryAccountMapId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(map => map.Accounts)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_accounts");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>
/// Maps <see cref="InventoryAccountAssignment"/> to the
/// <c>inventory_account_assignments</c> table.
/// </summary>
public sealed class InventoryAccountAssignmentConfiguration
    : IEntityTypeConfiguration<InventoryAccountAssignment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InventoryAccountAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("inventory_account_assignments");

        // The pair is the key: one account per kind of posting per firm, enforced by
        // the database rather than by the code that assigns them.
        builder.HasKey(entry => new { entry.InventoryAccountMapId, entry.Account });

        builder.Property(entry => entry.Account).HasConversion<int>().IsRequired();

        builder.HasOne<Ledger>()
            .WithMany()
            .HasForeignKey(entry => entry.LedgerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => entry.LedgerId)
            .HasDatabaseName("ix_inventory_account_assignments_ledger");

        builder.HasIndex(entry => entry.TenantId)
            .HasDatabaseName("ix_inventory_account_assignments_tenant");
    }
}
