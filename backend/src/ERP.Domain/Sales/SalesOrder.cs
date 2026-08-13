using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.Domain.Tenancy;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Sales;

/// <summary>Where a sales order stands in its lifecycle.</summary>
/// <remarks>
/// Four states rather than three, because "finished" and "abandoned" are different facts
/// about an order and a report that could not tell them apart would be a report nobody
/// could act on.
/// </remarks>
public enum SalesOrderStatus
{
    /// <summary>Being entered. Editable, and nobody has promised anything.</summary>
    Draft = 1,

    /// <summary>Agreed with the customer, and waiting to be filled.</summary>
    Confirmed = 2,

    /// <summary>Everything on it has been invoiced.</summary>
    Completed = 3,

    /// <summary>Closed with goods still owed, or cancelled before anything went out.</summary>
    Cancelled = 4,
}

/// <summary>
/// A sales order: what a customer asked for, and how much of it has gone out.
/// </summary>
/// <remarks>
/// <para>
/// The first link of §12.9's chain, and §12.2's <em>Create Invoice From</em>. Its own
/// aggregate rather than a third kind of <see cref="SalesInvoice"/>: an invoice posts,
/// moves stock, raises a debt and writes a journal, and an order does none of those. Folded
/// together, every one of those invariants would have to ask first whether the document was
/// the kind that moves anything, and the answer would be no more often than yes.
/// </para>
/// <para>
/// <b>An order reserves nothing.</b> The business's answer of 2026-08-13: stock moves when
/// an invoice posts and not before, so two orders can promise the same last unit and the
/// second invoice is the one that gets refused. That is a real limitation and it is the
/// cheap one - the alternative is orders that quietly starve the shop floor and need an
/// expiry discipline nobody has asked for. What the order does carry is the outstanding
/// quantity per line, which is what a shortage report would be built from.
/// </para>
/// <para>
/// <b>One order may become several invoices.</b> Each line records how much has been
/// invoiced, the order completes itself when every line is filled, and a part-filled order
/// that will not be finished is closed rather than cancelled - because goods have gone out
/// against it and a cancelled document that produced invoices is a document nobody can
/// reconcile.
/// </para>
/// </remarks>
public sealed class SalesOrder : AggregateRoot<SalesOrderId>, IFirmScoped, IAuditable, ISoftDeletable
{
    /// <summary>The longest a narration or a reference may be.</summary>
    public const int MaximumNarrationLength = 500;

    private readonly List<SalesOrderLine> _lines = [];
    private readonly List<SalesOrderCharge> _charges = [];

    private SalesOrder(
        SalesOrderId id,
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYearId financialYearId,
        string number,
        DateOnly date,
        LedgerId customerLedgerId,
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
        CustomerLedgerId = customerLedgerId;
        WarehouseId = warehouseId;
        Mode = mode;
        Currency = currency;
        ExpectedOn = expectedOn;
        Status = SalesOrderStatus.Draft;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesOrder() => Number = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the branch that took it.</summary>
    public BranchId BranchId { get; private set; }

    /// <summary>Gets the financial year it falls in.</summary>
    public FinancialYearId FinancialYearId { get; private set; }

    /// <summary>Gets the order number.</summary>
    public string Number { get; private set; }

    /// <summary>Gets the date it was taken.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets when the customer was told to expect the goods.</summary>
    public DateOnly? ExpectedOn { get; private set; }

    /// <summary>Gets the customer who placed it.</summary>
    public LedgerId CustomerLedgerId { get; private set; }

    /// <summary>Gets the warehouse it is expected to ship from.</summary>
    /// <remarks>
    /// Expected rather than committed. Nothing is held here, and an invoice raised from
    /// this order may ship from somewhere else entirely if that is where the goods turn
    /// out to be.
    /// </remarks>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Gets the tax mode the order was quoted under.</summary>
    public TaxMode Mode { get; private set; }

    /// <summary>Gets the currency it is stated in.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Gets the customer's own reference: their purchase order number, usually.</summary>
    public string? ReferenceNumber { get; private set; }

    /// <summary>Gets the narration recorded against it.</summary>
    public string? Narration { get; private set; }

    /// <summary>Gets where the order stands.</summary>
    public SalesOrderStatus Status { get; private set; }

    /// <summary>Gets the instant it was confirmed.</summary>
    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

    /// <summary>Gets the user who confirmed it.</summary>
    public UserId? ConfirmedBy { get; private set; }

    /// <summary>Gets why it was closed or cancelled.</summary>
    public string? ClosureReason { get; private set; }

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<SalesOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>Gets the charges quoted beside the goods.</summary>
    public IReadOnlyList<SalesOrderCharge> Charges => _charges.AsReadOnly();

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
    public bool IsEditable => Status == SalesOrderStatus.Draft;

    /// <summary>Gets whether an invoice may still be raised from it.</summary>
    public bool IsOpen => Status == SalesOrderStatus.Confirmed;

    /// <summary>Gets whether anything on it has been invoiced.</summary>
    public bool IsPartlyInvoiced => _lines.Exists(line => line.InvoicedQuantity > 0m);

    /// <summary>Gets the goods total, before tax and before charges.</summary>
    public Money Taxable => Sum(_lines.Select(line => line.TaxableAmount));

    /// <summary>Gets the tax quoted on the goods.</summary>
    public Money Tax => Sum(_lines.Select(line => line.TaxAmount));

    /// <summary>Gets what the charges add, net of what they deduct.</summary>
    public Money ChargeTotal => Sum(_charges.Select(charge => charge.SignedAmount));

    /// <summary>Gets the order total before it is rounded.</summary>
    public Money GrossTotal => Taxable + Tax + ChargeTotal;

    /// <summary>Gets the rounding difference, to the currency's own precision.</summary>
    public Money RoundingDifference => GrossTotal.Rounded() - GrossTotal;

    /// <summary>Gets what the customer was quoted.</summary>
    public Money Total => GrossTotal + RoundingDifference;

    /// <summary>Starts a draft sales order.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="branchId">The branch taking it.</param>
    /// <param name="financialYear">The year it falls in.</param>
    /// <param name="number">The number its series issued.</param>
    /// <param name="date">The date it was taken.</param>
    /// <param name="customer">The customer placing it.</param>
    /// <param name="warehouse">The warehouse it is expected to ship from.</param>
    /// <param name="mode">The tax mode, defaulted from the firm's regime.</param>
    /// <param name="currency">The currency it is stated in.</param>
    /// <param name="expectedOn">When the customer was told to expect the goods.</param>
    /// <returns>The draft, or the reason it was refused.</returns>
    public static Result<SalesOrder> CreateDraft(
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYear financialYear,
        string number,
        DateOnly date,
        Ledger customer,
        Warehouse warehouse,
        TaxMode mode,
        CurrencyCode currency,
        DateOnly? expectedOn = null)
    {
        ArgumentNullException.ThrowIfNull(financialYear);
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(warehouse);

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<SalesOrder>(Error.Validation(
                "SalesOrder.NumberRequired", "An order number is required."));
        }

        if (!Enum.IsDefined(mode))
        {
            return Result.Failure<SalesOrder>(Error.Validation(
                "SalesOrder.UnknownMode", $"'{mode}' is not a recognised tax mode."));
        }

        if (customer.Kind != LedgerKind.Customer)
        {
            return Result.Failure<SalesOrder>(Error.BusinessRule(
                "SalesOrder.NotACustomer", $"'{customer.Name}' is not a customer account."));
        }

        if (customer.FirmId != firmId || warehouse.FirmId != firmId)
        {
            return Result.Failure<SalesOrder>(Error.Validation(
                "SalesOrder.NotInFirm",
                "The customer and the warehouse must both belong to the selected firm."));
        }

        if (!customer.IsActive)
        {
            return Result.Failure<SalesOrder>(Error.BusinessRule(
                "SalesOrder.CustomerWithdrawn",
                $"'{customer.Name}' has been withdrawn from use."));
        }

        if (!warehouse.IsActive)
        {
            return Result.Failure<SalesOrder>(Error.BusinessRule(
                "SalesOrder.WarehouseWithdrawn",
                $"Warehouse '{warehouse.Name}' has been withdrawn from use."));
        }

        // An order dated inside a closed year is one nobody could invoice, because the
        // invoice would fall in the same year and be refused there instead - later, and
        // with a message about a different document.
        Result canPost = financialYear.CanPostOn(date);

        if (canPost.IsFailure)
        {
            return Result.Failure<SalesOrder>(canPost.Error);
        }

        // A delivery promised before the order was taken is a typing mistake, and the one
        // that would otherwise show up as an order overdue on the day it was entered.
        return expectedOn is { } expected && expected < date
            ? Result.Failure<SalesOrder>(Error.Validation(
                "SalesOrder.ExpectedBeforeOrdered",
                "The expected date cannot fall before the order was taken."))
            : Result.Success(new SalesOrder(
                SalesOrderId.NewId(),
                tenantId,
                firmId,
                branchId,
                financialYear.Id,
                number.Trim(),
                date,
                customer.Id,
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
    /// <param name="rate">What one entered unit is quoted at.</param>
    /// <param name="assessment">What the tax engine made of the line.</param>
    /// <param name="discount">What was taken off the line before tax.</param>
    /// <returns>The line, or the reason it was refused.</returns>
    /// <remarks>
    /// The same consistency check an invoice line makes, for the same reason: a quoted
    /// total its own lines contradict is a total somebody will argue about later. What it
    /// does not check is stock, because an order for goods the firm has not got yet is the
    /// ordinary case rather than the mistake.
    /// </remarks>
    public Result<SalesOrderLine> AddLine(
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
            return Result.Failure<SalesOrderLine>(Error.BusinessRule(
                "SalesOrder.NotEditable",
                $"Order '{Number}' is {Status} and can no longer be changed."));
        }

        if (product.FirmId != FirmId)
        {
            return Result.Failure<SalesOrderLine>(Error.Validation(
                "SalesOrder.ProductNotInFirm", $"'{product.Code}' belongs to another firm."));
        }

        if (quantity <= 0m || stockQuantity <= 0m)
        {
            return Result.Failure<SalesOrderLine>(Error.Validation(
                "SalesOrder.QuantityNotPositive",
                "An order line must be for a positive quantity."));
        }

        if (rate < 0m)
        {
            return Result.Failure<SalesOrderLine>(Error.Validation(
                "SalesOrder.RateNegative", "A rate cannot be negative."));
        }

        if (discount < 0m)
        {
            return Result.Failure<SalesOrderLine>(Error.Validation(
                "SalesOrder.DiscountNegative", "A discount cannot be negative."));
        }

        decimal gross = quantity * rate;

        if (discount > gross)
        {
            return Result.Failure<SalesOrderLine>(Error.Validation(
                "SalesOrder.DiscountExceedsLine",
                "A discount cannot be more than the line it comes off."));
        }

        if (assessment.TaxableAmount.Currency != Currency)
        {
            return Result.Failure<SalesOrderLine>(Error.Validation(
                "SalesOrder.CurrencyMismatch",
                "The tax was assessed in a different currency from the order."));
        }

        if (decimal.Round(assessment.TaxableAmount.Amount, 4)
            != decimal.Round(gross - discount, 4))
        {
            return Result.Failure<SalesOrderLine>(Error.Validation(
                "SalesOrder.TaxNotForThisLine",
                "The tax assessment does not match what this line comes to. Recompute it "
                + "against the rate and discount actually entered."));
        }

        SalesOrderLine line = new(
            SalesOrderLineId.NewId(),
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

    /// <summary>Adds a charge quoted beside the goods.</summary>
    /// <param name="mapping">The charge, from the firm's matrix.</param>
    /// <param name="amount">What it comes to. Always positive; the mapping decides the sign.</param>
    /// <returns>The charge, or the reason it was refused.</returns>
    public Result<SalesOrderCharge> AddCharge(AdditionalLedger mapping, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (!IsEditable)
        {
            return Result.Failure<SalesOrderCharge>(Error.BusinessRule(
                "SalesOrder.NotEditable",
                $"Order '{Number}' is {Status} and can no longer be changed."));
        }

        if (mapping.FirmId != FirmId)
        {
            return Result.Failure<SalesOrderCharge>(Error.Validation(
                "SalesOrder.ChargeNotInFirm", "That charge belongs to another firm."));
        }

        if (mapping.Document != ChargeableDocument.SalesOrder)
        {
            return Result.Failure<SalesOrderCharge>(Error.Validation(
                "SalesOrder.ChargeNotForOrders",
                "That charge is mapped to another kind of document."));
        }

        if (!mapping.AppliesTo(Mode))
        {
            return Result.Failure<SalesOrderCharge>(Error.BusinessRule(
                "SalesOrder.ChargeNotInMode",
                $"That charge does not apply to a {Mode} order."));
        }

        if (amount <= 0m)
        {
            return Result.Failure<SalesOrderCharge>(Error.Validation(
                "SalesOrder.ChargeNotPositive",
                "A charge is entered as a positive amount; whether it adds or deducts is "
                + "decided by the charge itself."));
        }

        if (_charges.Exists(charge => charge.LedgerId == mapping.LedgerId))
        {
            return Result.Failure<SalesOrderCharge>(Error.BusinessRule(
                "SalesOrder.ChargeRepeated",
                "That charge is already on this order. Change the amount rather than "
                + "adding it twice."));
        }

        SalesOrderCharge added = new(
            SalesOrderChargeId.NewId(),
            TenantId,
            Id,
            mapping.LedgerId,
            Money.Of(amount, Currency),
            mapping.IsAddition);

        _charges.Add(added);

        return Result.Success(added);
    }

    /// <summary>Sets the descriptive fields.</summary>
    /// <param name="referenceNumber">The customer's own reference.</param>
    /// <param name="narration">What is recorded against the order.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetDetails(string? referenceNumber, string? narration)
    {
        if (!IsEditable)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesOrder.NotEditable",
                $"Order '{Number}' is {Status} and can no longer be changed."));
        }

        ReferenceNumber = Trimmed(referenceNumber);
        Narration = Trimmed(narration);

        return Result.Success();
    }

    /// <summary>Confirms the order, so invoices may be raised from it.</summary>
    /// <param name="confirmedBy">The user confirming it.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// A separate step from entering, as posting is on an invoice. A draft is a
    /// conversation with a customer and can be corrected; a confirmed order is something
    /// the firm has agreed to, which is why it stops being editable and starts being
    /// something a warehouse can work from.
    /// </remarks>
    public Result Confirm(UserId confirmedBy, DateTimeOffset nowUtc)
    {
        if (Status != SalesOrderStatus.Draft)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesOrder.NotDraft", $"Order '{Number}' is already {Status}."));
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesOrder.NoLines", $"Order '{Number}' has nothing on it."));
        }

        if (Total.Amount <= 0m)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesOrder.NothingToQuote",
                $"Order '{Number}' comes to nothing once its discounts and charges are "
                + "applied."));
        }

        Status = SalesOrderStatus.Confirmed;
        ConfirmedAtUtc = nowUtc;
        ConfirmedBy = confirmedBy;

        return Result.Success();
    }

    /// <summary>Records what an invoice raised from this order took off it.</summary>
    /// <param name="invoiced">How much of each line went out, by line.</param>
    /// <returns>Success, or the first line that could not be filled.</returns>
    /// <remarks>
    /// Applied to every line or to none. A conversion that filled three lines of four and
    /// then met a quantity it could not take would leave an order claiming goods had gone
    /// out that no invoice carries, so the quantities are checked against what is
    /// outstanding before any of them is recorded.
    /// <para>
    /// The order completes itself when the last line is filled. Nobody has to remember to
    /// close it, which is the difference between an outstanding-orders report that is
    /// worth reading and one that fills up with orders finished months ago.
    /// </para>
    /// </remarks>
    public Result RecordInvoiced(IReadOnlyDictionary<SalesOrderLineId, decimal> invoiced)
    {
        ArgumentNullException.ThrowIfNull(invoiced);

        if (!IsOpen)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesOrder.NotOpen",
                $"Order '{Number}' is {Status}, so nothing can be invoiced against it."));
        }

        if (invoiced.Count == 0)
        {
            return Result.Failure(Error.Validation(
                "SalesOrder.NothingInvoiced",
                "An invoice raised from an order has to take something off it."));
        }

        // Checked in full before anything is changed, so a refusal leaves the order
        // exactly as it was.
        foreach ((SalesOrderLineId lineId, decimal quantity) in invoiced)
        {
            SalesOrderLine? line = _lines.Find(candidate => candidate.Id == lineId);

            if (line is null)
            {
                return Result.Failure(Error.NotFound(
                    "SalesOrder.LineNotFound",
                    $"Order '{Number}' has no such line."));
            }

            if (quantity <= 0m)
            {
                return Result.Failure(Error.Validation(
                    "SalesOrder.InvoicedQuantityNotPositive",
                    $"Line {line.LineNumber} was invoiced for {quantity}."));
            }

            if (quantity > line.OutstandingQuantity)
            {
                return Result.Failure(Error.BusinessRule(
                    "SalesOrder.OverInvoiced",
                    $"Line {line.LineNumber} has {line.OutstandingQuantity} left to invoice "
                    + $"and {quantity} was asked for."));
            }
        }

        foreach ((SalesOrderLineId lineId, decimal quantity) in invoiced)
        {
            _lines.Find(candidate => candidate.Id == lineId)!.Invoice(quantity);
        }

        if (_lines.TrueForAll(line => line.IsFulfilled))
        {
            Status = SalesOrderStatus.Completed;
        }

        return Result.Success();
    }

    /// <summary>Puts back what a cancelled invoice had taken off the order.</summary>
    /// <param name="released">How much of each line comes back, by line.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// A completed order reopens when one of its invoices is cancelled, because the goods
    /// are owed again. It cannot reopen once somebody has closed it deliberately - that is
    /// a decision about the customer rather than about the paperwork, and undoing it
    /// silently would put an order back in front of a warehouse that was told to stop.
    /// </remarks>
    public Result ReleaseInvoiced(IReadOnlyDictionary<SalesOrderLineId, decimal> released)
    {
        ArgumentNullException.ThrowIfNull(released);

        if (Status is not (SalesOrderStatus.Confirmed or SalesOrderStatus.Completed))
        {
            return Result.Failure(Error.BusinessRule(
                "SalesOrder.NotReopenable",
                $"Order '{Number}' is {Status} and will not take goods back."));
        }

        foreach ((SalesOrderLineId lineId, decimal quantity) in released)
        {
            _lines.Find(candidate => candidate.Id == lineId)?.ReleaseInvoiced(quantity);
        }

        if (Status == SalesOrderStatus.Completed && _lines.Exists(line => !line.IsFulfilled))
        {
            Status = SalesOrderStatus.Confirmed;
        }

        return Result.Success();
    }

    /// <summary>Closes an order that will not be finished, or cancels one that never started.</summary>
    /// <param name="reason">Why. Required, and kept on the order.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// One operation for both, because the difference is a fact about the order rather
    /// than a choice the caller makes: an order nothing has gone out against is cancelled,
    /// and one that has been part-filled is closed short. Both land in the same state and
    /// both keep the reason, which is what an outstanding-orders report needs to explain
    /// why a line stopped being owed.
    /// </remarks>
    public Result Close(string reason)
    {
        if (Status is SalesOrderStatus.Completed or SalesOrderStatus.Cancelled)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesOrder.AlreadyFinished", $"Order '{Number}' is already {Status}."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "SalesOrder.ClosureReasonRequired",
                "A reason is required when closing an order."));
        }

        Status = SalesOrderStatus.Cancelled;
        ClosureReason = reason.Trim();

        return Result.Success();
    }

    private Money Sum(IEnumerable<Money> amounts) =>
        amounts.Aggregate(Money.Zero(Currency), (running, amount) => running + amount);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
