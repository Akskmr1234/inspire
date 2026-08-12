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

/// <summary>Where a sales invoice stands in its lifecycle.</summary>
public enum SalesInvoiceStatus
{
    /// <summary>Being entered. Editable, and nothing has moved.</summary>
    Draft = 1,

    /// <summary>Posted: stock has left, a bill is outstanding, the books have it.</summary>
    Posted = 2,

    /// <summary>Reversed out, with the document retained.</summary>
    Cancelled = 3,
}

/// <summary>
/// A sale: what left the building, what it was taxed at, and what is owed for it.
/// </summary>
/// <remarks>
/// <para>
/// Section 12. The document and its lines and charges are one aggregate, for the same
/// reason a voucher and its lines are: an invoice that billed three of its four products
/// would be worse than one that billed none.
/// </para>
/// <para>
/// What this aggregate does <em>not</em> do is move anything. Posting it is a transition
/// here; the stock it issues, the bill it raises and the journal it writes are separate
/// aggregates moved by the application layer inside the same transaction - the standing
/// rule in this codebase, and it matters here because a line can be refused by a
/// position it has never seen and the refusal has to name the line.
/// </para>
/// <para>
/// Tax is computed by the tax engine and handed in per line, in the same way a stock
/// document is handed a converted quantity. The engine belongs to another aggregate and
/// answers a question about jurisdictions; this one records what it answered, so a
/// reprint of an old invoice shows the tax that was charged rather than the tax today's
/// rates would produce.
/// </para>
/// </remarks>
public sealed class SalesInvoice : AggregateRoot<SalesInvoiceId>, IFirmScoped, IAuditable, ISoftDeletable
{
    /// <summary>The longest a narration or a reference may be.</summary>
    public const int MaximumNarrationLength = 500;

    private readonly List<SalesInvoiceLine> _lines = [];
    private readonly List<SalesInvoiceCharge> _charges = [];

    private SalesInvoice(
        SalesInvoiceId id,
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYearId financialYearId,
        string number,
        DateOnly date,
        LedgerId customerLedgerId,
        WarehouseId warehouseId,
        TaxMode mode,
        CurrencyCode currency)
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
        Status = SalesInvoiceStatus.Draft;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesInvoice() => Number = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the branch that raised it.</summary>
    public BranchId BranchId { get; private set; }

    /// <summary>Gets the financial year it falls in.</summary>
    public FinancialYearId FinancialYearId { get; private set; }

    /// <summary>Gets the invoice number.</summary>
    public string Number { get; private set; }

    /// <summary>Gets the invoice date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the customer billed.</summary>
    public LedgerId CustomerLedgerId { get; private set; }

    /// <summary>Gets the warehouse the goods leave.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Gets the tax mode the invoice was entered under.</summary>
    /// <remarks>
    /// Defaulted from the firm's regime by the caller, which is the business's answer of
    /// 2026-08-10, and recorded here because a reprint has to show the basis the invoice
    /// was actually raised on.
    /// </remarks>
    public TaxMode Mode { get; private set; }

    /// <summary>Gets the currency the invoice is stated in.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Gets the customer's reference: their order number, usually.</summary>
    public string? ReferenceNumber { get; private set; }

    /// <summary>Gets the narration printed on it.</summary>
    public string? Narration { get; private set; }

    /// <summary>Gets where the invoice stands.</summary>
    public SalesInvoiceStatus Status { get; private set; }

    /// <summary>Gets the instant it was posted.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

    /// <summary>Gets the user who posted it.</summary>
    public UserId? PostedBy { get; private set; }

    /// <summary>Gets why it was cancelled.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Gets the issue that took the goods off the shelf.</summary>
    /// <remarks>
    /// <para>
    /// A sale moves stock by raising a stock document of its own - a material issue,
    /// numbered from its own series - rather than reaching into the positions directly.
    /// That is a deliberate choice with a visible consequence: every sale leaves two
    /// documents, and somebody reading a stock ledger sees the issue rather than the
    /// invoice.
    /// </para>
    /// <para>
    /// The alternative was to let a sales invoice move stock itself, which would mean
    /// the stock ledger no longer pointing at a single kind of document, and every rule
    /// the issue already enforces - batch positions, serial transitions, average
    /// costing, refusing to go negative - either reimplemented here or generalised
    /// across both. One audit trail for stock, with one kind of document in it, is worth
    /// more than one document per sale.
    /// </para>
    /// </remarks>
    public StockDocumentId? StockDocumentId { get; private set; }

    /// <summary>Gets the bill this invoice put into the customer's outstanding.</summary>
    public BillId? BillId { get; private set; }

    /// <summary>Gets the journal it raised in the nominal ledger.</summary>
    public VoucherId? JournalVoucherId { get; private set; }

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<SalesInvoiceLine> Lines => _lines.AsReadOnly();

    /// <summary>Gets the charges carried beside the goods.</summary>
    public IReadOnlyList<SalesInvoiceCharge> Charges => _charges.AsReadOnly();

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

    /// <summary>Gets whether the invoice may still be changed.</summary>
    public bool IsEditable => Status == SalesInvoiceStatus.Draft;

    /// <summary>Gets the goods total, before tax and before charges.</summary>
    public Money Taxable => Sum(_lines.Select(line => line.TaxableAmount));

    /// <summary>Gets the tax on the goods.</summary>
    public Money Tax => Sum(_lines.Select(line => line.TaxAmount));

    /// <summary>Gets what the charges add, net of what they deduct.</summary>
    public Money ChargeTotal => Sum(_charges.Select(charge => charge.SignedAmount));

    /// <summary>Gets the invoice total before it is rounded.</summary>
    public Money GrossTotal => Taxable + Tax + ChargeTotal;

    /// <summary>Gets the rounding difference, which the Round Off ledger takes.</summary>
    /// <remarks>
    /// <para>
    /// The business's answer of 2026-08-10: tax is computed per component at full
    /// precision, and only the total is rounded, once, at the end. Rounding each
    /// component would make the invoice disagree with the return produced from the same
    /// figures.
    /// </para>
    /// <para>
    /// To the currency's own precision, which is not always two places. A dinar invoice
    /// has three and rounding it to two would throw away a fils the customer is actually
    /// billed; a yen invoice has none and rounding it to two would leave fractions of a
    /// unit that does not subdivide. The difference is kept unrounded on purpose - it is
    /// the remainder that rounding produced, and rounding it again to the same scale
    /// would collapse it to nothing.
    /// </para>
    /// </remarks>
    public Money RoundingDifference => GrossTotal.Rounded() - GrossTotal;

    /// <summary>Gets what the customer owes.</summary>
    public Money Total => GrossTotal + RoundingDifference;

    /// <summary>Starts a draft invoice.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="branchId">The branch raising it.</param>
    /// <param name="financialYear">The year it falls in.</param>
    /// <param name="number">The number its series issued.</param>
    /// <param name="date">The invoice date.</param>
    /// <param name="customer">The customer billed.</param>
    /// <param name="warehouse">The warehouse the goods leave.</param>
    /// <param name="mode">The tax mode, defaulted from the firm's regime.</param>
    /// <param name="currency">The currency it is stated in.</param>
    /// <returns>The draft, or the reason it was refused.</returns>
    public static Result<SalesInvoice> CreateDraft(
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYear financialYear,
        string number,
        DateOnly date,
        Ledger customer,
        Warehouse warehouse,
        TaxMode mode,
        CurrencyCode currency)
    {
        ArgumentNullException.ThrowIfNull(financialYear);
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(warehouse);

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<SalesInvoice>(Error.Validation(
                "SalesInvoice.NumberRequired", "An invoice number is required."));
        }

        if (!Enum.IsDefined(mode))
        {
            return Result.Failure<SalesInvoice>(Error.Validation(
                "SalesInvoice.UnknownMode", $"'{mode}' is not a recognised tax mode."));
        }

        // Billed to somebody who is not a customer. A sale to a bank account or an
        // expense head is a mistake that would sit in the debtors report for ever, and
        // the party is the one thing on an invoice nobody re-reads before posting.
        if (customer.Kind != LedgerKind.Customer)
        {
            return Result.Failure<SalesInvoice>(Error.BusinessRule(
                "SalesInvoice.NotACustomer",
                $"'{customer.Name}' is not a customer account."));
        }

        if (customer.FirmId != firmId || warehouse.FirmId != firmId)
        {
            return Result.Failure<SalesInvoice>(Error.Validation(
                "SalesInvoice.NotInFirm",
                "The customer and the warehouse must both belong to the selected firm."));
        }

        if (!customer.IsActive)
        {
            return Result.Failure<SalesInvoice>(Error.BusinessRule(
                "SalesInvoice.CustomerWithdrawn",
                $"'{customer.Name}' has been withdrawn from use."));
        }

        if (!warehouse.IsActive)
        {
            return Result.Failure<SalesInvoice>(Error.BusinessRule(
                "SalesInvoice.WarehouseWithdrawn",
                $"Warehouse '{warehouse.Name}' has been withdrawn from use."));
        }

        Result canPost = financialYear.CanPostOn(date);

        return canPost.IsFailure
            ? Result.Failure<SalesInvoice>(canPost.Error)
            : Result.Success(new SalesInvoice(
                SalesInvoiceId.NewId(),
                tenantId,
                firmId,
                branchId,
                financialYear.Id,
                number.Trim(),
                date,
                customer.Id,
                warehouse.Id,
                mode,
                currency));
    }

    /// <summary>Adds a line to a draft invoice.</summary>
    /// <param name="product">The product sold.</param>
    /// <param name="unit">The unit the quantity is entered in.</param>
    /// <param name="quantity">How much, in that unit.</param>
    /// <param name="stockQuantity">The same quantity in the product's stock unit.</param>
    /// <param name="rate">What one entered unit is sold at.</param>
    /// <param name="assessment">What the tax engine made of the line.</param>
    /// <param name="batch">The batch sold, where the product is batched.</param>
    /// <param name="serials">The units sold, where the product is serialised.</param>
    /// <param name="discount">What was taken off the line before tax.</param>
    /// <returns>The line, or the reason it was refused.</returns>
    /// <remarks>
    /// The conversion and the tax both arrive computed, for the same reason: they belong
    /// to aggregates this one may not reach into. What it checks is that what arrived is
    /// consistent - a taxable amount that does not match the rate times the quantity less
    /// the discount would put a figure on a printed invoice that its own lines contradict.
    /// </remarks>
    public Result<SalesInvoiceLine> AddLine(
        Product product,
        UnitOfMeasure unit,
        decimal quantity,
        decimal stockQuantity,
        decimal rate,
        TaxAssessment assessment,
        Batch? batch = null,
        IReadOnlyCollection<SerialNumber>? serials = null,
        decimal discount = 0m)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(assessment);

        if (!IsEditable)
        {
            return Result.Failure<SalesInvoiceLine>(Error.BusinessRule(
                "SalesInvoice.NotEditable",
                $"Invoice '{Number}' is {Status} and can no longer be changed."));
        }

        if (product.FirmId != FirmId)
        {
            return Result.Failure<SalesInvoiceLine>(Error.Validation(
                "SalesInvoice.ProductNotInFirm", $"'{product.Code}' belongs to another firm."));
        }

        if (quantity <= 0m || stockQuantity <= 0m)
        {
            return Result.Failure<SalesInvoiceLine>(Error.Validation(
                "SalesInvoice.QuantityNotPositive",
                "A sales line must be for a positive quantity. Goods coming back are a "
                + "return, not a negative sale."));
        }

        if (rate < 0m)
        {
            return Result.Failure<SalesInvoiceLine>(Error.Validation(
                "SalesInvoice.RateNegative", "A rate cannot be negative."));
        }

        if (discount < 0m)
        {
            return Result.Failure<SalesInvoiceLine>(Error.Validation(
                "SalesInvoice.DiscountNegative",
                "A discount cannot be negative. A surcharge is a charge, not a discount."));
        }

        decimal gross = quantity * rate;

        if (discount > gross)
        {
            return Result.Failure<SalesInvoiceLine>(Error.Validation(
                "SalesInvoice.DiscountExceedsLine",
                "A discount cannot be more than the line it comes off."));
        }

        if (assessment.TaxableAmount.Currency != Currency)
        {
            return Result.Failure<SalesInvoiceLine>(Error.Validation(
                "SalesInvoice.CurrencyMismatch",
                "The tax was assessed in a different currency from the invoice."));
        }

        // The engine was asked about this line or about a different one, and only the
        // amount can tell the difference. A mismatch means the rate or the discount
        // changed after the tax was computed, which is a printed invoice whose figures
        // do not add up.
        if (decimal.Round(assessment.TaxableAmount.Amount, 4)
            != decimal.Round(gross - discount, 4))
        {
            return Result.Failure<SalesInvoiceLine>(Error.Validation(
                "SalesInvoice.TaxNotForThisLine",
                "The tax assessment does not match what this line comes to. Recompute it "
                + "against the rate and discount actually entered."));
        }

        Result paired = EnsureTrackingMatches(product, batch, serials, stockQuantity);

        if (paired.IsFailure)
        {
            return Result.Failure<SalesInvoiceLine>(paired.Error);
        }

        SalesInvoiceLine line = new(
            SalesInvoiceLineId.NewId(),
            TenantId,
            Id,
            product.Id,
            batch?.Id,
            unit.Id,
            quantity,
            stockQuantity,
            rate,
            discount,
            assessment,
            _lines.Count + 1);

        foreach (SerialNumber serial in serials ?? [])
        {
            line.AddSerial(serial.Id);
        }

        _lines.Add(line);

        return Result.Success(line);
    }

    /// <summary>Adds a charge beside the goods: freight, packing, a discount.</summary>
    /// <param name="mapping">The charge, from the firm's matrix.</param>
    /// <param name="amount">What it comes to. Always positive; the mapping decides the sign.</param>
    /// <returns>The charge, or the reason it was refused.</returns>
    /// <remarks>
    /// The amount is entered positive whichever way the charge moves the total, and the
    /// mapping's own flag decides whether it adds or deducts. A user who types a negative
    /// discount means the opposite of what they typed, and the two mistakes cancelling
    /// each other on some invoices and not others is worse than either alone.
    /// </remarks>
    public Result<SalesInvoiceCharge> AddCharge(AdditionalLedger mapping, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (!IsEditable)
        {
            return Result.Failure<SalesInvoiceCharge>(Error.BusinessRule(
                "SalesInvoice.NotEditable",
                $"Invoice '{Number}' is {Status} and can no longer be changed."));
        }

        if (mapping.FirmId != FirmId)
        {
            return Result.Failure<SalesInvoiceCharge>(Error.Validation(
                "SalesInvoice.ChargeNotInFirm", "That charge belongs to another firm."));
        }

        if (mapping.Document != ChargeableDocument.Sales)
        {
            return Result.Failure<SalesInvoiceCharge>(Error.Validation(
                "SalesInvoice.ChargeNotForSales",
                "That charge is mapped to another kind of document."));
        }

        if (!mapping.AppliesTo(Mode))
        {
            return Result.Failure<SalesInvoiceCharge>(Error.BusinessRule(
                "SalesInvoice.ChargeNotInMode",
                $"That charge does not apply to a {Mode} invoice."));
        }

        if (amount <= 0m)
        {
            return Result.Failure<SalesInvoiceCharge>(Error.Validation(
                "SalesInvoice.ChargeNotPositive",
                "A charge is entered as a positive amount; whether it adds or deducts is "
                + "decided by the charge itself."));
        }

        if (_charges.Exists(charge => charge.LedgerId == mapping.LedgerId))
        {
            return Result.Failure<SalesInvoiceCharge>(Error.BusinessRule(
                "SalesInvoice.ChargeRepeated",
                "That charge is already on this invoice. Change the amount rather than "
                + "adding it twice."));
        }

        SalesInvoiceCharge added = new(
            SalesInvoiceChargeId.NewId(),
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
    /// <param name="narration">What is printed on the invoice.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetDetails(string? referenceNumber, string? narration)
    {
        if (!IsEditable)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesInvoice.NotEditable",
                $"Invoice '{Number}' is {Status} and can no longer be changed."));
        }

        ReferenceNumber = Trimmed(referenceNumber);
        Narration = Trimmed(narration);

        return Result.Success();
    }

    /// <summary>Marks the invoice posted, once every invariant it owns is satisfied.</summary>
    /// <param name="postedBy">The user posting it.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Success, or the first invariant that fails.</returns>
    /// <remarks>
    /// The stock, the bill and the journal are the handler's work. This is the gate that
    /// decides whether they may happen: an invoice that cannot pass these checks never
    /// reaches a stock position.
    /// </remarks>
    public Result Post(UserId postedBy, DateTimeOffset nowUtc)
    {
        if (Status != SalesInvoiceStatus.Draft)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesInvoice.AlreadyPosted", $"Invoice '{Number}' is already {Status}."));
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesInvoice.NoLines", $"Invoice '{Number}' has nothing on it."));
        }

        // An invoice for nothing is not a sale. It would raise a bill of nothing, issue
        // stock against no consideration, and sit in a debtors report for ever as a
        // balance nobody can collect or write off.
        if (Total.Amount <= 0m)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesInvoice.NothingToBill",
                $"Invoice '{Number}' comes to nothing once its discounts and charges are "
                + "applied."));
        }

        Status = SalesInvoiceStatus.Posted;
        PostedAtUtc = nowUtc;
        PostedBy = postedBy;

        return Result.Success();
    }

    /// <summary>Names what the posting produced: the issue, the bill and the journal.</summary>
    /// <param name="stockDocumentId">The issue that took the goods off the shelf.</param>
    /// <param name="billId">The bill the customer now owes.</param>
    /// <param name="journalVoucherId">The journal raised in the nominal ledger.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// All three together, once, by the handler that made them, in the same transaction
    /// as the posting. Recorded rather than derived because each is what somebody
    /// reading the invoice afterwards actually wants to reach - the goods, the debt and
    /// the accounts - and none of the three can be found from the invoice by any other
    /// route.
    /// <para>
    /// An invoice that named one of them twice would be an invoice claiming two issues
    /// or two debts for one sale, so a second attempt is refused rather than allowed to
    /// overwrite the first.
    /// </para>
    /// </remarks>
    public Result RecordPosting(
        StockDocumentId stockDocumentId,
        BillId billId,
        VoucherId journalVoucherId)
    {
        if (Status != SalesInvoiceStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesInvoice.NotPosted",
                $"Invoice '{Number}' is {Status}, so it has produced nothing to record."));
        }

        if (StockDocumentId is not null || BillId is not null || JournalVoucherId is not null)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesInvoice.AlreadyRecorded",
                $"Invoice '{Number}' already names what its posting produced."));
        }

        StockDocumentId = stockDocumentId;
        BillId = billId;
        JournalVoucherId = journalVoucherId;

        return Result.Success();
    }

    /// <summary>Cancels a posted invoice.</summary>
    /// <param name="reason">Why. Required.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// The stock, the bill and the journal are put back by the handler. Goods a customer
    /// has actually taken away come back as a sales return instead, which is a document
    /// of its own: cancelling is for an invoice that should never have been raised, and
    /// returning is for one that should.
    /// </remarks>
    public Result Cancel(string reason)
    {
        if (Status != SalesInvoiceStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesInvoice.NotPosted",
                $"Only a posted invoice can be cancelled, and '{Number}' is {Status}."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "SalesInvoice.CancellationReasonRequired",
                "A reason is required when cancelling an invoice."));
        }

        Status = SalesInvoiceStatus.Cancelled;
        CancellationReason = reason.Trim();

        return Result.Success();
    }

    /// <summary>Checks the batch and units a line names against what the product needs.</summary>
    private static Result EnsureTrackingMatches(
        Product product,
        Batch? batch,
        IReadOnlyCollection<SerialNumber>? serials,
        decimal stockQuantity)
    {
        if (product.TracksBatches && batch is null)
        {
            return Result.Failure(Error.Validation(
                "SalesInvoice.BatchRequired",
                $"'{product.Code}' is tracked in batches, so the line must say which batch "
                + "was sold."));
        }

        if (batch is not null && (!product.TracksBatches || batch.ProductId != product.Id))
        {
            return Result.Failure(Error.Validation(
                "SalesInvoice.BatchWrong",
                $"That batch does not belong to '{product.Code}'."));
        }

        int offered = serials?.Count ?? 0;

        if (!product.TracksSerialNumbers)
        {
            return offered > 0
                ? Result.Failure(Error.Validation(
                    "SalesInvoice.SerialsNotTracked",
                    $"'{product.Code}' is not tracked by serial number."))
                : Result.Success();
        }

        // One number per unit sold, whole units only - the same rule the stock document
        // enforces, because it is the same fact about the goods.
        return stockQuantity != decimal.Truncate(stockQuantity) || offered != stockQuantity
            ? Result.Failure(Error.Validation(
                "SalesInvoice.SerialCountMismatch",
                $"'{product.Code}' sells {stockQuantity} units on this line, so that many "
                + $"serial numbers are needed and {offered} were given."))
            : Result.Success();
    }

    private Money Sum(IEnumerable<Money> amounts) =>
        amounts.Aggregate(Money.Zero(Currency), (running, amount) => running + amount);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
