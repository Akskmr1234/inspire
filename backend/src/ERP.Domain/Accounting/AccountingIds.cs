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
