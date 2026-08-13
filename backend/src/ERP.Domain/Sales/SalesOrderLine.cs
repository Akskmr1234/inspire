using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Sales;

/// <summary>Identifies a sales order.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct SalesOrderId(Guid Value) : IStronglyTypedId<SalesOrderId>
{
    /// <inheritdoc />
    public static SalesOrderId From(Guid value) => new(value);

    /// <inheritdoc />
    public static SalesOrderId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one line of a sales order.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct SalesOrderLineId(Guid Value) : IStronglyTypedId<SalesOrderLineId>
{
    /// <inheritdoc />
    public static SalesOrderLineId From(Guid value) => new(value);

    /// <inheritdoc />
    public static SalesOrderLineId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one charge on a sales order.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct SalesOrderChargeId(Guid Value) : IStronglyTypedId<SalesOrderChargeId>
{
    /// <inheritdoc />
    public static SalesOrderChargeId From(Guid value) => new(value);

    /// <inheritdoc />
    public static SalesOrderChargeId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>One product a customer asked for, and how much of it has gone out.</summary>
/// <remarks>
/// <para>
/// The shape of a sales line with one column added: how much of it has been invoiced. That
/// column is what lets an order be filled across several deliveries and what tells the
/// order when it is finished, and it is the whole difference between this and an invoice
/// line.
/// </para>
/// <para>
/// No batch and no serial numbers. An order is for goods nobody has picked yet - naming
/// the lot a customer will receive next week is a promise the shelf has not made, and the
/// invoice picks both at the moment the goods actually leave.
/// </para>
/// </remarks>
public sealed class SalesOrderLine : Entity<SalesOrderLineId>, ITenantScoped
{
    private readonly List<SalesOrderLineTax> _components = [];

    internal SalesOrderLine(
        SalesOrderLineId id,
        TenantId tenantId,
        SalesOrderId orderId,
        ProductId productId,
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
        SalesOrderId = orderId;
        ProductId = productId;
        UnitId = unitId;
        Quantity = quantity;
        StockQuantity = stockQuantity;
        Rate = rate;
        Discount = discount;
        TaxableAmount = assessment.TaxableAmount;
        TaxAmount = assessment.TotalTax;
        _components.AddRange(assessment.Components.Select(component =>
            new SalesOrderLineTax(
                tenantId, id, component.Type, component.Rate.Percentage,
                component.Amount.Amount)));
        LineNumber = lineNumber;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesOrderLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the order this line belongs to.</summary>
    public SalesOrderId SalesOrderId { get; private set; }

    /// <summary>Gets the product ordered.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the unit the quantity was entered in.</summary>
    public UnitOfMeasureId UnitId { get; private set; }

    /// <summary>Gets how much was ordered, as entered.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the same quantity in the product's stock unit.</summary>
    public decimal StockQuantity { get; private set; }

    /// <summary>Gets how much has been invoiced so far, in the entered unit.</summary>
    public decimal InvoicedQuantity { get; private set; }

    /// <summary>Gets how much is still to go out.</summary>
    public decimal OutstandingQuantity => Quantity - InvoicedQuantity;

    /// <summary>Gets whether everything this line asked for has been invoiced.</summary>
    public bool IsFulfilled => OutstandingQuantity <= 0m;

    /// <summary>Gets what one entered unit was quoted at.</summary>
    public decimal Rate { get; private set; }

    /// <summary>Gets what was taken off the line before tax.</summary>
    public decimal Discount { get; private set; }

    /// <summary>Gets what the line comes to before tax.</summary>
    public Money TaxableAmount { get; private set; }

    /// <summary>Gets the tax on it, across every component.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Gets the tax broken down by component, as it was assessed.</summary>
    public IReadOnlyList<SalesOrderLineTax> Components => _components.AsReadOnly();

    /// <summary>Gets the position of this line on the order, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets what the line comes to including its tax.</summary>
    public Money LineTotal => TaxableAmount + TaxAmount;

    /// <summary>Records that some of this line has been invoiced.</summary>
    /// <param name="quantity">How much, in the entered unit.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Refused past the quantity ordered rather than allowed to run over. An order
    /// invoiced for more than it asked for is either two invoices raised from one order by
    /// mistake or a quantity somebody typed over, and both are worth stopping at the
    /// moment they happen - the alternative is a negative outstanding figure that every
    /// report then has to decide what to do with.
    /// </remarks>
    internal Result Invoice(decimal quantity)
    {
        if (quantity <= 0m)
        {
            return Result.Failure(Error.Validation(
                "SalesOrder.InvoicedQuantityNotPositive",
                "An invoiced quantity must be positive."));
        }

        if (quantity > OutstandingQuantity)
        {
            return Result.Failure(Error.BusinessRule(
                "SalesOrder.OverInvoiced",
                $"Line {LineNumber} has {OutstandingQuantity} left to invoice and "
                + $"{quantity} was asked for."));
        }

        InvoicedQuantity += quantity;

        return Result.Success();
    }

    /// <summary>Takes back what a cancelled invoice had recorded against this line.</summary>
    internal void ReleaseInvoiced(decimal quantity) =>
        InvoicedQuantity = Math.Max(0m, InvoicedQuantity - quantity);
}

/// <summary>One charge carried beside the goods on an order.</summary>
/// <remarks>
/// Section 9 lists <c>SalesOrder</c> among the transaction types that carry additional
/// ledgers, so an order quotes the freight it expects to charge. Nothing posts here - what
/// the charge is for is telling a customer what the order comes to.
/// </remarks>
public sealed class SalesOrderCharge : Entity<SalesOrderChargeId>, ITenantScoped
{
    internal SalesOrderCharge(
        SalesOrderChargeId id,
        TenantId tenantId,
        SalesOrderId orderId,
        LedgerId ledgerId,
        Money amount,
        bool isAddition)
        : base(id)
    {
        TenantId = tenantId;
        SalesOrderId = orderId;
        LedgerId = ledgerId;
        Amount = amount;
        IsAddition = isAddition;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesOrderCharge()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the order this charge belongs to.</summary>
    public SalesOrderId SalesOrderId { get; private set; }

    /// <summary>Gets the account it would post to.</summary>
    public LedgerId LedgerId { get; private set; }

    /// <summary>Gets what it comes to, always positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>Gets whether it adds to the total rather than deducting.</summary>
    public bool IsAddition { get; private set; }

    /// <summary>Gets the amount with the sign its direction gives it.</summary>
    public Money SignedAmount => IsAddition ? Amount : Money.Zero(Amount.Currency) - Amount;
}

/// <summary>One tax head as it was quoted on an order line.</summary>
/// <remarks>
/// Quoted rather than charged: an order owes the state nothing, and no tax return reads
/// this. It is here so the total a customer was quoted is the total they are billed, and
/// so a conversion can be checked against what was agreed.
/// </remarks>
public sealed class SalesOrderLineTax : ITenantScoped
{
    internal SalesOrderLineTax(
        TenantId tenantId,
        SalesOrderLineId lineId,
        TaxComponentType type,
        decimal percentage,
        decimal amount)
    {
        TenantId = tenantId;
        SalesOrderLineId = lineId;
        Type = type;
        Percentage = percentage;
        Amount = amount;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private SalesOrderLineTax()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the line the tax was quoted on.</summary>
    public SalesOrderLineId SalesOrderLineId { get; private set; }

    /// <summary>Gets the component: VAT, CGST, SGST, IGST, cess.</summary>
    public TaxComponentType Type { get; private set; }

    /// <summary>Gets the rate it was quoted at.</summary>
    public decimal Percentage { get; private set; }

    /// <summary>Gets what that came to.</summary>
    public decimal Amount { get; private set; }
}
