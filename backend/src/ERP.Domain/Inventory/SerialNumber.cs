using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>Where one serialised unit stands.</summary>
/// <remarks>
/// The four states section 12.7 names. <c>Available</c> is called
/// <see cref="InStock"/> here and <c>Sold</c> is called <see cref="Issued"/>, because
/// this module knows only that a unit left: a sale is an issue with a customer on it,
/// and the sales document will supply the customer without needing a fifth state.
/// </remarks>
public enum SerialStatus
{
    /// <summary>On a shelf, and available to go out.</summary>
    InStock = 1,

    /// <summary>Gone out - sold, consumed, or written off.</summary>
    Issued = 2,

    /// <summary>Sent back to the supplier it came from. Out of stock, and final.</summary>
    ReturnedToSupplier = 3,

    /// <summary>Back from a customer, and on a shelf again.</summary>
    ReturnedFromCustomer = 4,

    /// <summary>
    /// Written down on a document that has not put it into stock: a draft, or one that
    /// was posted and then cancelled.
    /// </summary>
    /// <remarks>
    /// A fifth state the specification does not name, and it earns its place. The other
    /// four describe where a real unit is; this one describes a number somebody has
    /// entered against goods that have not arrived - or have un-arrived. Leaving such a
    /// unit <see cref="InStock"/> would offer a draft's units for sale; deleting the row
    /// on cancellation would lose the trail of a receipt that was posted and reversed.
    /// </remarks>
    Recorded = 5,
}

/// <summary>
/// One physical unit of a serialised product, tracked by the number on its case.
/// </summary>
/// <remarks>
/// <para>
/// Section 12.7. A batch says which lot goods came from; a serial says <em>which
/// one</em> - the handset with this IMEI, the compressor with this plate. That is what
/// a warranty claim, a service job, and a recall all start from, and none of them can
/// be answered by a quantity.
/// </para>
/// <para>
/// One row per unit, and the row is the unit: where it is, what it cost, how long its
/// warranty runs, and which document last moved it. There is no quantity here, because
/// a serial is always exactly one - which is also why the number of serials a document
/// line names has to equal the quantity that line moves, and why serialised quantities
/// must be whole.
/// </para>
/// <para>
/// Serials do not carry the valuation. What the goods are worth is the business of
/// <see cref="StockBalance"/> and, where the product is batched, of
/// <see cref="BatchBalance"/>; the cost held here is what this unit came in at, kept so
/// the margin on a serialised sale can be measured against the actual unit rather than
/// against an average. Making it a third valuation layer would be a third figure that
/// could disagree with the other two.
/// </para>
/// </remarks>
public sealed class SerialNumber : AggregateRoot<SerialNumberId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a serial number may be.</summary>
    /// <remarks>
    /// Long enough for an IMEI with a check digit, a manufacturer's plate number, and
    /// the hyphenated forms both are copied down in.
    /// </remarks>
    public const int MaximumNumberLength = 60;

    private SerialNumber(
        SerialNumberId id,
        TenantId tenantId,
        FirmId firmId,
        ProductId productId,
        BatchId? batchId,
        string number)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        ProductId = productId;
        BatchId = batchId;
        Number = number;
        Status = SerialStatus.Recorded;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SerialNumber() => Number = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the product this is a unit of.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the batch it arrived in, where the product is also batched.</summary>
    /// <remarks>
    /// Independent of serial tracking rather than exclusive with it. A handset arrives
    /// in a batch and still has an IMEI of its own, and the service module is built on
    /// being able to find the one unit.
    /// </remarks>
    public BatchId? BatchId { get; private set; }

    /// <summary>Gets the number on the unit, unique within the product.</summary>
    public string Number { get; private set; }

    /// <summary>Gets where the unit stands.</summary>
    public SerialStatus Status { get; private set; }

    /// <summary>Gets the warehouse holding it, or null once it has left.</summary>
    public WarehouseId? WarehouseId { get; private set; }

    /// <summary>Gets what this unit cost when it came in.</summary>
    public decimal UnitCost { get; private set; }

    /// <summary>Gets the date the warranty on this unit runs to.</summary>
    /// <remarks>
    /// Held per unit rather than per product, because it is a fact about the unit: two
    /// of the same model received six months apart are under warranty for different
    /// lengths of time, and a service desk asked "is this one covered" cannot answer
    /// from the model. A warranty master that supplies the term is a master this system
    /// does not have yet; until it does, the term arrives with the goods.
    /// </remarks>
    public DateOnly? WarrantyUntil { get; private set; }

    /// <summary>Gets the date the unit was taken into stock.</summary>
    public DateOnly? ReceivedOn { get; private set; }

    /// <summary>Gets the date it last left stock.</summary>
    public DateOnly? IssuedOn { get; private set; }

    /// <summary>Gets the document that first took this unit into stock.</summary>
    /// <remarks>
    /// Kept apart from <see cref="LastDocumentId"/> because cancelling a receipt has to
    /// know whether <em>this</em> is the receipt that created the unit. A unit received
    /// once and transferred twice has had three documents move it, and only the first
    /// can be undone by removing it from the books.
    /// </remarks>
    public StockDocumentId? OriginDocumentId { get; private set; }

    /// <summary>Gets the document that last moved it.</summary>
    /// <remarks>
    /// One reference rather than a history, because the stock ledger is the history and
    /// it already carries every movement. This is the pointer a screen follows when
    /// somebody asks what happened to this unit most recently.
    /// </remarks>
    public StockDocumentId? LastDocumentId { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Gets whether the unit is on a shelf and may go out.</summary>
    /// <remarks>
    /// Two of the four states mean "on a shelf". A unit back from a customer is
    /// available again - section 12.7 says so plainly - and the state is kept distinct
    /// only so that a screen can say where it has been.
    /// </remarks>
    public bool IsAvailable =>
        Status is SerialStatus.InStock or SerialStatus.ReturnedFromCustomer;

    /// <summary>Writes a serialised unit down against the document bringing it in.</summary>
    /// <remarks>
    /// Recorded rather than in stock. The document that names it may still be a draft,
    /// and a draft moves nothing: offering a draft's units for sale would be the serial
    /// equivalent of a draft receipt raising a stock position. Posting the document
    /// calls <see cref="TakeIntoStock"/>, which is when the unit becomes real.
    /// </remarks>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="product">The product, which must be tracked by serial number.</param>
    /// <param name="number">The number on the unit.</param>
    /// <param name="warehouseId">Where it was taken in.</param>
    /// <param name="receivedOn">The date it was taken in.</param>
    /// <param name="documentId">The document taking it in.</param>
    /// <param name="unitCost">What it cost. May be zero where nothing was stated.</param>
    /// <param name="batch">The batch it arrived in, where the product is batched.</param>
    /// <param name="warrantyUntil">The date its warranty runs to.</param>
    /// <returns>The unit, or the reason it was refused.</returns>
    public static Result<SerialNumber> Receive(
        TenantId tenantId,
        FirmId firmId,
        Product product,
        string number,
        WarehouseId warehouseId,
        DateOnly receivedOn,
        StockDocumentId documentId,
        decimal unitCost = 0m,
        Batch? batch = null,
        DateOnly? warrantyUntil = null)
    {
        ArgumentNullException.ThrowIfNull(product);

        // Refused rather than tolerated, for the same reason a batch of an unbatched
        // product is: a serial nothing consults is a unit recorded in a place no
        // screen looks, beside a quantity that already counts it.
        if (!product.TracksSerialNumbers)
        {
            return Result.Failure<SerialNumber>(Error.BusinessRule(
                "Serial.NotTracked",
                $"'{product.Code}' is not tracked by serial number. Turn serial tracking "
                + "on for the product before recording units of it."));
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<SerialNumber>(Error.Validation(
                "Serial.NumberRequired", "A serial number is required."));
        }

        string trimmed = number.Trim().ToUpperInvariant();

        if (trimmed.Length > MaximumNumberLength)
        {
            return Result.Failure<SerialNumber>(Error.Validation(
                "Serial.NumberTooLong",
                $"A serial number cannot exceed {MaximumNumberLength} characters."));
        }

        if (unitCost < 0m)
        {
            return Result.Failure<SerialNumber>(Error.Validation(
                "Serial.CostNegative", "A unit cannot have cost less than nothing."));
        }

        if (batch is not null && batch.ProductId != product.Id)
        {
            return Result.Failure<SerialNumber>(Error.Validation(
                "Serial.BatchWrongProduct",
                $"Batch '{batch.Number}' is a batch of another product."));
        }

        return new SerialNumber(
            SerialNumberId.NewId(), tenantId, firmId, product.Id, batch?.Id, trimmed)
        {
            WarehouseId = warehouseId,
            ReceivedOn = receivedOn,
            UnitCost = unitCost,
            WarrantyUntil = warrantyUntil,
            OriginDocumentId = documentId,
            LastDocumentId = documentId,
        };
    }

    /// <summary>Puts a recorded unit onto a shelf, when its document posts.</summary>
    /// <param name="warehouseId">Where it went in.</param>
    /// <param name="receivedOn">The date it went in.</param>
    /// <param name="documentId">The document putting it there.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result TakeIntoStock(
        WarehouseId warehouseId,
        DateOnly receivedOn,
        StockDocumentId documentId)
    {
        if (Status != SerialStatus.Recorded)
        {
            return Result.Failure(Error.BusinessRule(
                "Serial.AlreadyInStock",
                $"Serial '{Number}' is {Describe(Status)} and cannot be taken in again. "
                + "A unit that has come back is recorded as a return, not as a receipt."));
        }

        Status = SerialStatus.InStock;
        WarehouseId = warehouseId;
        ReceivedOn = receivedOn;
        LastDocumentId = documentId;

        return Result.Success();
    }

    /// <summary>Records the unit going out.</summary>
    /// <param name="issuedOn">The date it left.</param>
    /// <param name="documentId">The document that took it out.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// A unit that has already gone cannot go again, and that refusal is the whole
    /// point of tracking serials on the way out: section 12.7 says a sold serial never
    /// reappears, and the only way to keep that promise is to refuse the second sale
    /// rather than to hope nobody attempts one.
    /// </remarks>
    public Result Issue(DateOnly issuedOn, StockDocumentId documentId)
    {
        if (!IsAvailable)
        {
            return Result.Failure(Error.BusinessRule(
                "Serial.NotAvailable",
                $"Serial '{Number}' is {Describe(Status)} and cannot go out again."));
        }

        Status = SerialStatus.Issued;
        WarehouseId = null;
        IssuedOn = issuedOn;
        LastDocumentId = documentId;

        return Result.Success();
    }

    /// <summary>Moves the unit to another warehouse.</summary>
    /// <param name="warehouseId">Where it went.</param>
    /// <param name="documentId">The transfer that moved it.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result TransferTo(WarehouseId warehouseId, StockDocumentId documentId)
    {
        if (!IsAvailable)
        {
            return Result.Failure(Error.BusinessRule(
                "Serial.NotAvailable",
                $"Serial '{Number}' is {Describe(Status)} and is not on a shelf to move."));
        }

        if (WarehouseId == warehouseId)
        {
            return Result.Failure(Error.Validation(
                "Serial.SameWarehouse",
                $"Serial '{Number}' is already in that warehouse."));
        }

        WarehouseId = warehouseId;
        LastDocumentId = documentId;

        return Result.Success();
    }

    /// <summary>Records the unit going back to the supplier it came from.</summary>
    /// <param name="returnedOn">The date it went back.</param>
    /// <param name="documentId">The document that sent it.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Final. A unit sent back to its supplier is out of the firm's stock and does not
    /// come round again: if the supplier replaces it, what arrives is a different unit
    /// with a different number, and recording it as this one returning would put a
    /// warranty and a service history on the wrong machine.
    /// </remarks>
    public Result ReturnToSupplier(DateOnly returnedOn, StockDocumentId documentId)
    {
        if (!IsAvailable)
        {
            return Result.Failure(Error.BusinessRule(
                "Serial.NotAvailable",
                $"Serial '{Number}' is {Describe(Status)} and cannot be sent back."));
        }

        Status = SerialStatus.ReturnedToSupplier;
        WarehouseId = null;
        IssuedOn = returnedOn;
        LastDocumentId = documentId;

        return Result.Success();
    }

    /// <summary>Takes a unit back from a customer, onto a shelf again.</summary>
    /// <param name="warehouseId">Where it was taken back in.</param>
    /// <param name="returnedOn">The date it came back.</param>
    /// <param name="documentId">The document that took it back.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Only a unit that went out can come back. A serial still on the shelf being
    /// "returned" means somebody has the wrong number in front of them, and accepting
    /// it would quietly overwrite where the unit actually is.
    /// </remarks>
    public Result ReturnFromCustomer(
        WarehouseId warehouseId,
        DateOnly returnedOn,
        StockDocumentId documentId)
    {
        if (Status != SerialStatus.Issued)
        {
            return Result.Failure(Error.BusinessRule(
                "Serial.NotIssued",
                $"Serial '{Number}' is {Describe(Status)}, so it cannot come back from a "
                + "customer."));
        }

        Status = SerialStatus.ReturnedFromCustomer;
        WarehouseId = warehouseId;
        ReceivedOn = returnedOn;
        IssuedOn = null;
        LastDocumentId = documentId;

        return Result.Success();
    }

    /// <summary>Undoes the receipt that brought this unit onto the books.</summary>
    /// <param name="documentId">The document being cancelled.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Only by the document that created the unit, and only while the unit is still on
    /// the shelf it was received onto. A receipt whose goods have since been issued
    /// cannot be un-received - the unit is with somebody else - and the refusal points
    /// at the same answer the stock position gives for the same situation: post an
    /// adjustment, because that says what actually happened.
    /// </remarks>
    public Result UndoReceipt(StockDocumentId documentId)
    {
        if (OriginDocumentId != documentId)
        {
            return Result.Failure(Error.BusinessRule(
                "Serial.NotItsOrigin",
                $"Serial '{Number}' was not brought in by that document."));
        }

        if (!IsAvailable)
        {
            return Result.Failure(Error.BusinessRule(
                "Serial.NotAvailable",
                $"Serial '{Number}' is {Describe(Status)}, so the receipt that brought it "
                + "in can no longer be cancelled. Post an adjustment instead."));
        }

        Status = SerialStatus.Recorded;
        WarehouseId = null;
        ReceivedOn = null;
        LastDocumentId = documentId;

        return Result.Success();
    }

    /// <summary>Puts a unit back on the shelf a cancelled issue took it from.</summary>
    /// <param name="warehouseId">The warehouse it left.</param>
    /// <param name="documentId">The document being cancelled.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result UndoIssue(WarehouseId warehouseId, StockDocumentId documentId)
    {
        if (Status is not (SerialStatus.Issued or SerialStatus.ReturnedToSupplier))
        {
            return Result.Failure(Error.BusinessRule(
                "Serial.NotIssued",
                $"Serial '{Number}' is {Describe(Status)}, so there is no issue of it to "
                + "undo."));
        }

        Status = SerialStatus.InStock;
        WarehouseId = warehouseId;
        IssuedOn = null;
        LastDocumentId = documentId;

        return Result.Success();
    }

    /// <summary>Sets how long this unit is under warranty.</summary>
    /// <param name="warrantyUntil">The date it runs to, or null to clear it.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Correctable after the fact, like a batch's expiry date and for the same reason:
    /// it is transcribed off a docket, and the alternative to correcting it is writing
    /// the unit off and receiving it again.
    /// </remarks>
    public Result SetWarranty(DateOnly? warrantyUntil)
    {
        if (warrantyUntil is { } until && ReceivedOn is { } received && until < received)
        {
            return Result.Failure(Error.Validation(
                "Serial.WarrantyBeforeReceipt",
                "A warranty cannot have run out before the unit was received."));
        }

        WarrantyUntil = warrantyUntil;

        return Result.Success();
    }

    /// <summary>Says whether the unit is still under warranty on a date.</summary>
    /// <param name="on">The date to judge it on.</param>
    /// <returns><see langword="true"/> if the warranty still covers it.</returns>
    /// <remarks>
    /// Inclusive of the last day, as printed on the docket. A unit with no warranty
    /// date recorded is not under warranty: an unknown term is not a term, and treating
    /// a blank as cover would have a service desk giving away repairs.
    /// </remarks>
    public bool IsUnderWarrantyOn(DateOnly on) =>
        WarrantyUntil is { } until && on <= until;

    private static string Describe(SerialStatus status) => status switch
    {
        SerialStatus.InStock => "in stock",
        SerialStatus.Issued => "already gone out",
        SerialStatus.ReturnedToSupplier => "back with the supplier",
        SerialStatus.ReturnedFromCustomer => "back from a customer and in stock",
        SerialStatus.Recorded => "written down but not in stock",
        _ => status.ToString(),
    };
}
