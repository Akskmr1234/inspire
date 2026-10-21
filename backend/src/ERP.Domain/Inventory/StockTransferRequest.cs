using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>Identifies a stock transfer request.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct StockTransferRequestId(Guid Value)
    : IStronglyTypedId<StockTransferRequestId>
{
    /// <inheritdoc />
    public static StockTransferRequestId From(Guid value) => new(value);

    /// <inheritdoc />
    public static StockTransferRequestId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one line of a stock transfer request.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct StockTransferRequestLineId(Guid Value)
    : IStronglyTypedId<StockTransferRequestLineId>
{
    /// <inheritdoc />
    public static StockTransferRequestLineId From(Guid value) => new(value);

    /// <inheritdoc />
    public static StockTransferRequestLineId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Where a transfer request stands.</summary>
public enum StockTransferStatus
{
    /// <summary>Asked for, and still editable by whoever asked.</summary>
    Requested = 1,

    /// <summary>Agreed by the branch that holds the goods, for the quantities it agreed to.</summary>
    Approved = 2,

    /// <summary>On the road: out of the source, not yet in the destination.</summary>
    Dispatched = 3,

    /// <summary>Arrived and taken into the destination's stock.</summary>
    Received = 4,

    /// <summary>Turned down by the branch that holds the goods.</summary>
    Rejected = 5,

    /// <summary>Withdrawn before anything moved.</summary>
    Cancelled = 6,
}

/// <summary>
/// One branch asking another for goods: what it wants, who agreed, what was sent, and
/// whether it arrived.
/// </summary>
/// <remarks>
/// <para>
/// The request moves no stock itself. Dispatch and receipt are each an ordinary stock
/// transfer - out of the source into the firm's goods-in-transit location, then out of
/// transit into the destination - and this aggregate records which two documents those
/// were. Reusing the transfer means batches, serial numbers and costs travel exactly as
/// they do on a direct transfer, and goods on the road show up on the stock reports as
/// what they are rather than vanishing between two branches.
/// </para>
/// <para>
/// One line per product. Two lines for the same product would leave the dispatch unable
/// to say which of the two it was filling.
/// </para>
/// </remarks>
public sealed class StockTransferRequest
    : AggregateRoot<StockTransferRequestId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a narration or a reason may be.</summary>
    public const int MaximumNarrationLength = 500;

    private readonly List<StockTransferRequestLine> _lines = [];

    private StockTransferRequest(
        StockTransferRequestId id,
        TenantId tenantId,
        FirmId firmId,
        FinancialYearId financialYearId,
        string number,
        DateOnly date,
        WarehouseId fromWarehouseId,
        WarehouseId toWarehouseId)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        FinancialYearId = financialYearId;
        Number = number;
        Date = date;
        FromWarehouseId = fromWarehouseId;
        ToWarehouseId = toWarehouseId;
        Status = StockTransferStatus.Requested;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private StockTransferRequest() => Number = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the financial year the request was numbered in.</summary>
    public FinancialYearId FinancialYearId { get; private set; }

    /// <summary>Gets the request number.</summary>
    public string Number { get; private set; }

    /// <summary>Gets the date the goods were asked for.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the warehouse that holds the goods and sends them.</summary>
    public WarehouseId FromWarehouseId { get; private set; }

    /// <summary>Gets the warehouse that asked for them and receives them.</summary>
    public WarehouseId ToWarehouseId { get; private set; }

    /// <summary>Gets where the request stands.</summary>
    public StockTransferStatus Status { get; private set; }

    /// <summary>Gets what the requesting branch said about it.</summary>
    public string? Narration { get; private set; }

    /// <summary>Gets who approved or rejected it.</summary>
    public UserId? DecidedBy { get; private set; }

    /// <summary>Gets when it was approved or rejected.</summary>
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>Gets why it was rejected or cancelled.</summary>
    public string? Reason { get; private set; }

    /// <summary>Gets the transfer that took the goods out of the source.</summary>
    public StockDocumentId? DispatchDocumentId { get; private set; }

    /// <summary>Gets who dispatched the goods.</summary>
    public UserId? DispatchedBy { get; private set; }

    /// <summary>Gets when the goods were dispatched.</summary>
    public DateTimeOffset? DispatchedAtUtc { get; private set; }

    /// <summary>Gets the transfer that took the goods into the destination.</summary>
    public StockDocumentId? ReceiptDocumentId { get; private set; }

    /// <summary>Gets who received the goods.</summary>
    public UserId? ReceivedBy { get; private set; }

    /// <summary>Gets when the goods were received.</summary>
    public DateTimeOffset? ReceivedAtUtc { get; private set; }

    /// <summary>Gets the products asked for.</summary>
    public IReadOnlyList<StockTransferRequestLine> Lines => _lines.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets a value indicating whether the lines may still be changed.</summary>
    public bool IsEditable => Status == StockTransferStatus.Requested;

    /// <summary>Raises a request.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="year">The year it is numbered in.</param>
    /// <param name="number">The number its series issued.</param>
    /// <param name="date">The date the goods were asked for.</param>
    /// <param name="from">The warehouse that holds the goods.</param>
    /// <param name="to">The warehouse that wants them.</param>
    /// <returns>The request, or the reason it could not be raised.</returns>
    public static Result<StockTransferRequest> Create(
        TenantId tenantId,
        FirmId firmId,
        FinancialYear year,
        string number,
        DateOnly date,
        Warehouse from,
        Warehouse to)
    {
        ArgumentNullException.ThrowIfNull(year);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<StockTransferRequest>(Error.Validation(
                "StockTransfer.NumberRequired", "A request number is required."));
        }

        Result places = CheckPlaces(firmId, from, to);

        return places.IsFailure
            ? Result.Failure<StockTransferRequest>(places.Error)
            : Result.Success(new StockTransferRequest(
                StockTransferRequestId.NewId(), tenantId, firmId, year.Id, number.Trim(), date,
                from.Id, to.Id));
    }

    /// <summary>Changes the date, the two warehouses and the narration of a request.</summary>
    /// <param name="date">The date the goods were asked for.</param>
    /// <param name="from">The warehouse that holds the goods.</param>
    /// <param name="to">The warehouse that wants them.</param>
    /// <param name="narration">What the requesting branch said about it.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Revise(DateOnly date, Warehouse from, Warehouse to, string? narration)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        Result editable = EnsureEditable();

        if (editable.IsFailure)
        {
            return editable;
        }

        Result places = CheckPlaces(FirmId, from, to);

        if (places.IsFailure)
        {
            return places;
        }

        if (narration is not null && narration.Trim().Length > MaximumNarrationLength)
        {
            return Result.Failure(Error.Validation(
                "StockTransfer.NarrationTooLong",
                $"A narration cannot exceed {MaximumNarrationLength} characters."));
        }

        Date = date;
        FromWarehouseId = from.Id;
        ToWarehouseId = to.Id;
        Narration = string.IsNullOrWhiteSpace(narration) ? null : narration.Trim();

        return Result.Success();
    }

    /// <summary>Removes every line, ahead of the lines being entered again.</summary>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result ClearLines()
    {
        Result editable = EnsureEditable();

        if (editable.IsFailure)
        {
            return editable;
        }

        _lines.Clear();

        return Result.Success();
    }

    /// <summary>Asks for a product.</summary>
    /// <param name="product">The product.</param>
    /// <param name="unit">The unit the quantity is entered in.</param>
    /// <param name="quantity">How much, in that unit.</param>
    /// <param name="stockQuantity">The same quantity in the product's stock unit.</param>
    /// <param name="remarks">A remark for the line.</param>
    /// <returns>The line, or the reason it was refused.</returns>
    public Result<StockTransferRequestLine> AddLine(
        Product product,
        UnitOfMeasure unit,
        decimal quantity,
        decimal stockQuantity,
        string? remarks = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(unit);

        Result editable = EnsureEditable();

        if (editable.IsFailure)
        {
            return Result.Failure<StockTransferRequestLine>(editable.Error);
        }

        if (product.FirmId != FirmId)
        {
            return Result.Failure<StockTransferRequestLine>(Error.Validation(
                "StockTransfer.ProductNotInFirm", $"'{product.Code}' belongs to another firm."));
        }

        if (product.ItemType != ItemType.Stock)
        {
            return Result.Failure<StockTransferRequestLine>(Error.BusinessRule(
                "StockTransfer.NotStocked",
                $"'{product.Code}' is a {product.ItemType} item and does not hold stock."));
        }

        if (quantity <= 0m || stockQuantity <= 0m)
        {
            return Result.Failure<StockTransferRequestLine>(Error.Validation(
                "StockTransfer.QuantityNotPositive",
                $"Ask for a quantity of '{product.Code}' above zero."));
        }

        if (_lines.Any(line => line.ProductId == product.Id))
        {
            return Result.Failure<StockTransferRequestLine>(Error.Validation(
                "StockTransfer.DuplicateProduct",
                $"'{product.Code}' is on the request twice. Put the whole quantity on one line."));
        }

        Result precise = unit.EnsurePrecision(quantity);

        if (precise.IsFailure)
        {
            return Result.Failure<StockTransferRequestLine>(precise.Error);
        }

        StockTransferRequestLine line = new(
            StockTransferRequestLineId.NewId(), TenantId, Id, product.Id, unit.Id, quantity,
            stockQuantity, _lines.Count + 1, string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim());

        _lines.Add(line);

        return Result.Success(line);
    }

    /// <summary>Agrees to send the goods, for as much of each as the source can spare.</summary>
    /// <param name="by">The approver.</param>
    /// <param name="atUtc">When.</param>
    /// <param name="approved">
    /// The quantity agreed for a product, in its stock unit, where it differs from what
    /// was asked for. Nought turns a line down; more than was asked for is refused.
    /// </param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Approve(
        UserId by,
        DateTimeOffset atUtc,
        IReadOnlyDictionary<ProductId, decimal>? approved = null)
    {
        if (Status != StockTransferStatus.Requested)
        {
            return Result.Failure(NotAt("approved"));
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(Error.BusinessRule(
                "StockTransfer.NoLines", $"Request '{Number}' asks for nothing."));
        }

        foreach (StockTransferRequestLine line in _lines)
        {
            decimal agreed = approved is not null && approved.TryGetValue(line.ProductId, out decimal given)
                ? given
                : line.StockQuantity;

            if (agreed < 0m || agreed > line.StockQuantity)
            {
                return Result.Failure(Error.Validation(
                    "StockTransfer.ApprovedOutOfRange",
                    $"Line {line.LineNumber} can be approved for between 0 and "
                    + $"{line.StockQuantity:0.####}."));
            }

            line.Approve(agreed);
        }

        if (_lines.All(line => line.ApprovedQuantity == 0m))
        {
            return Result.Failure(Error.BusinessRule(
                "StockTransfer.NothingApproved",
                "Approving nothing is a rejection. Reject the request instead."));
        }

        Status = StockTransferStatus.Approved;
        DecidedBy = by;
        DecidedAtUtc = atUtc;

        return Result.Success();
    }

    /// <summary>Turns the request down.</summary>
    /// <param name="by">Who turned it down.</param>
    /// <param name="atUtc">When.</param>
    /// <param name="reason">Why. Required.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Reject(UserId by, DateTimeOffset atUtc, string reason)
    {
        if (Status is not (StockTransferStatus.Requested or StockTransferStatus.Approved))
        {
            return Result.Failure(NotAt("rejected"));
        }

        Result given = SetReason(reason, "rejecting");

        if (given.IsFailure)
        {
            return given;
        }

        Status = StockTransferStatus.Rejected;
        DecidedBy = by;
        DecidedAtUtc = atUtc;

        return Result.Success();
    }

    /// <summary>Withdraws the request before anything has moved.</summary>
    /// <param name="reason">Why. Required.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Cancel(string reason)
    {
        if (Status is not (StockTransferStatus.Requested or StockTransferStatus.Approved))
        {
            return Result.Failure(NotAt("cancelled"));
        }

        Result given = SetReason(reason, "cancelling");

        if (given.IsFailure)
        {
            return given;
        }

        Status = StockTransferStatus.Cancelled;

        return Result.Success();
    }

    /// <summary>Records the goods leaving the source.</summary>
    /// <param name="document">The transfer that took them out, into transit.</param>
    /// <param name="sent">What went, by product, in stock units.</param>
    /// <param name="by">Who sent them.</param>
    /// <param name="atUtc">When.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Less than was approved may go - the shelf may hold less than the system thought -
    /// but not more, and not a product nobody asked for: the receiving branch agreed to
    /// take what is on the request.
    /// </remarks>
    public Result Dispatch(
        StockDocumentId document,
        IReadOnlyDictionary<ProductId, decimal> sent,
        UserId by,
        DateTimeOffset atUtc)
    {
        ArgumentNullException.ThrowIfNull(sent);

        if (Status != StockTransferStatus.Approved)
        {
            return Result.Failure(NotAt("dispatched"));
        }

        foreach ((ProductId productId, decimal quantity) in sent)
        {
            StockTransferRequestLine? line = _lines.Find(candidate => candidate.ProductId == productId);

            if (line is null)
            {
                return Result.Failure(Error.Validation(
                    "StockTransfer.ProductNotRequested",
                    "A product that is not on the request cannot be dispatched against it."));
            }

            if (quantity > line.ApprovedQuantity)
            {
                return Result.Failure(Error.Validation(
                    "StockTransfer.DispatchAboveApproved",
                    $"Line {line.LineNumber} was approved for {line.ApprovedQuantity:0.####}; "
                    + $"{quantity:0.####} cannot be sent against it."));
            }
        }

        if (sent.Values.Sum() <= 0m)
        {
            return Result.Failure(Error.Validation(
                "StockTransfer.NothingDispatched", "A dispatch has to send something."));
        }

        foreach (StockTransferRequestLine line in _lines)
        {
            line.Dispatch(sent.TryGetValue(line.ProductId, out decimal quantity) ? quantity : 0m);
        }

        Status = StockTransferStatus.Dispatched;
        DispatchDocumentId = document;
        DispatchedBy = by;
        DispatchedAtUtc = atUtc;

        return Result.Success();
    }

    /// <summary>Puts the request back to approved, its dispatch having been cancelled.</summary>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result UndoDispatch()
    {
        if (Status != StockTransferStatus.Dispatched)
        {
            return Result.Failure(Error.BusinessRule(
                "StockTransfer.AlreadyReceived",
                $"Request '{Number}' has been received. Cancel the receipt before the dispatch."));
        }

        foreach (StockTransferRequestLine line in _lines)
        {
            line.Dispatch(0m);
        }

        Status = StockTransferStatus.Approved;
        DispatchDocumentId = null;
        DispatchedBy = null;
        DispatchedAtUtc = null;

        return Result.Success();
    }

    /// <summary>Records the goods arriving at the destination.</summary>
    /// <param name="document">The transfer that took them out of transit.</param>
    /// <param name="by">Who received them.</param>
    /// <param name="atUtc">When.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Receive(StockDocumentId document, UserId by, DateTimeOffset atUtc)
    {
        if (Status != StockTransferStatus.Dispatched)
        {
            return Result.Failure(NotAt("received"));
        }

        Status = StockTransferStatus.Received;
        ReceiptDocumentId = document;
        ReceivedBy = by;
        ReceivedAtUtc = atUtc;

        return Result.Success();
    }

    /// <summary>Puts the goods back on the road, the receipt having been cancelled.</summary>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result UndoReceipt()
    {
        if (Status != StockTransferStatus.Received)
        {
            return Result.Failure(NotAt("taken back to dispatched"));
        }

        Status = StockTransferStatus.Dispatched;
        ReceiptDocumentId = null;
        ReceivedBy = null;
        ReceivedAtUtc = null;

        return Result.Success();
    }

    private static Result CheckPlaces(FirmId firmId, Warehouse from, Warehouse to)
    {
        if (from.FirmId != firmId || to.FirmId != firmId)
        {
            return Result.Failure(Error.Validation(
                "StockTransfer.WarehouseNotInFirm", "Both warehouses must belong to the firm."));
        }

        if (from.Id == to.Id)
        {
            return Result.Failure(Error.Validation(
                "StockTransfer.SameWarehouse",
                "A transfer must be between two different warehouses."));
        }

        if (from.IsTransit || to.IsTransit)
        {
            return Result.Failure(Error.Validation(
                "StockTransfer.TransitNotAPlace",
                "Goods in transit are on their way somewhere. Ask for them from, and send them "
                + "to, a real warehouse."));
        }

        Warehouse? withdrawn = new[] { from, to }.FirstOrDefault(place => !place.IsActive);

        return withdrawn is not null
            ? Result.Failure(Error.BusinessRule(
                "StockTransfer.WarehouseWithdrawn",
                $"Warehouse '{withdrawn.Name}' has been withdrawn from use."))
            : Result.Success();
    }

    private Result EnsureEditable() =>
        IsEditable
            ? Result.Success()
            : Result.Failure(Error.BusinessRule(
                "StockTransfer.NotEditable",
                $"Request '{Number}' is {Status} and can no longer be changed."));

    private Result SetReason(string reason, string doing)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "StockTransfer.ReasonRequired", $"Give a reason when {doing} a request."));
        }

        if (reason.Trim().Length > MaximumNarrationLength)
        {
            return Result.Failure(Error.Validation(
                "StockTransfer.ReasonTooLong",
                $"A reason cannot exceed {MaximumNarrationLength} characters."));
        }

        Reason = reason.Trim();

        return Result.Success();
    }

    private Error NotAt(string what) => Error.BusinessRule(
        "StockTransfer.WrongStatus",
        $"Request '{Number}' is {Status} and cannot be {what}.");
}

/// <summary>One product on a transfer request, and how much of it was agreed and sent.</summary>
public sealed class StockTransferRequestLine : Entity<StockTransferRequestLineId>, ITenantScoped
{
    internal StockTransferRequestLine(
        StockTransferRequestLineId id,
        TenantId tenantId,
        StockTransferRequestId requestId,
        ProductId productId,
        UnitOfMeasureId unitId,
        decimal quantity,
        decimal stockQuantity,
        int lineNumber,
        string? remarks)
        : base(id)
    {
        TenantId = tenantId;
        StockTransferRequestId = requestId;
        ProductId = productId;
        UnitId = unitId;
        Quantity = quantity;
        StockQuantity = stockQuantity;
        LineNumber = lineNumber;
        Remarks = remarks;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private StockTransferRequestLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the request.</summary>
    public StockTransferRequestId StockTransferRequestId { get; private set; }

    /// <summary>Gets the product asked for.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the unit the quantity was entered in.</summary>
    public UnitOfMeasureId UnitId { get; private set; }

    /// <summary>Gets the quantity asked for, in <see cref="UnitId"/>.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the quantity asked for, in the product's stock unit.</summary>
    public decimal StockQuantity { get; private set; }

    /// <summary>Gets the quantity agreed, in the stock unit.</summary>
    public decimal ApprovedQuantity { get; private set; }

    /// <summary>Gets the quantity sent, in the stock unit.</summary>
    public decimal DispatchedQuantity { get; private set; }

    /// <summary>Gets the position of the line on the request, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets the line's remark.</summary>
    public string? Remarks { get; private set; }

    internal void Approve(decimal quantity) => ApprovedQuantity = quantity;

    internal void Dispatch(decimal quantity) => DispatchedQuantity = quantity;
}
