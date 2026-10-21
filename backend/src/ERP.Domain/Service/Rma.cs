using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Purchase;
using ERP.Domain.Sales;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Service;

/// <summary>Identifies a return merchandise authorisation.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct RmaId(Guid Value) : IStronglyTypedId<RmaId>
{
    /// <inheritdoc />
    public static RmaId From(Guid value) => new(value);

    /// <inheritdoc />
    public static RmaId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one product on a return.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct RmaLineId(Guid Value) : IStronglyTypedId<RmaLineId>
{
    /// <inheritdoc />
    public static RmaLineId From(Guid value) => new(value);

    /// <inheritdoc />
    public static RmaLineId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies a photograph kept as evidence on a return.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct RmaPhotoId(Guid Value) : IStronglyTypedId<RmaPhotoId>
{
    /// <inheritdoc />
    public static RmaPhotoId From(Guid value) => new(value);

    /// <inheritdoc />
    public static RmaPhotoId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one entry in a return's history.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct RmaEventId(Guid Value) : IStronglyTypedId<RmaEventId>
{
    /// <inheritdoc />
    public static RmaEventId From(Guid value) => new(value);

    /// <inheritdoc />
    public static RmaEventId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Which way the goods go back.</summary>
public enum RmaDirection
{
    /// <summary>A customer returns goods to the firm.</summary>
    FromCustomer = 1,

    /// <summary>The firm returns goods to its supplier.</summary>
    ToSupplier = 2,
}

/// <summary>Where a return stands.</summary>
public enum RmaStatus
{
    /// <summary>Asked for, awaiting approval.</summary>
    Requested = 1,

    /// <summary>Approved: the goods may come back.</summary>
    Approved = 2,

    /// <summary>The goods have changed hands.</summary>
    Received = 3,

    /// <summary>Being inspected.</summary>
    UnderInspection = 4,

    /// <summary>Repaired and handed back.</summary>
    Repaired = 5,

    /// <summary>Exchanged for a replacement.</summary>
    Replaced = 6,

    /// <summary>Settled by a credit note or a debit note.</summary>
    Refunded = 7,

    /// <summary>Finished with.</summary>
    Closed = 8,

    /// <summary>Refused at approval.</summary>
    Rejected = 9,

    /// <summary>Withdrawn before anything came of it.</summary>
    Cancelled = 10,
}

/// <summary>What the inspection found.</summary>
public enum RmaCondition
{
    /// <summary>Nothing wrong: it can go back on the shelf.</summary>
    Good = 1,

    /// <summary>Faulty, and can be repaired.</summary>
    Repairable = 2,

    /// <summary>Faulty beyond repair.</summary>
    Defective = 3,

    /// <summary>Physically damaged, which the warranty does not cover.</summary>
    Damaged = 4,
}

/// <summary>
/// A return merchandise authorisation: goods a customer brings back, or goods the firm sends
/// back to a supplier, from the request through approval and inspection to what came of it.
/// </summary>
/// <remarks>
/// <para>
/// The return moves nothing by itself. Its outcome does, through the documents the firm
/// already keeps: a refund is a credit note to the customer or a debit note to the supplier,
/// which bring the goods back or send them out and settle the account; an exchange is a
/// transfer between the godown the replacement comes from and the one the returned goods are
/// kept in. The return records which document that was.
/// </para>
/// <para>
/// Every step is written to its history with who took it and when, so the history is the
/// return's own record of what happened rather than something rebuilt from audit columns.
/// </para>
/// </remarks>
public sealed class Rma : AggregateRoot<RmaId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a reason, a note or a reference may be.</summary>
    public const int MaximumTextLength = 1000;

    /// <summary>The most photographs one return keeps.</summary>
    public const int MaximumPhotos = 6;

    private readonly List<RmaLine> _lines = [];
    private readonly List<RmaPhoto> _photos = [];
    private readonly List<RmaEvent> _events = [];

    private Rma(
        RmaId id,
        TenantId tenantId,
        FirmId firmId,
        FinancialYearId financialYearId,
        string number,
        DateOnly date,
        RmaDirection direction,
        LedgerId partyLedgerId,
        WarehouseId warehouseId)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        FinancialYearId = financialYearId;
        Number = number;
        Date = date;
        Direction = direction;
        PartyLedgerId = partyLedgerId;
        WarehouseId = warehouseId;
        Status = RmaStatus.Requested;
        Reason = string.Empty;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private Rma()
    {
        Number = string.Empty;
        Reason = string.Empty;
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the year the return was numbered in.</summary>
    public FinancialYearId FinancialYearId { get; private set; }

    /// <summary>Gets the return number.</summary>
    public string Number { get; private set; }

    /// <summary>Gets the day it was asked for.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets which way the goods go back.</summary>
    public RmaDirection Direction { get; private set; }

    /// <summary>Gets the customer returning the goods, or the supplier they go back to.</summary>
    public LedgerId PartyLedgerId { get; private set; }

    /// <summary>Gets the godown the returned goods are kept in.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Gets the invoice the goods were sold or bought on, as the party quoted it.</summary>
    public string? InvoiceReference { get; private set; }

    /// <summary>Gets the sale the goods came from, where it is known.</summary>
    public SalesInvoiceId? SalesInvoiceId { get; private set; }

    /// <summary>Gets the purchase the goods came in on, where it is known.</summary>
    public PurchaseInvoiceId? PurchaseInvoiceId { get; private set; }

    /// <summary>Gets why the goods are going back.</summary>
    public string Reason { get; private set; }

    /// <summary>Gets a note.</summary>
    public string? Narration { get; private set; }

    /// <summary>Gets where the return stands.</summary>
    public RmaStatus Status { get; private set; }

    /// <summary>Gets what the inspection found.</summary>
    public RmaCondition? Condition { get; private set; }

    /// <summary>Gets the inspection notes.</summary>
    public string? InspectionNotes { get; private set; }

    /// <summary>Gets the day it was inspected.</summary>
    public DateOnly? InspectedOn { get; private set; }

    /// <summary>Gets the transfer that exchanged the goods, where they were replaced.</summary>
    public StockDocumentId? ReplacementDocumentId { get; private set; }

    /// <summary>Gets the credit note that refunded the customer.</summary>
    public SalesInvoiceId? CreditNoteId { get; private set; }

    /// <summary>Gets the debit note that charged the supplier back.</summary>
    public PurchaseInvoiceId? DebitNoteId { get; private set; }

    /// <summary>Gets the products going back.</summary>
    public IReadOnlyList<RmaLine> Lines => _lines.AsReadOnly();

    /// <summary>Gets the photographs kept as evidence.</summary>
    public IReadOnlyList<RmaPhoto> Photos => _photos.AsReadOnly();

    /// <summary>Gets what has happened to the return, oldest first.</summary>
    public IReadOnlyList<RmaEvent> Events => _events.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets whether what is going back may still be changed.</summary>
    public bool IsEditable => Status == RmaStatus.Requested;

    /// <summary>Gets whether the goods are in hand and waiting on an outcome.</summary>
    public bool AwaitsOutcome => Status is RmaStatus.Received or RmaStatus.UnderInspection;

    /// <summary>Gets whether nothing more will happen to it.</summary>
    public bool IsFinished => Status is RmaStatus.Closed or RmaStatus.Rejected or RmaStatus.Cancelled;

    /// <summary>Asks for a return.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="year">The year it is numbered in.</param>
    /// <param name="number">The number its series issued.</param>
    /// <param name="date">The day it was asked for.</param>
    /// <param name="direction">Which way the goods go back.</param>
    /// <param name="party">The customer returning them, or the supplier they go back to.</param>
    /// <param name="warehouse">The godown the returned goods are kept in.</param>
    /// <param name="actor">Who asked for it, for the history.</param>
    /// <returns>The return, or the reason it was refused.</returns>
    public static Result<Rma> Create(
        TenantId tenantId,
        FirmId firmId,
        FinancialYear year,
        string number,
        DateOnly date,
        RmaDirection direction,
        Ledger party,
        Warehouse warehouse,
        RmaActor actor)
    {
        ArgumentNullException.ThrowIfNull(year);
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(warehouse);
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<Rma>(Error.Validation("Rma.NumberRequired", "A return number is required."));
        }

        if (!Enum.IsDefined(direction))
        {
            return Result.Failure<Rma>(Error.Validation(
                "Rma.UnknownDirection", "Say whether a customer is returning goods or the firm is."));
        }

        LedgerKind expected = direction == RmaDirection.FromCustomer ? LedgerKind.Customer : LedgerKind.Supplier;

        if (party.FirmId != firmId || party.Kind != expected)
        {
            return Result.Failure<Rma>(Error.Validation(
                "Rma.WrongParty",
                direction == RmaDirection.FromCustomer
                    ? $"'{party.Name}' is not a customer of this firm."
                    : $"'{party.Name}' is not a supplier of this firm."));
        }

        if (warehouse.FirmId != firmId || warehouse.IsTransit)
        {
            return Result.Failure<Rma>(Error.Validation(
                "Rma.WrongGodown", "Returned goods are kept in one of this firm's own godowns."));
        }

        Rma rma = new(
            RmaId.NewId(), tenantId, firmId, year.Id, number.Trim(), date, direction, party.Id, warehouse.Id);

        rma.Record(actor, null);

        return Result.Success(rma);
    }

    /// <summary>Records why the goods are going back and what they were bought or sold on.</summary>
    /// <param name="reason">Why. Required.</param>
    /// <param name="narration">A note.</param>
    /// <param name="invoiceReference">The invoice as the party quoted it.</param>
    /// <param name="salesInvoice">The sale, for goods a customer returns.</param>
    /// <param name="purchaseInvoice">The purchase, for goods returned to a supplier.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Describe(
        string reason,
        string? narration,
        string? invoiceReference,
        SalesInvoiceId? salesInvoice,
        PurchaseInvoiceId? purchaseInvoice)
    {
        if (!IsEditable)
        {
            return Result.Failure(Locked());
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Rma.ReasonRequired", "Say why the goods are going back."));
        }

        if ((Direction == RmaDirection.FromCustomer && purchaseInvoice is not null)
            || (Direction == RmaDirection.ToSupplier && salesInvoice is not null))
        {
            return Result.Failure(Error.Validation(
                "Rma.WrongInvoice",
                "A customer's return names the sale; a return to a supplier names the purchase."));
        }

        Reason = Text(reason)!;
        Narration = Text(narration);
        InvoiceReference = Text(invoiceReference);
        SalesInvoiceId = salesInvoice;
        PurchaseInvoiceId = purchaseInvoice;

        return Result.Success();
    }

    /// <summary>Removes every product, ahead of them being entered again.</summary>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result ClearLines()
    {
        if (!IsEditable)
        {
            return Result.Failure(Locked());
        }

        _lines.Clear();

        return Result.Success();
    }

    /// <summary>Adds a product going back.</summary>
    /// <param name="product">The product.</param>
    /// <param name="quantity">How many, in its stock unit.</param>
    /// <param name="reason">What is wrong with these in particular.</param>
    /// <param name="serialNumber">The serial number or IMEI of the unit.</param>
    /// <param name="batch">The batch, for a product tracked in batches.</param>
    /// <returns>The line, or the reason it was refused.</returns>
    public Result<RmaLine> AddLine(
        Product product,
        decimal quantity,
        string? reason = null,
        string? serialNumber = null,
        Batch? batch = null)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (!IsEditable)
        {
            return Result.Failure<RmaLine>(Locked());
        }

        if (product.FirmId != FirmId || !product.IsStocked)
        {
            return Result.Failure<RmaLine>(Error.Validation(
                "Rma.NotGoods", $"'{product.Code}' is not goods this firm stocks, so it cannot go back."));
        }

        if (quantity <= 0m)
        {
            return Result.Failure<RmaLine>(Error.Validation(
                "Rma.QuantityInvalid", $"'{product.Code}' needs a quantity above zero."));
        }

        if (batch is not null && (!product.TracksBatches || batch.ProductId != product.Id))
        {
            return Result.Failure<RmaLine>(Error.Validation(
                "Rma.BatchWrong", $"That batch does not belong to '{product.Code}'."));
        }

        RmaLine line = new(
            RmaLineId.NewId(), TenantId, Id, product.Id, quantity, _lines.Count + 1, Text(reason),
            Short(serialNumber), batch?.Id);

        _lines.Add(line);

        return Result.Success(line);
    }

    /// <summary>Approves the return: the goods may come back.</summary>
    /// <param name="actor">Who approved it.</param>
    /// <param name="note">A note.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Approve(RmaActor actor, string? note)
    {
        if (Status != RmaStatus.Requested)
        {
            return Result.Failure(WrongStatus("approved"));
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(Error.BusinessRule(
                "Rma.NothingToReturn", $"Return '{Number}' has no products on it."));
        }

        return Move(RmaStatus.Approved, actor, note);
    }

    /// <summary>Refuses the return.</summary>
    /// <param name="actor">Who refused it.</param>
    /// <param name="reason">Why. Required.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Reject(RmaActor actor, string reason)
    {
        if (Status != RmaStatus.Requested)
        {
            return Result.Failure(WrongStatus("rejected"));
        }

        return string.IsNullOrWhiteSpace(reason)
            ? Result.Failure(Error.Validation("Rma.ReasonRequired", "Say why the return is refused."))
            : Move(RmaStatus.Rejected, actor, reason);
    }

    /// <summary>Withdraws the return before anything has come of it.</summary>
    /// <param name="actor">Who withdrew it.</param>
    /// <param name="reason">Why. Required.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Cancel(RmaActor actor, string reason)
    {
        if (Status is not (RmaStatus.Requested or RmaStatus.Approved or RmaStatus.Received or RmaStatus.UnderInspection))
        {
            return Result.Failure(WrongStatus("cancelled"));
        }

        return string.IsNullOrWhiteSpace(reason)
            ? Result.Failure(Error.Validation("Rma.ReasonRequired", "Say why the return is withdrawn."))
            : Move(RmaStatus.Cancelled, actor, reason);
    }

    /// <summary>Records that the goods have changed hands.</summary>
    /// <param name="actor">Who received or handed them over.</param>
    /// <param name="note">A note.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Receive(RmaActor actor, string? note) =>
        Status != RmaStatus.Approved
            ? Result.Failure(WrongStatus("received"))
            : Move(RmaStatus.Received, actor, note);

    /// <summary>Records what the inspection found.</summary>
    /// <param name="condition">The condition the goods are in.</param>
    /// <param name="notes">What was found.</param>
    /// <param name="on">The day of the inspection.</param>
    /// <param name="actor">Who inspected them.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Inspect(RmaCondition condition, string? notes, DateOnly on, RmaActor actor)
    {
        if (!AwaitsOutcome)
        {
            return Result.Failure(WrongStatus("inspected"));
        }

        if (!Enum.IsDefined(condition))
        {
            return Result.Failure(Error.Validation("Rma.UnknownCondition", "Choose the condition found."));
        }

        Condition = condition;
        InspectionNotes = Text(notes);
        InspectedOn = on;

        return Move(RmaStatus.UnderInspection, actor, InspectionNotes);
    }

    /// <summary>Keeps a photograph as evidence.</summary>
    /// <param name="content">The picture, as a data URL.</param>
    /// <param name="caption">What it shows.</param>
    /// <returns>The photograph, or the reason it was refused.</returns>
    public Result<RmaPhoto> AddPhoto(string content, string? caption)
    {
        if (IsFinished)
        {
            return Result.Failure<RmaPhoto>(WrongStatus("given photographs"));
        }

        if (_photos.Count >= MaximumPhotos)
        {
            return Result.Failure<RmaPhoto>(Error.BusinessRule(
                "Rma.TooManyPhotos", $"A return keeps at most {MaximumPhotos} photographs."));
        }

        if (string.IsNullOrWhiteSpace(content)
            || !content.StartsWith("data:image/", StringComparison.Ordinal)
            || content.Length > RmaPhoto.MaximumLength)
        {
            return Result.Failure<RmaPhoto>(Error.Validation(
                "Rma.PhotoInvalid", "A photograph is a picture of at most about half a megabyte."));
        }

        RmaPhoto photo = new(RmaPhotoId.NewId(), TenantId, Id, content, Short(caption));

        _photos.Add(photo);

        return Result.Success(photo);
    }

    /// <summary>Removes a photograph.</summary>
    /// <param name="id">The photograph.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result RemovePhoto(RmaPhotoId id)
    {
        if (IsFinished)
        {
            return Result.Failure(WrongStatus("changed"));
        }

        return _photos.RemoveAll(photo => photo.Id == id) == 0
            ? Result.Failure(Error.NotFound("Rma.PhotoNotFound", "No such photograph on this return."))
            : Result.Success();
    }

    /// <summary>Records that the goods were repaired and handed back.</summary>
    /// <param name="actor">Who recorded it.</param>
    /// <param name="note">What was done.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Repair(RmaActor actor, string? note) =>
        !AwaitsOutcome ? Result.Failure(WrongStatus("repaired")) : Move(RmaStatus.Repaired, actor, note);

    /// <summary>Records that the goods were exchanged.</summary>
    /// <param name="transfer">The transfer that moved the goods, where any moved.</param>
    /// <param name="actor">Who recorded it.</param>
    /// <param name="note">A note.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Replace(StockDocumentId? transfer, RmaActor actor, string? note)
    {
        if (!AwaitsOutcome)
        {
            return Result.Failure(WrongStatus("replaced"));
        }

        ReplacementDocumentId = transfer;

        return Move(RmaStatus.Replaced, actor, note);
    }

    /// <summary>Records the credit note that refunded a customer.</summary>
    /// <param name="creditNote">The credit note.</param>
    /// <param name="actor">Who raised it.</param>
    /// <param name="note">A note.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Refund(SalesInvoiceId creditNote, RmaActor actor, string? note)
    {
        if (!AwaitsOutcome || Direction != RmaDirection.FromCustomer)
        {
            return Result.Failure(WrongStatus("refunded with a credit note"));
        }

        CreditNoteId = creditNote;

        return Move(RmaStatus.Refunded, actor, note);
    }

    /// <summary>Records the debit note that charged a supplier back.</summary>
    /// <param name="debitNote">The debit note.</param>
    /// <param name="actor">Who raised it.</param>
    /// <param name="note">A note.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Refund(PurchaseInvoiceId debitNote, RmaActor actor, string? note)
    {
        if (!AwaitsOutcome || Direction != RmaDirection.ToSupplier)
        {
            return Result.Failure(WrongStatus("refunded with a debit note"));
        }

        DebitNoteId = debitNote;

        return Move(RmaStatus.Refunded, actor, note);
    }

    /// <summary>
    /// Takes back a refund whose credit or debit note was cancelled, so the return can be
    /// settled some other way.
    /// </summary>
    /// <param name="actor">Who noticed.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result ReopenRefund(RmaActor actor)
    {
        if (Status != RmaStatus.Refunded)
        {
            return Result.Failure(WrongStatus("reopened"));
        }

        CreditNoteId = null;
        DebitNoteId = null;

        return Move(RmaStatus.UnderInspection, actor, "Its note was cancelled.");
    }

    /// <summary>Closes the return.</summary>
    /// <param name="actor">Who closed it.</param>
    /// <param name="note">A note.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Close(RmaActor actor, string? note) =>
        Status is RmaStatus.Repaired or RmaStatus.Replaced or RmaStatus.Refunded
            ? Move(RmaStatus.Closed, actor, note)
            : Result.Failure(WrongStatus("closed"));

    private static string? Text(string? value) => Clip(value, MaximumTextLength);

    private static string? Short(string? value) => Clip(value, RmaLine.MaximumSerialLength);

    private static string? Clip(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();

        return trimmed.Length <= length ? trimmed : trimmed[..length];
    }

    private Result Move(RmaStatus status, RmaActor actor, string? note)
    {
        ArgumentNullException.ThrowIfNull(actor);

        Status = status;
        Record(actor, note);

        return Result.Success();
    }

    private void Record(RmaActor actor, string? note) =>
        _events.Add(new RmaEvent(
            RmaEventId.NewId(), TenantId, Id, Status, actor.At, actor.UserId, actor.Name, Text(note)));

    private Error Locked() => Error.BusinessRule(
        "Rma.Locked", $"Return '{Number}' is {Status}; what is going back can no longer change.");

    private Error WrongStatus(string what) => Error.BusinessRule(
        "Rma.WrongStatus", $"Return '{Number}' is {Status} and cannot be {what}.");
}

/// <summary>Who took a step on a return, and when, for its history.</summary>
/// <param name="UserId">The user.</param>
/// <param name="Name">Their name as it was then.</param>
/// <param name="At">When.</param>
public sealed record RmaActor(UserId UserId, string Name, DateTimeOffset At);

/// <summary>One product going back.</summary>
public sealed class RmaLine : Entity<RmaLineId>, ITenantScoped
{
    /// <summary>The longest a serial number may be.</summary>
    public const int MaximumSerialLength = 120;

    internal RmaLine(
        RmaLineId id,
        TenantId tenantId,
        RmaId rmaId,
        ProductId productId,
        decimal quantity,
        int lineNumber,
        string? reason,
        string? serialNumber,
        BatchId? batchId)
        : base(id)
    {
        TenantId = tenantId;
        RmaId = rmaId;
        ProductId = productId;
        Quantity = quantity;
        LineNumber = lineNumber;
        Reason = reason;
        SerialNumber = serialNumber;
        BatchId = batchId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private RmaLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the return.</summary>
    public RmaId RmaId { get; private set; }

    /// <summary>Gets the product.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets how many, in its stock unit.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the position on the return, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets what is wrong with these in particular.</summary>
    public string? Reason { get; private set; }

    /// <summary>Gets the serial number or IMEI of the unit.</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>Gets the batch, for a product tracked in batches.</summary>
    public BatchId? BatchId { get; private set; }
}

/// <summary>A photograph kept as evidence of the goods' condition.</summary>
public sealed class RmaPhoto : Entity<RmaPhotoId>, ITenantScoped
{
    /// <summary>The largest picture accepted, as the length of its data URL.</summary>
    public const int MaximumLength = 700_000;

    internal RmaPhoto(RmaPhotoId id, TenantId tenantId, RmaId rmaId, string content, string? caption)
        : base(id)
    {
        TenantId = tenantId;
        RmaId = rmaId;
        Content = content;
        Caption = caption;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private RmaPhoto() => Content = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the return.</summary>
    public RmaId RmaId { get; private set; }

    /// <summary>Gets the picture, as a data URL.</summary>
    public string Content { get; private set; }

    /// <summary>Gets what it shows.</summary>
    public string? Caption { get; private set; }
}

/// <summary>One step in a return's history.</summary>
public sealed class RmaEvent : Entity<RmaEventId>, ITenantScoped
{
    internal RmaEvent(
        RmaEventId id,
        TenantId tenantId,
        RmaId rmaId,
        RmaStatus status,
        DateTimeOffset at,
        UserId userId,
        string userName,
        string? note)
        : base(id)
    {
        TenantId = tenantId;
        RmaId = rmaId;
        Status = status;
        At = at;
        UserId = userId;
        UserName = userName;
        Note = note;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private RmaEvent() => UserName = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the return.</summary>
    public RmaId RmaId { get; private set; }

    /// <summary>Gets the status the step left it in.</summary>
    public RmaStatus Status { get; private set; }

    /// <summary>Gets when.</summary>
    public DateTimeOffset At { get; private set; }

    /// <summary>Gets who took the step.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets their name as it was then.</summary>
    public string UserName { get; private set; }

    /// <summary>Gets the note they left.</summary>
    public string? Note { get; private set; }
}
