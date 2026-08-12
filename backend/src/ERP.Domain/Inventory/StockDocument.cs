using ERP.Domain.Accounting;
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

    /// <summary>Goods leaving because they were sold. Raised by a sales invoice.</summary>
    /// <remarks>
    /// Its own kind rather than a material issue with a note on it. The two differ in
    /// what they cost the firm - one is the cost of goods sold, the other is consumption -
    /// and a stock ledger that could not tell them apart would leave somebody adding up
    /// issues by hand to find out what was actually sold.
    /// </remarks>
    SalesIssue = 8,

    /// <summary>Goods coming back from a customer. Raised by a sales return.</summary>
    /// <remarks>
    /// The mirror of <see cref="SalesIssue"/>, and its own kind for the same reason: what
    /// comes back from a customer is not the same event as goods arriving from anywhere
    /// else, and a stock ledger that could not tell them apart would leave somebody
    /// adding up receipts by hand to find out what was actually returned.
    /// </remarks>
    SalesReturn = 9,
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

    /// <summary>Gets the journal this document raised in the nominal ledger.</summary>
    /// <remarks>
    /// Held so the two sides can be traced to each other: the stock ledger says goods
    /// moved, the journal says what that did to the accounts, and a reader looking at
    /// either should be able to reach the other. Null on a document that raised none -
    /// a draft, or a transfer, which moves goods between shelves without changing whose
    /// they are or what they are worth.
    /// </remarks>
    public VoucherId? JournalVoucherId { get; private set; }

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
            or StockDocumentType.StockAdjustment or StockDocumentType.SalesReturn;

    /// <summary>
    /// Gets a value indicating whether this kind of document may put a batch on the
    /// books that was not there before.
    /// </summary>
    /// <remarks>
    /// The documents that can increase stock. Everything else moves goods that are
    /// already somewhere, so a batch number it does not recognise is a typing mistake
    /// rather than a new lot - and creating one would put an issue's worth of stock
    /// into a batch that had never received any.
    /// </remarks>
    public bool OpensBatches =>
        Type is StockDocumentType.OpeningStock or StockDocumentType.MaterialReceipt
            or StockDocumentType.StockAdjustment or StockDocumentType.PhysicalVerification;

    /// <summary>
    /// Gets a value indicating whether this kind of document may invent the batch
    /// number as well as the batch.
    /// </summary>
    /// <remarks>
    /// Section 10's auto-generation, kept to the documents that bring goods in from
    /// outside. A physical verification is deliberately excluded: somebody counting a
    /// shelf is reading a number off a carton, and generating one for them would file
    /// the count against a batch that exists nowhere but here.
    /// </remarks>
    /// <remarks>
    /// Not simply the documents that carry a rate, though it was once the same list. A
    /// sales return carries a cost - the goods have to come back in at something - but it
    /// may not open a batch: what came back left in a batch that already exists, and a
    /// new one would mean a customer returned goods from a lot nobody ever received.
    /// </remarks>
    public bool GeneratesBatchNumbers =>
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
    /// <param name="batch">The batch that moved, on a product tracked in batches.</param>
    /// <param name="serials">
    /// The units that moved, on a product tracked by serial number. As many as the line
    /// moves, no more and no fewer.
    /// </param>
    /// <param name="remarks">A line-level remark.</param>
    /// <returns>The line, or the reason it was refused.</returns>
    /// <remarks>
    /// The conversion is done by the caller and passed in rather than computed here.
    /// The unit and its factor belong to a different aggregate, and an aggregate that
    /// reached into another to convert its own quantities would be holding a
    /// reference this design does not permit it to hold. The batch arrives the same
    /// way and for the same reason: finding or generating it is the caller's work.
    /// </remarks>
    public Result<StockDocumentLine> AddLine(
        Product product,
        UnitOfMeasure unit,
        decimal quantity,
        decimal stockQuantity,
        decimal rate = 0m,
        Batch? batch = null,
        IReadOnlyCollection<SerialNumber>? serials = null,
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

        Result batched = EnsureBatchMatches(product, batch);

        if (batched.IsFailure)
        {
            return Result.Failure<StockDocumentLine>(batched.Error);
        }

        Result serialised = EnsureSerialsMatch(product, serials, stockQuantity, batch);

        if (serialised.IsFailure)
        {
            return Result.Failure<StockDocumentLine>(serialised.Error);
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
            batch?.Id,
            unit.Id,
            quantity,
            stockQuantity,
            rate,
            _lines.Count + 1,
            remarks?.Trim());

        foreach (SerialNumber serial in serials ?? [])
        {
            line.AddSerial(serial.Id);
        }

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
        //
        // Twice in two batches is the exception, and the one case where it is not a
        // mistake at all: an issue of thirty from a batch holding twenty has to draw
        // the rest from somewhere, and the two lots leave at two costs and carry two
        // expiry dates. Each moves its own batch position, so neither is valued at
        // something the other has just changed.
        bool duplicated = _lines
            .GroupBy(line => (line.ProductId, line.BatchId))
            .Any(group => group.Count() > 1);

        if (duplicated)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.DuplicateProduct",
                $"Document '{Number}' has the same product and batch on more than one "
                + "line."));
        }

        Status = StockDocumentStatus.Posted;
        PostedAtUtc = nowUtc;
        PostedBy = postedBy;

        return Result.Success();
    }

    /// <summary>Names the journal this document raised.</summary>
    /// <param name="voucherId">The journal voucher.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Recorded by the handler that raised it, in the same transaction as the posting.
    /// Only a posted document can name one, and only once: a stock document pointing at
    /// two journals would leave a reader unable to say which of them accounts for the
    /// goods it moved.
    /// </remarks>
    public Result RecordJournal(VoucherId voucherId)
    {
        if (Status != StockDocumentStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.NotPosted",
                $"Document '{Number}' is {Status}, so it has raised no journal."));
        }

        if (JournalVoucherId is not null)
        {
            return Result.Failure(Error.BusinessRule(
                "StockDocument.AlreadyJournalled",
                $"Document '{Number}' already names the journal it raised."));
        }

        JournalVoucherId = voucherId;

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

    /// <summary>Checks that the line names a batch exactly when the product needs one.</summary>
    /// <param name="product">The product on the line.</param>
    /// <param name="batch">The batch offered for it, if any.</param>
    /// <returns>Success, or the reason the pairing was refused.</returns>
    /// <remarks>
    /// Both directions are refused, and the second matters as much as the first. A
    /// batch on a product that is not tracked in batches would be recorded, printed,
    /// and ignored by the position - a number on a document that means nothing to the
    /// stock it describes.
    /// </remarks>
    private Result EnsureBatchMatches(Product product, Batch? batch)
    {
        if (product.TracksBatches && batch is null)
        {
            return Result.Failure(Error.Validation(
                "StockDocument.BatchRequired",
                $"'{product.Code}' is tracked in batches, so the line must say which "
                + "batch moved."));
        }

        if (batch is null)
        {
            return Result.Success();
        }

        if (!product.TracksBatches)
        {
            return Result.Failure(Error.Validation(
                "StockDocument.BatchNotTracked",
                $"'{product.Code}' is not tracked in batches, so a batch cannot be "
                + "given for it."));
        }

        if (batch.ProductId != product.Id)
        {
            return Result.Failure(Error.Validation(
                "StockDocument.BatchWrongProduct",
                $"Batch '{batch.Number}' is a batch of another product."));
        }

        return batch.FirmId != FirmId
            ? Result.Failure(Error.Validation(
                "StockDocument.BatchNotInFirm",
                $"Batch '{batch.Number}' belongs to another firm."))
            : Result.Success();
    }

    /// <summary>Checks the units a line names against the quantity it moves.</summary>
    /// <param name="product">The product on the line.</param>
    /// <param name="serials">The units offered for it, if any.</param>
    /// <param name="stockQuantity">The quantity, in the product's stock unit.</param>
    /// <param name="batch">The batch the line moves, where it has one.</param>
    /// <returns>Success, or the reason the pairing was refused.</returns>
    /// <remarks>
    /// One unit per serial, always. That makes three things true at once and each is
    /// worth enforcing: a serialised quantity is whole, because half a handset is not a
    /// thing; the count matches the quantity, because a line for three that names two
    /// leaves one unit untracked for ever; and no unit appears twice, because the
    /// second mention would move something that had already moved.
    /// </remarks>
    private Result EnsureSerialsMatch(
        Product product,
        IReadOnlyCollection<SerialNumber>? serials,
        decimal stockQuantity,
        Batch? batch)
    {
        int offered = serials?.Count ?? 0;

        if (!product.TracksSerialNumbers)
        {
            return offered > 0
                ? Result.Failure(Error.Validation(
                    "StockDocument.SerialsNotTracked",
                    $"'{product.Code}' is not tracked by serial number, so units cannot be "
                    + "named for it."))
                : Result.Success();
        }

        decimal units = Math.Abs(stockQuantity);

        if (units != decimal.Truncate(units))
        {
            return Result.Failure(Error.Validation(
                "StockDocument.SerialQuantityFractional",
                $"'{product.Code}' is tracked by serial number, so {stockQuantity} of it "
                + "is not a quantity that can be identified unit by unit."));
        }

        if (offered != units)
        {
            return Result.Failure(Error.Validation(
                "StockDocument.SerialCountMismatch",
                $"'{product.Code}' moves {units} units on this line, so {units} serial "
                + $"numbers are needed and {offered} were given."));
        }

        foreach (SerialNumber serial in serials ?? [])
        {
            if (serial.ProductId != product.Id)
            {
                return Result.Failure(Error.Validation(
                    "StockDocument.SerialWrongProduct",
                    $"Serial '{serial.Number}' is a unit of another product."));
            }

            if (serial.FirmId != FirmId)
            {
                return Result.Failure(Error.Validation(
                    "StockDocument.SerialNotInFirm",
                    $"Serial '{serial.Number}' belongs to another firm."));
            }

            // A unit carries its batch from the day it is received. A line moving it
            // under a different batch number would put one of the two on the wrong
            // paperwork, and the expiry date follows the batch.
            if (batch is not null && serial.BatchId is { } held && held != batch.Id)
            {
                return Result.Failure(Error.Validation(
                    "StockDocument.SerialWrongBatch",
                    $"Serial '{serial.Number}' belongs to a different batch."));
            }
        }

        return serials?.DistinctBy(serial => serial.Id).Count() != offered
            ? Result.Failure(Error.Validation(
                "StockDocument.SerialRepeated",
                "The same unit is named twice on one line."))
            : Result.Success();
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
