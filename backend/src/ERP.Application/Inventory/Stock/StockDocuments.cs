using ERP.Application.Abstractions.Messaging;
using ERP.Domain.Inventory;
using ERP.SharedKernel.Tenancy;
using FluentValidation;

namespace ERP.Application.Inventory.Stock;

// -------------------------------------------------------------------------- writing

/// <summary>One line of a stock document being entered.</summary>
/// <param name="ProductId">The product moving.</param>
/// <param name="Quantity">
/// How much, in <paramref name="UnitId"/>. Negative only on an adjustment; on a
/// physical verification this is what was counted rather than what moved.
/// </param>
/// <param name="UnitId">
/// The unit the quantity is entered in. Omit for the product's stock unit.
/// </param>
/// <param name="Rate">
/// What one stock unit cost, on the documents that carry a cost. Omit on an
/// adjustment to value the goods at what the position already says they are worth.
/// </param>
/// <param name="Remarks">A line-level remark.</param>
/// <param name="BatchId">
/// The batch that moved, chosen from those in stock. Required on a product tracked in
/// batches unless <paramref name="BatchNumber"/> names it instead.
/// </param>
/// <param name="BatchNumber">
/// The batch by number rather than by identifier - what a storekeeper reads off the
/// carton. On a document that brings goods in, an unknown number opens that batch, and
/// no number at all generates the next one for the product.
/// </param>
/// <param name="ManufacturedOn">When the goods in the batch were produced.</param>
/// <param name="ExpiresOn">
/// When the batch expires. Taken from the product's shelf life when it is omitted and
/// the manufacturing date is given.
/// </param>
public sealed record StockDocumentLineInput(
    Guid ProductId,
    decimal Quantity,
    Guid? UnitId = null,
    decimal Rate = 0m,
    string? Remarks = null,
    Guid? BatchId = null,
    string? BatchNumber = null,
    DateOnly? ManufacturedOn = null,
    DateOnly? ExpiresOn = null);

/// <summary>Enters a stock document, and by default posts it.</summary>
/// <param name="Type">The kind of operation.</param>
/// <param name="Date">The document date.</param>
/// <param name="WarehouseId">The warehouse acted on, or moved out of.</param>
/// <param name="Lines">What moved.</param>
/// <param name="DestinationWarehouseId">The warehouse a transfer moves into.</param>
/// <param name="ReferenceNumber">A related reference.</param>
/// <param name="Narration">The document narration.</param>
/// <param name="PostImmediately">
/// Whether to post on save, or leave an editable draft for somebody else to post.
/// </param>
/// <remarks>
/// Entering and posting are one command because that is how the screen behaves: the
/// storekeeper presses Save and expects the stock to have moved. The flag exists for
/// the firms where a storekeeper enters and a supervisor posts.
/// </remarks>
public sealed record CreateStockDocumentCommand(
    StockDocumentType Type,
    DateOnly Date,
    Guid WarehouseId,
    IReadOnlyList<StockDocumentLineInput> Lines,
    Guid? DestinationWarehouseId = null,
    string? ReferenceNumber = null,
    string? Narration = null,
    bool PostImmediately = true) : ICommand<CreateStockDocumentResponse>, ITransactional;

/// <summary>The document that was entered.</summary>
/// <param name="StockDocumentId">The new document.</param>
/// <param name="Number">The number its series issued.</param>
/// <param name="Status">Whether it was left a draft or posted.</param>
/// <param name="Movements">How many stock ledger entries it produced.</param>
/// <param name="TotalValue">
/// The value of the goods it moved, in the firm's currency. A transfer reports the
/// value of one leg rather than both, because the goods moved once.
/// </param>
public sealed record CreateStockDocumentResponse(
    Guid StockDocumentId,
    string Number,
    StockDocumentStatus Status,
    int Movements,
    decimal TotalValue);

/// <summary>Posts a draft stock document.</summary>
/// <param name="StockDocumentId">The document.</param>
public sealed record PostStockDocumentCommand(Guid StockDocumentId)
    : ICommand<CreateStockDocumentResponse>, ITransactional;

/// <summary>Cancels a posted stock document, reversing what it moved.</summary>
/// <param name="StockDocumentId">The document.</param>
/// <param name="Reason">Why. Required.</param>
/// <remarks>
/// Reversal rather than deletion, and reversal at the cost each movement was valued
/// at rather than at today's average. A receipt whose goods have since been sold
/// cannot be reversed at all - the refusal says so and points at an adjustment, which
/// is the entry that actually describes what happened.
/// </remarks>
public sealed record CancelStockDocumentCommand(Guid StockDocumentId, string Reason)
    : ICommand, ITransactional;

/// <summary>Validates a <see cref="CreateStockDocumentCommand"/>.</summary>
/// <remarks>
/// Shape only. Whether the goods are there, whether the units convert, and whether
/// the warehouses differ are all domain rules enforced where they cannot be
/// bypassed - a second copy here would eventually disagree with the first.
/// </remarks>
public sealed class CreateStockDocumentCommandValidator
    : AbstractValidator<CreateStockDocumentCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CreateStockDocumentCommandValidator"/> class.</summary>
    public CreateStockDocumentCommandValidator()
    {
        RuleFor(c => c.Type).IsInEnum();

        RuleFor(c => c.Date)
            .NotEqual(default(DateOnly))
            .WithMessage("A document date is required.");

        RuleFor(c => c.WarehouseId).NotEqual(Guid.Empty);

        RuleFor(c => c.Lines)
            .NotEmpty()
            .WithMessage("A stock document needs at least one line.");

        RuleForEach(c => c.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId)
                .NotEqual(Guid.Empty)
                .WithMessage("Each line must name a product.");

            line.RuleFor(l => l.Rate)
                .GreaterThanOrEqualTo(0m)
                .WithMessage("A rate cannot be negative.");

            line.RuleFor(l => l.Remarks).MaximumLength(StockDocument.MaximumNarrationLength);

            line.RuleFor(l => l.BatchNumber).MaximumLength(Batch.MaximumNumberLength);
        });

        RuleFor(c => c.ReferenceNumber).MaximumLength(StockDocument.MaximumReferenceLength);
        RuleFor(c => c.Narration).MaximumLength(StockDocument.MaximumNarrationLength);
    }
}

/// <summary>Validates a <see cref="CancelStockDocumentCommand"/>.</summary>
public sealed class CancelStockDocumentCommandValidator
    : AbstractValidator<CancelStockDocumentCommand>
{
    /// <summary>Initialises a new instance of the <see cref="CancelStockDocumentCommandValidator"/> class.</summary>
    public CancelStockDocumentCommandValidator()
    {
        RuleFor(c => c.StockDocumentId).NotEqual(Guid.Empty);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(StockDocument.MaximumNarrationLength);
    }
}

// -------------------------------------------------------------------------- reading

/// <summary>Lists stock documents.</summary>
/// <param name="From">The earliest document date.</param>
/// <param name="To">The latest document date.</param>
/// <param name="Type">One kind of operation, or null for all.</param>
/// <param name="WarehouseId">One warehouse, or null for all.</param>
/// <param name="Status">One lifecycle state, or null for all.</param>
public sealed record ListStockDocumentsQuery(
    DateOnly From,
    DateOnly To,
    StockDocumentType? Type = null,
    Guid? WarehouseId = null,
    StockDocumentStatus? Status = null) : IQuery<IReadOnlyList<StockDocumentSummary>>;

/// <summary>A stock document as the list shows it.</summary>
/// <param name="Id">The document.</param>
/// <param name="Number">Its number.</param>
/// <param name="Type">The kind of operation.</param>
/// <param name="Date">The document date.</param>
/// <param name="WarehouseName">The warehouse acted on.</param>
/// <param name="DestinationWarehouseName">Where a transfer moved the goods to.</param>
/// <param name="ReferenceNumber">The reference it relates to.</param>
/// <param name="Narration">The narration.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="LineCount">How many products it names.</param>
/// <param name="TotalQuantity">The total quantity it moved, in stock units.</param>
/// <param name="TotalValue">What those goods were valued at.</param>
public sealed record StockDocumentSummary(
    Guid Id,
    string Number,
    StockDocumentType Type,
    DateOnly Date,
    string WarehouseName,
    string? DestinationWarehouseName,
    string? ReferenceNumber,
    string? Narration,
    StockDocumentStatus Status,
    int LineCount,
    decimal TotalQuantity,
    decimal TotalValue);

/// <summary>Reads one stock document in full.</summary>
/// <param name="StockDocumentId">The document.</param>
public sealed record GetStockDocumentQuery(Guid StockDocumentId) : IQuery<StockDocumentDetail>;

/// <summary>One line of a stock document, as shown.</summary>
/// <param name="Id">The line.</param>
/// <param name="LineNumber">Its position on the document.</param>
/// <param name="ProductId">The product.</param>
/// <param name="ProductCode">The product's code.</param>
/// <param name="ProductDescription">What the product is called.</param>
/// <param name="UnitId">The unit the quantity was entered in.</param>
/// <param name="UnitCode">That unit's code.</param>
/// <param name="Quantity">The quantity as entered.</param>
/// <param name="StockQuantity">The same quantity in the product's stock unit.</param>
/// <param name="StockUnitCode">The stock unit's code.</param>
/// <param name="Rate">The rate, on the documents that carry one.</param>
/// <param name="Remarks">The line remark.</param>
/// <param name="BatchId">The batch that moved, on a product tracked in batches.</param>
/// <param name="BatchNumber">That batch's number.</param>
/// <param name="ExpiresOn">When the batch expires.</param>
public sealed record StockDocumentLineView(
    Guid Id,
    int LineNumber,
    Guid ProductId,
    string ProductCode,
    string ProductDescription,
    Guid UnitId,
    string UnitCode,
    decimal Quantity,
    decimal StockQuantity,
    string StockUnitCode,
    decimal Rate,
    string? Remarks,
    Guid? BatchId = null,
    string? BatchNumber = null,
    DateOnly? ExpiresOn = null);

/// <summary>One movement a document produced.</summary>
/// <param name="ProductCode">The product.</param>
/// <param name="WarehouseName">Where it moved.</param>
/// <param name="Quantity">The signed quantity, in stock units.</param>
/// <param name="UnitCost">What one unit was valued at.</param>
/// <param name="Value">The signed value.</param>
/// <param name="BalanceQuantity">What was on hand afterwards.</param>
/// <param name="BalanceAverageCost">The average cost afterwards.</param>
/// <param name="BatchNumber">The batch that moved, where the product is batched.</param>
public sealed record StockMovementView(
    string ProductCode,
    string WarehouseName,
    decimal Quantity,
    decimal UnitCost,
    decimal Value,
    decimal BalanceQuantity,
    decimal BalanceAverageCost,
    string? BatchNumber = null);

/// <summary>A stock document in full.</summary>
/// <param name="Id">The document.</param>
/// <param name="Number">Its number.</param>
/// <param name="Type">The kind of operation.</param>
/// <param name="Date">The document date.</param>
/// <param name="WarehouseId">The warehouse acted on.</param>
/// <param name="WarehouseName">Its name.</param>
/// <param name="DestinationWarehouseId">Where a transfer moved the goods to.</param>
/// <param name="DestinationWarehouseName">That warehouse's name.</param>
/// <param name="ReferenceNumber">The reference it relates to.</param>
/// <param name="Narration">The narration.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Currency">The currency values are stated in.</param>
/// <param name="CancellationReason">Why it was cancelled.</param>
/// <param name="Lines">What it names.</param>
/// <param name="Movements">
/// What it actually did. Empty on a draft, and on a cancelled document it holds the
/// reversals alongside the originals — which is the point of reversing rather than
/// deleting.
/// </param>
public sealed record StockDocumentDetail(
    Guid Id,
    string Number,
    StockDocumentType Type,
    DateOnly Date,
    Guid WarehouseId,
    string WarehouseName,
    Guid? DestinationWarehouseId,
    string? DestinationWarehouseName,
    string? ReferenceNumber,
    string? Narration,
    StockDocumentStatus Status,
    string Currency,
    string? CancellationReason,
    IReadOnlyList<StockDocumentLineView> Lines,
    IReadOnlyList<StockMovementView> Movements);

/// <summary>Reads stock documents.</summary>
public interface IStockDocumentReader
{
    /// <summary>Lists documents in a date range.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="from">The earliest date.</param>
    /// <param name="to">The latest date.</param>
    /// <param name="type">One kind, or null for all.</param>
    /// <param name="warehouseId">One warehouse, or null for all.</param>
    /// <param name="status">One state, or null for all.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The documents, newest first.</returns>
    Task<IReadOnlyList<StockDocumentSummary>> ListAsync(
        FirmId firmId,
        DateOnly from,
        DateOnly to,
        StockDocumentType? type,
        WarehouseId? warehouseId,
        StockDocumentStatus? status,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one document with its lines and its movements.</summary>
    /// <param name="firmId">The firm.</param>
    /// <param name="documentId">The document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document, or <see langword="null"/>.</returns>
    Task<StockDocumentDetail?> FindAsync(
        FirmId firmId,
        StockDocumentId documentId,
        CancellationToken cancellationToken = default);
}
