using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Sales;

/// <summary>Identifies a sales invoice.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct SalesInvoiceId(Guid Value) : IStronglyTypedId<SalesInvoiceId>
{
    /// <inheritdoc />
    public static SalesInvoiceId From(Guid value) => new(value);

    /// <inheritdoc />
    public static SalesInvoiceId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one line of a sales invoice.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct SalesInvoiceLineId(Guid Value) : IStronglyTypedId<SalesInvoiceLineId>
{
    /// <inheritdoc />
    public static SalesInvoiceLineId From(Guid value) => new(value);

    /// <inheritdoc />
    public static SalesInvoiceLineId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one charge on a sales invoice.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct SalesInvoiceChargeId(Guid Value)
    : IStronglyTypedId<SalesInvoiceChargeId>
{
    /// <inheritdoc />
    public static SalesInvoiceChargeId From(Guid value) => new(value);

    /// <inheritdoc />
    public static SalesInvoiceChargeId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>One product on an invoice: what was sold, at what price, taxed how.</summary>
/// <remarks>
/// The tax is held per component rather than as a single figure, which is what makes one
/// posting produce both a VAT return and a GST return. It is also what makes a reprint
/// honest: the invoice shows the tax that was charged, not the tax today's rates would
/// produce for the same goods.
/// </remarks>
public sealed class SalesInvoiceLine : Entity<SalesInvoiceLineId>, ITenantScoped
{
    private readonly List<SalesInvoiceLineSerial> _serials = [];
    private readonly List<SalesInvoiceLineTax> _components = [];

    internal SalesInvoiceLine(
        SalesInvoiceLineId id,
        TenantId tenantId,
        SalesInvoiceId invoiceId,
        ProductId productId,
        BatchId? batchId,
        UnitOfMeasureId unitId,
        decimal quantity,
        decimal stockQuantity,
        decimal rate,
        decimal discount,
        TaxAssessment assessment,
        int lineNumber)
        : base(id)
    {
        TenantId = tenantId;
        SalesInvoiceId = invoiceId;
        ProductId = productId;
        BatchId = batchId;
        UnitId = unitId;
        Quantity = quantity;
        StockQuantity = stockQuantity;
        Rate = rate;
        Discount = discount;
        TaxableAmount = assessment.TaxableAmount;
        TaxAmount = assessment.TotalTax;
        _components.AddRange(assessment.Components.Select(component =>
            new SalesInvoiceLineTax(
                tenantId, id, component.Type, component.Rate.Percentage,
                component.Amount.Amount)));
        LineNumber = lineNumber;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesInvoiceLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the invoice this line belongs to.</summary>
    public SalesInvoiceId SalesInvoiceId { get; private set; }

    /// <summary>Gets the product sold.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the batch sold, where the product is batched.</summary>
    public BatchId? BatchId { get; private set; }

    /// <summary>Gets the unit the quantity was entered in.</summary>
    public UnitOfMeasureId UnitId { get; private set; }

    /// <summary>Gets the quantity as entered.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the same quantity in the product's stock unit.</summary>
    public decimal StockQuantity { get; private set; }

    /// <summary>Gets the price of one entered unit.</summary>
    public decimal Rate { get; private set; }

    /// <summary>Gets what was taken off the line before tax.</summary>
    public decimal Discount { get; private set; }

    /// <summary>Gets what the line comes to before tax.</summary>
    public Money TaxableAmount { get; private set; }

    /// <summary>Gets the tax on it, across every component.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Gets the tax broken down by component, as it was assessed.</summary>
    public IReadOnlyList<SalesInvoiceLineTax> Components => _components.AsReadOnly();

    /// <summary>Gets the position of this line on the invoice, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets the serialised units this line sells.</summary>
    public IReadOnlyList<SalesInvoiceLineSerial> Serials => _serials.AsReadOnly();

    /// <summary>Gets what the line comes to including its tax.</summary>
    public Money LineTotal => TaxableAmount + TaxAmount;

    /// <summary>Names a serialised unit this line sells.</summary>
    /// <param name="serialId">The unit.</param>
    internal void AddSerial(SerialNumberId serialId) =>
        _serials.Add(new SalesInvoiceLineSerial(TenantId, Id, serialId));
}

/// <summary>One serialised unit sold by one invoice line.</summary>
public sealed class SalesInvoiceLineSerial : ITenantScoped
{
    internal SalesInvoiceLineSerial(
        TenantId tenantId,
        SalesInvoiceLineId lineId,
        SerialNumberId serialNumberId)
    {
        TenantId = tenantId;
        SalesInvoiceLineId = lineId;
        SerialNumberId = serialNumberId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesInvoiceLineSerial()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the line that sells the unit.</summary>
    public SalesInvoiceLineId SalesInvoiceLineId { get; private set; }

    /// <summary>Gets the unit.</summary>
    public SerialNumberId SerialNumberId { get; private set; }
}

/// <summary>One charge carried beside the goods on an invoice.</summary>
/// <remarks>
/// The amount is always positive and the direction is a flag, taken from the matrix when
/// the charge was added. A freight of minus fifty and a discount of plus fifty both read
/// as somebody fighting the form, and one of them is silently wrong on the total.
/// </remarks>
public sealed class SalesInvoiceCharge : Entity<SalesInvoiceChargeId>, ITenantScoped
{
    internal SalesInvoiceCharge(
        SalesInvoiceChargeId id,
        TenantId tenantId,
        SalesInvoiceId invoiceId,
        LedgerId ledgerId,
        Money amount,
        bool isAddition)
        : base(id)
    {
        TenantId = tenantId;
        SalesInvoiceId = invoiceId;
        LedgerId = ledgerId;
        Amount = amount;
        IsAddition = isAddition;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesInvoiceCharge()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the invoice this charge belongs to.</summary>
    public SalesInvoiceId SalesInvoiceId { get; private set; }

    /// <summary>Gets the account it posts to.</summary>
    public LedgerId LedgerId { get; private set; }

    /// <summary>Gets what it comes to, always positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>Gets whether it adds to the total rather than deducting.</summary>
    public bool IsAddition { get; private set; }

    /// <summary>Gets the amount with the sign its direction gives it.</summary>
    public Money SignedAmount => IsAddition ? Amount : Money.Zero(Amount.Currency) - Amount;
}

/// <summary>One tax head as it was charged on a line.</summary>
/// <remarks>
/// A row per head rather than a figure per invoice. That is what makes one posting
/// produce both a VAT return and a GST return: each return reads the heads it knows
/// about and ignores the rest, instead of re-deriving them from a total and today's
/// rates. Held as three plain figures - which head, at what rate, for how much - because
/// that is what a return asks for, and because the engine's own value objects would put
/// a currency on every component of every line.
/// </remarks>
public sealed class SalesInvoiceLineTax : ITenantScoped
{
    internal SalesInvoiceLineTax(
        TenantId tenantId,
        SalesInvoiceLineId lineId,
        TaxComponentType type,
        decimal percentage,
        decimal amount)
    {
        TenantId = tenantId;
        SalesInvoiceLineId = lineId;
        Type = type;
        Percentage = percentage;
        Amount = amount;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesInvoiceLineTax()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the line the tax was charged on.</summary>
    public SalesInvoiceLineId SalesInvoiceLineId { get; private set; }

    /// <summary>Gets the component: VAT, CGST, SGST, IGST, cess.</summary>
    public TaxComponentType Type { get; private set; }

    /// <summary>Gets the rate it was charged at.</summary>
    public decimal Percentage { get; private set; }

    /// <summary>Gets what that came to.</summary>
    public decimal Amount { get; private set; }
}
