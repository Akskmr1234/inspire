using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>The stock operations of section 8.3.</summary>
/// <remarks>
/// Seven kinds rather than the eight the specification names, because Stock
/// Adjustment covers increases and decreases in one document - a stocktake correction
/// is one piece of work and splitting it in two would make somebody enter it twice.
/// Delivery Note and Receipt Note are not here: they belong to a sales order and a
/// purchase order respectively, and modelling them before those exist would mean
/// guessing at the link they carry.
/// </remarks>
public enum StockDocumentType
{
    /// <summary>The stock a firm holds on the day it starts using the system.</summary>
    OpeningStock = 1,

    /// <summary>Goods in, from something other than a purchase.</summary>
    MaterialReceipt = 2,

    /// <summary>Goods out, consumed rather than sold.</summary>
    MaterialIssue = 3,

    /// <summary>Goods moved from one warehouse to another.</summary>
    StockTransfer = 4,

    /// <summary>A correction, up or down, to what the system believes is on hand.</summary>
    StockAdjustment = 5,

    /// <summary>Goods written off as damaged.</summary>
    DamagedStock = 6,

    /// <summary>A count, where the line quantity is what was found on the shelf.</summary>
    PhysicalVerification = 7,
}

/// <summary>Where a stock document stands in its lifecycle.</summary>
public enum StockDocumentStatus
{
    /// <summary>Being entered. Editable, and no goods have moved.</summary>
    Draft = 1,

    /// <summary>Posted. The stock ledger and the balances have it.</summary>
    Posted = 2,

    /// <summary>Reversed out, with the movements undone and the document retained.</summary>
    Cancelled = 3,
}

/// <summary>
/// A document that moves stock: a receipt, an issue, a transfer, a correction.
/// </summary>
/// <remarks>
/// <para>
/// The document and its lines are one aggregate, for the same reason a voucher and
/// its lines are: a transfer that moved three of its four products would be worse
/// than one that moved none, and only saving them together rules that out.
/// </para>
/// <para>
/// What this aggregate does <em>not</em> do is touch stock. Posting it is a
/// transition on the document; the balances it moves are separate aggregates, and
/// they are moved by the application layer inside the same transaction. That is the
/// codebase's standing rule - one aggregate per transaction, the second changed by
/// the handler rather than as a side effect - and it matters more here than usual,
/// because a document line can be refused by a balance it has never seen (there is
/// not enough on hand) and the refusal has to name the line.
/// </para>
/// <para>
/// No accounting entry is raised. Stock movements have a general-ledger consequence -
/// inventory against consumption, against damage, against opening equity - but the
/// mapping from a document to the accounts it posts to is a master this system does
/// not have yet, and inventing one here would put figures in the books that nobody
/// specified. The stock ledger is complete and self-consistent; the bridge to the
/// nominal ledger is a deliberate, recorded gap.
/// </para>
/// </remarks>
public sealed class StockDocument
    : AggregateRoot<StockDocumentId>, IFirmScoped, IAuditable, ISoftDeletable
{
    /// <summary>The longest a narration or remark may be.</summary>
    public const int MaximumNarrationLength = 500;

    /// <summary>The longest a reference number may be.</summary>
    public const int MaximumReferenceLength = 60;

    private readonly List<StockDocumentLine> _lines = [];

    private StockDocument(
        StockDocumentId id,
        TenantId tenantId,
        FirmId firmId,
        FinancialYearId financialYearId,
        StockDocumentType type,
        string number,
        DateOnly date,
        WarehouseId warehouseId,
        WarehouseId? destinationWarehouseId)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        FinancialYearId = financialYearId;
        Type = type;
        Number = number;
        Date = date;
        WarehouseId = warehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        Status = StockDocumentStatus.Draft;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private StockDocument() => Number = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the financial year the movement falls in.</summary>
    public FinancialYearId FinancialYearId { get; private set; }

    /// <summary>Gets the kind of operation.</summary>
    public StockDocumentType Type { get; private set; }

    /// <summary>Gets the document number, from the numbering series.</summary>
    public string Number { get; private set; }

    /// <summary>Gets the document date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the warehouse the document acts on, or moves goods out of.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Gets the warehouse a transfer moves goods into.</summary>
    public WarehouseId? DestinationWarehouseId { get; private set; }

    /// <summary>Gets the reference this document relates to.</summary>
    public string? ReferenceNumber { get; private set; }

    /// <summary>Gets the document-level narration.</summary>
    public string? Narration { get; private set; }

    /// <summary>Gets the current lifecycle state.</summary>
    public StockDocumentStatus Status { get; private set; }

    /// <summary>Gets the instant the document was posted, in UTC.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

    /// <summary>Gets the user who posted it.</summary>
    public UserId? PostedBy { get; private set; }

    /// <summary>Gets why the document was cancelled.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<StockDocumentLine> Lines => _lines.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <inheritdoc />
    public bool IsDeleted { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? DeletedBy { get; private set; }

    /// <summary>Gets a value indicating whether the document may still be changed.</summary>
    public bool IsEditable => Status == StockDocumentStatus.Draft;

    /// <summary>Gets a value indicating whether this kind of document moves goods between two places.</summary>
    public bool IsTransfer => Type == StockDocumentType.StockTransfer;

    /// <summary>
    /// Gets a value indicating whether a line of this document carries the cost of
    /// the goods.
    /// </summary>
    /// <remarks>
    /// Only where goods arrive from outside the firm's existing stock. Everything
    /// else - an issue, a transfer, a write-off - is valued at what the position it
    /// leaves already says the goods cost, because those goods are the ones already
    /// counted and priced.
    /// </remarks>
    public bool CarriesRate =>
        Type is StockDocumentType.OpeningStock or StockDocumentType.MaterialReceipt
            or StockDocumentType.StockAdjustment;

    /// <summary>
    /// Gets a value indicating whether a line quantity may be negative.
    /// </summary>
    /// <remarks>
    /// Only an adjustment, where the sign is the whole point: found stock and lost
    /// stock are the same correction pointed two ways, and forcing them into separate
    /// documents would mean entering one stocktake twice.
    /// </remarks>
    public bool AllowsSignedQuantity => Type == StockDocumentType.StockAdjustment;

    /// <summary>Starts a draft stock document.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="financialYear">The year the movement falls in.</param>
    /// <param name="type">The kind of operation.</param>
    /// <param name="number">The document number from the numbering series.</param>
    /// <param name="date">The document date.</param>
    /// <param name="warehouse">The warehouse acted on, or moved out of.</param>
    /// <param name="destination">The warehouse a transfer moves into.</param>
    /// <returns>The draft, or the reason it could not be started.</returns>
    public static Result<StockDocument> CreateDraft(
        TenantId tenantId,
        FirmId firmId,
        FinancialYear financialYear,
        StockDocumentType type,
        string number,
        DateOnly date,
        Warehouse warehouse,
        Warehouse? destination = null)
    {
        ArgumentNullException.ThrowIfNull(financialYear);
        ArgumentNullException.ThrowIfNull(warehouse);

        if (!Enum.IsDefined(type))
        {
            return Result.Failure<StockDocument>(Error.Validation(
                "StockDocument.UnknownType", $"'{type}' is not a recognised stock operation."));
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<StockDocument>(Error.Validation(
                "StockDocument.NumberRequired", "A document number is required."));
        }

        if (!warehouse.IsActive)
        {
            return Result.Failure<StockDocument>(Error.BusinessRule(
                "StockDocument.WarehouseWithdrawn",
                $"Warehouse '{warehouse.Name}' has been withdrawn from use."));
        }

        bool isTransfer = type == StockDocumentType.StockTransfer;

        if (isTransfer)
        {
            if (destination is null)
            {
                return Result.Failure<StockDocument>(Error.Validation(
                    "StockDocument.DestinationRequired",
                    "A transfer needs a warehouse to move the goods into."));
            }

            if (!destination.IsActive)
            {
                return Result.Failure<StockDocument>(Error.BusinessRule(
                    "StockDocument.WarehouseWithdrawn",
                    $"Warehouse '{destination.Name}' has been withdrawn from use."));
            }

            // A transfer to the same place moves nothing and would post two equal
            // and opposite ledger entries, which reads on a stock ledger as goods
            // having gone somewhere.
            if (destination.Id == warehouse.Id)
            {
                return Result.Failure<StockDocument>(Error.Validation(
                    "StockDocument.SameWarehouse",
                    "A transfer must be between two different warehouses."));
            }
        }
        else if (destination is not null)
        {
            return Result.Failure<StockDocument>(Error.Validation(
                "StockDocument.DestinationNotAllowed",
                $"A {type} acts on one warehouse, so a destination cannot be given."));
        }

        // The single gate covering both the date range and whether the year is still
        // open, exactly as a voucher passes through.
        Result canPost = financialYear.CanPostOn(date);

        if (canPost.IsFailure)
        {
            return Result.Failure<StockDocument>(canPost.Error);
        }

        return Result.Success(new StockDocument(
            StockDocumentId.NewId(),
            tenantId,
            firmId,
            financialYear.Id,
            type,
            number.Trim(),
            date,
            warehouse.Id,
            isTransfer ? destination!.Id : null));
    }

    /// <summary>Adds a line to a draft document.</summary>
    /// <param name="product">The product moving.</param>
    /// <param name="unit">The unit the quantity is entered in.</param>
    /// <param name="quantity">How much, in that unit.</param>
    /// <param name="stockQuantity">The same quantity converted to the stock unit.</param>
    /// <param name="rate">What one stock unit cost, where the document carries a rate.</param>
    /// <param name="remarks">A line-level remark.</param>
    /// <returns>The line, or the reason it was refused.</returns>
    /// <remarks>
    /// The conversion is done by the caller and passed in rather than computed here.
    /// The unit and its factor belong to a different aggregate, and an aggregate that
    /// reached into another to convert its own quantities would be holding a
    /// reference this design does not permit it to hold.
    /// </remarks>
    public Result<StockDocumentLine> AddLine(
        Product product,
        UnitOfMeasure unit,
        decimal quantity,
        decimal stockQuantity,
        decimal rate = 0m,
        string? remarks = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(unit);

        if (!IsEditable)
        {
            return Result.Failure<StockDocumentLine>(Error.BusinessRule(
                "StockDocument.NotEditable",
                $"Document '{Number}' is {Status} and can no longer be changed."));
        }

        if (product.FirmId != FirmId)
        {
            return Result.Failure<StockDocumentLine>(Error.Validation(
                "StockDocument.ProductNotInFirm",
                $"'{product.Code}' belongs to another firm."));
        }

        // A service has no physical unit to move, and a non-stock item is one the
        // firm has decided not to track. Either on a stock document would produce a
        // balance for something that does not have one.
        if (product.ItemType != ItemType.Stock)
        {
            return Result.Failure<StockDocumentLine>(Error.BusinessRule(
                "StockDocument.NotStocked",
                $"'{product.Code}' is a {product.ItemType} item and does not hold stock."));
        }

        if (quantity == 0m || stockQuantity == 0m)
        {
            return Result.Failure<StockDocumentLine>(Error.Validation(
                "StockDocument.QuantityZero",
                "A line for no quantity would record nothing."));
        }

        if (!AllowsSignedQuantity && (quantity < 0m || stockQuantity < 0m))
        {
            return Result.Failure<StockDocumentLine>(Error.Validation(
                "StockDocument.QuantityNegative",
                $"A {Type} line must be for a positive quantity. Use the opposite " +
                $"document type rather than a negative quantity."));
        }

        if (rate < 0m)
        {
            return Result.Failure<StockDocumentLine>(Error.Validation(
                "StockDocument.RateNegative", "A rate cannot be negative."));
        }

        // The rate on a document that does not carry one would be recorded, shown,
        // and ignored - which is worse than refusing it, because somebody would set
        // it and believe it had done something.
        if (rate > 0m && !CarriesRate)
        {
            return Result.Failure<StockDocumentLine>(Error.Validation(
                "StockDocument.RateNotAllowed",
                $"A {Type} is valued at what the goods already cost, so a rate " +
                $"cannot be given."));
        }

        Result precise = unit.EnsurePrecision(quantity);

        if (precise.IsFailure)
        {
            return Result.Failure<StockDocumentLine>(precise.Error);
        }

        StockDocumentLine line = new(
            StockDocumentLineId.NewId(),
            TenantId,
            Id,
            product.Id,
            unit.Id,
            quantity,
            stockQuantity,
            rate,
            _lines.Count + 1,
            remarks?.Trim());

        _lines.Add(line);

        return Result.Success(line);
    }

    /// <summary>Removes a line from a draft document.</summary>
    /// <param name="lineId">The line.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result RemoveLine(StockDocumentLineId lineId)
    {
        if (!IsEditable)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.NotEditable",
                $"Document '{Number}' is {Status} and can no longer be changed."));
        }

        if (_lines.RemoveAll(line => line.Id == lineId) == 0)
        {
            return Result.Failure(Error.NotFound(
                "StockDocument.LineNotFound", "That line does not belong to this document."));
        }

        for (int index = 0; index < _lines.Count; index++)
        {
            _lines[index].Renumber(index + 1);
        }

        return Result.Success();
    }

    /// <summary>Sets the descriptive fields.</summary>
    /// <param name="referenceNumber">The reference this relates to.</param>
    /// <param name="narration">The narration.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetDetails(string? referenceNumber, string? narration)
    {
        if (!IsEditable)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.NotEditable",
                $"Document '{Number}' is {Status} and can no longer be changed."));
        }

        ReferenceNumber = Trimmed(referenceNumber);
        Narration = Trimmed(narration);

        return Result.Success();
    }

    /// <summary>
    /// Marks the document posted, once every invariant it owns is satisfied.
    /// </summary>
    /// <param name="postedBy">The user posting it.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Success, or the first invariant that fails.</returns>
    /// <remarks>
    /// The stock itself moves in the handler, not here. This is the gate that decides
    /// whether it may: a document that cannot pass these checks never reaches a
    /// balance.
    /// </remarks>
    public Result Post(UserId postedBy, DateTimeOffset nowUtc)
    {
        if (Status != StockDocumentStatus.Draft)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.AlreadyPosted",
                $"Document '{Number}' is already {Status}."));
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.NoLines",
                $"Document '{Number}' has no lines and would move nothing."));
        }

        // One product twice on one document is almost always a mistake, and where it
        // is not, the two lines net to something the entry screen can show as one.
        // Allowing it would also make a transfer post two movements against the same
        // position, so the second would be valued at an average the first had just
        // changed.
        List<ProductId> duplicated = _lines
            .GroupBy(line => line.ProductId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicated.Count > 0)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.DuplicateProduct",
                $"Document '{Number}' has the same product on more than one line."));
        }

        Status = StockDocumentStatus.Posted;
        PostedAtUtc = nowUtc;
        PostedBy = postedBy;

        return Result.Success();
    }

    /// <summary>Cancels a posted document.</summary>
    /// <param name="reason">Why. Required.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// The movements are reversed by the handler, which writes contra entries into
    /// the stock ledger rather than deleting the originals. A stock ledger that could
    /// lose a movement is a stock ledger nobody can reconcile against a count.
    /// </remarks>
    public Result Cancel(string reason)
    {
        if (Status != StockDocumentStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.NotPosted",
                $"Only a posted document can be cancelled, and '{Number}' is {Status}."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "StockDocument.CancellationReasonRequired",
                "A reason is required when cancelling a stock document."));
        }

        Status = StockDocumentStatus.Cancelled;
        CancellationReason = reason.Trim();

        return Result.Success();
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
