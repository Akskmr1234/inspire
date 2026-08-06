using ERP.SharedKernel.Primitives;

namespace ERP.Domain.Inventory;

/// <summary>Identifies a unit of measurement.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct UnitOfMeasureId(Guid Value)
    : IStronglyTypedId<UnitOfMeasureId>
{
    /// <inheritdoc />
    public static UnitOfMeasureId From(Guid value) => new(value);

    /// <inheritdoc />
    public static UnitOfMeasureId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a product category.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct CategoryId(Guid Value) : IStronglyTypedId<CategoryId>
{
    /// <inheritdoc />
    public static CategoryId From(Guid value) => new(value);

    /// <inheritdoc />
    public static CategoryId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a brand.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct BrandId(Guid Value) : IStronglyTypedId<BrandId>
{
    /// <inheritdoc />
    public static BrandId From(Guid value) => new(value);

    /// <inheritdoc />
    public static BrandId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a warehouse, called a godown in the reference application.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct WarehouseId(Guid Value) : IStronglyTypedId<WarehouseId>
{
    /// <inheritdoc />
    public static WarehouseId From(Guid value) => new(value);

    /// <inheritdoc />
    public static WarehouseId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
