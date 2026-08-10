using ERP.SharedKernel.Primitives;

namespace ERP.Domain.Inventory;

/// <summary>Identifies a stock document.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct StockDocumentId(Guid Value) : IStronglyTypedId<StockDocumentId>
{
    /// <inheritdoc />
    public static StockDocumentId From(Guid value) => new(value);

    /// <inheritdoc />
    public static StockDocumentId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one line of a stock document.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct StockDocumentLineId(Guid Value)
    : IStronglyTypedId<StockDocumentLineId>
{
    /// <inheritdoc />
    public static StockDocumentLineId From(Guid value) => new(value);

    /// <inheritdoc />
    public static StockDocumentLineId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies the stock position of one product in one warehouse.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct StockBalanceId(Guid Value) : IStronglyTypedId<StockBalanceId>
{
    /// <inheritdoc />
    public static StockBalanceId From(Guid value) => new(value);

    /// <inheritdoc />
    public static StockBalanceId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one batch of one product.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct BatchId(Guid Value) : IStronglyTypedId<BatchId>
{
    /// <inheritdoc />
    public static BatchId From(Guid value) => new(value);

    /// <inheritdoc />
    public static BatchId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies the position of one batch in one warehouse.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct BatchBalanceId(Guid Value) : IStronglyTypedId<BatchBalanceId>
{
    /// <inheritdoc />
    public static BatchBalanceId From(Guid value) => new(value);

    /// <inheritdoc />
    public static BatchBalanceId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one serialised unit.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct SerialNumberId(Guid Value) : IStronglyTypedId<SerialNumberId>
{
    /// <inheritdoc />
    public static SerialNumberId From(Guid value) => new(value);

    /// <inheritdoc />
    public static SerialNumberId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one movement in the stock ledger.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct StockLedgerEntryId(Guid Value)
    : IStronglyTypedId<StockLedgerEntryId>
{
    /// <inheritdoc />
    public static StockLedgerEntryId From(Guid value) => new(value);

    /// <inheritdoc />
    public static StockLedgerEntryId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
