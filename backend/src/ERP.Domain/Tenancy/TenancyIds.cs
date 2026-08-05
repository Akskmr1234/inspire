using ERP.SharedKernel.Primitives;

namespace ERP.Domain.Tenancy;

/// <summary>Identifies a financial year.</summary>
/// <param name="Value">The underlying value.</param>
/// <remarks>
/// <see cref="SharedKernel.Tenancy.TenantId"/>,
/// <see cref="SharedKernel.Tenancy.FirmId"/>,
/// <see cref="SharedKernel.Tenancy.BranchId"/> and
/// <see cref="SharedKernel.Tenancy.UserId"/> deliberately live in the Shared
/// Kernel instead of here, because the kernel's own tenancy and audit contracts
/// are expressed in terms of them.
/// </remarks>
public readonly record struct FinancialYearId(Guid Value) : IStronglyTypedId<FinancialYearId>
{
    /// <inheritdoc />
    public static FinancialYearId From(Guid value) => new(value);

    /// <inheritdoc />
    public static FinancialYearId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
