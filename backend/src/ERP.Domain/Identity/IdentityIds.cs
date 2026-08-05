using ERP.SharedKernel.Primitives;

namespace ERP.Domain.Identity;

/// <summary>Identifies a role.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct RoleId(Guid Value) : IStronglyTypedId<RoleId>
{
    /// <inheritdoc />
    public static RoleId From(Guid value) => new(value);

    /// <inheritdoc />
    public static RoleId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a permission in the catalogue.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct PermissionId(Guid Value) : IStronglyTypedId<PermissionId>
{
    /// <inheritdoc />
    public static PermissionId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PermissionId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies an issued refresh token.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct RefreshTokenId(Guid Value) : IStronglyTypedId<RefreshTokenId>
{
    /// <inheritdoc />
    public static RefreshTokenId From(Guid value) => new(value);

    /// <inheritdoc />
    public static RefreshTokenId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
