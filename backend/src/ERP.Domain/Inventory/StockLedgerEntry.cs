using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Inventory;

/// <summary>
/// One movement of one product in one warehouse: the stock ledger, a row at a time.
/// </summary>
/// <remarks>
/// <para>
/// Written once and never changed. A correction is another entry, and a cancelled
/// document leaves its original movements in place alongside the contra entries that
/// undo them. A stock ledger that could lose or edit a row is one nobody can
/// reconcile against a physical count, which is the only thing a stock ledger is
/// really for.
/// </para>
/// <para>
/// Each row carries the position <em>after</em> it: the quantity on hand and the
/// average cost as they stood once this movement had been applied. That is
/// redundant - the position could be recomputed by replaying every earlier row - and
/// it is stored anyway, because the recomputation is exactly what makes a stock
/// ledger report slow, and because a row that records what the system believed at the
/// time is worth far more in an investigation than one that can only be reconstructed
/// from what it believes now.
/// </para>
/// <para>
/// Movements apply in the order they are posted, not the order they are dated. A
/// document dated last week and entered today lands after everything entered before
/// it, and the running average reflects that. The alternative - recomputing every
/// average from the back-dated point forward - would silently restate the cost of
/// goods already sold, and a valuation that changes after the fact is worse than one
/// that is merely late.
/// </para>
/// </remarks>
public sealed class StockLedgerEntry : AggregateRoot<StockLedgerEntryId>, IFirmScoped
{
    private StockLedgerEntry(
        StockLedgerEntryId id,
        TenantId tenantId,
        FirmId firmId,
        ProductId productId,
        WarehouseId warehouseId,
        DateOnly date,
        StockDocumentId documentId,
        StockDocumentType documentType,
        string documentNumber,
        decimal quantity,
        decimal unitCost,
        Money value,
        decimal balanceQuantity,
        decimal balanceAverageCost,
        DateTimeOffset postedAtUtc,
        string? narration)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        ProductId = productId;
        WarehouseId = warehouseId;
        Date = date;
        DocumentId = documentId;
        DocumentType = documentType;
        DocumentNumber = documentNumber;
        Quantity = quantity;
        UnitCost = unitCost;
        Value = value;
        BalanceQuantity = balanceQuantity;
        BalanceAverageCost = balanceAverageCost;
        PostedAtUtc = postedAtUtc;
        Narration = narration;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private StockLedgerEntry() => DocumentNumber = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the product that moved.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the warehouse it moved in or out of.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Gets the document date, which is the date the report reads.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the document that caused the movement.</summary>
    public StockDocumentId DocumentId { get; private set; }

    /// <summary>Gets the kind of document, so the ledger reads without a join.</summary>
    public StockDocumentType DocumentType { get; private set; }

    /// <summary>Gets the document number, copied so the ledger reads without a join.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>
    /// Gets the quantity moved, in the product's stock unit. Positive in, negative out.
    /// </summary>
    /// <remarks>
    /// Signed rather than split into an in column and an out column. One number cannot
    /// disagree with itself about direction, and a report wanting two columns splits
    /// it on the sign in one line of SQL.
    /// </remarks>
    public decimal Quantity { get; private set; }

    /// <summary>Gets what one stock unit was valued at for this movement.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>Gets the value of the movement. Signed, like the quantity.</summary>
    public Money Value { get; private set; }

    /// <summary>Gets the quantity on hand after this movement.</summary>
    public decimal BalanceQuantity { get; private set; }

    /// <summary>Gets the weighted average cost after this movement.</summary>
    public decimal BalanceAverageCost { get; private set; }

    /// <summary>Gets the instant the movement was posted.</summary>
    /// <remarks>
    /// The ordering key within a date. Two documents dated the same day are read in
    /// the order they were posted, which is the order the running average was
    /// computed in and therefore the only order in which the balance column makes
    /// sense.
    /// </remarks>
    public DateTimeOffset PostedAtUtc { get; private set; }

    /// <summary>Gets the narration carried from the document or its line.</summary>
    public string? Narration { get; private set; }

    /// <summary>Records a movement, with the position it left behind.</summary>
    /// <param name="balance">The position after the movement was applied to it.</param>
    /// <param name="date">The document date.</param>
    /// <param name="document">The document that caused it.</param>
    /// <param name="quantity">The signed quantity, in stock units.</param>
    /// <param name="unitCost">What one stock unit was valued at.</param>
    /// <param name="value">The signed value of the movement.</param>
    /// <param name="postedAtUtc">The instant it was posted.</param>
    /// <param name="narration">The narration to carry.</param>
    /// <returns>The entry, or the reason it could not be recorded.</returns>
    /// <remarks>
    /// Takes the balance rather than its two figures, so the running position on the
    /// row cannot be passed in wrong. The caller has just applied the movement to that
    /// balance; asking it to also report what the balance now says would be asking it
    /// to repeat something it can get wrong.
    /// </remarks>
    public static Result<StockLedgerEntry> Record(
        StockBalance balance,
        DateOnly date,
        StockDocument document,
        decimal quantity,
        decimal unitCost,
        Money value,
        DateTimeOffset postedAtUtc,
        string? narration = null)
    {
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentNullException.ThrowIfNull(document);

        if (quantity == 0m)
        {
            return Result.Failure<StockLedgerEntry>(Error.Validation(
                "StockLedgerEntry.QuantityZero",
                "A movement of nothing is not a movement."));
        }

        return Result.Success(new StockLedgerEntry(
            StockLedgerEntryId.NewId(),
            balance.TenantId,
            balance.FirmId,
            balance.ProductId,
            balance.WarehouseId,
            date,
            document.Id,
            document.Type,
            document.Number,
            quantity,
            unitCost,
            value,
            balance.Quantity,
            balance.AverageCost,
            postedAtUtc,
            string.IsNullOrWhiteSpace(narration) ? null : narration.Trim()));
    }
}
