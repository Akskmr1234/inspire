using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Inventory;

/// <summary>
/// How much of one batch is in one warehouse, and what that warehouse carries it at.
/// </summary>
/// <remarks>
/// <para>
/// The batch equivalent of <see cref="StockBalance"/>, and split from it for the same
/// reason: a batch is a fact about goods, a quantity is a fact about a shelf, and the
/// same batch delivered into two godowns is two positions.
/// </para>
/// <para>
/// The two are kept in step by arithmetic rather than by hope. Every batch movement
/// is applied to this position and to the product's position in the same warehouse,
/// at the same cost - so the product position's quantity is the sum of its batches'
/// quantities, and its value is the sum of their values, at every instant. That is
/// what makes the product-level valuation and the batch-wise valuation two views of
/// one number instead of two numbers that drift.
/// </para>
/// <para>
/// A cost is kept here as well as on the <see cref="Batch"/> because they answer
/// different questions. The batch's rate is what the goods were bought at, and the
/// margin on a sale is measured against it. This is what this warehouse currently
/// carries them at, which differs the moment the same batch is delivered twice at two
/// prices, or transferred in from a godown that valued it differently.
/// </para>
/// </remarks>
public sealed class BatchBalance : AggregateRoot<BatchBalanceId>, IFirmScoped, IAuditable
{
    private BatchBalance(
        BatchBalanceId id,
        TenantId tenantId,
        FirmId firmId,
        ProductId productId,
        BatchId batchId,
        WarehouseId warehouseId,
        CurrencyCode currency)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        ProductId = productId;
        BatchId = batchId;
        WarehouseId = warehouseId;
        Currency = currency;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private BatchBalance()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the product the batch is of.</summary>
    /// <remarks>
    /// Carried here as well as on the batch, so a report of what one product holds in
    /// batches does not have to join to find out which batches are its own.
    /// </remarks>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the batch.</summary>
    public BatchId BatchId { get; private set; }

    /// <summary>Gets the warehouse this quantity is in.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Gets the quantity on hand, in the product's stock unit.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets what one stock unit of this batch is carried at here.</summary>
    /// <remarks>
    /// A weighted average, like the product's, and almost always a single figure: a
    /// batch is normally one delivery at one price. It averages only where the same
    /// batch arrives twice at different prices, and averaging within a batch is still
    /// a far finer answer than averaging across the product, which is the whole point
    /// of section 10's "profit always uses actual batch cost".
    /// </remarks>
    public decimal UnitCost { get; private set; }

    /// <summary>Gets the currency the cost is stated in: the firm's own.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Gets the instant of the movement that last changed this position.</summary>
    public DateTimeOffset? LastMovementAtUtc { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets what the quantity on hand is worth.</summary>
    public Money Value => Money.Of(Quantity * UnitCost, Currency);

    /// <summary>Opens an empty position for a batch in a warehouse.</summary>
    /// <param name="batch">The batch.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="currency">The firm's base currency.</param>
    /// <returns>The empty position.</returns>
    public static BatchBalance Open(Batch batch, WarehouseId warehouseId, CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(batch);

        return new BatchBalance(
            BatchBalanceId.NewId(),
            batch.TenantId,
            batch.FirmId,
            batch.ProductId,
            batch.Id,
            warehouseId,
            currency);
    }

    /// <summary>Takes goods of this batch in.</summary>
    /// <param name="quantity">How much came in. Must be positive.</param>
    /// <param name="unitCost">What one stock unit of it cost. May be zero.</param>
    /// <param name="occurredAtUtc">When the movement was posted.</param>
    /// <returns>The value taken in, or the reason it was refused.</returns>
    public Result<Money> Receive(decimal quantity, decimal unitCost, DateTimeOffset occurredAtUtc)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "BatchBalance.QuantityNotPositive",
                "A receipt must be for a positive quantity."));
        }

        if (unitCost < 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "BatchBalance.CostNegative", "A receipt cannot be at a negative cost."));
        }

        decimal incoming = decimal.Round(
            quantity, StockBalance.QuantityScale, MidpointRounding.AwayFromZero);
        decimal held = Quantity + incoming;

        UnitCost = held <= 0m
            ? unitCost
            : decimal.Round(
                ((Quantity * UnitCost) + (incoming * unitCost)) / held,
                StockBalance.CostScale,
                MidpointRounding.AwayFromZero);

        Quantity = held;
        LastMovementAtUtc = occurredAtUtc;

        return Result.Success(Money.Of(incoming * unitCost, Currency));
    }

    /// <summary>Takes goods of this batch out, at what this batch costs.</summary>
    /// <param name="quantity">How much went out. Must be positive.</param>
    /// <param name="occurredAtUtc">When the movement was posted.</param>
    /// <returns>The value taken out, or the reason it was refused.</returns>
    /// <remarks>
    /// Refused where the batch does not hold enough, even if the product does. Stock
    /// of a batch is not interchangeable with stock of another batch - that is what
    /// tracking batches means - and quietly drawing the shortfall from elsewhere would
    /// send out goods with an expiry date nobody asked for.
    /// <para>
    /// An expired batch is <em>not</em> refused. Expired goods leave stock the same
    /// way any other goods do, through an issue or a write-off, and a position that
    /// refused to let them out would be a position they could never leave. Whether
    /// expired stock may be <em>sold</em> is a question for the sales document, which
    /// knows it is a sale; this only knows goods moved.
    /// </para>
    /// </remarks>
    public Result<Money> Issue(decimal quantity, DateTimeOffset occurredAtUtc)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "BatchBalance.QuantityNotPositive",
                "An issue must be for a positive quantity."));
        }

        decimal outgoing = decimal.Round(
            quantity, StockBalance.QuantityScale, MidpointRounding.AwayFromZero);

        if (outgoing > Quantity)
        {
            return Result.Failure<Money>(Error.BusinessRule(
                "BatchBalance.Insufficient",
                $"Only {Quantity} of this batch is on hand, so {outgoing} cannot be issued."));
        }

        Quantity -= outgoing;
        LastMovementAtUtc = occurredAtUtc;

        return Result.Success(Money.Of(outgoing * UnitCost, Currency));
    }

    /// <summary>Takes back out goods that came in, at the cost they came in at.</summary>
    /// <param name="quantity">How much to take back. Must be positive.</param>
    /// <param name="unitCost">What that receipt recorded as the cost of one unit.</param>
    /// <param name="occurredAtUtc">When the reversal was posted.</param>
    /// <returns>The value taken back out, or the reason it was refused.</returns>
    public Result<Money> ReverseReceipt(
        decimal quantity,
        decimal unitCost,
        DateTimeOffset occurredAtUtc)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "BatchBalance.QuantityNotPositive",
                "A reversal must be for a positive quantity."));
        }

        decimal removed = decimal.Round(
            quantity, StockBalance.QuantityScale, MidpointRounding.AwayFromZero);

        if (removed > Quantity)
        {
            return Result.Failure<Money>(Error.BusinessRule(
                "BatchBalance.ReceiptConsumed",
                $"Only {Quantity} of the {removed} received into this batch is still on "
                + "hand, so the receipt can no longer be reversed. Post an adjustment "
                + "instead."));
        }

        decimal remaining = Quantity - removed;
        decimal remainingValue = (Quantity * UnitCost) - (removed * unitCost);

        if (remainingValue < 0m)
        {
            return Result.Failure<Money>(Error.BusinessRule(
                "BatchBalance.ReversalBelowZero",
                "Reversing that receipt would leave the batch carrying a negative "
                + "value. Post an adjustment instead."));
        }

        UnitCost = remaining <= 0m
            ? UnitCost
            : decimal.Round(
                remainingValue / remaining, StockBalance.CostScale, MidpointRounding.AwayFromZero);

        Quantity = remaining;
        LastMovementAtUtc = occurredAtUtc;

        return Result.Success(Money.Of(removed * unitCost, Currency));
    }
}
