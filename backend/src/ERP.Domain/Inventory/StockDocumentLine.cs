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
    private readonly List<StockDocumentLineSerial> _serials = [];

    internal StockDocumentLine(
        StockDocumentLineId id,
        TenantId tenantId,
        StockDocumentId documentId,
        ProductId productId,
        BatchId? batchId,
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
        BatchId = batchId;
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

    /// <summary>Gets the batch that moved, on a product tracked in batches.</summary>
    /// <remarks>
    /// Null exactly when the product is not tracked in batches. One line moves one
    /// batch, so a sale drawing on two batches is two lines - which is also what the
    /// customer's delivery note has to say, since the two carry different expiry
    /// dates.
    /// </remarks>
    public BatchId? BatchId { get; private set; }

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

    /// <summary>Gets the serialised units this line moves.</summary>
    /// <remarks>
    /// Empty unless the product is tracked by serial number, and otherwise exactly as
    /// long as the quantity: a line for three handsets names three IMEIs. Held against
    /// the line rather than inferred from the units themselves, because a unit knows
    /// only which document touched it last, and cancelling a document from six months
    /// ago has to find the units <em>it</em> moved.
    /// </remarks>
    public IReadOnlyList<StockDocumentLineSerial> Serials => _serials.AsReadOnly();

    /// <summary>Names a serialised unit this line moves.</summary>
    /// <param name="serialId">The unit.</param>
    internal void AddSerial(SerialNumberId serialId) =>
        _serials.Add(new StockDocumentLineSerial(TenantId, Id, serialId));

    /// <summary>Renumbers the line after one before it was removed.</summary>
    /// <param name="lineNumber">The new position.</param>
    internal void Renumber(int lineNumber) => LineNumber = lineNumber;
}

/// <summary>One serialised unit named by one document line.</summary>
/// <remarks>
/// A join row and nothing more, which is why it carries no identity of its own: the
/// pair is the key, and a line naming the same unit twice is a mistake the database
/// should refuse rather than a row it should store.
/// </remarks>
public sealed class StockDocumentLineSerial : ITenantScoped
{
    internal StockDocumentLineSerial(
        TenantId tenantId,
        StockDocumentLineId lineId,
        SerialNumberId serialNumberId)
    {
        TenantId = tenantId;
        StockDocumentLineId = lineId;
        SerialNumberId = serialNumberId;
    }

    /// <summary>Constructor for EF Core materialisation.</summary>
    private StockDocumentLineSerial()
    {
    }

    /// <inheritdoc />
    public TenantId TenantId { get; private set; }

    /// <summary>Gets the line that names the unit.</summary>
    public StockDocumentLineId StockDocumentLineId { get; private set; }

    /// <summary>Gets the unit.</summary>
    public SerialNumberId SerialNumberId { get; private set; }
}
