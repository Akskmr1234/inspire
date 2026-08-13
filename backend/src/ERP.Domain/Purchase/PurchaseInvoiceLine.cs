using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Purchase;

/// <summary>Identifies a purchase invoice.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct PurchaseInvoiceId(Guid Value) : IStronglyTypedId<PurchaseInvoiceId>
{
    /// <inheritdoc />
    public static PurchaseInvoiceId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PurchaseInvoiceId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one line of a purchase invoice.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct PurchaseInvoiceLineId(Guid Value)
    : IStronglyTypedId<PurchaseInvoiceLineId>
{
    /// <inheritdoc />
    public static PurchaseInvoiceLineId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PurchaseInvoiceLineId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one charge on a purchase invoice.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct PurchaseInvoiceChargeId(Guid Value)
    : IStronglyTypedId<PurchaseInvoiceChargeId>
{
    /// <inheritdoc />
    public static PurchaseInvoiceChargeId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PurchaseInvoiceChargeId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>One product on a purchase: what was bought, at what cost, taxed how.</summary>
/// <remarks>
/// <para>
/// The mirror of a sales line, with one difference that matters. A sale <em>selects</em> a
/// batch and the units it ships, because both already exist on a shelf. A purchase
/// <em>names</em> them: the batch number and the serial numbers are printed on the
/// supplier's document and refer to goods the firm has never seen before, so they are
/// carried as text and become real when the receipt posts.
/// </para>
/// <para>
/// The rate is what the goods cost, not what they will sell for. It is the figure average
/// costing consumes, which is why it is the one thing on this line the stock ledger reads.
/// </para>
/// </remarks>
public sealed class PurchaseInvoiceLine : Entity<PurchaseInvoiceLineId>, ITenantScoped
{
    private readonly List<PurchaseInvoiceLineSerial> _serials = [];
    private readonly List<PurchaseInvoiceLineTax> _components = [];

    internal PurchaseInvoiceLine(
        PurchaseInvoiceLineId id,
        TenantId tenantId,
        PurchaseInvoiceId invoiceId,
        ProductId productId,
        UnitOfMeasureId unitId,
        decimal quantity,
        decimal stockQuantity,
        decimal rate,
        decimal discount,
        TaxAssessment assessment,
        int lineNumber,
        string? batchNumber,
        DateOnly? expiresOn)
        : base(id)
    {
        TenantId = tenantId;
        PurchaseInvoiceId = invoiceId;
        ProductId = productId;
        UnitId = unitId;
        Quantity = quantity;
        StockQuantity = stockQuantity;
        Rate = rate;
        Discount = discount;
        TaxableAmount = assessment.TaxableAmount;
        TaxAmount = assessment.TotalTax;
        BatchNumber = batchNumber;
        ExpiresOn = expiresOn;
        _components.AddRange(assessment.Components.Select(component =>
            new PurchaseInvoiceLineTax(
                tenantId, id, component.Type, component.Rate.Percentage,
                component.Amount.Amount)));
        LineNumber = lineNumber;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseInvoiceLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the invoice this line belongs to.</summary>
    public PurchaseInvoiceId PurchaseInvoiceId { get; private set; }

    /// <summary>Gets the product bought.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the batch it arrives in, where the product is batched.</summary>
    /// <remarks>
    /// A number rather than an identifier, because a purchase is usually the moment a
    /// batch comes into existence. The receipt opens it if it is new and adds to it if it
    /// is not, which is the same rule a material receipt already follows.
    /// </remarks>
    public string? BatchNumber { get; private set; }

    /// <summary>Gets when that batch expires, where the supplier stated it.</summary>
    public DateOnly? ExpiresOn { get; private set; }

    /// <summary>Gets the unit the quantity was entered in.</summary>
    public UnitOfMeasureId UnitId { get; private set; }

    /// <summary>Gets the quantity as entered.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the same quantity in the product's stock unit.</summary>
    public decimal StockQuantity { get; private set; }

    /// <summary>Gets what one entered unit cost.</summary>
    public decimal Rate { get; private set; }

    /// <summary>Gets what was taken off the line before tax.</summary>
    public decimal Discount { get; private set; }

    /// <summary>Gets what the line comes to before tax.</summary>
    public Money TaxableAmount { get; private set; }

    /// <summary>Gets the tax on it, across every component.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Gets the tax broken down by component, as it was assessed.</summary>
    public IReadOnlyList<PurchaseInvoiceLineTax> Components => _components.AsReadOnly();

    /// <summary>Gets the position of this line on the invoice, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets the serialised units this line brings in.</summary>
    public IReadOnlyList<PurchaseInvoiceLineSerial> Serials => _serials.AsReadOnly();

    /// <summary>Gets what the line comes to including its tax.</summary>
    public Money LineTotal => TaxableAmount + TaxAmount;

    /// <summary>Names a serialised unit this line brings in.</summary>
    /// <param name="serialNumber">The number printed on the unit.</param>
    internal void AddSerial(string serialNumber) =>
        _serials.Add(new PurchaseInvoiceLineSerial(TenantId, Id, serialNumber));
}

/// <summary>One serialised unit named by a purchase line.</summary>
/// <remarks>
/// A number rather than a reference. The unit does not exist in the register until the
/// receipt posts, so there is nothing yet to point at - only what the supplier printed on
/// the box.
/// </remarks>
public sealed class PurchaseInvoiceLineSerial : ITenantScoped
{
    internal PurchaseInvoiceLineSerial(
        TenantId tenantId,
        PurchaseInvoiceLineId lineId,
        string serialNumber)
    {
        TenantId = tenantId;
        PurchaseInvoiceLineId = lineId;
        SerialNumber = serialNumber;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseInvoiceLineSerial() => SerialNumber = string.Empty;

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the line that brings the unit in.</summary>
    public PurchaseInvoiceLineId PurchaseInvoiceLineId { get; private set; }

    /// <summary>Gets the number printed on it.</summary>
    public string SerialNumber { get; private set; }
}

/// <summary>One charge carried beside the goods on a purchase.</summary>
/// <remarks>
/// Freight a supplier bills alongside the goods adds to what is owed; a settlement
/// discount deducts. The amount is always positive and the direction is the matrix's, for
/// the same reason it is on a sale: two spellings of the same fact is one too many.
/// </remarks>
public sealed class PurchaseInvoiceCharge : Entity<PurchaseInvoiceChargeId>, ITenantScoped
{
    internal PurchaseInvoiceCharge(
        PurchaseInvoiceChargeId id,
        TenantId tenantId,
        PurchaseInvoiceId invoiceId,
        LedgerId ledgerId,
        Money amount,
        bool isAddition)
        : base(id)
    {
        TenantId = tenantId;
        PurchaseInvoiceId = invoiceId;
        LedgerId = ledgerId;
        Amount = amount;
        IsAddition = isAddition;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseInvoiceCharge()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the invoice this charge belongs to.</summary>
    public PurchaseInvoiceId PurchaseInvoiceId { get; private set; }

    /// <summary>Gets the account it posts to.</summary>
    public LedgerId LedgerId { get; private set; }

    /// <summary>Gets what it comes to, always positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>Gets whether it adds to the total rather than deducting.</summary>
    public bool IsAddition { get; private set; }

    /// <summary>Gets the amount with the sign its direction gives it.</summary>
    public Money SignedAmount => IsAddition ? Amount : Money.Zero(Amount.Currency) - Amount;
}

/// <summary>One tax head as it was charged on a purchase line.</summary>
/// <remarks>
/// Held per head for the same reason a sale holds it: this is the figure the input half of
/// a VAT or GST return is built from, and a return asks what was charged under each head
/// rather than what a total implies at today's rates.
/// </remarks>
public sealed class PurchaseInvoiceLineTax : ITenantScoped
{
    internal PurchaseInvoiceLineTax(
        TenantId tenantId,
        PurchaseInvoiceLineId lineId,
        TaxComponentType type,
        decimal percentage,
        decimal amount)
    {
        TenantId = tenantId;
        PurchaseInvoiceLineId = lineId;
        Type = type;
        Percentage = percentage;
        Amount = amount;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseInvoiceLineTax()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the line the tax was charged on.</summary>
    public PurchaseInvoiceLineId PurchaseInvoiceLineId { get; private set; }

    /// <summary>Gets the component: VAT, CGST, SGST, IGST, cess.</summary>
    public TaxComponentType Type { get; private set; }

    /// <summary>Gets the rate it was charged at.</summary>
    public decimal Percentage { get; private set; }

    /// <summary>Gets what that came to.</summary>
    public decimal Amount { get; private set; }
}
