using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Results;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>Identifies a bill of materials.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct BillOfMaterialsId(Guid Value) : IStronglyTypedId<BillOfMaterialsId>
{
    /// <inheritdoc />
    public static BillOfMaterialsId From(Guid value) => new(value);

    /// <inheritdoc />
    public static BillOfMaterialsId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>Identifies one component of a bill of materials.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct BillOfMaterialsLineId(Guid Value)
    : IStronglyTypedId<BillOfMaterialsLineId>
{
    /// <inheritdoc />
    public static BillOfMaterialsLineId From(Guid value) => new(value);

    /// <inheritdoc />
    public static BillOfMaterialsLineId NewId() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>
/// What goes into a finished good: each component, and how much of it makes a given
/// quantity of the product.
/// </summary>
/// <remarks>
/// <para>
/// A recipe rather than a record of anything that happened. It moves no stock; a
/// production run reads it to work out what to use up, and the run - which can still be
/// changed before it is saved - is what the stock ledger records.
/// </para>
/// <para>
/// One per finished product. A product made two ways is two products to the stock
/// ledger anyway, because the two would cost different amounts.
/// </para>
/// </remarks>
public sealed class BillOfMaterials : AggregateRoot<BillOfMaterialsId>, IFirmScoped, IAuditable
{
    /// <summary>The longest a note may be.</summary>
    public const int MaximumNarrationLength = 500;

    private readonly List<BillOfMaterialsLine> _lines = [];

    private BillOfMaterials(
        BillOfMaterialsId id,
        TenantId tenantId,
        FirmId firmId,
        ProductId productId,
        decimal outputQuantity)
        : base(id)
    {
        TenantId = tenantId;
        FirmId = firmId;
        ProductId = productId;
        OutputQuantity = outputQuantity;
        IsActive = true;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private BillOfMaterials()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <inheritdoc />
    public FirmId FirmId { get; private set; }

    /// <summary>Gets the finished good the bill makes.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets how much of the finished good the components make, in its stock unit.</summary>
    public decimal OutputQuantity { get; private set; }

    /// <summary>Gets a note on how it is made.</summary>
    public string? Narration { get; private set; }

    /// <summary>Gets whether production runs may still use it.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the components.</summary>
    public IReadOnlyList<BillOfMaterialsLine> Lines => _lines.AsReadOnly();

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <inheritdoc />
    public UserId? ModifiedBy { get; private set; }

    /// <summary>Starts a bill for a finished good.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="firmId">The owning firm.</param>
    /// <param name="product">The finished good.</param>
    /// <param name="outputQuantity">How much of it the components make, in its stock unit.</param>
    /// <returns>The bill, or the reason it was refused.</returns>
    public static Result<BillOfMaterials> Create(
        TenantId tenantId,
        FirmId firmId,
        Product product,
        decimal outputQuantity)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (product.FirmId != firmId)
        {
            return Result.Failure<BillOfMaterials>(Error.Validation(
                "Bom.ProductNotInFirm", $"'{product.Code}' belongs to another firm."));
        }

        if (product.ItemType != ItemType.Stock)
        {
            return Result.Failure<BillOfMaterials>(Error.BusinessRule(
                "Bom.NotStocked",
                $"'{product.Code}' does not hold stock, so nothing can be made of it."));
        }

        return outputQuantity <= 0m
            ? Result.Failure<BillOfMaterials>(Error.Validation(
                "Bom.OutputNotPositive", "A bill must make a quantity above zero."))
            : Result.Success(new BillOfMaterials(
                BillOfMaterialsId.NewId(), tenantId, firmId, product.Id, outputQuantity));
    }

    /// <summary>Changes the quantity the bill makes and its note.</summary>
    /// <param name="outputQuantity">How much the components make, in the stock unit.</param>
    /// <param name="narration">A note on how it is made.</param>
    /// <returns>Success, or the reason it was refused.</returns>
    public Result Revise(decimal outputQuantity, string? narration)
    {
        if (outputQuantity <= 0m)
        {
            return Result.Failure(Error.Validation(
                "Bom.OutputNotPositive", "A bill must make a quantity above zero."));
        }

        if (narration is not null && narration.Trim().Length > MaximumNarrationLength)
        {
            return Result.Failure(Error.Validation(
                "Bom.NarrationTooLong",
                $"A note cannot exceed {MaximumNarrationLength} characters."));
        }

        OutputQuantity = outputQuantity;
        Narration = string.IsNullOrWhiteSpace(narration) ? null : narration.Trim();

        return Result.Success();
    }

    /// <summary>Removes every component, ahead of the components being entered again.</summary>
    public void ClearComponents() => _lines.Clear();

    /// <summary>Adds a component.</summary>
    /// <param name="component">The material.</param>
    /// <param name="quantity">How much of it, in its stock unit, makes <see cref="OutputQuantity"/>.</param>
    /// <param name="remarks">A remark.</param>
    /// <returns>The component, or the reason it was refused.</returns>
    public Result<BillOfMaterialsLine> AddComponent(Product component, decimal quantity, string? remarks = null)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.FirmId != FirmId)
        {
            return Result.Failure<BillOfMaterialsLine>(Error.Validation(
                "Bom.ComponentNotInFirm", $"'{component.Code}' belongs to another firm."));
        }

        if (component.Id == ProductId)
        {
            return Result.Failure<BillOfMaterialsLine>(Error.Validation(
                "Bom.ComponentIsProduct", "A product cannot be made out of itself."));
        }

        if (component.ItemType != ItemType.Stock)
        {
            return Result.Failure<BillOfMaterialsLine>(Error.BusinessRule(
                "Bom.ComponentNotStocked",
                $"'{component.Code}' does not hold stock, so it cannot be used up."));
        }

        if (quantity <= 0m)
        {
            return Result.Failure<BillOfMaterialsLine>(Error.Validation(
                "Bom.QuantityNotPositive", $"Give a quantity of '{component.Code}' above zero."));
        }

        if (_lines.Any(line => line.ProductId == component.Id))
        {
            return Result.Failure<BillOfMaterialsLine>(Error.Validation(
                "Bom.DuplicateComponent",
                $"'{component.Code}' is on the bill twice. Put the whole quantity on one line."));
        }

        BillOfMaterialsLine line = new(
            BillOfMaterialsLineId.NewId(), TenantId, Id, component.Id, quantity, _lines.Count + 1,
            string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim());

        _lines.Add(line);

        return Result.Success(line);
    }

    /// <summary>Withdraws the bill from use, or brings it back.</summary>
    /// <param name="active">Whether production runs may use it.</param>
    public void SetActive(bool active) => IsActive = active;
}

/// <summary>One component of a bill of materials.</summary>
public sealed class BillOfMaterialsLine : Entity<BillOfMaterialsLineId>, ITenantScoped
{
    internal BillOfMaterialsLine(
        BillOfMaterialsLineId id,
        TenantId tenantId,
        BillOfMaterialsId billId,
        ProductId productId,
        decimal quantity,
        int lineNumber,
        string? remarks)
        : base(id)
    {
        TenantId = tenantId;
        BillOfMaterialsId = billId;
        ProductId = productId;
        Quantity = quantity;
        LineNumber = lineNumber;
        Remarks = remarks;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private BillOfMaterialsLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the bill.</summary>
    public BillOfMaterialsId BillOfMaterialsId { get; private set; }

    /// <summary>Gets the material.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets how much of it, in its stock unit, makes the bill's output quantity.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the position on the bill, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets the remark.</summary>
    public string? Remarks { get; private set; }
}
