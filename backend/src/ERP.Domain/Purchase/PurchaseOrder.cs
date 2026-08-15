using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Purchase;

/// <summary>Where a purchase order stands in its lifecycle.</summary>
/// <remarks>
/// Four states rather than three, because "finished" and "abandoned" are different facts
/// about an order and a report that could not tell them apart would be a report nobody
/// could act on.
/// </remarks>
public enum PurchaseOrderStatus
{
    /// <summary>Being entered. Editable, and nothing has been placed with anybody.</summary>
    Draft = 1,

    /// <summary>Placed with the supplier, and waiting to be filled.</summary>
    Confirmed = 2,

    /// <summary>Everything on it has been invoiced.</summary>
    Completed = 3,

    /// <summary>Closed with goods still owed, or cancelled before anything arrived.</summary>
    Cancelled = 4,
}

/// <summary>
/// A purchase order: what the firm asked a supplier for, and how much of it has arrived.
/// </summary>
/// <remarks>
/// <para>
/// The purchase side of §12.9's chain, and the mirror of <see cref="Sales.SalesOrder"/>
/// wherever the two are one thing pointed two ways. Its own aggregate rather than a third
/// kind of <see cref="PurchaseInvoice"/>, for the reason the sales order is its own: a
/// purchase invoice posts, receives stock, raises a debt and writes a journal, and an order
/// does none of those. Folded together, every one of those invariants would have to ask
/// first whether the document was the kind that moves anything.
/// </para>
/// <para>
/// <b>An order commits no money.</b> Nothing reaches the nominal ledger here and no bill is
/// raised, so a supplier's balance is what the firm has been invoiced rather than what it
/// has ordered. That is the same answer the sales side gave in the other direction on
/// 2026-08-13 - stock moves when a document posts and not before - and it is the reason a
/// committed-spend report would be built over the outstanding quantities this order carries
/// rather than out of the creditors ledger.
/// </para>
/// <para>
/// <b>One order becomes as many purchases as it takes.</b> Suppliers part-ship as a matter
/// of routine, rather more often than customers part-collect, so the outstanding quantity
/// per line is doing more work here than its opposite number does on a sales order. The
/// order completes itself when the last line is filled.
/// </para>
/// </remarks>
public sealed class PurchaseOrder : AggregateRoot<PurchaseOrderId>, IFirmScoped, IAuditable, ISoftDeletable
{
    /// <summary>The longest a narration or a reference may be.</summary>
    public const int MaximumNarrationLength = 500;

    private readonly List<PurchaseOrderLine> _lines = [];
    private readonly List<PurchaseOrderCharge> _charges = [];

    private PurchaseOrder(
        PurchaseOrderId id,
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYearId financialYearId,
        string number,
        DateOnly date,
        LedgerId supplierLedgerId,
        WarehouseId warehouseId,
        TaxMode mode,
        CurrencyCode currency,
        DateOnly? expectedOn)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        BranchId = branchId;
        FinancialYearId = financialYearId;
        Number = number;
        Date = date;
        SupplierLedgerId = supplierLedgerId;
        WarehouseId = warehouseId;
        Mode = mode;
        Currency = currency;
        ExpectedOn = expectedOn;
        Status = PurchaseOrderStatus.Draft;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseOrder() => Number = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the branch that raised it.</summary>
    public BranchId BranchId { get; private set; }

    /// <summary>Gets the financial year it falls in.</summary>
    public FinancialYearId FinancialYearId { get; private set; }

    /// <summary>Gets the order number.</summary>
    public string Number { get; private set; }

    /// <summary>Gets the date it was raised.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets when the supplier promised the goods.</summary>
    /// <remarks>
    /// The supplier's promise rather than the firm's, which is the difference from the same
    /// field on a sales order. It is what an overdue-purchase-orders report is read against,
    /// and the reason a buyer has anything to chase.
    /// </remarks>
    public DateOnly? ExpectedOn { get; private set; }

    /// <summary>Gets the supplier it was placed with.</summary>
    public LedgerId SupplierLedgerId { get; private set; }

    /// <summary>Gets the warehouse the goods are expected to arrive at.</summary>
    /// <remarks>
    /// Expected rather than committed. A purchase raised from this order may receive the
    /// goods somewhere else entirely if that is where the lorry is sent.
    /// </remarks>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Gets the tax mode the order was placed under.</summary>
    public TaxMode Mode { get; private set; }

    /// <summary>Gets the currency it is stated in.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Gets the supplier's own reference: their quotation number, usually.</summary>
    public string? ReferenceNumber { get; private set; }

    /// <summary>Gets the narration recorded against it.</summary>
    public string? Narration { get; private set; }

    /// <summary>Gets where the order stands.</summary>
    public PurchaseOrderStatus Status { get; private set; }

    /// <summary>Gets the instant it was confirmed.</summary>
    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

    /// <summary>Gets the user who confirmed it.</summary>
    public UserId? ConfirmedBy { get; private set; }

    /// <summary>Gets why it was closed or cancelled.</summary>
    public string? ClosureReason { get; private set; }

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>Gets the charges recorded beside the goods.</summary>
    public IReadOnlyList<PurchaseOrderCharge> Charges => _charges.AsReadOnly();

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

    /// <summary>Gets whether the order may still be changed.</summary>
    public bool IsEditable => Status == PurchaseOrderStatus.Draft;

    /// <summary>Gets whether a purchase may still be raised from it.</summary>
    public bool IsOpen => Status == PurchaseOrderStatus.Confirmed;

    /// <summary>Gets whether anything on it has been invoiced.</summary>
    public bool IsPartlyInvoiced => _lines.Exists(line => line.InvoicedQuantity > 0m);

    /// <summary>Gets the goods total, before tax and before charges.</summary>
    public Money Taxable => Sum(_lines.Select(line => line.TaxableAmount));

    /// <summary>Gets the tax expected on the goods.</summary>
    public Money Tax => Sum(_lines.Select(line => line.TaxAmount));

    /// <summary>Gets what the charges add, net of what they deduct.</summary>
    public Money ChargeTotal => Sum(_charges.Select(charge => charge.SignedAmount));

    /// <summary>Gets the order total before it is rounded.</summary>
    public Money GrossTotal => Taxable + Tax + ChargeTotal;

    /// <summary>Gets the rounding difference, to the currency's own precision.</summary>
    public Money RoundingDifference => GrossTotal.Rounded() - GrossTotal;

    /// <summary>Gets what the firm expects to be billed.</summary>
    public Money Total => GrossTotal + RoundingDifference;

    /// <summary>Starts a draft purchase order.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="branchId">The branch raising it.</param>
    /// <param name="financialYear">The year it falls in.</param>
    /// <param name="number">The number its series issued.</param>
    /// <param name="date">The date it was raised.</param>
    /// <param name="supplier">The supplier it is placed with.</param>
    /// <param name="warehouse">The warehouse the goods are expected at.</param>
    /// <param name="mode">The tax mode, defaulted from the firm's regime.</param>
    /// <param name="currency">The currency it is stated in.</param>
    /// <param name="expectedOn">When the supplier promised the goods.</param>
    /// <returns>The draft, or the reason it was refused.</returns>
    public static Result<PurchaseOrder> CreateDraft(
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYear financialYear,
        string number,
        DateOnly date,
        Ledger supplier,
        Warehouse warehouse,
        TaxMode mode,
        CurrencyCode currency,
        DateOnly? expectedOn = null)
    {
        ArgumentNullException.ThrowIfNull(financialYear);
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(warehouse);

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<PurchaseOrder>(Error.Validation(
                "PurchaseOrder.NumberRequired", "An order number is required."));
        }

        if (!Enum.IsDefined(mode))
        {
            return Result.Failure<PurchaseOrder>(Error.Validation(
                "PurchaseOrder.UnknownMode", $"'{mode}' is not a recognised tax mode."));
        }

        // Ordered from somebody who is not a supplier. The party is the one thing on an
        // order nobody re-reads before it goes out, and an order placed against a bank
        // account is one whose invoice will be refused later, by a different document.
        if (supplier.Kind != LedgerKind.Supplier)
        {
            return Result.Failure<PurchaseOrder>(Error.BusinessRule(
                "PurchaseOrder.NotASupplier", $"'{supplier.Name}' is not a supplier account."));
        }

        if (supplier.FirmId != firmId || warehouse.FirmId != firmId)
        {
            return Result.Failure<PurchaseOrder>(Error.Validation(
                "PurchaseOrder.NotInFirm",
                "The supplier and the warehouse must both belong to the selected firm."));
        }

        if (!supplier.IsActive)
        {
            return Result.Failure<PurchaseOrder>(Error.BusinessRule(
                "PurchaseOrder.SupplierWithdrawn",
                $"'{supplier.Name}' has been withdrawn from use."));
        }

        if (!warehouse.IsActive)
        {
            return Result.Failure<PurchaseOrder>(Error.BusinessRule(
                "PurchaseOrder.WarehouseWithdrawn",
                $"Warehouse '{warehouse.Name}' has been withdrawn from use."));
        }

        // An order dated inside a closed year is one nobody could invoice, because the
        // invoice would fall in the same year and be refused there instead - later, and
        // with a message about a different document.
        Result canPost = financialYear.CanPostOn(date);

        if (canPost.IsFailure)
        {
            return Result.Failure<PurchaseOrder>(canPost.Error);
        }

        // Goods promised before they were ordered is a typing mistake, and the one that
        // would otherwise show up as an order overdue on the day it was raised.
        return expectedOn is { } expected && expected < date
            ? Result.Failure<PurchaseOrder>(Error.Validation(
                "PurchaseOrder.ExpectedBeforeOrdered",
                "The expected date cannot fall before the order was raised."))
            : Result.Success(new PurchaseOrder(
                PurchaseOrderId.NewId(),
                tenantId,
                firmId,
                branchId,
                financialYear.Id,
                number.Trim(),
                date,
                supplier.Id,
                warehouse.Id,
                mode,
                currency,
                expectedOn));
    }

    /// <summary>Adds a line to a draft order.</summary>
    /// <param name="product">The product ordered.</param>
    /// <param name="unit">The unit the quantity is entered in.</param>
    /// <param name="quantity">How much, in that unit.</param>
    /// <param name="stockQuantity">The same quantity in the product's stock unit.</param>
    /// <param name="rate">What one entered unit was ordered at.</param>
    /// <param name="assessment">What the tax engine made of the line.</param>
    /// <param name="discount">What was agreed off the line before tax.</param>
    /// <returns>The line, or the reason it was refused.</returns>
    /// <remarks>
    /// The same consistency check a purchase invoice line makes, for the same reason: an
    /// order total its own lines contradict is a total somebody will argue about when the
    /// supplier's invoice does not match it. What it does not check is stock, because an
    /// order is by definition for goods the firm has not got.
    /// </remarks>
    public Result<PurchaseOrderLine> AddLine(
        Product product,
        UnitOfMeasure unit,
        decimal quantity,
        decimal stockQuantity,
        decimal rate,
        TaxAssessment assessment,
        decimal discount = 0m)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(assessment);

        if (!IsEditable)
        {
            return Result.Failure<PurchaseOrderLine>(Error.BusinessRule(
                "PurchaseOrder.NotEditable",
                $"Order '{Number}' is {Status} and can no longer be changed."));
        }

        if (product.FirmId != FirmId)
        {
            return Result.Failure<PurchaseOrderLine>(Error.Validation(
                "PurchaseOrder.ProductNotInFirm", $"'{product.Code}' belongs to another firm."));
        }

        if (quantity <= 0m || stockQuantity <= 0m)
        {
            return Result.Failure<PurchaseOrderLine>(Error.Validation(
                "PurchaseOrder.QuantityNotPositive",
                "An order line must be for a positive quantity."));
        }

        if (rate < 0m)
        {
            return Result.Failure<PurchaseOrderLine>(Error.Validation(
                "PurchaseOrder.RateNegative", "A rate cannot be negative."));
        }

        if (discount < 0m)
        {
            return Result.Failure<PurchaseOrderLine>(Error.Validation(
                "PurchaseOrder.DiscountNegative", "A discount cannot be negative."));
        }

        decimal gross = quantity * rate;

        if (discount > gross)
        {
            return Result.Failure<PurchaseOrderLine>(Error.Validation(
                "PurchaseOrder.DiscountExceedsLine",
                "A discount cannot be more than the line it comes off."));
        }

        if (assessment.TaxableAmount.Currency != Currency)
        {
            return Result.Failure<PurchaseOrderLine>(Error.Validation(
                "PurchaseOrder.CurrencyMismatch",
                "The tax was assessed in a different currency from the order."));
        }

        if (decimal.Round(assessment.TaxableAmount.Amount, 4)
            != decimal.Round(gross - discount, 4))
        {
            return Result.Failure<PurchaseOrderLine>(Error.Validation(
                "PurchaseOrder.TaxNotForThisLine",
                "The tax assessment does not match what this line comes to. Recompute it "
                + "against the rate and discount actually entered."));
        }

        PurchaseOrderLine line = new(
            PurchaseOrderLineId.NewId(),
            TenantId,
            Id,
            product.Id,
            unit.Id,
            quantity,
            stockQuantity,
            rate,
            discount,
            assessment,
            _lines.Count + 1);

        _lines.Add(line);

        return Result.Success(line);
    }

    /// <summary>Adds a charge recorded beside the goods.</summary>
    /// <param name="mapping">The charge, from the firm's matrix.</param>
    /// <param name="amount">What it comes to. Always positive; the mapping decides the sign.</param>
    /// <returns>The charge, or the reason it was refused.</returns>
    public Result<PurchaseOrderCharge> AddCharge(AdditionalLedger mapping, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (!IsEditable)
        {
            return Result.Failure<PurchaseOrderCharge>(Error.BusinessRule(
                "PurchaseOrder.NotEditable",
                $"Order '{Number}' is {Status} and can no longer be changed."));
        }

        if (mapping.FirmId != FirmId)
        {
            return Result.Failure<PurchaseOrderCharge>(Error.Validation(
                "PurchaseOrder.ChargeNotInFirm", "That charge belongs to another firm."));
        }

        if (mapping.Document != ChargeableDocument.PurchaseOrder)
        {
            return Result.Failure<PurchaseOrderCharge>(Error.Validation(
                "PurchaseOrder.ChargeNotForOrders",
                "That charge is mapped to another kind of document."));
        }

        if (!mapping.AppliesTo(Mode))
        {
            return Result.Failure<PurchaseOrderCharge>(Error.BusinessRule(
                "PurchaseOrder.ChargeNotInMode",
                $"That charge does not apply to a {Mode} order."));
        }

        if (amount <= 0m)
        {
            return Result.Failure<PurchaseOrderCharge>(Error.Validation(
                "PurchaseOrder.ChargeNotPositive",
                "A charge is entered as a positive amount; whether it adds or deducts is "
                + "decided by the charge itself."));
        }

        if (_charges.Exists(charge => charge.LedgerId == mapping.LedgerId))
        {
            return Result.Failure<PurchaseOrderCharge>(Error.BusinessRule(
                "PurchaseOrder.ChargeRepeated",
                "That charge is already on this order. Change the amount rather than "
                + "adding it twice."));
        }

        PurchaseOrderCharge added = new(
            PurchaseOrderChargeId.NewId(),
            TenantId,
            Id,
            mapping.LedgerId,
            Money.Of(amount, Currency),
            mapping.IsAddition);

        _charges.Add(added);

        return Result.Success(added);
    }

    /// <summary>Sets the descriptive fields.</summary>
    /// <param name="referenceNumber">The supplier's own reference.</param>
    /// <param name="narration">What is recorded against the order.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetDetails(string? referenceNumber, string? narration)
    {
        if (!IsEditable)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseOrder.NotEditable",
                $"Order '{Number}' is {Status} and can no longer be changed."));
        }

        ReferenceNumber = Trimmed(referenceNumber);
        Narration = Trimmed(narration);

        return Result.Success();
    }

    /// <summary>Confirms the order, so purchases may be raised from it.</summary>
    /// <param name="confirmedBy">The user confirming it.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// A separate step from entering, as posting is on an invoice. A draft is a buyer
    /// working out what to ask for and can be corrected; a confirmed order is something
    /// the firm has placed with somebody, which is why it stops being editable.
    /// </remarks>
    public Result Confirm(UserId confirmedBy, DateTimeOffset nowUtc)
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseOrder.NotDraft", $"Order '{Number}' is already {Status}."));
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseOrder.NoLines", $"Order '{Number}' has nothing on it."));
        }

        if (Total.Amount <= 0m)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseOrder.NothingToOrder",
                $"Order '{Number}' comes to nothing once its discounts and charges are "
                + "applied."));
        }

        Status = PurchaseOrderStatus.Confirmed;
        ConfirmedAtUtc = nowUtc;
        ConfirmedBy = confirmedBy;

        return Result.Success();
    }

    /// <summary>Records what a purchase raised from this order took off it.</summary>
    /// <param name="invoiced">How much of each line arrived, by line.</param>
    /// <returns>Success, or the first line that could not be filled.</returns>
    /// <remarks>
    /// Applied to every line or to none. A conversion that filled three lines of four and
    /// then met a quantity it could not take would leave an order claiming goods had
    /// arrived that no purchase carries, so the quantities are checked against what is
    /// outstanding before any of them is recorded.
    /// <para>
    /// The order completes itself when the last line is filled. Nobody has to remember to
    /// close it, which is the difference between an outstanding-orders report a buyer reads
    /// and one that fills up with orders finished months ago.
    /// </para>
    /// </remarks>
    public Result RecordInvoiced(IReadOnlyDictionary<PurchaseOrderLineId, decimal> invoiced)
    {
        ArgumentNullException.ThrowIfNull(invoiced);

        if (!IsOpen)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseOrder.NotOpen",
                $"Order '{Number}' is {Status}, so nothing can be invoiced against it."));
        }

        if (invoiced.Count == 0)
        {
            return Result.Failure(Error.Validation(
                "PurchaseOrder.NothingInvoiced",
                "A purchase raised from an order has to take something off it."));
        }

        // Checked in full before anything is changed, so a refusal leaves the order
        // exactly as it was.
        foreach ((PurchaseOrderLineId lineId, decimal quantity) in invoiced)
        {
            PurchaseOrderLine? line = _lines.Find(candidate => candidate.Id == lineId);

            if (line is null)
            {
                return Result.Failure(Error.NotFound(
                    "PurchaseOrder.LineNotFound", $"Order '{Number}' has no such line."));
            }

            if (quantity <= 0m)
            {
                return Result.Failure(Error.Validation(
                    "PurchaseOrder.InvoicedQuantityNotPositive",
                    $"Line {line.LineNumber} was invoiced for {quantity}."));
            }

            if (quantity > line.OutstandingQuantity)
            {
                return Result.Failure(Error.BusinessRule(
                    "PurchaseOrder.OverInvoiced",
                    $"Line {line.LineNumber} has {line.OutstandingQuantity} left to invoice "
                    + $"and {quantity} was asked for."));
            }
        }

        foreach ((PurchaseOrderLineId lineId, decimal quantity) in invoiced)
        {
            _lines.Find(candidate => candidate.Id == lineId)!.Invoice(quantity);
        }

        if (_lines.TrueForAll(line => line.IsFulfilled))
        {
            Status = PurchaseOrderStatus.Completed;
        }

        return Result.Success();
    }

    /// <summary>Puts back what a cancelled purchase had taken off the order.</summary>
    /// <param name="released">How much of each line comes back, by line.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// A completed order reopens when one of its purchases is cancelled, because the goods
    /// are owed again. It cannot reopen once somebody has closed it deliberately - that is
    /// a decision about the supplier rather than about the paperwork, and undoing it
    /// silently would put an order back in front of a buyer who was told to stop chasing it.
    /// </remarks>
    public Result ReleaseInvoiced(IReadOnlyDictionary<PurchaseOrderLineId, decimal> released)
    {
        ArgumentNullException.ThrowIfNull(released);

        if (Status is not (PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.Completed))
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseOrder.NotReopenable",
                $"Order '{Number}' is {Status} and will not take goods back."));
        }

        foreach ((PurchaseOrderLineId lineId, decimal quantity) in released)
        {
            _lines.Find(candidate => candidate.Id == lineId)?.ReleaseInvoiced(quantity);
        }

        if (Status == PurchaseOrderStatus.Completed && _lines.Exists(line => !line.IsFulfilled))
        {
            Status = PurchaseOrderStatus.Confirmed;
        }

        return Result.Success();
    }

    /// <summary>Closes an order that will not be filled, or cancels one that never started.</summary>
    /// <param name="reason">Why. Required, and kept on the order.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// One operation for both, because the difference is a fact about the order rather than
    /// a choice the caller makes: an order nothing has arrived against is cancelled, and one
    /// that has been part-filled is closed short. Both land in the same state and both keep
    /// the reason, which is what an outstanding-orders report needs to explain why a line
    /// stopped being owed.
    /// </remarks>
    public Result Close(string reason)
    {
        if (Status is PurchaseOrderStatus.Completed or PurchaseOrderStatus.Cancelled)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseOrder.AlreadyFinished", $"Order '{Number}' is already {Status}."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "PurchaseOrder.ClosureReasonRequired",
                "A reason is required when closing an order."));
        }

        Status = PurchaseOrderStatus.Cancelled;
        ClosureReason = reason.Trim();

        return Result.Success();
    }

    private Money Sum(IEnumerable<Money> amounts) =>
        amounts.Aggregate(Money.Zero(Currency), (running, amount) => running + amount);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
