using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Inventory;

/// <summary>
/// What is on hand of one product in one warehouse, and what it cost.
/// </summary>
/// <remarks>
/// <para>
/// This is where average costing actually lives. Open question 6 was answered
/// <em>average costing, FIFO is not required</em>, and this aggregate is the
/// consequence: a quantity and a single cost per unit, recomputed every time goods
/// come in, and read unchanged every time goods go out.
/// </para>
/// <para>
/// Per warehouse rather than per firm. Stock is a fact about a place - the same
/// product bought at two prices into two godowns genuinely cost two different things,
/// and averaging across them would report a value no location can produce. The firm's
/// position is the sum of the locations, which is a report rather than a stored row.
/// </para>
/// <para>
/// The balance is a running figure rather than a sum over the ledger. That is the one
/// deliberate departure from how ledger balances are handled in accounting, where the
/// balance is always derived: a stock valuation has to answer "what does the next
/// issue cost" on every line of every sales invoice, and replaying a product's entire
/// movement history to answer it would make invoicing quadratic in the life of the
/// business. The ledger keeps the audit trail; this keeps the answer.
/// </para>
/// </remarks>
public sealed class StockBalance : AggregateRoot<StockBalanceId>, IFirmScoped, IAuditable
{
    /// <summary>
    /// The decimal places the average cost is kept to.
    /// </summary>
    /// <remarks>
    /// Six rather than the currency's two. An average is a quotient - a hundred units
    /// at 3.33 and one at 10 average 3.395941... - and rounding it to the currency at
    /// every receipt would push the error into the valuation, in the same direction,
    /// for as long as the product exists. It is rounded to the currency when it is
    /// presented or posted, which is once, rather than compounded.
    /// </remarks>
    public const int CostScale = 6;

    /// <summary>The decimal places quantities are kept to.</summary>
    /// <remarks>
    /// Matches <see cref="UnitOfMeasure.MaximumDecimalPlaces"/>, so the balance can
    /// hold anything any unit can express. The unit itself refuses a quantity too
    /// precise for it; this only has to avoid being the narrower of the two.
    /// </remarks>
    public const int QuantityScale = 6;

    private StockBalance(
        StockBalanceId id,
        TenantId tenantId,
        FirmId firmId,
        ProductId productId,
        WarehouseId warehouseId,
        CurrencyCode currency)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        ProductId = productId;
        WarehouseId = warehouseId;
        Currency = currency;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private StockBalance()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the product this position is of.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the warehouse this position is in.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Gets the quantity on hand, in the product's stock unit.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the weighted average cost of one stock unit.</summary>
    /// <remarks>
    /// Survives the quantity reaching zero. A product that sells out and is bought
    /// again is not a product whose history began this morning, and keeping the last
    /// average means a report run between the two shows what the goods cost rather
    /// than nothing.
    /// </remarks>
    public decimal AverageCost { get; private set; }

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

    /// <summary>Gets what the quantity on hand is worth at the average cost.</summary>
    public Money Value => Money.Of(Quantity * AverageCost, Currency);

    /// <summary>Opens an empty position for a product in a warehouse.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="productId">The product.</param>
    /// <param name="warehouseId">The warehouse.</param>
    /// <param name="currency">The firm's base currency.</param>
    /// <returns>The empty position.</returns>
    /// <remarks>
    /// Created on the first movement rather than for every product in every warehouse
    /// up front. A firm with ten thousand products and six godowns does not have sixty
    /// thousand stock positions; it has the few thousand combinations it actually
    /// trades in, and rows for the rest would be noise in every report that reads them.
    /// </remarks>
    public static StockBalance Open(
        TenantId tenantId,
        FirmId firmId,
        ProductId productId,
        WarehouseId warehouseId,
        CurrencyCode currency) =>
        new(StockBalanceId.NewId(), tenantId, firmId, productId, warehouseId, currency);

    /// <summary>Takes goods in, and moves the average towards what they cost.</summary>
    /// <param name="quantity">How much came in. Must be positive.</param>
    /// <param name="unitCost">What one stock unit of it cost. May be zero.</param>
    /// <param name="occurredAtUtc">When the movement was posted.</param>
    /// <returns>The value taken in, or the reason it was refused.</returns>
    /// <remarks>
    /// The weighted average in one line: the value already held plus the value coming
    /// in, over the total quantity. Nothing here depends on the order goods arrived
    /// in, which is the point of answering open question 6 the way it was answered -
    /// there is no queue to consume, so there is nothing for a later correction to
    /// consume out of order.
    /// </remarks>
    public Result<Money> Receive(
        decimal quantity,
        decimal unitCost,
        DateTimeOffset occurredAtUtc)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "StockBalance.QuantityNotPositive",
                "A receipt must be for a positive quantity."));
        }

        if (unitCost < 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "StockBalance.CostNegative",
                "A receipt cannot be at a negative cost."));
        }

        decimal incoming = decimal.Round(quantity, QuantityScale, MidpointRounding.AwayFromZero);
        decimal held = Quantity + incoming;

        // Guarded rather than assumed. A position driven negative by some future
        // setting that permits overselling would otherwise divide by a quantity of
        // zero, or produce an average with the sign inverted.
        AverageCost = held <= 0m
            ? unitCost
            : decimal.Round(
                ((Quantity * AverageCost) + (incoming * unitCost)) / held,
                CostScale,
                MidpointRounding.AwayFromZero);

        Quantity = held;
        LastMovementAtUtc = occurredAtUtc;

        return Result.Success(Money.Of(incoming * unitCost, Currency));
    }

    /// <summary>Takes goods out at the average cost, leaving the average alone.</summary>
    /// <param name="quantity">How much went out. Must be positive.</param>
    /// <param name="occurredAtUtc">When the movement was posted.</param>
    /// <returns>The value taken out, or the reason it was refused.</returns>
    /// <remarks>
    /// An issue for more than is on hand is refused rather than allowed to drive the
    /// position negative. Negative stock is a real operational request - a delivery
    /// entered before the supplier's invoice arrives - but it produces a valuation
    /// nobody can defend, because there is no cost for goods the system does not
    /// believe exist. Permitting it belongs behind a firm-level setting and a decision
    /// about what such an issue costs; until somebody makes that decision, refusing is
    /// the honest answer.
    /// </remarks>
    public Result<Money> Issue(decimal quantity, DateTimeOffset occurredAtUtc)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "StockBalance.QuantityNotPositive",
                "An issue must be for a positive quantity."));
        }

        decimal outgoing = decimal.Round(quantity, QuantityScale, MidpointRounding.AwayFromZero);

        if (outgoing > Quantity)
        {
            return Result.Failure<Money>(Error.BusinessRule(
                "StockBalance.Insufficient",
                $"Only {Quantity} is on hand, so {outgoing} cannot be issued."));
        }

        Quantity -= outgoing;
        LastMovementAtUtc = occurredAtUtc;

        // The average is untouched. Taking goods out at the average by definition
        // leaves the average of what remains exactly where it was.
        return Result.Success(Money.Of(outgoing * AverageCost, Currency));
    }

    /// <summary>Takes goods out at a cost something else has already decided.</summary>
    /// <param name="quantity">How much went out. Must be positive.</param>
    /// <param name="unitCost">What those particular goods cost.</param>
    /// <param name="occurredAtUtc">When the movement was posted.</param>
    /// <returns>The value taken out, or the reason it was refused.</returns>
    /// <remarks>
    /// <para>
    /// What a batch-tracked issue uses. The average on this position is the average of
    /// the product across every batch in the warehouse; the goods actually picked came
    /// out of one batch, at that batch's cost. Removing them at the average would
    /// leave this position holding a value that no longer equals the sum of its
    /// batches, and the product valuation and the batch-wise valuation would report
    /// two different numbers for the same shelf.
    /// </para>
    /// <para>
    /// Removing a batch cheaper than the average pushes the average of what is left
    /// up, which is correct: what remains really is the more expensive stock.
    /// </para>
    /// </remarks>
    public Result<Money> IssueAt(
        decimal quantity,
        decimal unitCost,
        DateTimeOffset occurredAtUtc)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "StockBalance.QuantityNotPositive",
                "An issue must be for a positive quantity."));
        }

        if (unitCost < 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "StockBalance.CostNegative",
                "Goods cannot be issued at a negative cost."));
        }

        return RemoveAt(
            quantity,
            unitCost,
            occurredAtUtc,
            outgoing => Error.BusinessRule(
                "StockBalance.Insufficient",
                $"Only {Quantity} is on hand, so {outgoing} cannot be issued."),
            Error.BusinessRule(
                "StockBalance.IssueBelowZero",
                "Issuing those goods at what they cost would leave the remaining stock "
                + "carrying a negative value. Post an adjustment instead."));
    }

    /// <summary>Takes back out goods that came in, at the cost they came in at.</summary>
    /// <param name="quantity">How much to take back. Must be positive.</param>
    /// <param name="unitCost">What that receipt recorded as the cost of one unit.</param>
    /// <param name="occurredAtUtc">When the reversal was posted.</param>
    /// <returns>The value taken back out, or the reason it was refused.</returns>
    /// <remarks>
    /// <para>
    /// Cancelling a receipt is not the same as issuing what it brought in, and using
    /// <see cref="Issue"/> for it would be wrong in a way nothing would catch. Ten
    /// units received at 25 into a position that has since risen to an average of 30
    /// put 250 of value in; issuing ten takes 300 back out, and the difference lands
    /// in the average of whatever is left, silently. Reversing at the original cost
    /// removes exactly what was added.
    /// </para>
    /// <para>
    /// Refused if the goods are no longer there, or if taking their value back out
    /// would leave the remaining stock worth less than nothing. Both mean the same
    /// thing in practice: the receipt has been traded on, and un-receiving it is no
    /// longer an operation the books can express. An adjustment is the honest way to
    /// record that, because it says what actually happened.
    /// </para>
    /// </remarks>
    public Result<Money> ReverseReceipt(
        decimal quantity,
        decimal unitCost,
        DateTimeOffset occurredAtUtc)
    {
        if (quantity <= 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "StockBalance.QuantityNotPositive",
                "A reversal must be for a positive quantity."));
        }

        return RemoveAt(
            quantity,
            unitCost,
            occurredAtUtc,
            removed => Error.BusinessRule(
                "StockBalance.ReceiptConsumed",
                $"Only {Quantity} of the {removed} received is still on hand, so the "
                + "receipt can no longer be reversed. Post an adjustment instead."),
            Error.BusinessRule(
                "StockBalance.ReversalBelowZero",
                "Reversing that receipt would leave the remaining stock carrying a "
                + "negative value. Post an adjustment instead."));
    }

    /// <summary>Restates the cost of what is on hand, without moving any goods.</summary>
    /// <param name="unitCost">The cost one stock unit is to be carried at.</param>
    /// <param name="occurredAtUtc">When the revaluation was posted.</param>
    /// <returns>The change in value, or the reason it was refused.</returns>
    /// <remarks>
    /// Kept separate from a receipt because it is a different event. A receipt says
    /// more goods arrived and pulls the average towards their price; a revaluation
    /// says the goods already here are worth something else, which is a write-down or
    /// a correction and shows up in the accounts as one.
    /// </remarks>
    public Result<Money> Revalue(decimal unitCost, DateTimeOffset occurredAtUtc)
    {
        if (unitCost < 0m)
        {
            return Result.Failure<Money>(Error.Validation(
                "StockBalance.CostNegative",
                "Stock cannot be carried at a negative cost."));
        }

        Money before = Value;

        AverageCost = decimal.Round(unitCost, CostScale, MidpointRounding.AwayFromZero);
        LastMovementAtUtc = occurredAtUtc;

        return Result.Success(Value - before);
    }

    /// <summary>Removes a quantity and exactly the value it carried with it.</summary>
    /// <param name="quantity">How much to remove.</param>
    /// <param name="unitCost">What those goods were worth, one unit at a time.</param>
    /// <param name="occurredAtUtc">When the movement was posted.</param>
    /// <param name="insufficient">The error when there is not that much here.</param>
    /// <param name="belowZero">The error when the value removed exceeds the value held.</param>
    /// <returns>The value removed, or the reason it was refused.</returns>
    /// <remarks>
    /// Shared by the two operations that take goods out at a cost decided elsewhere -
    /// undoing a receipt, and issuing from a batch. They differ in what a refusal
    /// means, which is why the errors are passed in, but the arithmetic is the same
    /// and two copies of it would eventually be two answers.
    /// </remarks>
    private Result<Money> RemoveAt(
        decimal quantity,
        decimal unitCost,
        DateTimeOffset occurredAtUtc,
        Func<decimal, Error> insufficient,
        Error belowZero)
    {
        decimal removed = decimal.Round(quantity, QuantityScale, MidpointRounding.AwayFromZero);

        if (removed > Quantity)
        {
            return Result.Failure<Money>(insufficient(removed));
        }

        decimal remaining = Quantity - removed;
        decimal remainingValue = (Quantity * AverageCost) - (removed * unitCost);

        if (remainingValue < 0m)
        {
            return Result.Failure<Money>(belowZero);
        }

        // The average of what is left, not the average as it stood. Removing goods
        // that were cheaper than the average pushes the average of the remainder up,
        // which is exactly what it should do.
        AverageCost = remaining <= 0m
            ? AverageCost
            : decimal.Round(remainingValue / remaining, CostScale, MidpointRounding.AwayFromZero);

        Quantity = remaining;
        LastMovementAtUtc = occurredAtUtc;

        return Result.Success(Money.Of(removed * unitCost, Currency));
    }
}
