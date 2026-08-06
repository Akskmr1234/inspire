using ERP.SharedKernel.Primitives;

namespace ERP.Domain.Platform;

/// <summary>Identifies an entry in the navigation menu.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct MenuItemId(Guid Value) : IStronglyTypedId<MenuItemId>
{
    /// <inheritdoc />
    public static MenuItemId From(Guid value) => new(value);

    /// <inheritdoc />
    public static MenuItemId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
