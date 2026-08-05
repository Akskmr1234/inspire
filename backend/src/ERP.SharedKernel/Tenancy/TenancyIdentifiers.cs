using ERP.SharedKernel.Primitives;

namespace ERP.SharedKernel.Tenancy;

/// <summary>Identifies a tenant - the subscription and isolation boundary.</summary>
/// <param name="Value">The underlying value.</param>
/// <remarks>
/// These four identifiers live in the Shared Kernel rather than in the Domain
/// because the kernel's own tenancy and audit contracts are expressed in terms of
/// them. Keeping them here lets <see cref="Abstractions.ITenantScoped"/> and
/// <see cref="Abstractions.IAuditable"/> use the strong types directly instead of
/// falling back to bare <see cref="Guid"/> values, which would defeat the point
/// of having them.
/// </remarks>
public readonly record struct TenantId(Guid Value) : IStronglyTypedId<TenantId>
{
    /// <inheritdoc />
    public static TenantId From(Guid value) => new(value);

    /// <inheritdoc />
    public static TenantId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identifies a firm - an independent set of books within a tenant, with its own
/// chart of accounts, financial data, numbering, and users.
/// </summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct FirmId(Guid Value) : IStronglyTypedId<FirmId>
{
    /// <inheritdoc />
    public static FirmId From(Guid value) => new(value);

    /// <inheritdoc />
    public static FirmId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Identifies a branch, surfaced to users as "Stock Location" or
/// "Store Location".
/// </summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct BranchId(Guid Value) : IStronglyTypedId<BranchId>
{
    /// <inheritdoc />
    public static BranchId From(Guid value) => new(value);

    /// <inheritdoc />
    public static BranchId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a user.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct UserId(Guid Value) : IStronglyTypedId<UserId>
{
    /// <summary>
    /// The actor recorded when a change originates from the platform itself
    /// rather than a person - a migration, a seed, or a scheduled job.
    /// </summary>
    /// <remarks>
    /// A fixed, recognisable value rather than an empty <see cref="Guid"/>, so an
    /// audit row makes it obvious that no human was responsible instead of
    /// looking like a bug that failed to record the user.
    /// </remarks>
    public static readonly UserId System = new(new Guid("00000000-0000-0000-0000-00000000513E"));

    /// <inheritdoc />
    public static UserId From(Guid value) => new(value);

    /// <inheritdoc />
    public static UserId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
