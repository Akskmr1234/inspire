using ERP.Domain.Accounting;
using ERP.Domain.Inventory;
using ERP.Domain.Taxation;
using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;
using ERP.SharedKernel.ValueObjects;

namespace ERP.Domain.Purchase;

/// <summary>Identifies a purchase order.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct PurchaseOrderId(Guid Value) : IStronglyTypedId<PurchaseOrderId>
{
    /// <inheritdoc />
    public static PurchaseOrderId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PurchaseOrderId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one line of a purchase order.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct PurchaseOrderLineId(Guid Value)
    : IStronglyTypedId<PurchaseOrderLineId>
{
    /// <inheritdoc />
    public static PurchaseOrderLineId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PurchaseOrderLineId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one charge on a purchase order.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct PurchaseOrderChargeId(Guid Value)
    : IStronglyTypedId<PurchaseOrderChargeId>
{
    /// <inheritdoc />
    public static PurchaseOrderChargeId From(Guid value) => new(value);

    /// <inheritdoc />
    public static PurchaseOrderChargeId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>One product ordered from a supplier, and how much of it has arrived.</summary>
/// <remarks>
/// <para>
/// The shape of a purchase line with one column added: how much of it has been invoiced.
/// That column is what lets an order be filled across several deliveries and what tells the
/// order when it is finished, and it is the whole difference between this and a purchase
/// invoice line.
/// </para>
/// <para>
/// <b>No batch and no serial numbers</b>, and for a stronger reason than on a sales order.
/// A sales order declines to name the lot a customer will receive because the shelf has not
/// promised it yet; a purchase order cannot name one at all, because the batch does not
/// exist until the goods arrive and somebody reads the number off the carton. Both are keyed
/// on the invoice the receipt posts from.
/// </para>
/// </remarks>
public sealed class PurchaseOrderLine : Entity<PurchaseOrderLineId>, ITenantScoped
{
    private readonly List<PurchaseOrderLineTax> _components = [];

    internal PurchaseOrderLine(
        PurchaseOrderLineId id,
        TenantId tenantId,
        PurchaseOrderId orderId,
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
        PurchaseOrderId = orderId;
        ProductId = productId;
        UnitId = unitId;
        Quantity = quantity;
        StockQuantity = stockQuantity;
        Rate = rate;
        Discount = discount;
        TaxableAmount = assessment.TaxableAmount;
        TaxAmount = assessment.TotalTax;
        _components.AddRange(assessment.Components.Select(component =>
            new PurchaseOrderLineTax(
                tenantId, id, component.Type, component.Rate.Percentage,
                component.Amount.Amount)));
        LineNumber = lineNumber;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseOrderLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the order this line belongs to.</summary>
    public PurchaseOrderId PurchaseOrderId { get; private set; }

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

    /// <summary>Gets how much is still to arrive.</summary>
    public decimal OutstandingQuantity => Quantity - InvoicedQuantity;

    /// <summary>Gets whether everything this line asked for has been invoiced.</summary>
    public bool IsFulfilled => OutstandingQuantity <= 0m;

    /// <summary>Gets what one entered unit was ordered at.</summary>
    public decimal Rate { get; private set; }

    /// <summary>Gets what was agreed off the line before tax.</summary>
    public decimal Discount { get; private set; }

    /// <summary>Gets what the line comes to before tax.</summary>
    public Money TaxableAmount { get; private set; }

    /// <summary>Gets the tax expected on it, across every component.</summary>
    public Money TaxAmount { get; private set; }

    /// <summary>Gets the tax broken down by component, as it was assessed.</summary>
    public IReadOnlyList<PurchaseOrderLineTax> Components => _components.AsReadOnly();

    /// <summary>Gets the position of this line on the order, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets what the line comes to including its tax.</summary>
    public Money LineTotal => TaxableAmount + TaxAmount;

    /// <summary>Records that some of this line has been invoiced.</summary>
    /// <param name="quantity">How much, in the entered unit.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    /// <remarks>
    /// Refused past the quantity ordered rather than allowed to run over. A supplier who
    /// ships more than was ordered is a conversation somebody has to have, not a figure the
    /// order should absorb silently - and the alternative is a negative outstanding quantity
    /// that every report then has to decide what to do with.
    /// </remarks>
    internal Result Invoice(decimal quantity)
    {
        if (quantity <= 0m)
        {
            return Result.Failure(Error.Validation(
                "PurchaseOrder.InvoicedQuantityNotPositive",
                "An invoiced quantity must be positive."));
        }

        if (quantity > OutstandingQuantity)
        {
            return Result.Failure(Error.BusinessRule(
                "PurchaseOrder.OverInvoiced",
                $"Line {LineNumber} has {OutstandingQuantity} left to invoice and "
                + $"{quantity} was asked for."));
        }

        InvoicedQuantity += quantity;

        return Result.Success();
    }

    /// <summary>Takes back what a cancelled purchase had recorded against this line.</summary>
    internal void ReleaseInvoiced(decimal quantity) =>
        InvoicedQuantity = Math.Max(0m, InvoicedQuantity - quantity);
}

/// <summary>One charge carried beside the goods on a purchase order.</summary>
/// <remarks>
/// Section 9 lists <c>PurchaseOrder</c> among the transaction types that carry additional
/// ledgers, so an order records the carriage a supplier has quoted. Nothing posts here -
/// what the charge is for is knowing what the order will come to before the invoice
/// arrives, which is the figure a buyer checks the supplier's document against.
/// </remarks>
public sealed class PurchaseOrderCharge : Entity<PurchaseOrderChargeId>, ITenantScoped
{
    internal PurchaseOrderCharge(
        PurchaseOrderChargeId id,
        TenantId tenantId,
        PurchaseOrderId orderId,
        LedgerId ledgerId,
        Money amount,
        bool isAddition)
        : base(id)
    {
        TenantId = tenantId;
        PurchaseOrderId = orderId;
        LedgerId = ledgerId;
        Amount = amount;
        IsAddition = isAddition;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseOrderCharge()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the order this charge belongs to.</summary>
    public PurchaseOrderId PurchaseOrderId { get; private set; }

    /// <summary>Gets the account it would post to.</summary>
    public LedgerId LedgerId { get; private set; }

    /// <summary>Gets what it comes to, always positive.</summary>
    public Money Amount { get; private set; }

    /// <summary>Gets whether it adds to the total rather than deducting.</summary>
    public bool IsAddition { get; private set; }

    /// <summary>Gets the amount with the sign its direction gives it.</summary>
    public Money SignedAmount => IsAddition ? Amount : Money.Zero(Amount.Currency) - Amount;
}

/// <summary>One tax head as it was expected on a purchase order line.</summary>
/// <remarks>
/// Expected rather than charged: an order reclaims nothing, and no tax return reads this.
/// It is here so the total the firm expects is the total it checks the supplier's invoice
/// against, and so a conversion can be compared with what was agreed.
/// </remarks>
public sealed class PurchaseOrderLineTax : ITenantScoped
{
    internal PurchaseOrderLineTax(
        TenantId tenantId,
        PurchaseOrderLineId lineId,
        TaxComponentType type,
        decimal percentage,
        decimal amount)
    {
        TenantId = tenantId;
        PurchaseOrderLineId = lineId;
        Type = type;
        Percentage = percentage;
        Amount = amount;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private PurchaseOrderLineTax()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the line the tax was expected on.</summary>
    public PurchaseOrderLineId PurchaseOrderLineId { get; private set; }

    /// <summary>Gets the component: VAT, CGST, SGST, IGST, cess.</summary>
    public TaxComponentType Type { get; private set; }

    /// <summary>Gets the rate it was assessed at.</summary>
    public decimal Percentage { get; private set; }

    /// <summary>Gets what that came to.</summary>
    public decimal Amount { get; private set; }
}
