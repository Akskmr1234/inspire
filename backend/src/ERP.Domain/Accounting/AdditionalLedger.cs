using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Accounting;

/// <summary>The kinds of document that can carry an additional charge.</summary>
/// <remarks>
/// The list §9 names, and no more. A charge mapped to a document type that does not
/// exist yet would be a row nothing reads; the enum grows when the document does, which
/// is also the moment somebody can say whether the charge belongs on it.
/// </remarks>
public enum ChargeableDocument
{
    /// <summary>A sales invoice.</summary>
    Sales = 1,

    /// <summary>An order taken from a customer.</summary>
    SalesOrder = 2,

    /// <summary>A quotation given to a customer.</summary>
    SalesQuotation = 3,

    /// <summary>Goods coming back from a customer.</summary>
    SalesReturn = 4,

    /// <summary>A purchase invoice.</summary>
    Purchase = 5,

    /// <summary>An order placed with a supplier.</summary>
    PurchaseOrder = 6,

    /// <summary>Goods going back to a supplier.</summary>
    PurchaseReturn = 7,

    /// <summary>A note accompanying goods sent out.</summary>
    DeliveryNote = 8,

    /// <summary>A service job.</summary>
    Service = 9,

    /// <summary>A sale of services rather than goods.</summary>
    ServiceSales = 10,

    /// <summary>Goods made rather than bought.</summary>
    Manufacture = 11,

    /// <summary>A production run.</summary>
    Production = 12,
}

/// <summary>
/// One charge a document may carry, and the rules about when it applies.
/// </summary>
/// <remarks>
/// <para>
/// The matrix of §9: a transaction type, a ledger, and the flags that decide whether the
/// charge applies to a given document and which way it moves the total. Delivery,
/// packing, freight, insurance, a service charge, a discount, the rounding difference -
/// each is a ledger somebody chose, mapped to the documents it belongs on.
/// </para>
/// <para>
/// A row per pairing rather than a list on the ledger. The same account is charged
/// differently on a sale and on a purchase - freight a firm pays is a cost, freight it
/// recovers is income - and one row per pairing is what lets the two disagree without
/// either being wrong.
/// </para>
/// </remarks>
public sealed class AdditionalLedger : AggregateRoot<AdditionalLedgerId>, IFirmScoped, IAuditable
{
    private AdditionalLedger(
        AdditionalLedgerId id,
        TenantId tenantId,
        FirmId firmId,
        ChargeableDocument document,
        LedgerId ledgerId)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        Document = document;
        LedgerId = ledgerId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private AdditionalLedger()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the kind of document this charge may appear on.</summary>
    public ChargeableDocument Document { get; private set; }

    /// <summary>Gets the account the charge posts to.</summary>
    public LedgerId LedgerId { get; private set; }

    /// <summary>Gets whether the charge applies to a document in tax mode.</summary>
    public bool AppliesUnderTax { get; private set; }

    /// <summary>Gets whether the charge applies under CST.</summary>
    public bool AppliesUnderCst { get; private set; }

    /// <summary>Gets whether the charge applies to a non-tax document.</summary>
    public bool AppliesUnderNonTax { get; private set; }

    /// <summary>Gets whether the charge adds to the total rather than deducting.</summary>
    /// <remarks>
    /// Freight and packing add; a discount deducts. Held as a flag rather than inferred
    /// from the sign somebody types, because a negative freight and a positive discount
    /// are both mistakes worth catching, and neither can be caught without knowing
    /// which way the charge is supposed to go.
    /// </remarks>
    public bool IsAddition { get; private set; }

    /// <summary>Gets whether the charge loads onto a new document by itself.</summary>
    /// <remarks>
    /// Only <c>Round Off</c> is defaulted by the seeding, which is the business's answer
    /// of 2026-08-10. The rest are added by hand on the documents that carry them: five
    /// zero lines on every invoice is five things to look past on the ones that have no
    /// freight, no packing and no discount.
    /// </remarks>
    public bool IsDefault { get; private set; }

    /// <summary>Gets the order the charge appears in on a document.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>Gets whether the mapping may still be used.</summary>
    public bool IsActive { get; private set; } = true;

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Maps a ledger onto a kind of document as a charge.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="document">The kind of document.</param>
    /// <param name="ledger">The account the charge posts to.</param>
    /// <param name="isAddition">Whether it adds to the total rather than deducting.</param>
    /// <param name="displayOrder">Where it appears among the other charges.</param>
    /// <returns>The mapping, or the reason it was refused.</returns>
    public static Result<AdditionalLedger> Map(
        TenantId tenantId,
        FirmId firmId,
        ChargeableDocument document,
        Ledger ledger,
        bool isAddition = true,
        int displayOrder = 0)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        if (!Enum.IsDefined(document))
        {
            return Result.Failure<AdditionalLedger>(Error.Validation(
                "AdditionalLedger.UnknownDocument",
                $"'{document}' is not a kind of document that carries charges."));
        }

        if (ledger.FirmId != firmId)
        {
            return Result.Failure<AdditionalLedger>(Error.Validation(
                "AdditionalLedger.LedgerNotInFirm",
                $"'{ledger.Name}' belongs to another firm."));
        }

        if (displayOrder < 0)
        {
            return Result.Failure<AdditionalLedger>(Error.Validation(
                "AdditionalLedger.OrderNegative",
                "A display order cannot be negative."));
        }

        return Result.Success(new AdditionalLedger(
            AdditionalLedgerId.NewId(), tenantId, firmId, document, ledger.Id)
        {
            IsAddition = isAddition,
            DisplayOrder = displayOrder,

            // Applies everywhere until somebody narrows it. A charge that applied to
            // nothing would be a row on a settings screen that never appears on a
            // document, and nobody would be able to tell whether that was intended.
            AppliesUnderTax = true,
            AppliesUnderCst = true,
            AppliesUnderNonTax = true,
        });
    }

    /// <summary>Sets which tax modes the charge applies under.</summary>
    /// <param name="underTax">Whether it applies in tax mode.</param>
    /// <param name="underCst">Whether it applies under CST.</param>
    /// <param name="underNonTax">Whether it applies to a non-tax document.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// All three off is refused. It reads as a way to disable the charge, and there is
    /// already a way to do that which says so - a mapping that applies under no mode
    /// would sit on the settings screen looking enabled and never appear on a document.
    /// </remarks>
    public Result SetModes(bool underTax, bool underCst, bool underNonTax)
    {
        if (!underTax && !underCst && !underNonTax)
        {
            return Result.Failure(Error.Validation(
                "AdditionalLedger.NoModes",
                "A charge that applies under no tax mode would never appear. Withdraw it "
                + "instead."));
        }

        AppliesUnderTax = underTax;
        AppliesUnderCst = underCst;
        AppliesUnderNonTax = underNonTax;

        return Result.Success();
    }

    /// <summary>Sets whether the charge loads onto a new document by itself.</summary>
    /// <param name="isDefault">Whether it auto-loads.</param>
    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    /// <summary>Sets where the charge appears among the others.</summary>
    /// <param name="displayOrder">The position, from zero.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result SetDisplayOrder(int displayOrder)
    {
        if (displayOrder < 0)
        {
            return Result.Failure(Error.Validation(
                "AdditionalLedger.OrderNegative",
                "A display order cannot be negative."));
        }

        DisplayOrder = displayOrder;

        return Result.Success();
    }

    /// <summary>Says whether the charge applies to a document in a given mode.</summary>
    /// <param name="mode">The document's tax mode.</param>
    /// <returns><see langword="true"/> if the charge belongs on it.</returns>
    public bool AppliesTo(TaxMode mode) => IsActive && mode switch
    {
        TaxMode.Tax => AppliesUnderTax,
        TaxMode.Cst => AppliesUnderCst,
        TaxMode.NonTax => AppliesUnderNonTax,
        _ => false,
    };

    /// <summary>Stops the charge being offered on new documents.</summary>
    /// <remarks>
    /// Withdrawn rather than deleted. Documents already carrying the charge keep it and
    /// keep pointing at the account it posted to; a deleted mapping would leave those
    /// documents explaining themselves with a row that no longer exists.
    /// </remarks>
    public void Withdraw() => IsActive = false;

    /// <summary>Offers the charge again.</summary>
    public void Restore() => IsActive = true;
}

/// <summary>The tax mode a document is entered under.</summary>
/// <remarks>
/// §9's `Mode` field. The default comes from the firm's regime - a GST firm's documents
/// open in tax mode, a VAT firm's likewise - which is the business's answer of
/// 2026-08-10; a non-tax sale is the exception somebody selects deliberately.
/// </remarks>
public enum TaxMode
{
    /// <summary>Non-taxable. No component applies.</summary>
    NonTax = 1,

    /// <summary>Taxable under the firm's ordinary regime: VAT, or CGST/SGST/IGST.</summary>
    Tax = 2,

    /// <summary>Taxable under CST, which India's inter-state trade used before GST.</summary>
    Cst = 3,
}
