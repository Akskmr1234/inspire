using ERP.Domain.Inventory;
using ERP.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="UnitOfMeasure"/> to the <c>units_of_measure</c> table.</summary>
public sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("units_of_measure");
        builder.HasKey(unit => unit.Id);
        builder.Property(unit => unit.Id).ValueGeneratedNever();

        builder.Property(unit => unit.Code)
            .HasMaxLength(UnitOfMeasure.MaximumCodeLength).IsRequired();
        builder.Property(unit => unit.Name)
            .HasMaxLength(UnitOfMeasure.MaximumNameLength).IsRequired();
        builder.Property(unit => unit.Symbol).HasMaxLength(UnitOfMeasure.MaximumSymbolLength);
        builder.Property(unit => unit.DecimalPlaces).IsRequired();
        builder.Property(unit => unit.IsActive).IsRequired();

        // More precision than money. A factor may legitimately be a recurring third of
        // its base, and the error in a rounded factor multiplies with every quantity
        // converted through it.
        builder.Property(unit => unit.ConversionFactor).HasPrecision(18, 8).IsRequired();

        // Derived from the base unit rather than stored beside it, which could
        // disagree with it.
        builder.Ignore(unit => unit.IsBaseUnit);
        builder.Ignore(unit => unit.GroupId);

        builder.HasOne<UnitOfMeasure>()
            .WithMany()
            .HasForeignKey(unit => unit.BaseUnitId)
            // Restricted: deleting a base unit would strand every unit converting to
            // it, and the quantities recorded in those would become unreadable.
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(unit => new { unit.FirmId, unit.Code })
            .IsUnique()
            .HasDatabaseName("ix_units_of_measure_code");

        // How a product's permitted units are found: everything sharing its base.
        builder
            .HasIndex(unit => new { unit.FirmId, unit.BaseUnitId })
            .HasDatabaseName("ix_units_of_measure_group");

        builder.HasIndex(unit => unit.TenantId).HasDatabaseName("ix_units_of_measure_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="Category"/> to the <c>categories</c> table.</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("categories");
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();

        builder.Property(category => category.Code)
            .HasMaxLength(Category.MaximumCodeLength).IsRequired();
        builder.Property(category => category.Name)
            .HasMaxLength(Category.MaximumNameLength).IsRequired();
        builder.Property(category => category.NameArabic)
            .HasMaxLength(Category.MaximumNameLength);
        builder.Property(category => category.IsActive).IsRequired();

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(category => category.ParentId)
            // Restricted rather than cascading: deleting a category must not silently
            // take its sub-classes, and the products filed under them, with it.
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(category => new { category.FirmId, category.Code })
            .IsUnique()
            .HasDatabaseName("ix_categories_code");

        builder
            .HasIndex(category => new { category.FirmId, category.ParentId })
            .HasDatabaseName("ix_categories_tree");

        builder.HasIndex(category => category.TenantId).HasDatabaseName("ix_categories_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="Brand"/> to the <c>brands</c> table.</summary>
public sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("brands");
        builder.HasKey(brand => brand.Id);
        builder.Property(brand => brand.Id).ValueGeneratedNever();

        builder.Property(brand => brand.Code)
            .HasMaxLength(Brand.MaximumCodeLength).IsRequired();
        builder.Property(brand => brand.Name)
            .HasMaxLength(Brand.MaximumNameLength).IsRequired();
        builder.Property(brand => brand.NameArabic).HasMaxLength(Brand.MaximumNameLength);
        builder.Property(brand => brand.IsActive).IsRequired();

        builder
            .HasIndex(brand => new { brand.FirmId, brand.Code })
            .IsUnique()
            .HasDatabaseName("ix_brands_code");

        builder.HasIndex(brand => brand.TenantId).HasDatabaseName("ix_brands_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}

/// <summary>Maps <see cref="Warehouse"/> to the <c>warehouses</c> table.</summary>
public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("warehouses");
        builder.HasKey(warehouse => warehouse.Id);
        builder.Property(warehouse => warehouse.Id).ValueGeneratedNever();

        builder.Property(warehouse => warehouse.Code)
            .HasMaxLength(Warehouse.MaximumCodeLength).IsRequired();
        builder.Property(warehouse => warehouse.Name)
            .HasMaxLength(Warehouse.MaximumNameLength).IsRequired();
        builder.Property(warehouse => warehouse.NameArabic)
            .HasMaxLength(Warehouse.MaximumNameLength);
        builder.Property(warehouse => warehouse.Address)
            .HasMaxLength(Warehouse.MaximumAddressLength);
        builder.Property(warehouse => warehouse.IsDefault).IsRequired();
        builder.Property(warehouse => warehouse.IsActive).IsRequired();

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(warehouse => warehouse.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasIndex(warehouse => new { warehouse.FirmId, warehouse.Code })
            .IsUnique()
            .HasDatabaseName("ix_warehouses_code");

        // One default per firm, enforced where it can actually be enforced. The
        // aggregate can only see itself, so two concurrent requests each promoting a
        // different warehouse would both believe they had succeeded; a filtered unique
        // index makes the second one fail.
        builder
            .HasIndex(warehouse => warehouse.FirmId)
            .IsUnique()
            .HasFilter("is_default = true")
            .HasDatabaseName("ix_warehouses_default");

        builder.HasIndex(warehouse => warehouse.TenantId).HasDatabaseName("ix_warehouses_tenant");

        ConfigurationConventions.ApplyAggregateConventions(builder);
    }
}
