using ERP.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="MenuItem"/> to the <c>menu_items</c> table.</summary>
public sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("menu_items");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Code)
            .HasMaxLength(MenuItem.MaximumCodeLength).IsRequired();
        builder.Property(m => m.Label)
            .HasMaxLength(MenuItem.MaximumLabelLength).IsRequired();
        builder.Property(m => m.LabelArabic).HasMaxLength(MenuItem.MaximumLabelLength);
        builder.Property(m => m.Icon).HasMaxLength(MenuItem.MaximumIconLength);
        builder.Property(m => m.Route).HasMaxLength(MenuItem.MaximumRouteLength);
        builder.Property(m => m.Module)
            .HasMaxLength(MenuItem.MaximumModuleLength).IsRequired();

        // A permission code is module:resource:verb, and the catalogue's own columns
        // bound each part; 150 is comfortably beyond anything the catalogue can
        // produce.
        builder.Property(m => m.RequiredPermission).HasMaxLength(150);

        builder.Property(m => m.SortOrder).IsRequired();
        builder.Property(m => m.IsEnabled).IsRequired();
        builder.Property(m => m.IsSystem).IsRequired();

        // Projected from the route, not stored. A column saying whether an entry is a
        // link could disagree with whether it has one to follow.
        builder.Ignore(m => m.IsLink);

        // Self-referencing, and deliberately restricted rather than cascading:
        // deleting a heading must not silently take a subtree of screens with it. The
        // caller re-parents the children first, which is also what an administrator
        // reordering a menu expects.
        builder.HasOne<MenuItem>()
            .WithMany()
            .HasForeignKey(m => m.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // The whole menu is read on every page load, in tree order. This index covers
        // that read exactly: firm, then parent to gather a level, then the order the
        // level is displayed in.
        builder
            .HasIndex(m => new { m.FirmId, m.ParentId, m.SortOrder })
            .HasDatabaseName("ix_menu_items_tree");

        // A code identifies an entry within a firm, and a deployment that referred to
        // one would find two. Unique rather than merely indexed for that reason.
        builder
            .HasIndex(m => new { m.FirmId, m.Code })
            .IsUnique()
            .HasDatabaseName("ix_menu_items_code");

        builder.HasIndex(m => m.TenantId).HasDatabaseName("ix_menu_items_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
