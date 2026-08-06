using ERP.SharedKernel.Primitives;

namespace ERP.Domain.Accounting;

/// <summary>Identifies an account group in the chart of accounts.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct AccountGroupId(Guid Value) : IStronglyTypedId<AccountGroupId>
{
    /// <inheritdoc />
    public static AccountGroupId From(Guid value) => new(value);

    /// <inheritdoc />
    public static AccountGroupId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a ledger.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct LedgerId(Guid Value) : IStronglyTypedId<LedgerId>
{
    /// <inheritdoc />
    public static LedgerId From(Guid value) => new(value);

    /// <inheritdoc />
    public static LedgerId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a voucher.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct VoucherId(Guid Value) : IStronglyTypedId<VoucherId>
{
    /// <inheritdoc />
    public static VoucherId From(Guid value) => new(value);

    /// <inheritdoc />
    public static VoucherId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a single line of a voucher.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct VoucherLineId(Guid Value) : IStronglyTypedId<VoucherLineId>
{
    /// <inheritdoc />
    public static VoucherLineId From(Guid value) => new(value);

    /// <inheritdoc />
    public static VoucherLineId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a bill: one outstanding receivable or payable.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct BillId(Guid Value) : IStronglyTypedId<BillId>
{
    /// <inheritdoc />
    public static BillId From(Guid value) => new(value);

    /// <inheritdoc />
    public static BillId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a cheque.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct ChequeId(Guid Value) : IStronglyTypedId<ChequeId>
{
    /// <inheritdoc />
    public static ChequeId From(Guid value) => new(value);

    /// <inheritdoc />
    public static ChequeId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one settlement against a bill.</summary>
/// <param name="Value">The underlying value.</param>
/// <remarks>
/// An allocation needs an identity of its own because one voucher may settle the
/// same bill on more than one line, so neither the bill nor the voucher, nor the
/// two together, distinguishes it. Assigned in the constructor like every other
/// identifier here rather than left to the database, so an allocation is fully
/// formed the moment the domain creates it.
/// </remarks>
public readonly record struct BillAllocationId(Guid Value) : IStronglyTypedId<BillAllocationId>
{
    /// <inheritdoc />
    public static BillAllocationId From(Guid value) => new(value);

    /// <inheritdoc />
    public static BillAllocationId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
