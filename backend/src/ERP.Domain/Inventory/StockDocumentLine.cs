using ERP.SharedKernel.Abstractions;
using ERP.SharedKernel.Primitives;
using ERP.SharedKernel.Tenancy;

namespace ERP.Domain.Inventory;

/// <summary>
/// One product on a stock document: what moved, how much of it, and in what unit.
/// </summary>
/// <remarks>
/// <para>
/// Part of the <see cref="StockDocument"/> aggregate rather than an aggregate of its
/// own, so a line cannot be created or altered except through the document that owns
/// it - which is what keeps "a transfer moves all of its lines or none" enforceable.
/// </para>
/// <para>
/// The quantity is kept twice: as entered, and converted to the product's stock unit.
/// Both are facts, and neither can be recovered from the other later. The entered
/// figure is what the user typed and what the document must print - somebody who
/// received four cases will not accept a note saying ninety-six pieces - while the
/// stock figure is the only one the balances and the ledger can use, because they are
/// kept in one unit per product and nothing else would add up.
/// </para>
/// </remarks>
public sealed class StockDocumentLine : Entity<StockDocumentLineId>, ITenantScoped
{
    internal StockDocumentLine(
        StockDocumentLineId id,
        TenantId tenantId,
        StockDocumentId documentId,
        ProductId productId,
        UnitOfMeasureId unitId,
        decimal quantity,
        decimal stockQuantity,
        decimal rate,
        int lineNumber,
        string? remarks)
        : base(id)
    {
        TenantId = tenantId;
        StockDocumentId = documentId;
        ProductId = productId;
        UnitId = unitId;
        Quantity = quantity;
        StockQuantity = stockQuantity;
        Rate = rate;
        LineNumber = lineNumber;
        Remarks = remarks;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private StockDocumentLine()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the owning document.</summary>
    public StockDocumentId StockDocumentId { get; private set; }

    /// <summary>Gets the product that moved.</summary>
    public ProductId ProductId { get; private set; }

    /// <summary>Gets the unit the quantity was entered in.</summary>
    public UnitOfMeasureId UnitId { get; private set; }

    /// <summary>Gets the quantity as entered, in <see cref="UnitId"/>.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the quantity converted to the product's stock unit.</summary>
    /// <remarks>
    /// Negative on an adjustment that reduces stock. Every other document type carries
    /// its direction in the type itself, so the figure there is always positive.
    /// </remarks>
    public decimal StockQuantity { get; private set; }

    /// <summary>
    /// Gets what one stock unit cost, on the documents that carry a cost.
    /// </summary>
    /// <remarks>
    /// Per stock unit, not per entered unit. A case of ninety-six at 480 is recorded
    /// as 5 per piece, because that is the figure the weighted average is computed
    /// from and converting it at read time would mean storing the factor twice.
    /// </remarks>
    public decimal Rate { get; private set; }

    /// <summary>Gets the position of this line on the document, from one.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets the line-level remark.</summary>
    public string? Remarks { get; private set; }

    /// <summary>Renumbers the line after one before it was removed.</summary>
    /// <param name="lineNumber">The new position.</param>
    internal void Renumber(int lineNumber) => LineNumber = lineNumber;
}
