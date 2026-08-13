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

/// <summary>Which way a purchase document runs.</summary>
/// <remarks>
/// One document type rather than two, as on the sales side and for the same reasons. A
/// purchase and a debit note have the same shape - lines, tax per component, charges, a
/// rounded total - and differ only in which way the goods and the money move. Amounts stay
/// positive on both; the kind decides whether the goods are arriving or going back.
/// </remarks>
public enum PurchaseDocumentKind
{
    /// <summary>Goods arriving, and a debt to the supplier for them.</summary>
    Invoice = 1,

    /// <summary>Goods going back, and a debit against what the supplier is owed.</summary>
    Return = 2,
}

/// <summary>Where a purchase invoice stands in its lifecycle.</summary>
public enum PurchaseInvoiceStatus
{
    /// <summary>Being entered. Editable, and nothing has moved.</summary>
    Draft = 1,

    /// <summary>Posted: stock has arrived, the supplier is owed, the books have it.</summary>
    Posted = 2,

    /// <summary>Reversed out, with the document retained.</summary>
    Cancelled = 3,
}

/// <summary>
/// A purchase: what arrived, what it cost, what tax was charged on it, and what is owed.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="Sales.SalesInvoice"/>. The specification gives purchase no
/// section of its own - it appears throughout §7, §9, §10 and §12 as the counterpart of
/// sales, and §18's open questions never separated them - so this follows the sales
/// module's shape wherever the two are one thing pointed two ways. Its own aggregate
/// rather than a direction flag on the sales one, which is the business's answer of
/// 2026-08-13: the two documents read the same at a distance and diverge in every detail
/// that matters - a sale selects goods that exist and a purchase brings goods into
/// existence, a sale is numbered by the firm and a purchase carries the supplier's number
/// as well, a sale credits revenue and a purchase debits a clearing account. Sharing them
/// would mean a document where half the fields are always null.
/// </para>
/// <para>
/// This aggregate moves nothing. Posting is a transition here; the receipt that brings the
/// stock in, the bill the supplier is owed and the journal are separate aggregates moved
/// by the application layer inside one transaction.
/// </para>
/// <para>
/// Tax arrives assessed, as it does on a sale, and is recorded per head. What is recorded
/// is the input tax the supplier charged, which is the figure the reclaim half of a VAT or
/// GST return is built from - so it is kept as charged rather than as today's rates would
/// recompute it.
/// </para>
/// </remarks>
public sealed class PurchaseInvoice
    : AggregateRoot<PurchaseInvoiceId>, IFirmScoped, IAuditable, ISoftDeletable
{
    /// <summary>The longest a narration or a reference may be.</summary>
    public const int MaximumNarrationLength = 500;

    private readonly List<PurchaseInvoiceLine> _lines = [];
    private readonly List<PurchaseInvoiceCharge> _charges = [];

    private PurchaseInvoice(
        PurchaseInvoiceId id,
        TenantId tenantId,
        FirmId firmId,
        BranchId branchId,
        FinancialYearId financialYearId,
        PurchaseDocumentKind kind,
        string number,
        DateOnly date,
        LedgerId supplierLedgerId,
        WarehouseId warehouseId,
        TaxMode mode,
        CurrencyCode currency,
        PurchaseInvoiceId? returnsInvoiceId)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        BranchId = branchId;
        FinancialYearId = financialYearId;
        Kind = kind;
        Number = number;
        Date = date;
        SupplierLedgerId = supplierLedgerId;
        WarehouseId = warehouseId;
        Mode = mode;
        Currency = currency;
        ReturnsInvoiceId = returnsInvoiceId;
        Status = PurchaseInvoiceStatus.Draft;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseInvoice() => Number = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the branch that raised it.</summary>
    public BranchId BranchId { get; private set; }

    /// <summary>Gets the financial year it falls in.</summary>
    public FinancialYearId FinancialYearId { get; private set; }

    /// <summary>Gets which way this document runs: a purchase, or goods going back.</summary>
    public PurchaseDocumentKind Kind { get; private set; }

    /// <summary>Gets whether this document sends goods back rather than buying them.</summary>
    public bool IsReturn => Kind == PurchaseDocumentKind.Return;

    /// <summary>Gets the purchase this return is against, where it names one.</summary>
    /// <remarks>
    /// Optional, for the same reason a sales return's is: goods go back to a supplier
    /// without the original paperwork to hand often enough that refusing would leave a
    /// storekeeper unable to record what has just left the yard. Where it is named, the
    /// debit can be set against the bill the purchase raised instead of floating on the
    /// supplier's account.
    /// </remarks>
    public PurchaseInvoiceId? ReturnsInvoiceId { get; private set; }

    /// <summary>Gets the firm's own number for the document.</summary>
    public string Number { get; private set; }

    /// <summary>Gets the date the firm booked it on.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the supplier billed by.</summary>
    public LedgerId SupplierLedgerId { get; private set; }

    /// <summary>Gets the warehouse the goods arrive at.</summary>
    public WarehouseId WarehouseId { get; private set; }

    /// <summary>Gets the tax mode the purchase was entered under.</summary>
    public TaxMode Mode { get; private set; }

    /// <summary>Gets the currency the purchase is stated in.</summary>
    public CurrencyCode Currency { get; private set; }

    /// <summary>Gets the supplier's own invoice number.</summary>
    /// <remarks>
    /// Carried, unlike a sale's reference, because it is not a convenience. Input tax is
    /// only reclaimable against a tax invoice the supplier issued, and both a VAT return
    /// and a GSTR-2 line are reported against the supplier's number and date rather than
    /// against whatever the firm numbered its own entry.
    /// </remarks>
    public string? SupplierInvoiceNumber { get; private set; }

    /// <summary>Gets the date on the supplier's invoice.</summary>
    public DateOnly? SupplierInvoiceDate { get; private set; }

    /// <summary>Gets the narration recorded against it.</summary>
    public string? Narration { get; private set; }

    /// <summary>Gets where the purchase stands.</summary>
    public PurchaseInvoiceStatus Status { get; private set; }

    /// <summary>Gets the instant it was posted.</summary>
    public DateTimeOffset? PostedAtUtc { get; private set; }

    /// <summary>Gets the user who posted it.</summary>
    public UserId? PostedBy { get; private set; }

    /// <summary>Gets why it was cancelled.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Gets the receipt that put the goods on the shelf.</summary>
    /// <remarks>
    /// A purchase brings stock in by raising a stock document of its own - a material
    /// receipt, numbered from its own series - rather than reaching into positions
    /// directly. The same choice the sales side made, for the same reason: one audit trail
    /// for stock, with one kind of document in it.
    /// </remarks>
    public StockDocumentId? StockDocumentId { get; private set; }

    /// <summary>Gets the bill this purchase put into the supplier's outstanding.</summary>
    public BillId? BillId { get; private set; }

    /// <summary>Gets the journal it raised in the nominal ledger.</summary>
    public VoucherId? JournalVoucherId { get; private set; }

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<PurchaseInvoiceLine> Lines => _lines.AsReadOnly();

    /// <summary>Gets the charges carried beside the goods.</summary>
    public IReadOnlyList<PurchaseInvoiceCharge> Charges => _charges.AsReadOnly();

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

    /// <summary>Gets whether the purchase may still be changed.</summary>
    public bool IsEditable => Status == PurchaseInvoiceStatus.Draft;

    /// <summary>Gets the goods total, before tax and before charges.</summary>
    public Money Taxable => Sum(_lines.Select(line => line.TaxableAmount));

    /// <summary>Gets the tax on the goods.</summary>
    public Money Tax => Sum(_lines.Select(line => line.TaxAmount));

    /// <summary>Gets what the charges add, net of what they deduct.</summary>
    public Money ChargeTotal => Sum(_charges.Select(charge => charge.SignedAmount));

    /// <summary>Gets the purchase total before it is rounded.</summary>
    public Money GrossTotal => Taxable + Tax + ChargeTotal;

    /// <summary>Gets the rounding difference, which the Round Off ledger takes.</summary>
    /// <remarks>
    /// To the currency's own precision, which is not always two places, and kept unrounded
    /// because it is the remainder rounding produced. The same rule as a sale's, for the
    /// same reason: rounding each component would make the document disagree with the
    /// return built from the same figures.
    /// </remarks>
    public Money RoundingDifference => GrossTotal.Rounded() - GrossTotal;

    /// <summary>Gets what the supplier is owed.</summary>
    public Money Total => GrossTotal + RoundingDifference;

    /// <summary>Starts a draft purchase document.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="branchId">The branch raising it.</param>
    /// <param name="financialYear">The year it falls in.</param>
    /// <param name="number">The number its series issued.</param>
    /// <param name="date">The date the firm books it on.</param>
    /// <param name="supplier">The supplier billing.</param>
    /// <param name="warehouse">The warehouse the goods arrive at.</param>
    /// <param name="mode">The tax mode, defaulted from the firm's regime.</param>
    /// <param name="currency">The currency it is stated in.</param>
    /// <param name="kind">Whether goods are arriving or going back.</param>
    /// <param name="returnsInvoiceId">The purchase a return is against, where it names one.</param>
    /// <returns>The draft, or the reason it was refused.</returns>
    public static Result<PurchaseInvoice> CreateDraft(
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
        PurchaseDocumentKind kind = PurchaseDocumentKind.Invoice,
        PurchaseInvoiceId? returnsInvoiceId = null)
    {
        ArgumentNullException.ThrowIfNull(financialYear);
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(warehouse);

        if (string.IsNullOrWhiteSpace(number))
        {
            return Result.Failure<PurchaseInvoice>(Error.Validation(
                "PurchaseInvoice.NumberRequired", "A document number is required."));
        }

        if (!Enum.IsDefined(mode))
        {
            return Result.Failure<PurchaseInvoice>(Error.Validation(
                "PurchaseInvoice.UnknownMode", $"'{mode}' is not a recognised tax mode."));
        }

        if (!Enum.IsDefined(kind))
        {
            return Result.Failure<PurchaseInvoice>(Error.Validation(
                "PurchaseInvoice.UnknownKind",
                $"'{kind}' is not a kind of purchase document."));
        }

        // A purchase that says which purchase it returns is a document confused about what
        // it is, and the confusion reaches the accounts: the posting reads the kind to
        // decide which way the goods and the money move.
        if (kind == PurchaseDocumentKind.Invoice && returnsInvoiceId is not null)
        {
            return Result.Failure<PurchaseInvoice>(Error.Validation(
                "PurchaseInvoice.NotAReturn",
                "Only a return may name the purchase it is against."));
        }

        // Bought from somebody who is not a supplier. A purchase booked against a bank
        // account or an expense head would sit in the creditors report for ever, and the
        // party is the one thing on a purchase nobody re-reads before posting.
        if (supplier.Kind != LedgerKind.Supplier)
        {
            return Result.Failure<PurchaseInvoice>(Error.BusinessRule(
                "PurchaseInvoice.NotASupplier",
                $"'{supplier.Name}' is not a supplier account."));
        }

        if (supplier.FirmId != firmId || warehouse.FirmId != firmId)
        {
            return Result.Failure<PurchaseInvoice>(Error.Validation(
                "PurchaseInvoice.NotInFirm",
                "The supplier and the warehouse must both belong to the selected firm."));
        }

        if (!supplier.IsActive)
        {
            return Result.Failure<PurchaseInvoice>(Error.BusinessRule(
                "PurchaseInvoice.SupplierWithdrawn",
                $"'{supplier.Name}' has been withdrawn from use."));
        }

        if (!warehouse.IsActive)
        {
            return Result.Failure<PurchaseInvoice>(Error.BusinessRule(
                "PurchaseInvoice.WarehouseWithdrawn",
                $"Warehouse '{warehouse.Name}' has been withdrawn from use."));
        }

        Result canPost = financialYear.CanPostOn(date);

        return canPost.IsFailure
            ? Result.Failure<PurchaseInvoice>(canPost.Error)
            : Result.Success(new PurchaseInvoice(
                PurchaseInvoiceId.NewId(),
                tenantId,
                firmId,
                branchId,
                financialYear.Id,
                kind,
                number.Trim(),
                date,
                supplier.Id,
                warehouse.Id,
                mode,
                currency,
                returnsInvoiceId));
    }

    /// <summary>Adds a line to a draft purchase.</summary>
    /// <param name="product">The product bought.</param>
    /// <param name="unit">The unit the quantity is entered in.</param>
    /// <param name="quantity">How much, in that unit.</param>
    /// <param name="stockQuantity">The same quantity in the product's stock unit.</param>
    /// <param name="rate">What one entered unit cost.</param>
    /// <param name="assessment">What the tax engine made of the line.</param>
    /// <param name="batchNumber">The batch it arrives in, where the product is batched.</param>
    /// <param name="expiresOn">When that batch expires, where the supplier stated it.</param>
    /// <param name="serialNumbers">The units arriving, where the product is serialised.</param>
    /// <param name="discount">What was taken off the line before tax.</param>
    /// <returns>The line, or the reason it was refused.</returns>
    /// <remarks>
    /// The conversion and the tax both arrive computed, for the same reason they do on a
    /// sale: they belong to aggregates this one may not reach into. What it checks is that
    /// what arrived is consistent with the rate, the quantity and the discount actually
    /// entered.
    /// </remarks>
    public Result<PurchaseInvoiceLine> AddLine(
        Product product,
        UnitOfMeasure unit,
        decimal quantity,
        decimal stockQuantity,
        decimal rate,
        TaxAssessment assessment,
        string? batchNumber = null,
        DateOnly? expiresOn = null,
        IReadOnlyCollection<string>? serialNumbers = null,
        decimal discount = 0m)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(assessment);

        if (!IsEditable)
        {
            return Result.Failure<PurchaseInvoiceLine>(Error.BusinessRule(
                "PurchaseInvoice.NotEditable",
                $"Purchase '{Number}' is {Status} and can no longer be changed."));
        }

        if (product.FirmId != FirmId)
        {
            return Result.Failure<PurchaseInvoiceLine>(Error.Validation(
                "PurchaseInvoice.ProductNotInFirm",
                $"'{product.Code}' belongs to another firm."));
        }

        if (quantity <= 0m || stockQuantity <= 0m)
        {
            return Result.Failure<PurchaseInvoiceLine>(Error.Validation(
                "PurchaseInvoice.QuantityNotPositive",
                "A purchase line must be for a positive quantity. Goods going back are a "
                + "return, not a negative purchase."));
        }

        if (rate < 0m)
        {
            return Result.Failure<PurchaseInvoiceLine>(Error.Validation(
                "PurchaseInvoice.RateNegative", "A rate cannot be negative."));
        }

        if (discount < 0m)
        {
            return Result.Failure<PurchaseInvoiceLine>(Error.Validation(
                "PurchaseInvoice.DiscountNegative",
                "A discount cannot be negative. A surcharge is a charge, not a discount."));
        }

        decimal gross = quantity * rate;

        if (discount > gross)
        {
            return Result.Failure<PurchaseInvoiceLine>(Error.Validation(
                "PurchaseInvoice.DiscountExceedsLine",
                "A discount cannot be more than the line it comes off."));
        }

        if (assessment.TaxableAmount.Currency != Currency)
        {
            return Result.Failure<PurchaseInvoiceLine>(Error.Validation(
                "PurchaseInvoice.CurrencyMismatch",
                "The tax was assessed in a different currency from the purchase."));
        }

        // The engine was asked about this line or about a different one, and only the
        // amount can tell the difference. A mismatch means the rate or the discount
        // changed after the tax was computed, which is input tax reclaimed against a
        // figure the document itself contradicts.
        if (decimal.Round(assessment.TaxableAmount.Amount, 4)
            != decimal.Round(gross - discount, 4))
        {
            return Result.Failure<PurchaseInvoiceLine>(Error.Validation(
                "PurchaseInvoice.TaxNotForThisLine",
                "The tax assessment does not match what this line comes to. Recompute it "
                + "against the rate and discount actually entered."));
        }

        Result paired = EnsureTrackingMatches(
            product, batchNumber, expiresOn, serialNumbers, stockQuantity);

        if (paired.IsFailure)
        {
            return Result.Failure<PurchaseInvoiceLine>(paired.Error);
        }

        PurchaseInvoiceLine line = new(
            PurchaseInvoiceLineId.NewId(),
            TenantId,
            Id,
            product.Id,
            unit.Id,
            quantity,
            stockQuantity,
            rate,
            discount,
            assessment,
            _lines.Count + 1,
            Trimmed(batchNumber),
            expiresOn);

        foreach (string serial in serialNumbers ?? [])
        {
            line.AddSerial(serial.Trim());
        }

        _lines.Add(line);

        return Result.Success(line);
    }

    /// <summary>Adds a charge beside the goods: freight, insurance, a discount.</summary>
    /// <param name="mapping">The charge, from the firm's matrix.</param>
    /// <param name="amount">What it comes to. Always positive; the mapping decides the sign.</param>
    /// <returns>The charge, or the reason it was refused.</returns>
    /// <remarks>
    /// <para>
    /// The amount is entered positive whichever way the charge moves the total, and the
    /// mapping's own flag decides whether it adds or deducts.
    /// </para>
    /// <para>
    /// A return takes the same mappings as a purchase, which is what the sales side does
    /// and is the reason the matrix's <c>PurchaseReturn</c> member goes unread here: a firm
    /// that has set up freight for its purchases should not have to set it up a second time
    /// to put it on the one that goes back.
    /// </para>
    /// </remarks>
    public Result<PurchaseInvoiceCharge> AddCharge(AdditionalLedger mapping, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (!IsEditable)
        {
            return Result.Failure<PurchaseInvoiceCharge>(Error.BusinessRule(
                "PurchaseInvoice.NotEditable",
                $"Purchase '{Number}' is {Status} and can no longer be changed."));
        }

        if (mapping.FirmId != FirmId)
        {
            return Result.Failure<PurchaseInvoiceCharge>(Error.Validation(
                "PurchaseInvoice.ChargeNotInFirm", "That charge belongs to another firm."));
        }

        if (mapping.Document != ChargeableDocument.Purchase)
        {
            return Result.Failure<PurchaseInvoiceCharge>(Error.Validation(
                "PurchaseInvoice.ChargeNotForPurchases",
                "That charge is mapped to another kind of document."));
        }

        if (!mapping.AppliesTo(Mode))
        {
            return Result.Failure<PurchaseInvoiceCharge>(Error.BusinessRule(
                "PurchaseInvoice.ChargeNotInMode",
                $"That charge does not apply to a {Mode} purchase."));
        }

        if (amount <= 0m)
        {
            return Result.Failure<PurchaseInvoiceCharge>(Error.Validation(
                "PurchaseInvoice.ChargeNotPositive",
                "A charge is entered as a positive amount; whether it adds or deducts is "
                + "decided by the charge itself."));
        }

        if (_charges.Exists(charge => charge.LedgerId == mapping.LedgerId))
        {
            return Result.Failure<PurchaseInvoiceCharge>(Error.BusinessRule(
                "PurchaseInvoice.ChargeRepeated",
                "That charge is already on this purchase. Change the amount rather than "
                + "adding it twice."));
        }

        PurchaseInvoiceCharge added = new(
            PurchaseInvoiceChargeId.NewId(),
            TenantId,
            Id,
            mapping.LedgerId,
            Money.Of(amount, Currency),
            mapping.IsAddition);

        _charges.Add(added);

        return Result.Success(added);
    }

    /// <summary>Records the supplier's own invoice and the narration.</summary>
    /// <param name="supplierInvoiceNumber">The number printed on the supplier's invoice.</param>
    /// <param name="supplierInvoiceDate">The date printed on it.</param>
    /// <param name="narration">What is recorded against the entry.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// A date without a number is refused. On its own it is a fact about a document nobody
    /// can identify, and it would reach a return as a reclaim against an invoice that
    /// cannot be produced if the claim is questioned.
    /// </remarks>
    public Result SetSupplierDocument(
        string? supplierInvoiceNumber,
        DateOnly? supplierInvoiceDate,
        string? narration)
    {
        if (!IsEditable)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseInvoice.NotEditable",
                $"Purchase '{Number}' is {Status} and can no longer be changed."));
        }

        string? number = Trimmed(supplierInvoiceNumber);

        if (number is null && supplierInvoiceDate is not null)
        {
            return Result.Failure(Error.Validation(
                "PurchaseInvoice.SupplierNumberRequired",
                "A supplier invoice date needs the number it belongs to."));
        }

        SupplierInvoiceNumber = number;
        SupplierInvoiceDate = supplierInvoiceDate;
        Narration = Trimmed(narration);

        return Result.Success();
    }

    /// <summary>Marks the purchase posted, once every invariant it owns is satisfied.</summary>
    /// <param name="postedBy">The user posting it.</param>
    /// <param name="nowUtc">The current instant.</param>
    /// <returns>Success, or the first invariant that fails.</returns>
    public Result Post(UserId postedBy, DateTimeOffset nowUtc)
    {
        if (Status != PurchaseInvoiceStatus.Draft)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseInvoice.AlreadyPosted",
                $"Purchase '{Number}' is already {Status}."));
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseInvoice.NoLines", $"Purchase '{Number}' has nothing on it."));
        }

        // A purchase for nothing is not a purchase. It would raise a bill of nothing, put
        // stock in at no cost, and leave a creditor balance nobody can pay or write off.
        if (Total.Amount <= 0m)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseInvoice.NothingToBill",
                $"Purchase '{Number}' comes to nothing once its discounts and charges are "
                + "applied."));
        }

        Status = PurchaseInvoiceStatus.Posted;
        PostedAtUtc = nowUtc;
        PostedBy = postedBy;

        return Result.Success();
    }

    /// <summary>Names what the posting produced: the receipt, the bill and the journal.</summary>
    /// <param name="stockDocumentId">The stock document the goods moved on.</param>
    /// <param name="billId">
    /// The bill now owed to the supplier, where one was raised. A return raises none - it
    /// debits an existing debt rather than creating one.
    /// </param>
    /// <param name="journalVoucherId">The journal raised in the nominal ledger.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// All three together, once, by the handler that made them, in the same transaction as
    /// the posting. A purchase that named one of them twice would be claiming two receipts
    /// or two debts for one delivery, so a second attempt is refused rather than allowed
    /// to overwrite the first.
    /// </remarks>
    public Result RecordPosting(
        StockDocumentId stockDocumentId,
        BillId? billId,
        VoucherId journalVoucherId)
    {
        if (Status != PurchaseInvoiceStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseInvoice.NotPosted",
                $"Purchase '{Number}' is {Status}, so it has produced nothing to record."));
        }

        if (StockDocumentId is not null || BillId is not null || JournalVoucherId is not null)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseInvoice.AlreadyRecorded",
                $"Purchase '{Number}' already names what its posting produced."));
        }

        StockDocumentId = stockDocumentId;
        BillId = billId;
        JournalVoucherId = journalVoucherId;

        return Result.Success();
    }

    /// <summary>Cancels a posted purchase.</summary>
    /// <param name="reason">Why. Required.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// The stock, the bill and the journal are put back by the handler. Goods the firm has
    /// actually accepted and is sending back go on a purchase return instead: cancelling
    /// is for a purchase that should never have been entered, and returning is for one
    /// that should.
    /// </remarks>
    public Result Cancel(string reason)
    {
        if (Status != PurchaseInvoiceStatus.Posted)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseInvoice.NotPosted",
                $"Only a posted purchase can be cancelled, and '{Number}' is {Status}."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "PurchaseInvoice.CancellationReasonRequired",
                "A reason is required when cancelling a purchase."));
        }

        Status = PurchaseInvoiceStatus.Cancelled;
        CancellationReason = reason.Trim();

        return Result.Success();
    }

    /// <summary>Checks the batch and units a line names against what the product needs.</summary>
    private static Result EnsureTrackingMatches(
        Product product,
        string? batchNumber,
        DateOnly? expiresOn,
        IReadOnlyCollection<string>? serialNumbers,
        decimal stockQuantity)
    {
        bool batched = !string.IsNullOrWhiteSpace(batchNumber);

        if (product.TracksBatches && !batched)
        {
            return Result.Failure(Error.Validation(
                "PurchaseInvoice.BatchRequired",
                $"'{product.Code}' is tracked in batches, so the line must say which batch "
                + "arrived."));
        }

        if (batched && !product.TracksBatches)
        {
            return Result.Failure(Error.Validation(
                "PurchaseInvoice.BatchNotTracked",
                $"'{product.Code}' is not tracked in batches."));
        }

        if (expiresOn is not null && !batched)
        {
            return Result.Failure(Error.Validation(
                "PurchaseInvoice.ExpiryWithoutBatch",
                "An expiry date belongs to a batch, and this line names none."));
        }

        List<string> serials = [.. (serialNumbers ?? []).Where(
            number => !string.IsNullOrWhiteSpace(number))];

        if (!product.TracksSerialNumbers)
        {
            return serials.Count > 0
                ? Result.Failure(Error.Validation(
                    "PurchaseInvoice.SerialsNotTracked",
                    $"'{product.Code}' is not tracked by serial number."))
                : Result.Success();
        }

        // One number per unit received, whole units only - the same rule the receipt
        // enforces, because it is the same fact about the goods.
        if (stockQuantity != decimal.Truncate(stockQuantity) || serials.Count != stockQuantity)
        {
            return Result.Failure(Error.Validation(
                "PurchaseInvoice.SerialCountMismatch",
                $"'{product.Code}' arrives {stockQuantity} units on this line, so that many "
                + $"serial numbers are needed and {serials.Count} were given."));
        }

        // Two boxes with the same number on them is a number that identifies neither, and
        // the receipt would refuse the second one after the first had already gone in.
        return serials.Distinct(StringComparer.OrdinalIgnoreCase).Count() != serials.Count
            ? Result.Failure(Error.Validation(
                "PurchaseInvoice.SerialRepeated",
                $"The same serial number appears twice on this line for '{product.Code}'."))
            : Result.Success();
    }

    private Money Sum(IEnumerable<Money> amounts) =>
        amounts.Aggregate(Money.Zero(Currency), (running, amount) => running + amount);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
